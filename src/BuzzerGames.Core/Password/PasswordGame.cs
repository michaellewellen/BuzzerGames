using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.Password;

/// <summary>
/// One Password Plus game: two teams of two, one emcee, and puzzles worth more and more points until a team
/// reaches 500, then a bonus round for the winners.
///
///   A puzzle (a round) is a mystery "big word" and five password words that each hint at it, getting easier. The game is
///   spoken: the clue-giver sees the password on their phone and says one word out loud, their partner guesses out loud, and
///   the emcee rules with three buttons: right, wrong, or an invalid clue (more than one word, part of the password...).
///
///   The team with control (coin flip first, then whoever last got a word right) chooses to play (they give the first clue) or pass, on every word.
///   Clues then alternate between the teams. Each side gets at most 4 clues. A wrong guess or an invalid clue passes the turn.
///   If all 8 clues miss, the word is posted on the board, control stays put, and the
///   puzzle carries on.
///
///   When a guesser gets the word, that guesser (and only that guesser) gets one shot at the big word. The exception is the
///   fifth word: the guesser tries first, then their clue-giver, then the other team's guesser, then the other team's
///   clue-giver. If nobody gets the fifth word, the last team to guess a word in the puzzle starts that same chain.
///   A right guess scores the puzzle's value (100, 100, then 200 each) and reveals the whole puzzle. A wrong big-word guess on words 1 to 4 leaves control with the team that got the word.
///
///   Roles swap only when a new puzzle begins: player 1 gives and player 2 guesses in the first puzzle, then they swap.
///
///   The first team to reach 500 plays the bonus round (only one team scores per puzzle, so there are never ties): ten words in alphabetical order from a
///   random letter, 60 seconds, $100 per word and $10,000 for all ten. One partner faces the TV and gives clues, the other
///   guesses with their back to it, and the emcee rules: got it, pass (the word comes back later if there is time), or a
///   broken clue rule, which zaps the word.
///
/// Like Jeopardy and Press Your Luck, this class knows nothing about web pages, and every public method takes one lock.
/// </summary>
public sealed class PasswordGame
{
    public const int Teams = 2;
    public const int TeamSize = 2;
    public const int PlayerSeats = Teams * TeamSize;
    public const int NameMaxLength = 20;
    public const int WordsPerPuzzle = 5;
    public const int CluesPerSide = 4;
    public const int WinScore = 500;

    // The bonus round: ten words, one per letter in alphabetical order, in 60 seconds.
    public const int BonusSeconds = 60;
    public const int BonusLetterValue = 100;
    public const int BonusJackpot = 10_000;

    /// <summary>The most puzzles one game can use. A game can end sooner; it never needs more than this.</summary>
    public const int PuzzlesPerGame = 12;

    public static readonly IReadOnlyList<string> TeamNames = ["Team A", "Team B"];

    /// <summary>What a puzzle is worth: 100, 100, then 200 from then on. Index 0 is the first puzzle.</summary>
    public static int PuzzleValue(int puzzleIndex) => Math.Min(100 * (puzzleIndex / 2 + 1), 200);

    // ---- Private state -------------------------------------------------------------------

    private sealed class PlayerState
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid Token { get; } = Guid.NewGuid();
        public required string Name { get; init; }
        public required int Team { get; set; }
        public required int Slot { get; init; }
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
    private readonly List<int> _clueTeams = [];   // the team that gave each clue on this word, in order
    private int _startTeam;                       // the team that gave the first clue on this word

    private readonly List<Guid> _attempts = [];
    private int _attemptPos;

    private List<int> _winners = [];

    private readonly IBonusWordSource _bonusWords;
    private readonly Func<DateTime> _now;
    private int _bonusTeam = -1;
    private BonusRound? _bonusRound;
    private readonly BonusSlotState[] _bonusStates = new BonusSlotState[TextBonusWordSource.WordsPerRound];
    private readonly List<int> _bonusQueue = [];   // slots still to play, current first; passed words go to the back
    private int _bonusGiverSlot;
    private DateTime? _bonusEnds;
    private int _bonusEarned;
    private Timer? _bonusTimer;

    // ---- Public surface ------------------------------------------------------------------

    public PasswordGame(string code, IPasswordPuzzleSource source, Random? rng = null, IBonusWordSource? bonusWords = null, Func<DateTime>? clock = null)
    {
        _bonusWords = bonusWords ?? new TextBonusWordSource();
        _now = clock ?? (() => DateTime.UtcNow);
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

    /// <summary>
    /// Another game with the same four phones, but the partners switch: the two second players trade teams (so the new teams are A1+B2 against B1+A2). Nobody has to scan the QR code again.
    /// </summary>
    public Result RestartSwitched(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PasswordPhase.GameOver) return Result.Fail("The game isn't over yet.");

        ClearGameState();
        var a = PlayerAt(0, 1);
        var b = PlayerAt(1, 1);
        a.Team = 1;
        b.Team = 0;
        BeginGame();
        return Result.Success;
    });

    /// <summary>
    /// The emcee throws out the puzzle being played (it turned out to be a dud for this crowd) and the next one in the line-up takes
    /// its place, starting again at word 1. Scores and control stay as they are, nothing is held against either team, and the dud
    /// is not dealt again this game. Not available once the puzzle is over.
    /// </summary>
    public Result SwapPuzzle(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can swap the puzzle.");
        if (_phase is not (PasswordPhase.Control or PasswordPhase.Clue or PasswordPhase.PuzzleGuess or PasswordPhase.WordResult))
            return Result.Fail("There's no puzzle in play to swap.");

        var list = _puzzles.ToList();
        list.RemoveAt(_puzzleIndex);
        if (_puzzleIndex >= list.Count) list.AddRange(_source.CreatePuzzles(_rng, PuzzlesPerGame));
        _puzzles = list;

        _wordIndex = 0;
        _lastSolverTeam = -1;
        StartWord($"New puzzle! {TeamNames[_controlTeam]} still has control.");
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
        _clueTeams.Clear();
        _attempts.Clear();
        _attemptPos = 0;
        _winners = [];
        ResetBonus();
    }

    private void ResetBonus()
    {
        _bonusTimer?.Dispose();
        _bonusTimer = null;
        _bonusTeam = -1;
        _bonusRound = null;
        Array.Clear(_bonusStates);
        _bonusQueue.Clear();
        _bonusGiverSlot = 0;
        _bonusEnds = null;
        _bonusEarned = 0;
    }

    // ---- One word ------------------------------------------------------------------------

    /// <summary>The clue-giver of the team with control (who has seen the word) chooses: play (give the first clue) or pass (the other team does). The emcee can choose for them.</summary>
    public Result ChooseControl(Guid token, bool play) => Run(() =>
    {
        if (_phase != PasswordPhase.Control) return Result.Fail("There's no choice to make right now.");
        if (!IsEmcee(token))
        {
            var player = FindPlayer(token);
            if (player is null) return Result.Fail("Only the team with control can choose.");
            if (player.Team != _controlTeam)
                return Result.Fail($"{TeamNames[_controlTeam]} has control, so only they can choose.");
            if (player.Slot != GiverSlot)
                return Result.Fail("Only the clue-giver chooses: they have seen the word.");
        }

        _startTeam = play ? _controlTeam : 1 - _controlTeam;
        _turnTeam = _startTeam;
        _message = null;
        _phase = PasswordPhase.Clue;
        return Result.Success;
    });

    /// <summary>
    /// The emcee rules on a clue-and-guess: the clue-giver on the clock said one word out loud and their partner guessed. Right
    /// reveals the word and gives that guesser a shot at the big word; wrong passes the turn to the other team.
    /// </summary>
    public Result JudgeGuess(Guid emceeToken, bool correct) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can judge guesses.");
        if (_phase != PasswordPhase.Clue) return Result.Fail("Nobody is giving a clue right now.");

        var team = _turnTeam;
        _clueTeams.Add(team);
        if (!correct)
        {
            AfterMiss();
            return Result.Success;
        }

        _wordStates[_wordIndex] = WordState.Solved;
        _controlTeam = team;
        _lastSolverTeam = team;
        BeginBigWordGuess(team, null);
        return Result.Success;
    });

    /// <summary>
    /// The emcee calls the clue invalid (more than one word, part of the password, a made-up word...). It still counts as that side's
    /// clue, nobody guesses, and the turn passes to the other team.
    /// </summary>
    public Result RejectClue(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can throw out a clue.");
        if (_phase != PasswordPhase.Clue) return Result.Fail("There's no clue to throw out.");

        _clueTeams.Add(_turnTeam);
        AfterMiss();
        return Result.Success;
    });

    // A clue was wrong or thrown out: the other team is next, unless all eight clues are used up.
    private void AfterMiss()
    {
        if (_clueTeams.Count >= Teams * CluesPerSide)
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
                _message = $"Nobody got it. The word was \"{word}\", and it goes up on the board. {TeamNames[_controlTeam]} still has control.";
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
            _message = $"{who.Name} missed. {TeamNames[who.Team]} keeps control and chooses play or pass on the next word.";
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
            case PasswordPhase.BonusResult:
                FinishGame([_bonusTeam], _bonusEarned > 0
                    ? $"wins the game and ${_bonusEarned:N0} in the bonus round!"
                    : "wins the game, but not a penny in the bonus round.");
                return Result.Success;

            case PasswordPhase.WordResult:
                _wordIndex++;
                StartWord($"{TeamNames[_controlTeam]} has control.");
                return Result.Success;

            case PasswordPhase.PuzzleResult:
                var best = _scores.Max();
                if (best >= WinScore)
                {
                    // Only one team scores per puzzle, so whoever first reaches 500 is alone in front. No ties.
                    var leaders = _scores.Select((sc, i) => (sc, i)).Where(x => x.sc == best).Select(x => x.i).ToList();
                    BeginBonusReady(leaders[0]);
                    return Result.Success;
                }
                if (_puzzleIndex + 1 >= _puzzles.Count)
                {
                    // The game never ends by running out: nobody has 500 yet, so deal some more.
                    _puzzles = _puzzles.Concat(_source.CreatePuzzles(_rng, PuzzlesPerGame)).ToList();
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

    // Starts a word with the control choice: the team with control picks play (give the first clue) or pass.
    private void StartWord(string? message)
    {
        if (_wordIndex == 0)
        {
            Array.Clear(_wordStates);
            _lastSolverTeam = -1;
            _bigWordRevealed = false;
        }
        _wordStates[_wordIndex] = WordState.Current;
        _clueTeams.Clear();
        _attempts.Clear();
        _attemptPos = 0;
        _message = message;
        _turnTeam = -1;
        _phase = PasswordPhase.Control;
    }

    // ---- The bonus round -----------------------------------------------------------------

    private void BeginBonusReady(int team)
    {
        _winners = [team];
        _bonusTeam = team;
        _bonusGiverSlot = 0;
        _bonusRound = _bonusWords.CreateRound(_rng);
        Array.Clear(_bonusStates);
        _bonusQueue.Clear();
        _bonusQueue.AddRange(Enumerable.Range(0, TextBonusWordSource.WordsPerRound));
        _bonusEnds = null;
        _bonusEarned = 0;
        _turnTeam = -1;
        _message = $"{TeamNames[team]} reached {WinScore} and goes to the bonus round!";
        _phase = PasswordPhase.BonusReady;
    }

    /// <summary>Before the clock starts, the emcee can swap who gives the clues (and faces the TV) and who guesses.</summary>
    public Result BonusSwapRoles(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PasswordPhase.BonusReady) return Result.Fail("The roles can only be swapped before the clock starts.");
        _bonusGiverSlot = 1 - _bonusGiverSlot;
        return Result.Success;
    });

    /// <summary>The emcee starts the 60-second clock and the first word appears on the TV.</summary>
    public Result StartBonus(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can start the clock.");
        if (_phase != PasswordPhase.BonusReady) return Result.Fail("The bonus round isn't ready to start.");

        _bonusEnds = _now().AddSeconds(BonusSeconds);
        _bonusStates[_bonusQueue[0]] = BonusSlotState.Current;
        _message = null;
        _phase = PasswordPhase.BonusPlay;
        _bonusTimer?.Dispose();
        _bonusTimer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(BonusSeconds) + TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
        return Result.Success;
    });

    /// <summary>The guesser said the word: add $100 and show the next one.</summary>
    public Result BonusCorrect(Guid emceeToken) => Run(() => BonusRule(emceeToken, BonusSlotState.Correct));

    /// <summary>Somebody said "pass": move on, and the word comes back at the end of the line if there is time.</summary>
    public Result BonusPass(Guid emceeToken) => Run(() => BonusRule(emceeToken, BonusSlotState.Passed));

    /// <summary>A clue rule was broken: the word is zapped (lost) and the next one comes up.</summary>
    public Result BonusZap(Guid emceeToken) => Run(() => BonusRule(emceeToken, BonusSlotState.Zapped));

    private Result BonusRule(Guid emceeToken, BonusSlotState outcome)
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can rule on the bonus words.");
        if (_phase != PasswordPhase.BonusPlay) return Result.Fail("The bonus round isn't being played.");

        // A tap that lands just after the buzzer is too late: the round ends and the tap is ignored.
        if (_now() >= _bonusEnds) { FinishBonus(); return Result.Success; }

        var current = _bonusQueue[0];
        _bonusQueue.RemoveAt(0);
        _bonusStates[current] = outcome;
        if (outcome == BonusSlotState.Passed) _bonusQueue.Add(current);

        if (_bonusQueue.Count == 0) FinishBonus();
        else _bonusStates[_bonusQueue[0]] = BonusSlotState.Current;
        return Result.Success;
    }

    /// <summary>Ends the round if the clock has run out. A timer calls this when the time is up; it is safe to call any time.</summary>
    public Result Tick() => Run(() =>
    {
        if (_phase != PasswordPhase.BonusPlay || _bonusEnds is null) return Result.Fail("The clock isn't running.");
        var left = _bonusEnds.Value - _now();
        if (left > TimeSpan.Zero)
        {
            _bonusTimer?.Change(left + TimeSpan.FromMilliseconds(50), Timeout.InfiniteTimeSpan);
            return Result.Fail("There's still time.");
        }
        FinishBonus();
        return Result.Success;
    });

    private void FinishBonus()
    {
        _bonusTimer?.Dispose();
        _bonusTimer = null;
        _bonusQueue.Clear();
        for (var i = 0; i < _bonusStates.Length; i++)
            if (_bonusStates[i] is BonusSlotState.Current or BonusSlotState.Waiting) _bonusStates[i] = BonusSlotState.Waiting;
        var correct = _bonusStates.Count(x => x == BonusSlotState.Correct);
        _bonusEarned = correct == TextBonusWordSource.WordsPerRound ? BonusJackpot : correct * BonusLetterValue;
        _message = correct == TextBonusWordSource.WordsPerRound
            ? $"ALL TEN! {TeamNames[_bonusTeam]} wins ${_bonusEarned:N0}!"
            : $"{TeamNames[_bonusTeam]} got {correct} of {TextBonusWordSource.WordsPerRound}: ${_bonusEarned:N0}.";
        _phase = PasswordPhase.BonusResult;
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
            var inBonus = _phase is PasswordPhase.BonusReady or PasswordPhase.BonusPlay or PasswordPhase.BonusResult;
            var playing = inGame && _phase != PasswordPhase.GameOver && !inBonus;
            var puzzle = inGame ? _puzzles[_puzzleIndex] : null;
            var onClock = _phase is PasswordPhase.Clue or PasswordPhase.PuzzleGuess;
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
            foreach (var team in _clueTeams) cluesUsed[team]++;

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
                AttemptingId: attempting,
                AttemptsLeft: _phase == PasswordPhase.PuzzleGuess ? _attempts.Count - _attemptPos : 0,
                Message: _message,
                Winners: _winners.ToList(),
                CanStart: _phase == PasswordPhase.Lobby && IsFull(),
                BonusTeam: inBonus ? _bonusTeam : -1,
                BonusGiverId: inBonus ? PlayerAt(_bonusTeam, _bonusGiverSlot).Id : null,
                BonusGuesserId: inBonus ? PlayerAt(_bonusTeam, 1 - _bonusGiverSlot).Id : null,
                BonusSlots: inBonus && _bonusRound is not null
                    ? _bonusRound.Words.Select((w, i) => new BonusSlotInfo(_bonusRound.LetterAt(i), w, _bonusStates[i])).ToList()
                    : [],
                BonusCurrent: _phase == PasswordPhase.BonusPlay && _bonusQueue.Count > 0 ? _bonusQueue[0] : -1,
                BonusEndsAtUtc: inBonus ? _bonusEnds : null,
                BonusMillisLeft: _phase == PasswordPhase.BonusPlay && _bonusEnds is { } ends ? (int)Math.Max(0, (ends - _now()).TotalMilliseconds) : 0,
                BonusSeconds: BonusSeconds,
                BonusCorrect: inBonus ? _bonusStates.Count(x => x == BonusSlotState.Correct) : 0,
                BonusEarned: _phase == PasswordPhase.BonusResult ? _bonusEarned : 0);
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
}
