using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.PressYourLuck;

/// <summary>
/// One Press Your Luck game: a lobby, two rounds of "question, then spins", and a winner.
///
///   Question round: an open question goes up and buzzers open. The first to buzz gives their answer out
///   loud and the emcee judges it. Right: they earn 3 spins, and the others pick between the right answer
///   and two decoys. Wrong: the others pick between the right answer, the wrong answer, and one decoy.
///   Each correct pick earns 1 spin. Nothing is lost for a wrong pick.
///
///   Big Board round: the contestant with the fewest spins goes first. They spin (the light jumps around
///   the 18 squares) and press to stop it. Cash adds to the score, "+ SPIN" adds a spin, a Whammy wipes the
///   score to zero (four Whammies and you're out). After a first spin a contestant may pass their remaining
///   spins to the highest-scoring opponent, who must play them.
///
/// Like Jeopardy, this class knows nothing about web pages, and every public method takes one lock.
/// The rules of the light live here too: the server decides where the light stopped, from its own clock,
/// so a laggy phone can't change the outcome.
/// </summary>
public sealed class PressYourLuckGame
{
    public const int PlayerSeats = 3;
    public const int NameMaxLength = 20;
    public const int Rounds = 2;
    public const int QuestionsPerRound = 3;
    public const int SpinsForGuess = 3;
    public const int SpinsForPick = 1;
    public const int WhammiesToBeOut = 4;

    /// <summary>How long the light stays on each square, in milliseconds.</summary>
    public const int TickMs = 110;

    /// <summary>The light always has a script this long (about 3.5 minutes); after that it loops.</summary>
    public const int SequenceLength = 2000;

    /// <summary>A short pause between "Spin!" and the first lit square, so every screen is ready.</summary>
    public const int LeadInMs = 600;

    /// <summary>
    /// A press takes a moment to reach the server, so the light is judged as it was this long ago.
    /// Raise it if players feel the light stops "too late", lower it if "too early".
    /// </summary>
    public const int PressLagMs = 80;

    /// <summary>Which of the three faces a square is showing at a given tick. The big screen uses the same formula.</summary>

    // ---- Private state -------------------------------------------------------------------

    private sealed class PlayerState
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid Token { get; } = Guid.NewGuid();
        public required string Name { get; init; }
        public int Score { get; set; }
        public int Spins { get; set; }
        public int Whammies { get; set; }
        public bool IsOut { get; set; }
        public int? Pick { get; set; }
    }

    private readonly object _gate = new();
    private readonly IPylQuestionSource _questionSource;
    private readonly Random _rng;
    private readonly GameOptions _options;
    private readonly TimeProvider _time;

    private readonly List<PlayerState> _players = [];
    private Guid? _emceeToken;
    private string? _emceeName;
    private PylPhase _phase = PylPhase.Lobby;
    private int _round;
    private string? _message;

    // question state
    private IReadOnlyList<PylQuestion> _questions = [];
    private int _questionIndex;
    private Guid? _guesserId;
    private string? _guessText;
    private List<string> _choices = [];
    private int _correctIndex = -1;
    private readonly HashSet<Guid> _pickers = [];
    private bool _guesserRight;

    // Big Board state
    private Outcome[][] _board = [];
    private int _boardId;
    private List<PlayerState> _order = [];
    private int _orderPos;
    private Guid? _actingId;
    private bool _actingPassed;
    private int _passedLeft;
    private bool _spunThisTurn;
    private int _spinId;
    private int[] _sequence = [];
    private int[] _pages = [];    // which face (page) the whole board shows at each tick
    private DateTimeOffset _spinStart;
    private int _cornerTick;
    private int _cornerFace;
    private SpinResultInfo? _lastSpin;
    private int _resultId;

    // ---- Public surface ------------------------------------------------------------------

    public PressYourLuckGame(string code, IPylQuestionSource source, Random? rng = null, GameOptions? options = null, TimeProvider? time = null)
    {
        Code = code;
        _questionSource = source;
        _rng = rng ?? new Random();
        _options = options ?? GameOptions.Default;
        _time = time ?? TimeProvider.System;
    }

    public string Code { get; }

    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;

    /// <summary>Raised after every successful change. Pages listen to this and redraw.</summary>
    public event Action? Changed;

    // ---- Lobby ---------------------------------------------------------------------------

    public JoinResult JoinEmcee(string name) => Run(() =>
    {
        if (_phase != PylPhase.Lobby) return JoinResult.Fail("The game has already started.");
        if (_emceeToken is not null) return JoinResult.Fail("This game already has an emcee.");
        if (!TryCleanName(name, out var clean)) return JoinResult.Fail("Please enter a name.");

        _emceeToken = Guid.NewGuid();
        _emceeName = clean;
        return JoinResult.Success(_emceeToken.Value);
    });

    public JoinResult JoinPlayer(string name) => Run(() =>
    {
        if (_phase != PylPhase.Lobby) return JoinResult.Fail("The game has already started.");
        if (_players.Count >= PlayerSeats) return JoinResult.Fail("All three player seats are taken.");
        if (!TryCleanName(name, out var clean)) return JoinResult.Fail("Please enter a name.");
        if (_players.Any(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase)))
            return JoinResult.Fail("Someone is already using that name.");

        var player = new PlayerState { Name = clean };
        _players.Add(player);
        return JoinResult.Success(player.Token);
    });

    public Result Start() => Run(() =>
    {
        if (_phase != PylPhase.Lobby) return Result.Fail("The game has already started.");
        if (!IsFull()) return Result.Fail("We need 1 emcee and 3 players first.");

        BeginGame();
        return Result.Success;
    });

    /// <summary>
    /// After the game is over the emcee can play again. With samePlayers, everyone keeps their seat (and their
    /// phone stays signed in) and a fresh game starts at once. Without it, the players are cleared and the room goes
    /// back to the lobby with the same room code and the same emcee, so three new people can take the seats.
    /// </summary>
    public Result Restart(Guid emceeToken, bool samePlayers) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PylPhase.GameOver) return Result.Fail("The game isn't over yet.");

        ClearGameState();
        if (samePlayers)
        {
            foreach (var p in _players)
            {
                p.Score = 0;
                p.Spins = 0;
                p.Whammies = 0;
                p.IsOut = false;
                p.Pick = null;
            }
            BeginGame();
        }
        else
        {
            _players.Clear();
            _phase = PylPhase.Lobby;
        }
        return Result.Success;
    });

    private void BeginGame()
    {
        _questions = _questionSource.CreateQuestions(_rng, Rounds, QuestionsPerRound, _options);
        if (_questions.Count != Rounds * QuestionsPerRound)
            throw new InvalidOperationException($"The question source must supply {Rounds * QuestionsPerRound} questions.");

        _round = 1;
        _questionIndex = 0;
        BeginQuestion();
    }

    // Forgets everything about the game that just finished (but not who is in the room, nor the running counters).
    private void ClearGameState()
    {
        _message = null;
        _round = 0;
        _questions = [];
        _questionIndex = 0;
        _guesserId = null;
        _guessText = null;
        _choices = [];
        _correctIndex = -1;
        _pickers.Clear();
        _guesserRight = false;
        _board = [];
        _order = [];
        _orderPos = 0;
        _actingId = null;
        _actingPassed = false;
        _passedLeft = 0;
        _spunThisTurn = false;
        _sequence = [];
        _pages = [];
        _lastSpin = null;
    }

    // ---- Question round ------------------------------------------------------------------

    /// <summary>A player hits their buzzer. The first call to get the lock wins.</summary>
    public Result Buzz(Guid playerToken) => Run(() =>
    {
        var player = FindPlayer(playerToken);
        if (player is null) return Result.Fail("Only players can buzz in.");
        if (_phase != PylPhase.Question) return Result.Fail("Too slow! Someone else got there first.");

        _guesserId = player.Id;
        _phase = PylPhase.Answering;
        return Result.Success;
    });

    /// <summary>Nobody buzzed. Everyone gets to pick from the right answer and two decoys.</summary>
    public Result NoBuzz(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PylPhase.Question) return Result.Fail("There's no open question.");

        var q = CurrentQuestion;
        _guesserId = null;
        _guessText = null;
        StartPicking([q.Answer, q.Decoys[0], q.Decoys[1]], _players.Where(p => !p.IsOut));
        return Result.Success;
    });

    /// <summary>
    /// The emcee rules on what the buzzer said out loud. If it was wrong, the emcee can type what they said
    /// so it can be one of the three choices; leave it blank and a second decoy is used instead.
    /// </summary>
    public Result JudgeGuess(Guid emceeToken, bool correct, string? whatTheySaid = null) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can judge answers.");
        if (_phase != PylPhase.Answering) return Result.Fail("Nobody is answering right now.");

        var q = CurrentQuestion;
        var guesser = _players.First(p => p.Id == _guesserId);
        var others = _players.Where(p => p.Id != guesser.Id && !p.IsOut);

        _guesserRight = correct;
        if (correct)
        {
            if (!guesser.IsOut) guesser.Spins += SpinsForGuess;
            _guessText = null;
            StartPicking([q.Answer, q.Decoys[0], q.Decoys[1]], others);
            return Result.Success;
        }

        var said = (whatTheySaid ?? "").Trim();
        if (said.Length > 40) said = said[..40];
        var usable = said.Length > 0 && !string.Equals(said, q.Answer, StringComparison.OrdinalIgnoreCase)
            && !q.Decoys.Any(d => string.Equals(d, said, StringComparison.OrdinalIgnoreCase));
        _guessText = usable ? said : null;
        StartPicking([q.Answer, usable ? said : q.Decoys[1], q.Decoys[0]], others);
        return Result.Success;
    });

    /// <summary>A contestant picks one of the choices. When every picker has picked, the answer is revealed.</summary>
    public Result Pick(Guid playerToken, int optionIndex) => Run(() =>
    {
        var player = FindPlayer(playerToken);
        if (player is null) return Result.Fail("Only players can pick.");
        if (_phase != PylPhase.Picking) return Result.Fail("It isn't time to pick.");
        if (!_pickers.Contains(player.Id)) return Result.Fail("You don't pick on this one.");
        if (player.Pick is not null) return Result.Fail("You already locked in a pick.");
        if (optionIndex < 0 || optionIndex >= _choices.Count) return Result.Fail("No such choice.");

        player.Pick = optionIndex;
        if (_pickers.All(id => _players.First(p => p.Id == id).Pick is not null)) RevealQuestion();
        return Result.Success;
    });

    /// <summary>Safety valve: if a phone died mid-question, the emcee reveals the answer now.</summary>
    public Result RevealNow(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != PylPhase.Picking) return Result.Fail("Nothing to reveal right now.");
        RevealQuestion();
        return Result.Success;
    });

    /// <summary>After the answer is shown: the next question, or (after the last one in a round) the Big Board.</summary>
    public Result ContinueFromQuestion(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can continue.");
        if (_phase != PylPhase.Reveal) return Result.Fail("Nothing to continue from.");

        _questionIndex++;
        if (_questionIndex % QuestionsPerRound == 0) BeginBigBoard();
        else BeginQuestion();
        return Result.Success;
    });

    // ---- Big Board -----------------------------------------------------------------------

    /// <summary>The contestant whose turn it is (or the emcee, for them) starts the light.</summary>
    public Result Spin(Guid token) => Run(() =>
    {
        if (!CanActFor(token)) return Result.Fail("It isn't your turn.");
        if (_phase != PylPhase.SpinReady) return Result.Fail("You can't spin right now.");

        var actor = Acting!;
        if (_actingPassed) _passedLeft--; else actor.Spins--;
        _spunThisTurn = true;

        _spinId++;
        _sequence = NewSequence();
        _pages = NewPages();
        _spinStart = _time.GetUtcNow().AddMilliseconds(LeadInMs);
        _message = null;
        _phase = PylPhase.Spinning;
        return Result.Success;
    });

    /// <summary>Stops the light. Whichever square is lit when the server hears the press is the one you get.</summary>
    public Result Press(Guid token) => Run(() =>
    {
        if (!CanActFor(token)) return Result.Fail("It isn't your turn.");
        if (_phase != PylPhase.Spinning) return Result.Fail("The light isn't running.");

        var elapsed = (_time.GetUtcNow() - _spinStart).TotalMilliseconds - PressLagMs;
        var tick = elapsed < 0 ? 0 : (int)(elapsed / TickMs);
        var square = _sequence[tick % _sequence.Length];
        var face = _pages[tick % _pages.Length];
        var outcome = _board[square][face];

        if (outcome.Kind == OutcomeKind.Corner)
        {
            _cornerTick = tick;
            _cornerFace = face;
            _lastSpin = Announce(Acting!, tick, face, square, outcome, $"{Acting!.Name} landed on Pick a Corner!", pending: true);
            _phase = PylPhase.PickCorner;
            return Result.Success;
        }

        Resolve(Acting!, tick, face, square, outcome, null);
        return Result.Success;
    });

    /// <summary>After Pick a Corner: take whatever that corner is showing.</summary>
    public Result PickCorner(Guid token, int corner) => Run(() =>
    {
        if (!CanActFor(token)) return Result.Fail("It isn't your turn.");
        if (_phase != PylPhase.PickCorner) return Result.Fail("There's no corner to pick right now.");
        if (!BoardFactory.Corners.Contains(corner)) return Result.Fail("That isn't a corner.");

        var outcome = _board[corner][_cornerFace];
        Resolve(Acting!, _cornerTick, _cornerFace, corner, outcome, "picked a corner:");
        return Result.Success;
    });

    /// <summary>
    /// After at least one spin in a turn, a contestant may pass their remaining spins to the highest-scoring
    /// opponent, who must play them. If two opponents tie for the lead, say which one.
    /// </summary>
    public Result Pass(Guid token, Guid? toPlayerId = null) => Run(() =>
    {
        if (!CanActFor(token)) return Result.Fail("It isn't your turn.");
        if (_phase != PylPhase.SpinReady) return Result.Fail("You can't pass right now.");
        if (!CanPassNow()) return Result.Fail("You have to spin first. Spins passed to you can't be passed again.");

        var targets = PassTargets();
        if (targets.Count == 0) return Result.Fail("There's nobody to pass to.");
        PlayerState target;
        if (toPlayerId is null)
        {
            if (targets.Count > 1) return Result.Fail("Choose who gets your spins.");
            target = targets[0];
        }
        else
        {
            var chosen = targets.FirstOrDefault(t => t.Id == toPlayerId);
            if (chosen is null) return Result.Fail("You can only pass to the highest-scoring opponent.");
            target = chosen;
        }

        var actor = Acting!;
        var count = actor.Spins;
        actor.Spins = 0;
        _passedLeft = count;
        _actingId = target.Id;
        _actingPassed = true;
        _spunThisTurn = false;
        _message = $"{actor.Name} passes {count} spin{(count == 1 ? "" : "s")} to {target.Name}!";
        _lastSpin = null;
        return Result.Success;
    });

    /// <summary>After the round's last spin, the emcee moves on: round 2 questions, or the end of the game.</summary>
    public Result ContinueFromRound(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can continue.");
        if (_phase != PylPhase.RoundOver) return Result.Fail("Nothing to continue from.");

        if (_round >= Rounds)
        {
            _phase = PylPhase.GameOver;
            return Result.Success;
        }
        _round++;
        _lastSpin = null;
        _actingId = null;
        BeginQuestion();
        return Result.Success;
    });

    // ---- Looking things up ---------------------------------------------------------------

    public Seat GetSeat(Guid token)
    {
        lock (_gate)
        {
            if (_emceeToken == token) return new Seat(Role.Emcee);
            var player = FindPlayer(token);
            return player is null ? new Seat(Role.Spectator) : new Seat(Role.Player, player.Id);
        }
    }

    public PylSnapshot Snapshot()
    {
        lock (_gate)
        {
            var inQuestions = _phase is PylPhase.Question or PylPhase.Answering or PylPhase.Picking or PylPhase.Reveal;
            var inBoard = _phase is PylPhase.SpinReady or PylPhase.Spinning or PylPhase.PickCorner;

            var players = _players.Select(p => new PylPlayerInfo(
                Id: p.Id,
                Name: p.Name,
                Score: p.Score,
                Spins: p.Spins,
                PassedSpins: inBoard && _actingPassed && p.Id == _actingId ? _passedLeft : 0,
                Whammies: p.Whammies,
                IsOut: p.IsOut,
                IsActing: inBoard && p.Id == _actingId,
                IsGuesser: inQuestions && p.Id == _guesserId,
                IsPicker: (_phase is PylPhase.Picking or PylPhase.Reveal) && _pickers.Contains(p.Id),
                PickedIndex: inQuestions ? p.Pick : null)).ToList();

            QuestionInfo? question = null;
            if (inQuestions)
            {
                var q = CurrentQuestion;
                question = new QuestionInfo(_questionIndex % QuestionsPerRound + 1, QuestionsPerRound, q.Category, q.Text, q.Answer, q.AlsoAccept);
            }

            SpinInfo? spin = null;
            if (_phase == PylPhase.Spinning)
                spin = new SpinInfo(_spinId, _sequence, _pages, _spinStart.ToUnixTimeMilliseconds(), _time.GetUtcNow().ToUnixTimeMilliseconds(), TickMs);

            var winners = new List<Guid>();
            if (_phase == PylPhase.GameOver)
            {
                var best = _players.Max(p => p.Score);
                if (best > 0) winners = _players.Where(p => p.Score == best).Select(p => p.Id).ToList();
            }

            var canPass = _phase == PylPhase.SpinReady && CanPassNow();
            var actingLeft = Acting is null ? 0 : _actingPassed ? _passedLeft : Acting.Spins;

            return new PylSnapshot(
                Code: Code,
                Phase: _phase,
                Round: _round,
                Rounds: Rounds,
                EmceeName: _emceeName,
                Players: players,
                Question: question,
                Options: inQuestions ? _choices.ToList() : [],
                CorrectIndex: _phase == PylPhase.Reveal || _phase == PylPhase.Picking ? _correctIndex : -1,
                GuessText: inQuestions ? _guessText : null,
                Message: _message,
                BoardId: _boardId,
                Board: _board.Select(faces => (IReadOnlyList<OutcomeInfo>)faces.Select(o => new OutcomeInfo(o.Kind.ToString().ToLowerInvariant(), o.Text, o.Sub)).ToList()).ToList(),
                Spin: spin,
                LastSpin: _lastSpin,
                ActingId: inBoard || _phase == PylPhase.RoundOver ? _actingId : null,
                ActingSpinsLeft: inBoard ? actingLeft : 0,
                ActingIsPassed: inBoard && _actingPassed,
                CanPass: canPass,
                PassTargets: canPass ? PassTargets().Select(t => t.Id).ToList() : [],
                Corners: BoardFactory.Corners,
                Winners: winners,
                CanStart: _phase == PylPhase.Lobby && IsFull());
        }
    }

    // ---- Private helpers -----------------------------------------------------------------

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
    private PlayerState? Acting => _actingId is null ? null : _players.FirstOrDefault(p => p.Id == _actingId);
    private PylQuestion CurrentQuestion => _questions[_questionIndex];

    // The contestant whose turn it is may act, and so may the emcee, as a safety net for a dead phone.
    private bool CanActFor(Guid token) =>
        IsEmcee(token) || (Acting is { } a && a.Token == token);

    private static bool TryCleanName(string? name, out string clean)
    {
        clean = (name ?? "").Trim();
        if (clean.Length == 0) return false;
        if (clean.Length > NameMaxLength) clean = clean[..NameMaxLength].Trim();
        return true;
    }

    private void BeginQuestion()
    {
        foreach (var p in _players) p.Pick = null;
        _pickers.Clear();
        _guesserId = null;
        _guesserRight = false;
        _guessText = null;
        _choices = [];
        _correctIndex = -1;
        _message = null;
        _lastSpin = null;
        _phase = PylPhase.Question;
    }

    private void StartPicking(string[] options, IEnumerable<PlayerState> pickers)
    {
        var q = CurrentQuestion;
        _choices = options.OrderBy(_ => _rng.Next()).ToList();
        _correctIndex = _choices.IndexOf(q.Answer);
        _pickers.Clear();
        foreach (var p in pickers) _pickers.Add(p.Id);
        foreach (var p in _players) p.Pick = null;
        _phase = PylPhase.Picking;
        if (_pickers.Count == 0) RevealQuestion();
    }

    private void RevealQuestion()
    {
        var q = CurrentQuestion;
        var parts = new List<string>();

        if (_guesserId is not null)
        {
            var guesser = _players.First(p => p.Id == _guesserId);
            parts.Add(_guesserRight
                ? $"{guesser.Name} got it and earns {SpinsForGuess} spins!"
                : $"{guesser.Name} missed it.");
        }
        else
        {
            parts.Add("Nobody buzzed in.");
        }

        var winners = new List<string>();
        foreach (var p in _players.Where(p => _pickers.Contains(p.Id) && p.Pick == _correctIndex))
        {
            if (!p.IsOut) p.Spins += SpinsForPick;
            winners.Add(p.Name);
        }
        parts.Add(winners.Count == 0 ? "Nobody picked it." : $"{string.Join(" and ", winners)} picked it right: {SpinsForPick} spin each.");

        _message = string.Join(" ", parts);
        _phase = PylPhase.Reveal;
    }

    private void BeginBigBoard()
    {
        _board = BoardFactory.Deal(BoardConfig.Load(_round), _rng);
        _boardId++;
        _lastSpin = null;
        _message = null;

        // Fewest spins goes first; a tie goes to the lower score, then to seat order.
        _order = _players.Select((p, i) => (p, i)).OrderBy(t => t.p.Spins).ThenBy(t => t.p.Score).ThenBy(t => t.i).Select(t => t.p).ToList();
        _orderPos = _order.Count - 1;
        _actingPassed = false;
        _passedLeft = 0;
        AdvanceTurn();
    }

    // Finds whoever plays next: the next contestant in order who still has spins, or ends the round.
    private void AdvanceTurn()
    {
        _actingPassed = false;
        _passedLeft = 0;
        for (var i = 1; i <= _order.Count; i++)
        {
            var index = (_orderPos + i) % _order.Count;
            var player = _order[index];
            if (player.Spins > 0 && !player.IsOut)
            {
                _orderPos = index;
                _actingId = player.Id;
                _spunThisTurn = false;
                _phase = PylPhase.SpinReady;
                return;
            }
        }
        _actingId = null;
        _phase = PylPhase.RoundOver;
    }

    private bool CanPassNow() =>
        Acting is { } a && !_actingPassed && _spunThisTurn && a.Spins > 0 && PassTargets().Count > 0;

    // Opponents who are still in the game and have the top score among the opponents.
    private List<PlayerState> PassTargets()
    {
        var opponents = _players.Where(p => p.Id != _actingId && !p.IsOut).ToList();
        if (opponents.Count == 0) return [];
        var best = opponents.Max(p => p.Score);
        return opponents.Where(p => p.Score == best).ToList();
    }

    private int[] NewSequence()
    {
        var sequence = new int[SequenceLength];
        var previous = -1;
        for (var i = 0; i < sequence.Length; i++)
        {
            int next;
            do { next = _rng.Next(BoardFactory.Squares); } while (next == previous);   // always jump somewhere new
            sequence[i] = previous = next;
        }
        return sequence;
    }

    // The whole board changes face together, like the real one: it holds a face for 2 or 3 flashes, then jumps
    // to one of the other two. That is why the number of Whammies on screen is always one of the face counts.
    private int[] NewPages()
    {
        var pages = new int[SequenceLength];
        var previous = _rng.Next(BoardFactory.Faces);
        var i = 0;
        while (i < pages.Length)
        {
            int next;
            do { next = _rng.Next(BoardFactory.Faces); } while (next == previous);
            previous = next;
            var run = _rng.Next(2, 4);   // 2 or 3 flashes
            for (var k = 0; k < run && i < pages.Length; k++) pages[i++] = next;
        }
        return pages;
    }

    private SpinResultInfo Announce(PlayerState player, int tick, int face, int square, Outcome outcome, string message, bool pending = false) =>
        new(++_resultId, player.Id, player.Name, tick, face, square, outcome.Kind.ToString().ToLowerInvariant(), outcome.Text, outcome.Sub, message, pending);

    // Applies what the light landed on, then works out whose move is next.
    private void Resolve(PlayerState player, int tick, int face, int square, Outcome outcome, string? cornerPhrase)
    {
        string message;
        switch (outcome.Kind)
        {
            case OutcomeKind.Whammy:
                player.Score = 0;
                player.Whammies++;
                message = $"WHAMMY! {player.Name} loses everything!";
                if (player.Whammies >= WhammiesToBeOut)
                {
                    player.IsOut = true;
                    player.Spins = 0;
                    _passedLeft = 0;
                    message += $" That's {WhammiesToBeOut} Whammies. {player.Name} is out!";
                }
                break;

            case OutcomeKind.Prize:
                player.Score += outcome.Value;
                message = $"{player.Name} wins {outcome.Text.ToLowerInvariant()}, worth ${outcome.Value:N0}!";
                break;

            default:
                player.Score += outcome.Value;
                message = $"{player.Name} takes ${outcome.Value:N0}!";
                if (outcome.ExtraSpin)
                {
                    // A spin earned while playing passed spins is the player's own: it joins their own spins,
                    // and the passed spins keep counting down.
                    player.Spins++;
                    message = $"{player.Name} takes ${outcome.Value:N0} and an extra spin!";
                }
                break;
        }

        if (cornerPhrase is not null) message = $"{player.Name} {cornerPhrase} {message}";
        _lastSpin = Announce(player, tick, face, square, outcome, message);

        var remaining = player.IsOut ? 0 : _actingPassed ? _passedLeft : player.Spins;
        if (remaining > 0) _phase = PylPhase.SpinReady;
        else AdvanceTurn();
    }
}
