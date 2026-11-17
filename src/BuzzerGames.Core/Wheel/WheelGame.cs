using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.Wheel;

public enum WheelPhase
{
    Lobby,       // waiting for the emcee and three players
    Turn,        // the player up may spin, buy a vowel or solve
    Spinning,    // the wheel is turning; the room page calls FinishSpin when it has stopped
    Consonant,   // the wheel landed on cash: the player names a consonant, the emcee enters it and confirms
    Vowel,       // the player is buying a vowel: the emcee enters it and confirms
    Reveal,      // matching tiles are lit; the room page calls FinishReveal when the hand has touched them all
    Solving,     // the player is saying the answer; the emcee judges it
    PuzzleOver,  // the puzzle is done (solved or skipped); the emcee continues
    GameOver
}

public enum TileState { Hidden, Lit, Revealed }

public sealed record WheelPlayerInfo(Guid Id, string Name, string Color, int RoundBank, int Total, bool HoldsMillion, bool WonMillion);

/// <summary>A tile on the board. The letter is only filled in once it is revealed, so the TV can't leak it.</summary>
public sealed record BoardTileInfo(int Row, int Column, TileState State, char? Letter);

public sealed record WheelSnapshot(
    string Code,
    WheelPhase Phase,
    string? EmceeName,
    IReadOnlyList<WheelPlayerInfo> Players,
    int Active,                          // index of the player up, or -1 before the game starts
    int PuzzleNumber,
    int PuzzleCount,
    string Category,
    IReadOnlyList<BoardTileInfo> Board,
    string Answer,                       // SECRET: for the emcee's phone only, never the TV
    string CalledLetters,
    char? StagedLetter,                  // typed by the emcee, waiting for Confirm
    char? RevealLetter,                  // while tiles are lit: the letter they are about to show (already said aloud, so not a secret)
    IReadOnlyList<WheelSlice> Slices,
    double WheelAngle,
    SpinPlan? Spin,
    int SpinValue,                       // what each letter is worth on this turn
    string? Message,
    bool CanStart,
    bool CanSpin,
    bool CanBuyVowel,
    bool CanSolve,
    bool CanUndo,
    IReadOnlyList<Guid> Winners);

/// <summary>
/// One game of Wheel of Fortune: an emcee and three players (Red, Yellow, Blue) who spin from their phones while the emcee runs everything else.
/// This step covers the regular puzzles. Like the other games, this class knows nothing about web pages, and every public method takes one
/// lock and returns a Result instead of throwing.
///
/// Players say their letters out loud. The emcee types the letter, sees "Confirm T?", and only then does it count, so a typo can't cost anyone a turn.
/// Every emcee action can be undone, one step at a time.
/// </summary>
public sealed class WheelGame
{
    private sealed record Player(Guid Token, Guid Id, string Name);

    /// <summary>Everything that changes during play, so that Undo can put it all back by swapping one object.</summary>
    private sealed class State
    {
        public WheelPhase Phase = WheelPhase.Lobby;
        public int PuzzleNumber;
        public int Active = -1;
        public int[] RoundBank = new int[WheelRules.Players];
        public int[] Total = new int[WheelRules.Players];
        public int MillionHolder = -1;
        public bool MillionAvailable = true;
        public bool[] MillionWon = new bool[WheelRules.Players];
        public BoardLayout? Board;
        public string Category = "";
        public TileState[] Tiles = [];
        public HashSet<char> Called = [];
        public List<int> Lit = [];
        public char? Staged;
        public WheelLayout? Wheel;
        public double WheelAngle;
        public SpinPlan? Spin;
        public int SpinValue;
        public string? Message;
        public List<int> Winners = [];

        public State Clone()
        {
            var copy = (State)MemberwiseClone();
            copy.RoundBank = (int[])RoundBank.Clone();
            copy.Total = (int[])Total.Clone();
            copy.MillionWon = (bool[])MillionWon.Clone();
            copy.Tiles = (TileState[])Tiles.Clone();
            copy.Called = [.. Called];
            copy.Lit = [.. Lit];
            copy.Winners = [.. Winners];
            return copy;
        }
    }

    private const int HistoryLimit = 60;

    private readonly object _gate = new();
    private readonly IWheelContent _content;
    private readonly Random _rng;
    private readonly double _jitter;
    private readonly List<Player> _players = [];
    private readonly List<State> _history = [];
    private Guid? _emceeToken;
    private string? _emceeName;
    private WheelDeal? _deal;
    private List<BoardLayout> _boards = [];
    private State _s = new();

    public WheelGame(string code, IWheelContent content, Random? rng = null, double jitterDegrees = WheelRules.SpinJitterDegrees)
    {
        Code = code;
        _content = content;
        _rng = rng ?? new Random();
        _jitter = jitterDegrees;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string Code { get; }
    public DateTime CreatedAtUtc { get; }
    public event Action? Changed;

    // ---- The lobby -----------------------------------------------------------------------

    public JoinResult JoinEmcee(string name) => Run(() =>
    {
        if (_emceeToken is not null) return JoinResult.Fail("This game already has an emcee.");
        if (!TryCleanText(name, WheelRules.NameMaxLength, out var clean)) return JoinResult.Fail("Please enter a name.");
        _emceeToken = Guid.NewGuid();
        _emceeName = clean;
        return JoinResult.Success(_emceeToken.Value);
    });

    public JoinResult JoinPlayer(string name) => Run(() =>
    {
        if (_s.Phase != WheelPhase.Lobby) return JoinResult.Fail("The game has already started.");
        if (_players.Count >= WheelRules.Players) return JoinResult.Fail("The table is full.");
        if (!TryCleanText(name, WheelRules.NameMaxLength, out var clean)) return JoinResult.Fail("Please enter a name.");
        if (_players.Any(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase))) return JoinResult.Fail("Someone already has that name.");
        var player = new Player(Guid.NewGuid(), Guid.NewGuid(), clean);
        _players.Add(player);
        return JoinResult.Success(player.Token);
    });

    public Result Start(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can start the game.");
        if (_s.Phase != WheelPhase.Lobby) return Result.Fail("The game has already started.");
        if (_players.Count < WheelRules.Players) return Result.Fail($"The game needs {WheelRules.Players} players.");

        if (!_content.TryCreateDeal(_rng, out var deal, out var error)) return Result.Fail(error);
        var boards = new List<BoardLayout>();
        foreach (var puzzle in deal.Regular)
        {
            if (!WheelBoard.TryLayout(puzzle.Text, out var layout, out var problem))
                return Result.Fail($"\"{puzzle.Text}\" can't be shown on the board: {problem}");
            boards.Add(layout);
        }

        _deal = deal;
        _boards = boards;
        _content.MarkUsed(deal.Regular.Select(p => p.Id));
        StartPuzzle(1);
        return Result.Success;
    });

    // ---- The turn ------------------------------------------------------------------------

    /// <summary>
    /// The player up spins, from their own phone (or the emcee does it for them if their phone has died). <paramref name="power"/> is the slider,
    /// 0 to <see cref="WheelRules.PowerMax"/>. The wheel's landing is worked out now; the room page plays it back and then calls <see cref="FinishSpin"/>.
    /// </summary>
    public Result Spin(Guid token, int power) => Run(() =>
    {
        if (!IsEmcee(token) && !IsActivePlayer(token)) return Result.Fail("It isn't your turn to spin.");
        if (_s.Phase != WheelPhase.Turn) return Result.Fail("You can't spin right now.");
        if (HiddenLetters(c => !IsVowel(c)) == 0) return Result.Fail("There are no consonants left. Buy a vowel or solve the puzzle.");
        if (power is < 0 or > WheelRules.PowerMax) return Result.Fail($"The spin power goes from 0 to {WheelRules.PowerMax}.");

        Remember();
        var jitter = _jitter == 0 ? 0 : (_rng.NextDouble() * 2 - 1) * _jitter;
        var plan = WheelPhysics.Plan(_s.Wheel!, _s.WheelAngle, WheelRules.ClickerAngles[_s.Active], power, jitter);
        _s.Spin = plan;
        _s.WheelAngle = plan.SettledAngle;
        _s.Phase = WheelPhase.Spinning;
        _s.Message = $"{ActiveName()} spins...";
        return Result.Success;
    });

    /// <summary>The wheel has stopped (the room page calls this after the spin's duration). Applies what it landed on.</summary>
    public Result FinishSpin() => Run(() =>
    {
        if (_s.Phase != WheelPhase.Spinning) return Result.Fail("The wheel isn't spinning.");
        var landed = _s.Spin!.Landed;
        var name = ActiveName();

        switch (landed.Kind)
        {
            case SliceKind.Cash:
                _s.SpinValue = landed.Value;
                _s.Phase = WheelPhase.Consonant;
                _s.Message = $"{name} landed on ${landed.Value:N0}. Call a consonant.";
                break;

            case SliceKind.Million:
                _s.SpinValue = landed.Value;
                _s.Phase = WheelPhase.Consonant;
                // Picking it up is permanent for this puzzle: the wedge turns into a plain Bankrupt and never comes back.
                _s.MillionAvailable = false;
                _s.MillionHolder = _s.Active;
                _s.Wheel = _s.Wheel!.WithMillionCollected();
                _s.Message = $"{name} picked up the MILLION DOLLAR WEDGE! ${landed.Value:N0} a letter. Call a consonant.";
                break;

            case SliceKind.Bankrupt:
                var lostMillion = _s.MillionHolder == _s.Active;
                _s.RoundBank[_s.Active] = 0;
                if (lostMillion)
                {
                    _s.MillionHolder = -1; // gone for good: the wedge is already a plain Bankrupt on the wheel
                }
                PassTurn();
                _s.Phase = WheelPhase.Turn;
                _s.Message = $"BANKRUPT! {name} loses the round's money{(lostMillion ? " and the Million Dollar Wedge" : "")}. {ActiveName()} is up.";
                break;

            case SliceKind.LoseATurn:
                PassTurn();
                _s.Phase = WheelPhase.Turn;
                _s.Message = $"Lose a turn. {name} passes to {ActiveName()}.";
                break;
        }
        return Result.Success;
    });

    /// <summary>The player up wants to buy a vowel (it costs <see cref="WheelRules.VowelCost"/> from this round's money). The emcee goes on to enter the letter.</summary>
    public Result BuyVowel(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase != WheelPhase.Turn) return Result.Fail("You can't buy a vowel right now.");
        if (_s.RoundBank[_s.Active] < WheelRules.VowelCost) return Result.Fail($"{ActiveName()} needs ${WheelRules.VowelCost} this round to buy a vowel.");
        if (HiddenLetters(IsVowel) == 0) return Result.Fail("There are no vowels left in the puzzle.");

        Remember();
        _s.Phase = WheelPhase.Vowel;
        _s.Staged = null;
        _s.Message = $"{ActiveName()} buys a vowel for ${WheelRules.VowelCost}. Which one?";
        return Result.Success;
    });

    /// <summary>The emcee types the letter the player said out loud. Nothing happens until <see cref="ConfirmLetter"/>.</summary>
    public Result StageLetter(Guid emceeToken, char letter) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase is not (WheelPhase.Consonant or WheelPhase.Vowel)) return Result.Fail("It isn't time to call a letter.");
        var c = char.ToUpperInvariant(letter);
        if (c is < 'A' or > 'Z') return Result.Fail("Letters only.");
        if (_s.Called.Contains(c)) return Result.Fail($"{c} has already been called.");
        if (_s.Phase == WheelPhase.Consonant && IsVowel(c)) return Result.Fail("That's a vowel. Call a consonant; vowels are bought before a spin.");
        if (_s.Phase == WheelPhase.Vowel && !IsVowel(c)) return Result.Fail("A bought letter must be A, E, I, O or U.");

        _s.Staged = c;
        _s.Message = $"Confirm {c}?";
        return Result.Success;
    });

    /// <summary>Takes back a typed letter. If none is typed and a vowel was being bought, that purchase is called off (nothing was charged).</summary>
    public Result CancelLetter(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Staged is not null)
        {
            _s.Staged = null;
            _s.Message = _s.Phase == WheelPhase.Vowel ? $"Which vowel?" : $"{ActiveName()}: call a consonant.";
            return Result.Success;
        }
        if (_s.Phase == WheelPhase.Vowel)
        {
            _s.Phase = WheelPhase.Turn;
            _s.Message = $"{ActiveName()}: spin, buy a vowel, or solve.";
            return Result.Success;
        }
        return Result.Fail("There's nothing to cancel.");
    });

    /// <summary>The emcee confirms the typed letter. If it is in the puzzle the tiles light up; if not, the turn passes.</summary>
    public Result ConfirmLetter(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase is not (WheelPhase.Consonant or WheelPhase.Vowel)) return Result.Fail("It isn't time to call a letter.");
        if (_s.Staged is not { } letter) return Result.Fail("Type the letter first.");

        Remember();
        var buying = _s.Phase == WheelPhase.Vowel;
        var name = ActiveName();
        _s.Staged = null;
        _s.Called.Add(letter);
        if (buying) _s.RoundBank[_s.Active] -= WheelRules.VowelCost;

        var hits = new List<int>();
        for (var i = 0; i < _s.Board!.Tiles.Count; i++)
            if (_s.Board.Tiles[i].Letter == letter && _s.Tiles[i] == TileState.Hidden) hits.Add(i);

        if (hits.Count == 0)
        {
            PassTurn();
            _s.Phase = WheelPhase.Turn;
            _s.Message = $"No {letter} in the puzzle. {ActiveName()} is up.";
            return Result.Success;
        }

        if (!buying) _s.RoundBank[_s.Active] += _s.SpinValue * hits.Count;
        foreach (var i in hits) _s.Tiles[i] = TileState.Lit;
        _s.Lit = hits;
        _s.Phase = WheelPhase.Reveal;
        _s.Message = $"{hits.Count} {letter}{(hits.Count == 1 ? "" : "'s")}!";
        return Result.Success;
    });

    /// <summary>The hand has touched every lit tile (the room page calls this when its animation ends). The lit tiles show their letters.</summary>
    public Result FinishReveal() => Run(() =>
    {
        if (_s.Phase != WheelPhase.Reveal) return Result.Fail("Nothing is being revealed.");
        foreach (var i in _s.Lit) _s.Tiles[i] = TileState.Revealed;
        _s.Lit = [];
        _s.Phase = WheelPhase.Turn;
        _s.Message = HiddenLetters(_ => true) == 0
            ? $"The board is full. {ActiveName()}, solve it!"
            : $"{ActiveName()}: spin, buy a vowel, or solve.";
        return Result.Success;
    });

    // ---- Solving -------------------------------------------------------------------------

    /// <summary>The player up says they want to solve. The emcee hears the answer and judges it next.</summary>
    public Result BeginSolve(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase != WheelPhase.Turn) return Result.Fail("You can't solve right now.");
        Remember();
        _s.Phase = WheelPhase.Solving;
        _s.Message = $"{ActiveName()} is solving.";
        return Result.Success;
    });

    /// <summary>Takes back "I'd like to solve" (a mis-tap, or the player changed their mind).</summary>
    public Result CancelSolve(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase != WheelPhase.Solving) return Result.Fail("Nobody is solving.");
        _s.Phase = WheelPhase.Turn;
        _s.Message = $"{ActiveName()}: spin, buy a vowel, or solve.";
        return Result.Success;
    });

    /// <summary>
    /// The emcee's ruling on the answer. Right: the whole board shows at once (no slow reveal), the solver banks this round's money, and
    /// holding the Million Dollar Wedge now counts for good. Wrong: the turn passes.
    /// </summary>
    public Result JudgeSolve(Guid emceeToken, bool correct) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase != WheelPhase.Solving) return Result.Fail("Nobody is solving.");

        Remember();
        var name = ActiveName();
        if (!correct)
        {
            PassTurn();
            _s.Phase = WheelPhase.Turn;
            _s.Message = $"Sorry, {name}, that's not it. {ActiveName()} is up.";
            return Result.Success;
        }

        var won = _s.RoundBank[_s.Active];
        _s.Total[_s.Active] += won;
        var million = _s.MillionHolder == _s.Active;
        if (million) _s.MillionWon[_s.Active] = true;
        RevealEverything();
        _s.Phase = WheelPhase.PuzzleOver;
        _s.Message = $"{name} solved it and banks ${won:N0}{(million ? " and keeps the Million Dollar Wedge!" : ".")}";
        ClearRound();
        return Result.Success;
    });

    /// <summary>Nobody can or will solve it: show the answer, bank nothing, and move on.</summary>
    public Result SkipPuzzle(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase is not (WheelPhase.Turn or WheelPhase.Consonant or WheelPhase.Vowel or WheelPhase.Solving)) return Result.Fail("You can't skip the puzzle right now.");
        Remember();
        RevealEverything();
        _s.Phase = WheelPhase.PuzzleOver;
        _s.Message = "Nobody solved it. No money changes hands.";
        ClearRound();
        return Result.Success;
    });

    /// <summary>On to the next puzzle, or to the end of the game after the last one.</summary>
    public Result Continue(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase != WheelPhase.PuzzleOver) return Result.Fail("The puzzle isn't over yet.");
        Remember();
        if (_s.PuzzleNumber >= WheelRules.RegularPuzzles) EndGame();
        else StartPuzzle(_s.PuzzleNumber + 1);
        return Result.Success;
    });

    /// <summary>Steps back one emcee action. Not while the wheel is turning or tiles are being revealed; wait a moment for those.</summary>
    public Result Undo(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase is WheelPhase.Spinning or WheelPhase.Reveal) return Result.Fail("Wait for the wheel or the reveal to finish first.");
        if (_history.Count == 0) return Result.Fail("There's nothing to undo.");
        _s = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        return Result.Success;
    });

    // ---- Seats and snapshots -------------------------------------------------------------

    public Seat GetSeat(Guid token)
    {
        lock (_gate)
        {
            if (_emceeToken == token) return new Seat(Role.Emcee);
            var player = _players.FirstOrDefault(p => p.Token == token);
            return player is null ? new Seat(Role.Spectator) : new Seat(Role.Player, player.Id);
        }
    }

    public WheelSnapshot Snapshot()
    {
        lock (_gate)
        {
            var s = _s;
            var players = _players.Select((p, i) => new WheelPlayerInfo(
                p.Id, p.Name, WheelRules.PlayerColors[i], s.RoundBank[i], s.Total[i], s.MillionHolder == i, s.MillionWon[i])).ToList();

            var board = s.Board is null
                ? []
                : s.Board.Tiles.Select((t, i) => new BoardTileInfo(t.Row, t.Column, s.Tiles[i], s.Tiles[i] == TileState.Revealed ? t.Letter : null)).ToList();

            var inPlay = s.Phase != WheelPhase.Lobby;
            var onTurn = s.Phase == WheelPhase.Turn;
            return new WheelSnapshot(
                Code, s.Phase, _emceeName, players, s.Active, s.PuzzleNumber, WheelRules.RegularPuzzles, s.Category,
                board, s.Board?.Answer ?? "", string.Concat(s.Called.OrderBy(c => c)), s.Staged,
                s.Phase == WheelPhase.Reveal && s.Lit.Count > 0 ? s.Board!.Tiles[s.Lit[0]].Letter : null,
                s.Wheel?.Slices ?? [], s.WheelAngle, s.Spin, s.SpinValue, s.Message,
                CanStart: s.Phase == WheelPhase.Lobby && _emceeToken is not null && _players.Count == WheelRules.Players,
                CanSpin: onTurn && HiddenLetters(c => !IsVowel(c)) > 0,
                CanBuyVowel: onTurn && s.RoundBank[s.Active] >= WheelRules.VowelCost && HiddenLetters(IsVowel) > 0,
                CanSolve: onTurn,
                CanUndo: inPlay && s.Phase is not (WheelPhase.Spinning or WheelPhase.Reveal) && _history.Count > 0,
                Winners: s.Winners.Select(i => _players[i].Id).ToList());
        }
    }

    // ---- Internals -----------------------------------------------------------------------

    private void StartPuzzle(int number)
    {
        var s = _s;
        s.PuzzleNumber = number;
        s.Board = _boards[number - 1];
        s.Category = _deal!.Regular[number - 1].Category;
        s.Tiles = s.Board.Tiles.Select(t => t.IsLetter ? TileState.Hidden : TileState.Revealed).ToArray();
        s.Called = [];
        s.Lit = [];
        s.Staged = null;
        s.Spin = null;
        s.SpinValue = 0;
        s.RoundBank = new int[WheelRules.Players];
        s.MillionHolder = -1;
        s.MillionAvailable = true;
        s.Active = (number - 1) % WheelRules.Players;
        s.Wheel = WheelLayout.Create(_rng);

        // Start the wheel at rest with the first player's clicker in the middle of some slice.
        var rest = s.Wheel.Slices[_rng.Next(s.Wheel.Slices.Count)];
        s.WheelAngle = WheelRules.ClickerAngles[s.Active] - rest.Mid;

        s.Phase = WheelPhase.Turn;
        s.Message = $"Puzzle {number} of {WheelRules.RegularPuzzles}. The category is {s.Category}. {ActiveName()} is up.";
    }

    private void EndGame()
    {
        var best = _s.Total.Max();
        _s.Winners = Enumerable.Range(0, WheelRules.Players).Where(i => _s.Total[i] == best).ToList();
        _s.Phase = WheelPhase.GameOver;
        var names = string.Join(" and ", _s.Winners.Select(i => _players[i].Name));
        var wedge = _s.Winners.Any(i => _s.MillionWon[i]) ? " The Million Dollar Wedge is in the winner's hands." : "";
        _s.Message = _s.Winners.Count == 1
            ? $"{names} wins with ${best:N0}!{wedge}"
            : $"{names} tie with ${best:N0}!{wedge}";
    }

    private void RevealEverything()
    {
        for (var i = 0; i < _s.Tiles.Length; i++) _s.Tiles[i] = TileState.Revealed;
        _s.Lit = [];
        _s.Staged = null;
    }

    /// <summary>The puzzle is over: nobody keeps this round's money (the solver's was banked first) and an unclaimed wedge goes back.</summary>
    private void ClearRound()
    {
        _s.RoundBank = new int[WheelRules.Players];
        _s.MillionHolder = -1;
    }

    private void PassTurn()
    {
        _s.Active = (_s.Active + 1) % WheelRules.Players;
        _s.SpinValue = 0;
    }

    private void Remember()
    {
        _history.Add(_s.Clone());
        if (_history.Count > HistoryLimit) _history.RemoveAt(0);
    }

    private int HiddenLetters(Func<char, bool> wanted)
    {
        if (_s.Board is null) return 0;
        var n = 0;
        for (var i = 0; i < _s.Board.Tiles.Count; i++)
            if (_s.Tiles[i] == TileState.Hidden && _s.Board.Tiles[i].IsLetter && wanted(_s.Board.Tiles[i].Letter)) n++;
        return n;
    }

    private static bool IsVowel(char c) => c is 'A' or 'E' or 'I' or 'O' or 'U';

    private string ActiveName() => _s.Active >= 0 ? _players[_s.Active].Name : "";

    private bool IsEmcee(Guid token) => _emceeToken is not null && _emceeToken == token;

    private bool IsActivePlayer(Guid token) => _s.Active >= 0 && _players[_s.Active].Token == token;

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

    private static bool TryCleanText(string? text, int max, out string clean)
    {
        var chars = (text ?? "").Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray();
        clean = string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > max) clean = clean[..max].Trim();
        return clean.Length > 0;
    }
}
