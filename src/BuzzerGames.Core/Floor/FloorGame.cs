using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.Floor;

/// <summary>
/// One game of The Floor. Everyone joins from a phone and picks a topic; the emcee builds the floor (one square per player);
/// players challenge their neighbors to one-on-one picture duels in the defender's topics; the winner takes the loser's squares
/// (and, if inheritance is on, the loser's topics) until one player owns the floor.
///
/// Like the other games, every action returns a <see cref="Result"/> instead of throwing, all state sits behind one lock, and
/// <see cref="Changed"/> is raised after anything the screens should redraw for. The clocks are kept here, not on the phones:
/// only one clock runs at a time, and a timer ends the duel at the right moment even if nobody touches anything.
/// </summary>
public sealed class FloorGame : IDisposable
{
    public const int MinPlayers = 4;            // the party will have 10-20; 4 keeps testing easy
    public const int MaxPlayers = 20;
    public const int NameMaxLength = 20;
    public const int DefaultDuelSeconds = 45;
    public const int MinDuelSeconds = 10;
    public const int MaxDuelSeconds = 120;
    public const int PassPenaltyMs = 3000;      // after a pass the answer is shown this long, with the clock still running
    public const int LookAhead = 5;             // prompts drawn ahead, so the TV can load the pictures before they're needed
    public const int MinLivePrompts = 10;       // a defender with fewer than this left in all their topics must pick a new one
    public const double HomeTopicWeight = 1.5;  // a smashed-together defender is asked about their own topic a little more

    private readonly object _gate = new();
    private readonly string _code;
    private readonly IFloorContent _content;
    private readonly Random _rng;
    private readonly Func<DateTime> _now;

    private sealed class Player
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid Token { get; } = Guid.NewGuid();
        public required string Name { get; init; }
        public List<string> Topics { get; set; } = [];
        public bool Alive { get; set; } = true;
    }

    private sealed record Undo(FloorPrompt? Current, int ChallengerMs, int DefenderMs, Player Active, int ChallengerCorrect, int DefenderCorrect);

    private FloorPhase _phase = FloorPhase.Lobby;
    private Guid? _emceeToken;
    private string? _emceeName;
    private readonly List<Player> _players = [];
    private FloorShape? _shape;
    private Player?[] _owner = [];
    private bool _inherit = true;
    private int _duelMs = DefaultDuelSeconds * 1000;

    private Player? _challenger;
    private Player? _defender;
    private Player? _repickPlayer;
    private bool _finalDuel;
    private int _duelsPlayed;

    // Prompts still unused in this game, per topic. The last one in each list is drawn next.
    private readonly Dictionary<string, List<FloorPrompt>> _pools = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FloorPrompt> _queue = [];

    // The duel.
    private FloorPrompt? _current;
    private Player? _active;
    private int _msChallenger, _msDefender;
    private int _correctChallenger, _correctDefender;
    private DateTime _anchor;
    private bool _paused;
    private DateTime? _revealUntil;
    private Undo? _undo;
    private Player? _loser;
    private FloorEndReason? _endReason;
    private FloorTakeover? _takeover;
    private Player? _winner;

    private int _version;
    private bool _dirty;
    private Timer? _timer;

    public FloorGame(string code, IFloorContent content, Random? rng = null, Func<DateTime>? clock = null)
    {
        _code = code;
        _content = content;
        _rng = rng ?? new Random();
        _now = clock ?? (() => DateTime.UtcNow);
    }

    public string Code => _code;
    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;

    public event Action? Changed;

    // ---------------------------------------------------------------------------------------
    // Joining and the lobby
    // ---------------------------------------------------------------------------------------

    public JoinResult JoinEmcee(string name) => Run(() =>
    {
        if (_emceeToken is not null) return JoinResult.Fail("This game already has an emcee.");
        if (!TryCleanText(name, NameMaxLength, out var clean)) return JoinResult.Fail("Please enter a name.");
        _emceeToken = Guid.NewGuid();
        _emceeName = clean;
        return JoinResult.Success(_emceeToken.Value);
    });

    public JoinResult JoinPlayer(string name) => Run(() =>
    {
        if (_phase != FloorPhase.Lobby) return JoinResult.Fail("The floor is already built. Ask the emcee to start a new game.");
        if (_players.Count >= MaxPlayers) return JoinResult.Fail($"The floor is full ({MaxPlayers} players).");
        if (!TryCleanText(name, NameMaxLength, out var clean)) return JoinResult.Fail("Please enter a name.");
        if (_players.Any(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase)))
            return JoinResult.Fail("Someone already has that name.");
        var player = new Player { Name = clean };
        _players.Add(player);
        return JoinResult.Success(player.Token);
    });

    public Result RemovePlayer(Guid emceeToken, Guid playerId) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != FloorPhase.Lobby) return Result.Fail("Players can only be removed before the floor is built.");
        return _players.RemoveAll(p => p.Id == playerId) > 0 ? Result.Success : Result.Fail("That player isn't here.");
    });

    /// <summary>A player chooses their topic: in the lobby, their own; mid-game, a replacement after running out of prompts.</summary>
    public Result PickTopic(Guid token, string topic) => Run(() =>
    {
        var player = _players.FirstOrDefault(p => p.Token == token);
        if (player is null) return Result.Fail("Only players can pick a topic.");

        if (_phase == FloorPhase.Lobby)
        {
            if (player.Topics.Count == 1 && string.Equals(player.Topics[0], topic, StringComparison.OrdinalIgnoreCase)) return Result.Success;
            var error = CheckPick(topic, out var info);
            if (error is not null) return Result.Fail(error);
            player.Topics = [info!.Name];
            return Result.Success;
        }

        if (_phase == FloorPhase.Repick && player == _repickPlayer) return FinishRepick(player, topic);
        return Result.Fail("You can't pick a topic right now.");
    });

    /// <summary>The emcee picks a random topic for a player who can't or won't (in the lobby, or when a defender must re-pick).</summary>
    public Result AssignRandomTopic(Guid emceeToken, Guid playerId) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        var player = _players.FirstOrDefault(p => p.Id == playerId);
        if (player is null) return Result.Fail("That player isn't here.");
        if (_phase != FloorPhase.Lobby && !(_phase == FloorPhase.Repick && player == _repickPlayer))
            return Result.Fail("There's nothing to assign right now.");

        var held = HeldTopics();
        var options = _content.ListTopics()
            .Where(t => t.Total >= _content.MinPrompts && !t.Used && !held.Contains(t.Name))
            .Where(t => _phase == FloorPhase.Lobby || t.Total >= MinLivePrompts)
            .ToList();
        if (options.Count == 0) return Result.Fail("Every topic is taken or used up. Tap \"Make all topics eligible again\".");
        var pick = options[_rng.Next(options.Count)];

        if (_phase == FloorPhase.Lobby)
        {
            player.Topics = [pick.Name];
            return Result.Success;
        }
        return FinishRepick(player, pick.Name);
    });

    public Result SetInherit(Guid emceeToken, bool inherit) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase is FloorPhase.Duel or FloorPhase.TimeUp) return Result.Fail("Change that between duels.");
        _inherit = inherit;
        return Result.Success;
    });

    public Result SetDuelSeconds(Guid emceeToken, int seconds) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase is FloorPhase.Duel or FloorPhase.TimeUp or FloorPhase.Takeover) return Result.Fail("Change that between duels.");
        if (seconds < MinDuelSeconds || seconds > MaxDuelSeconds) return Result.Fail($"Pick between {MinDuelSeconds} and {MaxDuelSeconds} seconds.");
        _duelMs = seconds * 1000;
        return Result.Success;
    });

    /// <summary>Topics used in earlier games become pickable again.</summary>
    public Result ClearUsedTopics(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase is not (FloorPhase.Lobby or FloorPhase.Repick)) return Result.Fail("Do that in the lobby.");
        _content.ClearUsed();
        return Result.Success;
    });

    /// <summary>Lays out the floor, shuffles the players onto it and picks the first challenger at random.</summary>
    public Result Build(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can build the floor.");
        if (_phase != FloorPhase.Lobby) return Result.Fail("The floor is already built.");
        if (_players.Count < MinPlayers) return Result.Fail($"Need at least {MinPlayers} players ({_players.Count} so far).");
        var waiting = _players.Where(p => p.Topics.Count == 0).Select(p => p.Name).ToList();
        if (waiting.Count > 0) return Result.Fail("Still waiting on a topic from " + string.Join(", ", waiting) + ".");

        var pools = new Dictionary<string, List<FloorPrompt>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var topic in _players.Select(p => p.Topics[0]))
            {
                var prompts = _content.LoadPrompts(topic).ToList();
                if (prompts.Count < MinLivePrompts) return Result.Fail($"{topic} only has {prompts.Count} prompts. Add more, or have its player pick another topic.");
                Shuffle(prompts);
                pools[topic] = prompts;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail("Couldn't read the topics: " + ex.Message);
        }

        _pools.Clear();
        foreach (var (topic, prompts) in pools) _pools[topic] = prompts;
        foreach (var topic in pools.Keys) _content.MarkUsed(topic);

        _shape = FloorShape.For(_players.Count);
        var order = _players.ToList();
        Shuffle(order);
        _owner = new Player?[_shape.Cells.Count];
        for (var i = 0; i < order.Count; i++) _owner[i] = order[i];

        _duelsPlayed = 0;
        _winner = null;
        EnterChoose(order[_rng.Next(order.Count)]);
        return Result.Success;
    });

    /// <summary>Throws the game away and returns to the lobby. The same players stay, but everyone picks a topic again.</summary>
    public Result BackToLobby(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase == FloorPhase.Lobby) return Result.Fail("You're already in the lobby.");
        ResetToLobby();
        return Result.Success;
    });

    // ---------------------------------------------------------------------------------------
    // Choosing a duel
    // ---------------------------------------------------------------------------------------

    /// <summary>The challenger (or the emcee for them) picks a neighbor to challenge.</summary>
    public Result Challenge(Guid token, Guid defenderId) => Run(() =>
    {
        if (_phase != FloorPhase.Choose) return Result.Fail("Nobody is choosing an opponent right now.");
        if (!IsEmcee(token) && _challenger?.Token != token) return Result.Fail("It isn't your turn to challenge.");
        var defender = _players.FirstOrDefault(p => p.Id == defenderId);
        if (defender is null || !defender.Alive) return Result.Fail("That player isn't on the floor.");
        if (!AdjacentOpponents(_challenger!).Contains(defender)) return Result.Fail("You can only challenge a neighbor.");
        _defender = defender;
        AfterDefenderChosen();
        return Result.Success;
    });

    /// <summary>Back out of the challenge and pick someone else.</summary>
    public Result ChangeOpponent(Guid token) => Run(() =>
    {
        if (_phase is not (FloorPhase.Ready or FloorPhase.Repick)) return Result.Fail("There's no challenge to change.");
        if (!IsEmcee(token) && _challenger?.Token != token) return Result.Fail("Only the challenger or the emcee can do that.");
        if (_finalDuel) return Result.Fail("This is the final duel.");
        _defender = null;
        _repickPlayer = null;
        _phase = FloorPhase.Choose;
        return Result.Success;
    });

    // ---------------------------------------------------------------------------------------
    // The duel
    // ---------------------------------------------------------------------------------------

    public Result StartDuel(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can start the duel.");
        if (_phase != FloorPhase.Ready) return Result.Fail("There's no duel to start.");

        _queue.Clear();
        Refill();
        var first = NextPrompt();
        if (first is null) return Result.Fail("The defender has no prompts left.");

        _current = first;
        _active = _challenger;
        _msChallenger = _msDefender = _duelMs;
        _correctChallenger = _correctDefender = 0;
        _paused = false;
        _revealUntil = null;
        _undo = null;
        _loser = null;
        _endReason = null;
        _anchor = _now();
        _phase = FloorPhase.Duel;
        return Result.Success;
    });

    /// <summary>The player on the clock got it right: the clock passes to the other player and a new picture comes up.</summary>
    public Result Correct(Guid emceeToken) => Run(() =>
    {
        var error = CheckCanJudge(emceeToken);
        if (error is not null) return Result.Fail(error);

        Fold(_now());
        TakeUndo();
        if (_active == _challenger) _correctChallenger++; else _correctDefender++;
        _active = _active == _challenger ? _defender : _challenger;
        _current = NextPrompt();
        if (_current is null) EndOutOfPrompts();
        return Result.Success;
    });

    /// <summary>The player on the clock passes: the answer is shown for a few seconds while their clock keeps running.</summary>
    public Result Pass(Guid emceeToken) => Run(() =>
    {
        var error = CheckCanJudge(emceeToken);
        if (error is not null) return Result.Fail(error);

        var now = _now();
        Fold(now);
        TakeUndo();
        _revealUntil = now.AddMilliseconds(PassPenaltyMs);
        return Result.Success;
    });

    /// <summary>Takes back the last Correct or Pass, including the time it cost.</summary>
    public Result UndoLast(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != FloorPhase.Duel) return Result.Fail("There's nothing to undo.");
        if (_undo is null) return Result.Fail("There's nothing to undo.");

        var u = _undo;
        if (_current is not null && !ReferenceEquals(_current, u.Current)) _queue.Insert(0, _current);
        _current = u.Current;
        _active = u.Active;
        _msChallenger = u.ChallengerMs;
        _msDefender = u.DefenderMs;
        _correctChallenger = u.ChallengerCorrect;
        _correctDefender = u.DefenderCorrect;
        _revealUntil = null;
        _anchor = _now();
        _undo = null;
        return Result.Success;
    });

    public Result Pause(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != FloorPhase.Duel) return Result.Fail("There's no duel to pause.");
        if (_paused) return Result.Fail("Already paused.");
        if (_revealUntil is not null) return Result.Fail("Wait for the answer to finish showing.");
        Fold(_now());
        _paused = true;
        return Result.Success;
    });

    public Result Resume(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != FloorPhase.Duel || !_paused) return Result.Fail("The duel isn't paused.");
        _paused = false;
        _anchor = _now();
        return Result.Success;
    });

    /// <summary>Moves the game along: after time is up the winner takes the territory, and after the takeover the winner challenges next.</summary>
    public Result Continue(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");

        switch (_phase)
        {
            case FloorPhase.TimeUp:
                ApplyTransfer();
                _phase = FloorPhase.Takeover;
                return Result.Success;

            case FloorPhase.Takeover:
                if (_players.Count(p => p.Alive) <= 1)
                {
                    _winner = _players.First(p => p.Alive);
                    _phase = FloorPhase.GameOver;
                }
                else
                {
                    EnterChoose(_winner ?? _players.First(p => p.Alive));
                }
                return Result.Success;

            default:
                return Result.Fail("There's nothing to continue.");
        }
    });

    /// <summary>Lets the clocks catch up with the real time. A timer calls this when a clock should run out; tests call it by hand.</summary>
    public Result Tick()
    {
        bool raise;
        lock (_gate)
        {
            Settle();
            raise = _dirty;
            if (raise) _version++;
            _dirty = false;
            Reschedule();
        }
        if (raise) Changed?.Invoke();
        return Result.Success;
    }

    // ---------------------------------------------------------------------------------------
    // Reading
    // ---------------------------------------------------------------------------------------

    public Seat GetSeat(Guid token)
    {
        lock (_gate)
        {
            if (_emceeToken == token) return new Seat(Role.Emcee);
            var player = _players.FirstOrDefault(p => p.Token == token);
            return player is null ? new Seat(Role.Spectator) : new Seat(Role.Player, player.Id);
        }
    }

    public FloorSnapshot Snapshot()
    {
        lock (_gate)
        {
            Settle();
            var now = _now();

            var players = _players.Select(p => new FloorPlayerInfo(
                p.Id, p.Name, p.Topics.ToList(),
                _phase == FloorPhase.Lobby ? [] : p.Topics.Where(t => PoolCount(t) == 0).ToList(),
                _owner.Count(o => o == p), p.Alive)).ToList();

            var squares = new List<FloorSquare>();
            if (_shape is not null)
                for (var i = 0; i < _shape.Cells.Count; i++)
                    if (_owner[i] is { } owner) squares.Add(new FloorSquare(i, _shape.Cells[i].Row, _shape.Cells[i].Col, owner.Id));

            var candidates = _phase == FloorPhase.Choose && _challenger is not null
                ? AdjacentOpponents(_challenger).Select(p => p.Id).ToList()
                : new List<Guid>();

            var topics = new List<FloorTopicChoice>();
            var deadCount = 0;
            if (_phase is FloorPhase.Lobby or FloorPhase.Repick)
            {
                var all = _content.ListTopics();
                deadCount = all.Count(t => t.Used);
                foreach (var t in all.Where(t => t.Total >= _content.MinPrompts))
                {
                    var owner = _players.FirstOrDefault(p => p.Topics.Contains(t.Name, StringComparer.OrdinalIgnoreCase));
                    topics.Add(new FloorTopicChoice(t.Name, t.Total, t.Used, owner?.Name));
                }
            }

            FloorDuelInfo? duel = null;
            if (_phase is FloorPhase.Duel or FloorPhase.TimeUp && _challenger is not null && _defender is not null)
            {
                var inDuel = _phase == FloorPhase.Duel;
                var reveal = inDuel && _revealUntil is not null;
                duel = new FloorDuelInfo(
                    _challenger.Id, _defender.Id, (_active ?? _challenger).Id,
                    LeftMs(_challenger, now), LeftMs(_defender, now),
                    _correctChallenger, _correctDefender,
                    Running: inDuel && !_paused, Paused: inDuel && _paused,
                    Topic: _current?.Topic ?? "",
                    PromptText: inDuel ? _current?.Text : null,
                    ImageKey: inDuel ? _current?.ImageKey : null,
                    Answer: inDuel ? _current?.Answer ?? "" : "",
                    Alternates: inDuel ? _current?.Alternates ?? [] : [],
                    RevealAnswer: reveal,
                    RevealMs: reveal ? (int)Math.Max(0, (_revealUntil!.Value - now).TotalMilliseconds) : 0,
                    Upcoming: inDuel ? _queue.Where(q => q.ImageKey is not null).Select(q => q.ImageKey!).Take(LookAhead).ToList() : [],
                    CanUndo: inDuel && _undo is not null,
                    PromptsLeft: PromptsLeft(_defender) + _queue.Count + (_current is null ? 0 : 1));
            }

            return new FloorSnapshot(
                _code, _phase, _version,
                EmceeJoined: _emceeToken is not null, EmceeName: _emceeName,
                Players: players,
                Rows: _shape?.Rows ?? 0, Cols: _shape?.Cols ?? 0, Squares: squares,
                ChallengerId: _phase is FloorPhase.Lobby or FloorPhase.GameOver ? null : _challenger?.Id,
                DefenderId: _phase is FloorPhase.Lobby or FloorPhase.GameOver or FloorPhase.Choose ? null : _defender?.Id,
                Candidates: candidates,
                FinalDuel: _finalDuel && _phase is not (FloorPhase.Lobby or FloorPhase.GameOver),
                RepickPlayerId: _phase == FloorPhase.Repick ? _repickPlayer?.Id : null,
                Topics: topics, DeadTopicCount: deadCount,
                Inherit: _inherit, DuelSeconds: _duelMs / 1000,
                Duel: duel,
                EndReason: _phase is FloorPhase.TimeUp ? _endReason : null,
                LoserId: _phase is FloorPhase.TimeUp or FloorPhase.Takeover ? _loser?.Id : null,
                Takeover: _phase == FloorPhase.Takeover ? _takeover : null,
                WinnerId: _phase == FloorPhase.GameOver ? _winner?.Id : null,
                DuelsPlayed: _duelsPlayed,
                MinPlayers: MinPlayers, MaxPlayers: MaxPlayers);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------------------

    private T Run<T>(Func<T> action) where T : IResult
    {
        T result;
        bool raise;
        lock (_gate)
        {
            Settle();
            result = action();
            Settle();
            raise = result.Ok || _dirty;
            if (raise) _version++;
            _dirty = false;
            Reschedule();
        }
        if (raise) Changed?.Invoke();
        return result;
    }

    private bool IsEmcee(Guid token) => _emceeToken is not null && _emceeToken == token;

    private bool Running => _phase == FloorPhase.Duel && !_paused;

    private int ActiveStored => _active == _challenger ? _msChallenger : _msDefender;

    private void SetActiveStored(int ms)
    {
        if (_active == _challenger) _msChallenger = ms; else _msDefender = ms;
    }

    /// <summary>Moves the running clock's elapsed time into the stored value, as of <paramref name="at"/>.</summary>
    private void Fold(DateTime at)
    {
        if (!Running || _active is null) return;
        var elapsed = Math.Max(0, (at - _anchor).TotalMilliseconds);
        SetActiveStored((int)Math.Max(0, ActiveStored - elapsed));
        _anchor = at;
    }

    private int LeftMs(Player player, DateTime now)
    {
        var stored = player == _challenger ? _msChallenger : _msDefender;
        if (Running && player == _active) stored -= (int)Math.Max(0, (now - _anchor).TotalMilliseconds);
        return Math.Max(0, stored);
    }

    /// <summary>
    /// Applies everything that should already have happened by now: a passed picture's answer window closing, or a clock reaching zero.
    /// Sets <see cref="_dirty"/> so the screens are told even if the call that noticed it was refused.
    /// </summary>
    private void Settle()
    {
        for (var guard = 0; guard < 20 && Running; guard++)
        {
            var now = _now();
            var expiry = _anchor.AddMilliseconds(ActiveStored);

            if (_revealUntil is { } reveal && reveal <= now && reveal <= expiry)
            {
                Fold(reveal);
                _revealUntil = null;
                _dirty = true;
                _current = NextPrompt();
                if (_current is null) EndOutOfPrompts();
                continue;
            }

            if (expiry <= now)
            {
                SetActiveStored(0);
                _anchor = expiry;
                EndDuel(_active!, FloorEndReason.Clock);
                _dirty = true;
            }
            return;
        }
    }

    private void Reschedule()
    {
        _timer?.Dispose();
        _timer = null;
        if (!Running || _active is null) return;

        var expiry = _anchor.AddMilliseconds(ActiveStored);
        var next = _revealUntil is { } reveal && reveal < expiry ? reveal : expiry;
        var delay = next - _now() + TimeSpan.FromMilliseconds(50);
        if (delay < TimeSpan.FromMilliseconds(50)) delay = TimeSpan.FromMilliseconds(50);
        _timer = new Timer(_ => Tick(), null, delay, Timeout.InfiniteTimeSpan);
    }

    private string? CheckCanJudge(Guid emceeToken)
    {
        if (!IsEmcee(emceeToken)) return "Only the emcee can do that.";
        if (_phase != FloorPhase.Duel) return "There's no duel running.";
        if (_paused) return "The duel is paused.";
        if (_revealUntil is not null) return "The answer is still showing.";
        return null;
    }

    private void TakeUndo() =>
        _undo = new Undo(_current, _msChallenger, _msDefender, _active!, _correctChallenger, _correctDefender);

    private void EndOutOfPrompts()
    {
        // Nothing left to ask: whoever has more time on their clock wins. On a tie the player on the clock loses.
        var loser = _msChallenger == _msDefender ? _active! : _msChallenger < _msDefender ? _challenger! : _defender!;
        EndDuel(loser, FloorEndReason.OutOfPrompts);
    }

    private void EndDuel(Player loser, FloorEndReason reason)
    {
        // The pictures queued up behind the one on screen were never shown, so they go back for a later duel. The one that was on
        // screen is spent, so nobody sees the same picture twice in a game.
        for (var i = _queue.Count - 1; i >= 0; i--) ReturnToPool(_queue[i]);
        _queue.Clear();
        _current = null;
        _revealUntil = null;
        _undo = null;
        _paused = false;
        _loser = loser;
        _endReason = reason;
        _duelsPlayed++;
        _phase = FloorPhase.TimeUp;
    }

    private void ApplyTransfer()
    {
        var loser = _loser!;
        var winner = loser == _challenger ? _defender! : _challenger!;
        var cells = new List<int>();
        for (var i = 0; i < _owner.Length; i++)
        {
            if (_owner[i] != loser) continue;
            _owner[i] = winner;
            cells.Add(i);
        }

        var gained = new List<string>();
        if (_inherit)
            foreach (var topic in loser.Topics)
                if (!winner.Topics.Contains(topic, StringComparer.OrdinalIgnoreCase))
                {
                    winner.Topics.Add(topic);
                    gained.Add(topic);
                }

        loser.Alive = false;
        _winner = winner;
        _takeover = new FloorTakeover(winner.Id, loser.Id, cells, gained);
    }

    private void EnterChoose(Player challenger)
    {
        _challenger = challenger;
        _defender = null;
        _repickPlayer = null;
        _finalDuel = false;
        _takeover = null;
        _loser = null;
        _endReason = null;

        var alive = _players.Where(p => p.Alive).ToList();
        if (alive.Count == 2)
        {
            // Two left: no choosing, just one last duel in the topics of whoever holds more of the floor.
            var other = alive.First(p => p != challenger);
            if (_owner.Count(o => o == challenger) > _owner.Count(o => o == other))
            {
                _defender = challenger;
                _challenger = other;
            }
            else
            {
                _defender = other;
            }
            _finalDuel = true;
            AfterDefenderChosen();
            return;
        }
        _phase = FloorPhase.Choose;
    }

    private void AfterDefenderChosen()
    {
        if (PromptsLeft(_defender!) < MinLivePrompts)
        {
            _repickPlayer = _defender;
            _phase = FloorPhase.Repick;
        }
        else
        {
            _phase = FloorPhase.Ready;
        }
    }

    private Result FinishRepick(Player player, string topic)
    {
        var error = CheckPick(topic, out var info);
        if (error is not null) return Result.Fail(error);

        List<FloorPrompt> prompts;
        try
        {
            prompts = _content.LoadPrompts(info!.Name).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail("Couldn't read that topic: " + ex.Message);
        }
        if (prompts.Count < MinLivePrompts) return Result.Fail($"{info.Name} is too small to finish a game with. Pick another.");

        Shuffle(prompts);
        _pools[info.Name] = prompts;
        _content.MarkUsed(info.Name);
        player.Topics.Add(info.Name);
        _repickPlayer = null;
        _phase = FloorPhase.Ready;
        return Result.Success;
    }

    /// <summary>Whether a topic can be picked right now. Null means yes.</summary>
    private string? CheckPick(string topic, out FloorTopicInfo? info)
    {
        info = _content.ListTopics().FirstOrDefault(t => string.Equals(t.Name, topic?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (info is null) return "That topic doesn't exist.";
        if (info.Total < _content.MinPrompts) return "That topic doesn't have enough in it yet.";
        if (info.Used) return "That topic was used in an earlier game.";
        if (HeldTopics().Contains(info.Name)) return "Someone already picked that topic.";
        return null;
    }

    private HashSet<string> HeldTopics() =>
        new(_players.SelectMany(p => p.Topics), StringComparer.OrdinalIgnoreCase);

    private List<Player> AdjacentOpponents(Player player)
    {
        var result = new List<Player>();
        if (_shape is null) return result;
        for (var i = 0; i < _owner.Length; i++)
        {
            if (_owner[i] != player) continue;
            foreach (var n in _shape.Neighbors(i))
                if (_owner[n] is { Alive: true } other && other != player && !result.Contains(other)) result.Add(other);
        }
        return result;
    }

    private void ResetToLobby()
    {
        _timer?.Dispose();
        _timer = null;
        foreach (var p in _players)
        {
            p.Topics = [];
            p.Alive = true;
        }
        _shape = null;
        _owner = [];
        _pools.Clear();
        _queue.Clear();
        _current = null;
        _active = _challenger = _defender = _repickPlayer = _loser = _winner = null;
        _takeover = null;
        _undo = null;
        _endReason = null;
        _revealUntil = null;
        _paused = false;
        _finalDuel = false;
        _duelsPlayed = 0;
        _phase = FloorPhase.Lobby;
    }

    // ---- prompts ----

    private int PoolCount(string topic) => _pools.TryGetValue(topic, out var pool) ? pool.Count : 0;

    private int PromptsLeft(Player player) => player.Topics.Sum(PoolCount);

    private void ReturnToPool(FloorPrompt prompt)
    {
        if (!_pools.TryGetValue(prompt.Topic, out var pool)) _pools[prompt.Topic] = pool = [];
        pool.Add(prompt);
    }

    /// <summary>One prompt from the defender's topics. A topic that has run out drops out of the mix.</summary>
    private FloorPrompt? Draw()
    {
        if (_defender is null) return null;
        var live = _defender.Topics.Where(t => PoolCount(t) > 0).ToList();
        if (live.Count == 0) return null;

        // The defender's own topic counts a little more than the ones they won.
        double Weight(string t) => t == _defender.Topics[0] ? HomeTopicWeight : 1.0;
        var roll = _rng.NextDouble() * live.Sum(Weight);
        var chosen = live[^1];
        foreach (var t in live)
        {
            roll -= Weight(t);
            if (roll < 0)
            {
                chosen = t;
                break;
            }
        }

        var pool = _pools[chosen];
        var prompt = pool[^1];
        pool.RemoveAt(pool.Count - 1);
        return prompt;
    }

    private void Refill()
    {
        while (_queue.Count < LookAhead && Draw() is { } prompt) _queue.Add(prompt);
    }

    private FloorPrompt? NextPrompt()
    {
        Refill();
        if (_queue.Count == 0) return null;
        var next = _queue[0];
        _queue.RemoveAt(0);
        Refill();
        return next;
    }

    private void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static bool TryCleanText(string? text, int max, out string clean)
    {
        var chars = (text ?? "").Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray();
        clean = string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > max) clean = clean[..max].Trim();
        return clean.Length > 0;
    }
}
