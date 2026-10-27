using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.Password;

/// <summary>
/// One Password Plus game: two teams of two, one emcee, and puzzles worth more and more points until a team
/// reaches 500.
///
///   A puzzle is a mystery "big word" and five password words that each hint at it. Each password is a "word"
///   (the unit that is played): one partner gives one-word clues, the other guesses.
///
///   The team that most recently got a right ruling has control (a coin flip decides the first word) and chooses
///   to play (their clue-giver gives the first clue) or pass. Clues then alternate between the teams, each clue
///   followed by one guess from that team's guesser. Each side gets at most 4 clues. If all 8 clues miss, the word
///   is posted on the board and nobody may guess the big word.
///
///   When a guesser gets the word, that guesser (and only that guesser) gets one shot at the big word. The
///   exception is the fifth word: the guesser tries first, then their clue-giver, then the other team's guesser,
///   then the other team's clue-giver. A right guess scores the puzzle's value for that player's team. Whenever a
///   puzzle ends, any words not yet played are shown.
///
///   The roles swap after every word: on the first word player 1 gives and player 2 guesses, on the second word
///   player 2 gives and player 1 guesses, and so on, carrying on across puzzles.
///
///   Puzzles are worth 100, 100, 200, 200, 300, 300, then 400 each. The first team to reach 500 wins (and would go
///   on to the bonus round).
///
/// Like Jeopardy and Press Your Luck, this class knows nothing about web pages, and every public method takes one lock.
/// </summary>
public sealed class PasswordGame
{
    public const int Teams = 2;
    public const int TeamSize = 2;
    public const int PlayerSeats = Teams * TeamSize;
    public const int NameMaxLength = 20;
    public const int ClueMaxLength = 24;
    public const int WordsPerPuzzle = 5;
    public const int CluesPerSide = 4;
    public const int WinScore = 500;

    /// <summary>The most puzzles one game can use. A game can end sooner; it never needs more than this.</summary>
    public const int PuzzlesPerGame = 12;

    public static readonly IReadOnlyList<string> TeamNames = ["Team A", "Team B"];

    /// <summary>What a puzzle is worth: 100, 100, 200, 200, 300, 300, then 400 from then on. Index 0 is the first puzzle.</summary>
    public static int PuzzleValue(int puzzleIndex) => Math.Min(100 * (puzzleIndex / 2 + 1), 400);

    // ---- Private state -------------------------------------------------------------------

    private sealed class PlayerState
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid Token { get; } = Guid.NewGuid();
        public required string Name { get; init; }
        public required int Team { get; init; }
        public required int Slot { get; init; }
    }

    private sealed class ClueEntry(int team, string text)
    {
        public int Team { get; } = team;
        public string Text { get; } = text;
        public ClueOutcome Outcome { get; set; } = ClueOutcome.Pending;
    }

    private readonly object _gate = new();
    private readonly IPasswordPuzzleSource _source;
    private readonly Random _rng;

    private readonly List<PlayerState> _players = [];
    private Guid? _emceeToken;
    private string? _emceeName;
    private PasswordPhase _phase = PasswordPhase.Lobby;
    private string? _message;

    private readonly int[] _scores = new int[Teams];
    private IReadOnlyList<PasswordPuzzle> _puzzles = [];
    private int _puzzleIndex;
    private int _wordIndex;     // 0 to 4, within the puzzle
    private int _lastSolverTeam = -1; // the team that most recently guessed a word in this puzzle
    private readonly WordState[] _wordStates = new WordState[WordsPerPuzzle];
    private bool _bigWordRevealed;

    private int _controlTeam;
    private int _turnTeam = -1;
    private readonly List<ClueEntry> _clues = [];

    private readonly List<Guid> _attempts = [];
    private int _attemptPos;

    private List<int> _winners = [];

    // ---- Public surface ------------------------------------------------------------------

    public PasswordGame(string code, IPasswordPuzzleSource source, Random? rng = null)
    {
        Code = code;
        _source = source;
        _rng = rng ?? new Random();
    }

    public string Code { get; }

    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;

    /// <summary>Raised after every successful change. Pages listen to this and redraw.</summary>
    public event Action? Changed;

    // ---- Lobby ---------------------------------------------------------------------------

    public JoinResult JoinEmcee(string name) => Run(() =>
    {
        if (_phase != PasswordPhase.Lobby) return JoinResult.Fail("The game has already started.");
        if (_emceeToken is not null) return JoinResult.Fail("This game already has an emcee.");
        if (!TryCleanName(name, out var clean)) return JoinResult.Fail("Please enter a name.");

        _emceeToken = Guid.NewGuid();
        _emceeName = clean;
        return JoinResult.Success(_emceeToken.Value);
    });

    /// <summary>Takes a seat on team 0 (Team A) or team 1 (Team B). The first person on a team is player 1.</summary>
    public JoinResult JoinPlayer(string name, int team) => Run(() =>
    {
        if (_phase != PasswordPhase.Lobby) return JoinResult.Fail("The game has already started.");
        if (team is < 0 or >= Teams) return JoinResult.Fail("Pick Team A or Team B.");
        var onTeam = _players.Count(p => p.Team == team);
        if (onTeam >= TeamSize) return JoinResult.Fail($"{TeamNames[team]} is full.");
        if (!TryCleanName(name, out var clean)) return JoinResult.Fail("Please enter a name.");
        if (_players.Any(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase)))
            return JoinResult.Fail("Someone is already using that name.");

        var player = new PlayerState { Name = clean, Team = team, Slot = onTeam };
        _players.Add(player);
        return JoinResult.Success(player.Token);
    });

    public Result Start() => Run(() =>
    {
        if (_phase != PasswordPhase.Lobby) return Result.Fail("The game has already started.");
        if (!IsFull()) return Result.Fail("We need 1 emcee and 2 players on each team first.");

        BeginGame();
        return Result.Success;
    });

    /// <summary>
    /// After the game is over the emcee can play again. With samePlayers, everyone keeps their seat and a fresh game
    /// starts at once. Without it, the players are cleared and the room goes back to the lobby with the same room
    /// code and the same emcee, so four new people can take the seats.
    /// </summary>
    public Result Restart(Guid emceeToken, bool samePlayers) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PasswordPhase.GameOver) return Result.Fail("The game isn't over yet.");

        ClearGameState();
        if (samePlayers)
        {
            BeginGame();
        }
        else
        {
            _players.Clear();
            _phase = PasswordPhase.Lobby;
        }
        return Result.Success;
    });

    private void BeginGame()
    {
        _puzzles = _source.CreatePuzzles(_rng, PuzzlesPerGame);
        if (_puzzles.Count < 1) throw new InvalidOperationException("The puzzle source must supply at least one puzzle.");

        Array.Clear(_scores);
        _puzzleIndex = 0;
        _wordIndex = 0;
        _lastSolverTeam = -1;
        _winners = [];
        _controlTeam = _rng.Next(Teams);
        StartWord($"{TeamNames[_controlTeam]} won the coin flip.");
    }

    // Forgets everything about the game that just finished (but not who is in the room).
    private void ClearGameState()
    {
        _message = null;
        Array.Clear(_scores);
        _puzzles = [];
        _puzzleIndex = 0;
        _wordIndex = 0;
        _lastSolverTeam = -1;
        Array.Clear(_wordStates);
        _bigWordRevealed = false;
        _turnTeam = -1;
        _clues.Clear();
        _attempts.Clear();
        _attemptPos = 0;
        _winners = [];
    }

    // ---- One word ------------------------------------------------------------------------

    /// <summary>The team with control chooses: play (give the first clue) or pass (the other team does).</summary>
    public Result ChooseControl(Guid token, bool play) => Run(() =>
    {
        if (_phase != PasswordPhase.Control) return Result.Fail("There's no choice to make right now.");
        if (!IsEmcee(token))
        {
            var player = FindPlayer(token);
            if (player is null) return Result.Fail("Only the team with control can choose.");
            if (player.Team != _controlTeam)
                return Result.Fail($"{TeamNames[_controlTeam]} has control, so only they can choose.");
        }

        _turnTeam = play ? _controlTeam : 1 - _controlTeam;
        _message = null;
        _phase = PasswordPhase.Clue;
        return Result.Success;
    });

    /// <summary>
    /// The clue-giver on the clock sends a one-word clue. The emcee, not the computer, judges whether it is legal.
    /// The emcee may also send it, as a safety net for a dead phone. The emcee can still throw a clue out afterwards with <see cref="RejectClue"/>.
    /// </summary>
    public Result SubmitClue(Guid token, string? text) => Run(() =>
    {
        if (_phase != PasswordPhase.Clue) return Result.Fail("It isn't time for a clue.");
        var giver = PlayerAt(_turnTeam, GiverSlot);
        if (!IsEmcee(token) && giver.Token != token)
            return Result.Fail($"{giver.Name} is giving the clues for {TeamNames[_turnTeam]} on this word.");
        if (!TryCleanClue(text, out var clean, out var error))
            return Result.Fail(error);

        _clues.Add(new ClueEntry(_turnTeam, clean));
        _phase = PasswordPhase.Guess;
        return Result.Success;
    });

    /// <summary>The emcee rules on what the guesser said out loud after a clue.</summary>
    public Result JudgeGuess(Guid emceeToken, bool correct) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can judge guesses.");
        if (_phase != PasswordPhase.Guess) return Result.Fail("Nobody is guessing right now.");

        var clue = _clues[^1];
        if (!correct)
        {
            clue.Outcome = ClueOutcome.Miss;
            AfterMiss();
            return Result.Success;
        }

        clue.Outcome = ClueOutcome.Hit;
        _wordStates[_wordIndex] = WordState.Solved;
        _controlTeam = clue.Team;
        _lastSolverTeam = clue.Team;
        BeginBigWordGuess(clue.Team, null);
        return Result.Success;
    });

    /// <summary>
    /// The emcee throws out the clue that was just given (more than one word, part of the answer, a made-up word...). It still counts as that side's clue, nobody guesses, and the turn passes.
    /// </summary>
    public Result RejectClue(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can throw out a clue.");
        if (_phase != PasswordPhase.Guess) return Result.Fail("There's no clue to throw out.");

        _clues[^1].Outcome = ClueOutcome.Rejected;
        AfterMiss();
        return Result.Success;
    });

    // A clue was wrong or thrown out: the other team is next, unless all eight clues are used up.
    private void AfterMiss()
    {
        if (_clues.Count >= Teams * CluesPerSide)
        {
            _wordStates[_wordIndex] = WordState.Posted;
            var word = CurrentPuzzle.Words[_wordIndex];
            if (_wordIndex == WordsPerPuzzle - 1)
            {
                // Nobody got the last word. The last team to guess a word in this puzzle gets the first shot at the big word.
                if (_lastSolverTeam >= 0)
                {
                    var guesserName = PlayerAt(_lastSolverTeam, 1 - GiverSlot).Name;
                    BeginBigWordGuess(_lastSolverTeam,
                        $"Nobody got \"{word}\". {guesserName} was the last to guess a word, so they get the first shot at the big word.");
                }
                else
                {
                    EndPuzzle($"Nobody got a single word, so there's no shot at the big word. No points for this puzzle.");
                }
            }
            else
            {
                _turnTeam = -1;
                _message = $"Nobody got it. The word was \"{word}\", and it goes up on the board.";
                _phase = PasswordPhase.WordResult;
            }
            return;
        }

        _turnTeam = 1 - _turnTeam;
        _phase = PasswordPhase.Clue;
    }

    // ---- The big word --------------------------------------------------------------------

    private void BeginBigWordGuess(int team, string? message)
    {
        var giverSlot = GiverSlot;
        var guesserSlot = 1 - giverSlot;
        var guesser = PlayerAt(team, guesserSlot);

        _attempts.Clear();
        _attempts.Add(guesser.Id);
        if (_wordIndex == WordsPerPuzzle - 1)
        {
            // The last word: their clue-giver, then the other team's guesser, then the other team's clue-giver.
            _attempts.Add(PlayerAt(team, giverSlot).Id);
            _attempts.Add(PlayerAt(1 - team, guesserSlot).Id);
            _attempts.Add(PlayerAt(1 - team, giverSlot).Id);
        }
        _attemptPos = 0;
        _turnTeam = team;
        _message = message ?? $"{guesser.Name} got \"{CurrentPuzzle.Words[_wordIndex]}\"! Now a guess at the big word.";
        _phase = PasswordPhase.PuzzleGuess;
    }

    /// <summary>The emcee rules on a guess at the big word.</summary>
    public Result JudgePuzzleGuess(Guid emceeToken, bool correct) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can judge guesses.");
        if (_phase != PasswordPhase.PuzzleGuess) return Result.Fail("Nobody is guessing the big word right now.");

        var who = _players.First(p => p.Id == _attempts[_attemptPos]);

        if (correct)
        {
            var value = PuzzleValue(_puzzleIndex);
            _scores[who.Team] += value;
            _controlTeam = who.Team;
            EndPuzzle($"{who.Name} solved it! {TeamNames[who.Team]} scores {value}.");
            return Result.Success;
        }

        _attemptPos++;
        if (_attemptPos < _attempts.Count)
        {
            _turnTeam = _players.First(p => p.Id == _attempts[_attemptPos]).Team;
            _message = $"{who.Name} missed. Next up: {_players.First(p => p.Id == _attempts[_attemptPos]).Name}.";
            return Result.Success;
        }

        if (_wordIndex == WordsPerPuzzle - 1)
        {
            EndPuzzle("Nobody got the big word. No points for this puzzle.");
        }
        else
        {
            _turnTeam = -1;
            _message = $"{who.Name} missed. The big word stays secret, and the puzzle carries on.";
            _phase = PasswordPhase.WordResult;
        }
        return Result.Success;
    });

    // The puzzle is over, solved or not: put every word on the board and show the big word.
    private void EndPuzzle(string message)
    {
        for (var i = 0; i < WordsPerPuzzle; i++)
            if (_wordStates[i] == WordState.Waiting) _wordStates[i] = WordState.Posted;
        _bigWordRevealed = true;
        _turnTeam = -1;
        _message = message;
        _phase = PasswordPhase.PuzzleResult;
    }

    // ---- Moving on -----------------------------------------------------------------------

    /// <summary>The emcee moves on from a result screen: to the next word, the next puzzle, or the end of the game.</summary>
    public Result Continue(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can continue.");

        switch (_phase)
        {
            case PasswordPhase.WordResult:
                _wordIndex++;
                StartWord(null);
                return Result.Success;

            case PasswordPhase.PuzzleResult:
                var best = _scores.Max();
                if (best >= WinScore)
                {
                    FinishGame(_scores.Select((s, i) => (s, i)).Where(x => x.s == best).Select(x => x.i).ToList(),
                        "goes to the bonus round!");
                    return Result.Success;
                }
                if (_puzzleIndex + 1 >= _puzzles.Count)
                {
                    // Out of puzzles: the higher score wins.
                    FinishGame(_scores.Select((s, i) => (s, i)).Where(x => x.s == best).Select(x => x.i).ToList(),
                        "wins on points!");
                    return Result.Success;
                }
                _puzzleIndex++;
                _wordIndex = 0;
                StartWord(null);
                return Result.Success;

            default:
                return Result.Fail("There's nothing to continue from.");
        }
    });

    private void FinishGame(List<int> winners, string ending)
    {
        _winners = winners;
        _turnTeam = -1;
        _message = winners.Count == 1
            ? $"{TeamNames[winners[0]]} {ending}"
            : "It's a tie!";
        _phase = PasswordPhase.GameOver;
    }

    private void StartWord(string? message)
    {
        if (_wordIndex == 0)
        {
            Array.Clear(_wordStates);
            _lastSolverTeam = -1;
            _bigWordRevealed = false;
        }
        _wordStates[_wordIndex] = WordState.Current;
        _clues.Clear();
        _attempts.Clear();
        _attemptPos = 0;
        _turnTeam = -1;
        _message = message;
        _phase = PasswordPhase.Control;
    }

    // ---- Seats and snapshots -------------------------------------------------------------

    public Seat GetSeat(Guid token)
    {
        lock (_gate)
        {
            if (_emceeToken == token) return new Seat(Role.Emcee);
            var player = FindPlayer(token);
            return player is null ? new Seat(Role.Spectator) : new Seat(Role.Player, player.Id);
        }
    }

    public PasswordSnapshot Snapshot()
    {
        lock (_gate)
        {
            var inGame = _phase != PasswordPhase.Lobby && _puzzles.Count > 0;
            var playing = inGame && _phase != PasswordPhase.GameOver;
            var puzzle = inGame ? _puzzles[_puzzleIndex] : null;
            var onClock = _phase is PasswordPhase.Clue or PasswordPhase.Guess or PasswordPhase.PuzzleGuess;
            var attempting = _phase == PasswordPhase.PuzzleGuess ? _attempts[_attemptPos] : (Guid?)null;

            var players = _players
                .OrderBy(p => p.Team).ThenBy(p => p.Slot)
                .Select(p => new PasswordPlayerInfo(
                    p.Id, p.Name, p.Team, p.Slot,
                    IsGiver: playing && p.Slot == GiverSlot,
                    IsGuesser: playing && p.Slot != GiverSlot,
                    OnClock: onClock && p.Team == _turnTeam,
                    IsAttempting: p.Id == attempting))
                .ToList();

            var cluesUsed = new int[Teams];
            foreach (var c in _clues) cluesUsed[c.Team]++;

            var teams = Enumerable.Range(0, Teams)
                .Select(t => new PasswordTeamInfo(
                    t, TeamNames[t], _scores[t],
                    HasControl: playing && t == _controlTeam,
                    OnClock: onClock && t == _turnTeam,
                    CluesLeft: CluesPerSide - cluesUsed[t],
                    PlayerIds: _players.Where(p => p.Team == t).OrderBy(p => p.Slot).Select(p => p.Id).ToList()))
                .ToList();

            return new PasswordSnapshot(
                Code: Code,
                Phase: _phase,
                PuzzleNumber: inGame ? _puzzleIndex + 1 : 0,
                PuzzleValue: inGame ? PuzzleValue(_puzzleIndex) : 0,
                WordNumber: playing ? _wordIndex + 1 : 0,
                WordsPerPuzzle: WordsPerPuzzle,
                WinScore: WinScore,
                EmceeName: _emceeName,
                Players: players,
                Teams: teams,
                ControlTeam: playing ? _controlTeam : -1,
                TurnTeam: _turnTeam,
                PuzzleKind: puzzle?.Kind ?? "",
                Words: puzzle is null
                    ? []
                    : puzzle.Words.Select((w, i) => new PasswordWordInfo(i + 1, w, _wordStates[i])).ToList(),
                CurrentWord: playing && puzzle is not null ? puzzle.Words[_wordIndex] : "",
                BigWord: puzzle?.Answer ?? "",
                BigWordAlso: puzzle is { AlsoAccept.Count: > 0 } ? string.Join(", ", puzzle.AlsoAccept) : null,
                BigWordRevealed: _bigWordRevealed,
                Clues: _clues.Select(c => new PasswordClueInfo(c.Team, c.Text, c.Outcome)).ToList(),
                AttemptingId: attempting,
                AttemptsLeft: _phase == PasswordPhase.PuzzleGuess ? _attempts.Count - _attemptPos : 0,
                Message: _message,
                Winners: _winners.ToList(),
                CanStart: _phase == PasswordPhase.Lobby && IsFull());
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

    private bool IsFull() => _emceeToken is not null && _players.Count == PlayerSeats;
    private bool IsEmcee(Guid token) => _emceeToken is not null && _emceeToken == token;
    private PlayerState? FindPlayer(Guid token) => _players.FirstOrDefault(p => p.Token == token);
    private PlayerState PlayerAt(int team, int slot) => _players.First(p => p.Team == team && p.Slot == slot);
    private PasswordPuzzle CurrentPuzzle => _puzzles[_puzzleIndex];

    /// <summary>Which player on each team gives the clues in this puzzle: player 1 (slot 0) in puzzle 1, player 2 in puzzle 2, and so on.
    /// The roles only swap when a new puzzle (a new big word) begins.</summary>
    private int GiverSlot => _puzzleIndex % 2;

    private static bool TryCleanName(string? name, out string clean)
    {
        clean = (name ?? "").Trim();
        if (clean.Length == 0) return false;
        if (clean.Length > NameMaxLength) clean = clean[..NameMaxLength].Trim();
        return true;
    }

    // The emcee decides whether a clue is legal (one word, no part of the answer). All that's checked here is that
    // something sensible was typed; a bad clue is thrown out by the emcee, which ends just that clue.
    private static bool TryCleanClue(string? text, out string clean, out string error)
    {
        clean = (text ?? "").Trim();
        error = "";
        if (clean.Length == 0) { error = "Type a clue first."; return false; }
        if (clean.Length > ClueMaxLength) { error = $"That's too long for a one-word clue (the limit is {ClueMaxLength} letters)."; return false; }
        return true;
    }
}
