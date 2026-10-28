using System.Globalization;
using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.Price;

/// <summary>
/// One Price Is Right show: an emcee, and an audience of anyone who scans the QR code.
///
/// The announcer calls audience members down one at a time until Contestants' Row has four. An item is revealed and described,
/// and the four contestants bid in turn (nobody may repeat a bid). The closest bid without going over wins and goes on stage for a
/// pricing game. The other three stay in the row, one more person is called down, and the next item comes out. If all four bids
/// are over, the price is announced to be lower than the lowest bid and everyone bids again. After the last pricing game, contestants
/// who were called down but never made it on stage receive parting gifts.
///
/// The winner plays a pricing game on stage (Plinko so far; see Plinko.cs). While it is being played the winner's phone runs it, and the
/// emcee can step in or skip it. When it is over the emcee taps Continue.
///
/// Like the other games, this is plain C# with one lock. Every action returns a Result instead of throwing, and
/// Snapshot() hands out frozen pictures for the screens.
/// </summary>
public sealed class PriceGame
{
    public const int RowSize = 4;
    public const int NameMaxLength = 20;
    public const int SayAsMaxLength = 30;
    public const int MaxAudience = 200;
    public const int MaxBid = 999_999;
    public const int CommentMaxLength = 80;
    public const int MaxComments = 12;
    public const int ExactBonus = 500;
    public const int DefaultStageRounds = 6;

    private static readonly TimeSpan ReactionGap = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan CommentGap = TimeSpan.FromSeconds(3);

    private sealed class Member
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid Token { get; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public string? SayAs { get; init; }
        public Avatar Avatar { get; init; } = new(0, 0, 0, 0, false);
        public bool WentOnStage;
        public bool Appeared;
        public DateTime LastReaction = DateTime.MinValue;
        public DateTime LastComment = DateTime.MinValue;
        public string Spoken => string.IsNullOrWhiteSpace(SayAs) ? Name : SayAs!;
    }

    private readonly object _gate = new();
    private readonly string _code;
    private readonly IPriceItemSource _itemSource;
    private readonly IPartingGiftSource _giftSource;
    private readonly IPlinkoItemSource _plinkoSource;
    private readonly Random _rng;
    private readonly Func<DateTime> _now;
    private readonly int _stageRounds;

    private PricePhase _phase = PricePhase.Lobby;
    private Guid? _emceeToken;
    private string? _emceeName;
    private readonly List<Member> _audience = [];
    private readonly Guid?[] _row = new Guid?[RowSize];
    private Guid? _lastCalled;
    private bool _lastCalledWasNew;
    private List<PriceItem> _items = [];
    private int _itemIndex;
    private PriceItem? _item;
    private readonly List<(int Position, Guid Id, int Amount)> _bids = [];
    private int[] _order = [0, 1, 2, 3];
    private int _ceiling;
    private Guid? _winner;
    private bool _exact;
    private Guid? _onStage;
    private int _stageDone;
    private PlinkoRound? _plinko;
    private readonly int[] _reactions = new int[Enum.GetValues<Reaction>().Length];
    private readonly List<PriceComment> _comments = [];
    private int _commentSeq;
    private bool _commentsOn = true;
    private int _announceSeq;
    private string _announceText = "";
    private bool _announceFanfare;
    private List<PartingGiftInfo> _gifts = [];
    private string? _message;

    public PriceGame(string code, IPriceItemSource items, IPartingGiftSource gifts, Random? rng = null,
        Func<DateTime>? clock = null, int stageRounds = DefaultStageRounds, IPlinkoItemSource? plinkoItems = null)
    {
        _code = code;
        _plinkoSource = plinkoItems ?? new FixedPlinkoItemSource();
        _itemSource = items;
        _giftSource = gifts;
        _rng = rng ?? new Random();
        _now = clock ?? (() => DateTime.UtcNow);
        _stageRounds = Math.Max(1, stageRounds);
    }

    public string Code => _code;

    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;

    /// <summary>Raised after every successful change. Pages listen to this and redraw.</summary>
    public event Action? Changed;

    // ---- Joining -------------------------------------------------------------------------

    public JoinResult JoinEmcee(string name) => Run(() =>
    {
        if (_emceeToken is not null) return JoinResult.Fail("This show already has an emcee.");
        if (!TryCleanName(name, NameMaxLength, out var clean)) return JoinResult.Fail("Please enter a name.");
        _emceeToken = Guid.NewGuid();
        _emceeName = clean;
        return JoinResult.Success(_emceeToken.Value);
    });

    /// <summary>
    /// Anyone can join the audience, at any time. <paramref name="sayAs"/> is an optional spelling for the announcer to read
    /// (for a name a computer voice would get wrong).
    /// </summary>
    public JoinResult JoinAudience(string name, string? sayAs, Avatar avatar) => Run(() =>
    {
        if (_audience.Count >= MaxAudience) return JoinResult.Fail("The studio is full!");
        if (!TryCleanName(name, NameMaxLength, out var clean)) return JoinResult.Fail("Please enter a name.");
        if (_audience.Any(m => string.Equals(m.Name, clean, StringComparison.OrdinalIgnoreCase))
            || string.Equals(_emceeName, clean, StringComparison.OrdinalIgnoreCase))
            return JoinResult.Fail("Someone is already using that name.");

        TryCleanName(sayAs, SayAsMaxLength, out var say);
        var member = new Member { Name = clean, SayAs = say.Length == 0 ? null : say, Avatar = (avatar ?? Avatar.Random(_rng)).Clean() };
        _audience.Add(member);
        return JoinResult.Success(member.Token);
    });

    /// <summary>The emcee sends someone home (or out of the row) if they need to go.</summary>
    public Result RemoveAudience(Guid emceeToken, Guid audienceId) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        var member = _audience.FirstOrDefault(m => m.Id == audienceId);
        if (member is null) return Result.Fail("That person has already left.");
        if (_phase is PricePhase.Reveal or PricePhase.Bidding or PricePhase.BidResult or PricePhase.OnStage
            && (_row.Contains(member.Id) || _onStage == member.Id))
            return Result.Fail("They're in the middle of a round. Wait until it's over.");
        for (var i = 0; i < RowSize; i++) if (_row[i] == member.Id) _row[i] = null;
        if (_lastCalled == member.Id) _lastCalled = null;
        _audience.Remove(member);
        return Result.Success;
    });

    public Seat GetSeat(Guid token)
    {
        if (_emceeToken == token) return new Seat(Role.Emcee);
        lock (_gate)
        {
            var member = _audience.FirstOrDefault(m => m.Token == token);
            return member is null ? new Seat(Role.Spectator) : new Seat(Role.Player, member.Id);
        }
    }

    // ---- Running the show ----------------------------------------------------------------

    public Result Start(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can start the show.");
        if (_phase != PricePhase.Lobby) return Result.Fail("The show has already started.");
        if (_audience.Count < RowSize) return Result.Fail($"We need at least {RowSize} people in the audience.");
        BeginShow();
        return Result.Success;
    });

    /// <summary>Calls a random audience member down to the next empty seat in Contestants' Row.</summary>
    public Result CallDown(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PricePhase.Callout) return Result.Fail("Nobody is being called down right now.");
        if (_row.All(r => r is not null)) return Result.Fail("Contestants' Row is full.");
        return CallSomeone(excluding: null);
    });

    /// <summary>The person who was just called isn't there (or can't play): send them back and call someone else.</summary>
    public Result Reroll(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PricePhase.Callout) return Result.Fail("Nobody is being called down right now.");
        var last = _audience.FirstOrDefault(m => m.Id == _lastCalled);
        if (last is null || !_row.Contains(last.Id)) return Result.Fail("There's nobody to send back.");

        var seat = Array.IndexOf(_row, last.Id);
        var wasNew = _lastCalledWasNew;
        var wasAppeared = last.Appeared;
        _row[seat] = null;
        last.Appeared = !wasNew && wasAppeared;
        _lastCalled = null;
        var result = CallSomeone(excluding: last.Id);
        if (!result.Ok)
        {
            // Nobody else to call: put them back where they were rather than leaving an empty seat.
            _row[seat] = last.Id;
            last.Appeared = wasAppeared;
            _lastCalled = last.Id;
            _lastCalledWasNew = wasNew;
        }
        return result;
    });

    /// <summary>Contestants' Row is full: bring out the item.</summary>
    public Result ShowItem(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PricePhase.Callout) return Result.Fail("It isn't time for an item.");
        if (_row.Any(r => r is null)) return Result.Fail($"Contestants' Row needs {RowSize} people first.");
        BringOutItem();
        return Result.Success;
    });

    /// <summary>The emcee doesn't like the item (the crowd can't price it, say): bring out another before bidding starts.</summary>
    public Result SwapItem(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PricePhase.Reveal) return Result.Fail("The item can only be swapped before bidding starts.");
        BringOutItem();
        return Result.Success;
    });

    public Result OpenBidding(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PricePhase.Reveal) return Result.Fail("There's no item to bid on yet.");

        var start = _stageDone % RowSize;
        _order = Enumerable.Range(0, RowSize).Select(i => (start + i) % RowSize).ToArray();
        _bids.Clear();
        _ceiling = 0;
        ClearReactions();
        _phase = PricePhase.Bidding;
        _message = null;
        Say($"Bidding starts with {Spoken(_order[0])}!", fanfare: false);
        return Result.Success;
    });

    /// <summary>A contestant bids from their own phone, when it is their turn.</summary>
    public Result Bid(Guid token, int amount) => Run(() =>
    {
        var member = _audience.FirstOrDefault(m => m.Token == token);
        if (member is null) return Result.Fail("Only contestants can bid.");
        if (_phase != PricePhase.Bidding) return Result.Fail("Bidding isn't open.");
        if (_row[_order[_bids.Count]] != member.Id) return Result.Fail("It isn't your turn to bid.");
        return PlaceBid(amount);
    });

    /// <summary>The emcee bids for whoever is up (a phone died, or someone says their bid out loud).</summary>
    public Result BidFor(Guid emceeToken, int amount) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PricePhase.Bidding) return Result.Fail("Bidding isn't open.");
        return PlaceBid(amount);
    });

    /// <summary>The emcee moves the show along: show the stage, then finish the pricing game and carry on.</summary>
    public Result Continue(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        switch (_phase)
        {
            case PricePhase.BidResult:
            {
                var winner = _audience.First(m => m.Id == _winner);
                for (var i = 0; i < RowSize; i++) if (_row[i] == winner.Id) _row[i] = null;
                _onStage = winner.Id;
                _phase = PricePhase.OnStage;
                _message = $"{winner.Name} is on stage for Plinko!";
                _plinko = new PlinkoRound(_plinkoSource.CreateItems(_rng, PlinkoBoard.ItemCount), _rng, Say);
                ClearReactions();
                _plinko.Begin($"{winner.Spoken}, come on up to the stage!");
                return Result.Success;
            }

            case PricePhase.OnStage:
            {
                if (_plinko is { Done: false }) return Result.Fail("The pricing game isn't over yet. You can skip it if it's stuck.");
                _plinko = null;
                var player = _audience.FirstOrDefault(m => m.Id == _onStage);
                if (player is not null) player.WentOnStage = true;
                _onStage = null;
                _stageDone++;
                if (_stageDone >= _stageRounds)
                {
                    EndShow();
                }
                else
                {
                    _item = null;
                    _phase = PricePhase.Callout;
                    _message = "One more person comes down to Contestants' Row.";
                }
                return Result.Success;
            }

            default:
                return Result.Fail("There's nothing to continue from.");
        }
    });

    // ---- Plinko (the contestant on stage plays it from their phone; the emcee can do any of it for them) ---

    public Result PlinkoGuess(Guid token, bool higher) => Run(() => Plinko(token, false, r => r.Guess(higher)));
    public Result PlinkoGuessFor(Guid emceeToken, bool higher) => Run(() => Plinko(emceeToken, true, r => r.Guess(higher)));

    /// <summary>Slides the chip along the top. This returns a failure when nothing changed, so quiet repeats don't redraw every screen.</summary>
    public Result PlinkoAim(Guid token, int slot) => Run(() => Plinko(token, false, r => r.Aim(slot)));

    public Result PlinkoDrop(Guid token, int slot) => Run(() => Plinko(token, false, r => r.DropChip(slot)));
    public Result PlinkoDropFor(Guid emceeToken, int slot) => Run(() => Plinko(emceeToken, true, r => r.DropChip(slot)));

    /// <summary>
    /// The big screen says a chip has finished falling. Anyone may report it: the result was settled when the chip dropped,
    /// so reporting early only moves the scoreboard up a few seconds.
    /// </summary>
    public Result PlinkoLand(int dropNumber) => Run(() => _phase != PricePhase.OnStage || _plinko is null
        ? Result.Fail("Plinko isn't being played.")
        : _plinko.Land(dropNumber));

    /// <summary>The emcee gives up on the pricing game (a phone died, someone walked off). Chips already dropped still count.</summary>
    public Result SkipPricingGame(Guid emceeToken) => Run(() => Plinko(emceeToken, true, r => r.Skip()));

    private Result Plinko(Guid token, bool emceeOnly, Func<PlinkoRound, Result> act)
    {
        if (_phase != PricePhase.OnStage || _plinko is null) return Result.Fail("Plinko isn't being played.");
        var allowed = IsEmcee(token)
            || (!emceeOnly && _onStage is not null && _audience.FirstOrDefault(m => m.Token == token)?.Id == _onStage);
        if (!allowed) return Result.Fail(emceeOnly ? "Only the emcee can do that." : "Only the contestant on stage can do that.");
        return act(_plinko);
    }

    /// <summary>After the show: do it again with the same audience.</summary>
    public Result Restart(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PricePhase.ShowOver) return Result.Fail("The show isn't over yet.");
        foreach (var m in _audience) { m.WentOnStage = false; m.Appeared = false; }
        _comments.Clear();
        if (_audience.Count >= RowSize) BeginShow(); else _phase = PricePhase.Lobby;
        return Result.Success;
    });

    /// <summary>The announcer says the last thing again (the TV's sound was off, or the room was too loud).</summary>
    public Result Repeat(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_announceText.Length == 0) return Result.Fail("The announcer hasn't said anything yet.");
        _announceSeq++;
        return Result.Success;
    });

    /// <summary>The announcer stops talking (a long description is dragging on).</summary>
    public Result Hush(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        Say("", fanfare: false);
        return Result.Success;
    });

    // ---- The audience speaks -------------------------------------------------------------

    public Result React(Guid token, Reaction kind) => Run(() =>
    {
        var member = _audience.FirstOrDefault(m => m.Token == token);
        if (member is null) return Result.Fail("Only the audience can do that.");
        if (!Enum.IsDefined(kind)) return Result.Fail("Unknown reaction.");
        var now = _now();
        if (now - member.LastReaction < ReactionGap) return Result.Fail("Easy there!");
        member.LastReaction = now;
        _reactions[(int)kind]++;
        return Result.Success;
    });

    public Result Comment(Guid token, string? text) => Run(() =>
    {
        var member = _audience.FirstOrDefault(m => m.Token == token);
        if (member is null) return Result.Fail("Only the audience can do that.");
        if (!_commentsOn) return Result.Fail("Comments are turned off.");
        if (!TryCleanName(text, CommentMaxLength, out var clean)) return Result.Fail("Type something first.");
        var now = _now();
        if (now - member.LastComment < CommentGap) return Result.Fail("Give it a few seconds between comments.");
        member.LastComment = now;
        _comments.Add(new PriceComment(++_commentSeq, member.Name, clean));
        while (_comments.Count > MaxComments) _comments.RemoveAt(0);
        return Result.Success;
    });

    public Result DeleteComment(Guid emceeToken, int commentId) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        return _comments.RemoveAll(c => c.Id == commentId) > 0 ? Result.Success : Result.Fail("That comment is already gone.");
    });

    public Result SetComments(Guid emceeToken, bool on) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        _commentsOn = on;
        if (!on) _comments.Clear();
        return Result.Success;
    });

    // ---- Inside the show -----------------------------------------------------------------

    private void BeginShow()
    {
        _items = _itemSource.CreateItems(_rng, 24).ToList();
        _itemIndex = 0;
        _item = null;
        Array.Clear(_row);
        _lastCalled = null;
        _winner = null;
        _onStage = null;
        _plinko = null;
        _stageDone = 0;
        _gifts = [];
        ClearReactions();
        _phase = PricePhase.Callout;
        _message = "Let's fill Contestants' Row!";
        Say("Welcome to the show! Let's meet our first contestants.", fanfare: true);
    }

    private Result CallSomeone(Guid? excluding)
    {
        var free = _audience.Where(m => !_row.Contains(m.Id) && m.Id != _onStage && m.Id != excluding && !m.WentOnStage).ToList();
        var fresh = free.Where(m => !m.Appeared).ToList();
        var pool = fresh.Count > 0 ? fresh : free;
        if (pool.Count == 0) pool = _audience.Where(m => !_row.Contains(m.Id) && m.Id != excluding && m.Id != _onStage).ToList();
        if (pool.Count == 0) return Result.Fail("There's nobody left to call down.");

        var pick = pool[_rng.Next(pool.Count)];
        var slot = Array.FindIndex(_row, r => r is null);
        _row[slot] = pick.Id;
        _lastCalledWasNew = !pick.Appeared;
        pick.Appeared = true;
        _lastCalled = pick.Id;
        _message = _row.All(r => r is not null)
            ? $"{pick.Name}, come on down! Contestants' Row is full."
            : $"{pick.Name}, come on down!";
        Say($"{pick.Spoken}, come on down!", fanfare: true);
        return Result.Success;
    }

    private void BringOutItem()
    {
        if (_itemIndex >= _items.Count) _items.AddRange(_itemSource.CreateItems(_rng, 24));
        _item = _items[_itemIndex++];
        _phase = PricePhase.Reveal;
        _bids.Clear();
        _ceiling = 0;
        ClearReactions();
        _message = null;
        Say($"Here is our item up for bid! {_item.Description} This {_item.SaidName} could be yours, if the price is right!", fanfare: true);
    }

    private Result PlaceBid(int amount)
    {
        if (amount < 1) return Result.Fail("A bid has to be at least $1.");
        if (amount > MaxBid) return Result.Fail($"The most anyone can bid is {Money(MaxBid)}.");
        if (_ceiling > 0 && amount >= _ceiling) return Result.Fail($"The price is less than {Money(_ceiling)}, so bid lower than that.");
        if (_bids.Any(b => b.Amount == amount)) return Result.Fail("Someone already bid that. Pick a different amount.");

        var position = _order[_bids.Count];
        _bids.Add((position, _row[position]!.Value, amount));
        ClearReactions();
        if (_bids.Count == RowSize) Resolve();
        return Result.Success;
    }

    private void Resolve()
    {
        var item = _item!;
        var under = _bids.Where(b => b.Amount <= item.Price).ToList();
        if (under.Count == 0)
        {
            _ceiling = _bids.Min(b => b.Amount);
            _bids.Clear();
            _message = $"Everybody went over! The price is less than {Money(_ceiling)}. Bid again.";
            Say($"Everybody went over! The price is less than {Money(_ceiling)}. Bid again!", fanfare: false);
            return;
        }

        var best = under.MaxBy(b => b.Amount);
        _winner = best.Id;
        _exact = best.Amount == item.Price;
        var winner = _audience.First(m => m.Id == best.Id);
        _phase = PricePhase.BidResult;
        _message = _exact
            ? $"{winner.Name} bid exactly {Money(item.Price)}! A {Money(ExactBonus)} bonus!"
            : $"{winner.Name} wins with {Money(best.Amount)}.";
        Say($"The actual retail price is {Money(item.Price)}! "
            + (_exact ? $"{winner.Spoken} bid exactly right! " : $"{winner.Spoken} was closest, with {Money(best.Amount)}. ")
            + "Come on up!", fanfare: true);
    }

    private void EndShow()
    {
        var losers = _audience.Where(m => m.Appeared && !m.WentOnStage).ToList();
        var gifts = losers.Count == 0 ? [] : _giftSource.PickGifts(_rng, losers.Count);
        _gifts = losers.Select((m, i) => new PartingGiftInfo(m.Name, gifts[i])).ToList();
        _phase = PricePhase.ShowOver;
        _message = losers.Count == 0 ? "Everyone who came down made it on stage!" : "Thanks for playing!";

        var lines = new List<string> { "That's our show! Thank you for coming on down." };
        if (losers.Count > 0)
        {
            lines.Add("And for our contestants who did not make it on stage, we have some wonderful parting gifts.");
            for (var i = 0; i < losers.Count; i++) lines.Add($"{losers[i].Spoken}, you will receive {gifts[i]}!");
        }
        Say(string.Join(" ", lines), fanfare: false);
    }

    private void ClearReactions() => Array.Clear(_reactions);

    private void Say(string text, bool fanfare)
    {
        _announceSeq++;
        _announceText = text;
        _announceFanfare = fanfare;
    }

    private string Spoken(int position) => _audience.First(m => m.Id == _row[position]).Spoken;

    // ---- The picture ---------------------------------------------------------------------

    public PriceSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new PriceSnapshot(
                Code: _code,
                Phase: _phase,
                EmceeName: _emceeName,
                Audience: _audience.Select(m => new AudienceInfo(m.Id, m.Name, m.Avatar, _row.Contains(m.Id), m.WentOnStage, m.Appeared)).ToList(),
                Row: Enumerable.Range(0, RowSize).Select(i => new RowSlotInfo(i, _row[i])).ToList(),
                LastCalledId: _lastCalled,
                Item: _item is null ? null : new PriceItemInfo(_item.Name, _item.Description, _item.Icon, _item.Image),
                ItemNumber: _item is null ? 0 : _itemIndex,
                Price: _item?.Price ?? 0,
                Bids: _bids.Select(b => new BidInfo(b.Position, b.Id, b.Amount)).ToList(),
                BidTurnPosition: _phase == PricePhase.Bidding && _bids.Count < RowSize ? _order[_bids.Count] : -1,
                BidCeiling: _ceiling,
                WinnerId: _winner,
                ExactPrice: _exact,
                OnStageId: _onStage,
                StageRound: _stageDone,
                StageRounds: _stageRounds,
                Reactions: _reactions.ToList(),
                Comments: _comments.ToList(),
                CommentsOn: _commentsOn,
                AnnounceSeq: _announceSeq,
                AnnounceText: _announceText,
                AnnounceFanfare: _announceFanfare,
                PartingGifts: _gifts.ToList(),
                Message: _message,
                CanStart: _phase == PricePhase.Lobby && _emceeToken is not null && _audience.Count >= RowSize,
                Plinko: _plinko?.Info());
        }
    }

    // ---- Helpers -------------------------------------------------------------------------

    private T Run<T>(Func<T> action) where T : IResult
    {
        T result;
        lock (_gate)
        {
            result = action();
        }
        if (result.Ok) Changed?.Invoke();
        return result;
    }

    private bool IsEmcee(Guid token) => _emceeToken is not null && _emceeToken == token;

    public static string Money(int amount) => "$" + amount.ToString("N0", CultureInfo.InvariantCulture);

    private static bool TryCleanName(string? text, int max, out string clean)
    {
        // Collapse whitespace and drop control characters, so a pasted tab or newline can't mess up a screen.
        var chars = (text ?? "").Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray();
        clean = string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > max) clean = clean[..max].Trim();
        return clean.Length > 0;
    }
}
