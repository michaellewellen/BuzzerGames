namespace BuzzerGames.Core.Jeopardy;

/// <summary>
/// One Jeopardy game: the lobby, both rounds, Daily Doubles, and Final Jeopardy.
///
/// This class knows nothing about web pages. Pages call its methods (SelectClue, Buzz, Judge...)
/// and redraw themselves whenever <see cref="Changed"/> fires. Keeping the rules in plain C#
/// means we can test them without a browser.
///
/// Thread safety: many phones call in at the same moment, so every public method takes one lock.
/// That lock is also what makes "first buzz wins" fair: whichever call gets the lock first wins.
/// </summary>
public sealed class JeopardyGame
{
    public const int PlayerSeats = 3;
    public const int NameMaxLength = 20;
    public const int CategoriesPerRound = 6;
    public const int CluesPerCategory = 5;
    public const int Round1BaseValue = 200;
    public const int Round2BaseValue = 400;
    public const int MinDailyDoubleWager = 5;

    // ---- Private state -------------------------------------------------------------------

    private sealed class PlayerState
    {
        public Guid Id { get; } = Guid.NewGuid();     // public: safe to show on screens
        public Guid Token { get; } = Guid.NewGuid();   // secret: proves "I am this player"
        public required string Name { get; init; }
        public int Score { get; set; }
        public bool InFinal { get; set; }
        public int? FinalWager { get; set; }
        public string? FinalAnswer { get; set; }
        public bool? FinalCorrect { get; set; }
    }

    private sealed class Cell
    {
        public required int Value { get; init; }
        public required string Text { get; init; }
        public required string Answer { get; init; }
        public bool IsDailyDouble { get; set; }
        public bool IsUsed { get; set; }
    }

    private sealed class CategoryState
    {
        public required string Name { get; init; }
        public required Cell[] Cells { get; init; }
    }

    private readonly object _gate = new();
    private readonly IGameDataSource _source;
    private readonly Random _rng;
    private readonly GameOptions _options;

    private readonly List<PlayerState> _players = [];
    private Guid? _emceeToken;
    private string? _emceeName;

    private GameData? _data;
    private List<CategoryState> _categories = [];
    private int _round;
    private GamePhase _phase = GamePhase.Lobby;

    private Cell? _currentCell;
    private string? _currentCategoryName;
    private Guid? _controlId;       // who picks the next clue
    private Guid? _answeringId;     // who currently has the floor
    private readonly HashSet<Guid> _lockedOut = [];
    private int _wager;             // the Daily Double wager
    private string? _revealMessage;

    // ---- Public surface ------------------------------------------------------------------

    public JeopardyGame(string code, IGameDataSource source, Random? rng = null, GameOptions? options = null)
    {
        Code = code;
        _source = source;
        _options = options ?? GameOptions.Default;
        _rng = rng ?? new Random();
    }

    public string Code { get; }

    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;

    /// <summary>Raised after every successful change. Pages listen to this and redraw.</summary>
    public event Action? Changed;

    // ---- Lobby ---------------------------------------------------------------------------

    public JoinResult JoinEmcee(string name) => Run(() =>
    {
        if (_phase != GamePhase.Lobby) return JoinResult.Fail("The game has already started.");
        if (_emceeToken is not null) return JoinResult.Fail("This game already has an emcee.");
        if (!TryCleanName(name, out var clean)) return JoinResult.Fail("Please enter a name.");

        _emceeToken = Guid.NewGuid();
        _emceeName = clean;
        return JoinResult.Success(_emceeToken.Value);
    });

    public JoinResult JoinPlayer(string name) => Run(() =>
    {
        if (_phase != GamePhase.Lobby) return JoinResult.Fail("The game has already started.");
        if (_players.Count >= PlayerSeats) return JoinResult.Fail("All three player seats are taken.");
        if (!TryCleanName(name, out var clean)) return JoinResult.Fail("Please enter a name.");
        if (_players.Any(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase)))
            return JoinResult.Fail("Someone is already using that name.");

        var player = new PlayerState { Name = clean };
        _players.Add(player);
        return JoinResult.Success(player.Token);
    });

    /// <summary>Anyone can start once the emcee and all three players are in.</summary>
    public Result Start() => Run(() =>
    {
        if (_phase != GamePhase.Lobby) return Result.Fail("The game has already started.");
        if (!IsFull()) return Result.Fail("We need 1 emcee and 3 players first.");

        _data = _source.CreateGame(_rng, _options);
        ValidateData(_data);

        _round = 1;
        BuildBoard(_data.Round1, Round1BaseValue, dailyDoubles: 1);
        _controlId = _players[_rng.Next(_players.Count)].Id;   // random player goes first
        _phase = GamePhase.Board;
        return Result.Success;
    });

    // ---- Playing a clue ------------------------------------------------------------------

    /// <summary>The emcee taps a clue that a contestant called out.</summary>
    public Result SelectClue(Guid emceeToken, int categoryIndex, int row) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can pick clues.");
        if (_phase != GamePhase.Board) return Result.Fail("It isn't time to pick a clue.");
        if (categoryIndex < 0 || categoryIndex >= _categories.Count) return Result.Fail("No such category.");
        var category = _categories[categoryIndex];
        if (row < 0 || row >= category.Cells.Length) return Result.Fail("No such clue.");
        var cell = category.Cells[row];
        if (cell.IsUsed) return Result.Fail("That clue has already been played.");

        cell.IsUsed = true;
        _currentCell = cell;
        _currentCategoryName = category.Name;
        _lockedOut.Clear();
        _answeringId = null;

        if (cell.IsDailyDouble)
        {
            _answeringId = _controlId;            // only the player in control plays a Daily Double
            _phase = GamePhase.DailyDoubleWager;
        }
        else
        {
            _phase = GamePhase.ClueOpen;
        }
        return Result.Success;
    });

    /// <summary>
    /// A player hits their buzzer. Buzzers are open any time the clue is showing (no lockout while
    /// it is being read), and the first call to get the lock wins.
    /// </summary>
    public Result Buzz(Guid playerToken) => Run(() =>
    {
        var player = FindPlayer(playerToken);
        if (player is null) return Result.Fail("Only players can buzz in.");
        if (_phase != GamePhase.ClueOpen) return Result.Fail("Too slow! Someone else got there first.");
        if (_lockedOut.Contains(player.Id)) return Result.Fail("You already missed this clue.");

        _answeringId = player.Id;
        _phase = GamePhase.Answering;
        return Result.Success;
    });

    /// <summary>The emcee says yes or no to the answer the contestant gave out loud.</summary>
    public Result Judge(Guid emceeToken, bool correct) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can judge answers.");
        if (_phase is not (GamePhase.Answering or GamePhase.DailyDoubleAnswering))
            return Result.Fail("Nobody is answering right now.");

        var player = _players.First(p => p.Id == _answeringId);
        var isDailyDouble = _phase == GamePhase.DailyDoubleAnswering;
        var stake = isDailyDouble ? _wager : _currentCell!.Value;

        if (correct)
        {
            player.Score += stake;
            _controlId = player.Id;   // the last correct answer picks next
            _revealMessage = isDailyDouble ? $"{player.Name} nailed the Daily Double!" : $"{player.Name} got it!";
            _phase = GamePhase.Reveal;
            return Result.Success;
        }

        player.Score -= stake;

        if (isDailyDouble)
        {
            _revealMessage = $"{player.Name} missed the Daily Double.";
            _phase = GamePhase.Reveal;   // nobody else gets a shot at a Daily Double
        }
        else
        {
            _lockedOut.Add(player.Id);
            _answeringId = null;
            if (_lockedOut.Count >= _players.Count)
            {
                _revealMessage = "Nobody got it.";
                _phase = GamePhase.Reveal;
            }
            else
            {
                _phase = GamePhase.ClueOpen;   // wrong answer: the clue is live again for everyone else
            }
        }
        return Result.Success;
    });

    /// <summary>Nobody knows it. The emcee reveals the answer and nobody's score changes.</summary>
    public Result GiveUp(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can give up on a clue.");
        if (_phase != GamePhase.ClueOpen) return Result.Fail("There's no open clue to give up on.");

        _revealMessage = "Nobody got it.";
        _phase = GamePhase.Reveal;   // control stays with whoever had it
        return Result.Success;
    });

    /// <summary>After the answer has been shown, the emcee moves the game along.</summary>
    public Result Continue(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can continue.");
        if (_phase != GamePhase.Reveal) return Result.Fail("Nothing to continue from.");

        _currentCell = null;
        _currentCategoryName = null;
        _answeringId = null;
        _lockedOut.Clear();
        _revealMessage = null;

        if (_categories.Any(c => c.Cells.Any(cell => !cell.IsUsed)))
        {
            _phase = GamePhase.Board;
        }
        else if (_round == 1)
        {
            _round = 2;
            BuildBoard(_data!.Round2, Round2BaseValue, dailyDoubles: 2);
            _controlId = _players.OrderBy(p => p.Score).First().Id;   // the show gives Round 2 to whoever is behind
            _phase = GamePhase.Board;
        }
        else
        {
            BeginFinal();
        }
        return Result.Success;
    });

    // ---- Daily Double --------------------------------------------------------------------

    /// <summary>Lowest and highest wager the player in control may make right now.</summary>
    private (int Min, int Max) WagerRange(PlayerState player)
    {
        var boardMax = _round == 1 ? Round1BaseValue * CluesPerCategory : Round2BaseValue * CluesPerCategory;
        return (MinDailyDoubleWager, Math.Max(player.Score, boardMax));
    }

    public Result SubmitDailyDoubleWager(Guid playerToken, int amount) => Run(() =>
    {
        var player = FindPlayer(playerToken);
        if (player is null) return Result.Fail("Only players can wager.");
        if (_phase != GamePhase.DailyDoubleWager) return Result.Fail("It isn't time to wager.");
        if (player.Id != _controlId) return Result.Fail("Only the player who found the Daily Double can wager.");

        var (min, max) = WagerRange(player);
        if (amount < min || amount > max) return Result.Fail($"Your wager must be between ${min} and ${max}.");

        _wager = amount;
        _phase = GamePhase.DailyDoubleAnswering;
        return Result.Success;
    });

    // ---- Final Jeopardy ------------------------------------------------------------------

    private void BeginFinal()
    {
        _controlId = null;
        foreach (var p in _players)
        {
            p.InFinal = p.Score > 0;   // the show only lets contestants with a positive score play
            p.FinalWager = null;
            p.FinalAnswer = null;
            p.FinalCorrect = null;
        }
        _phase = _players.Any(p => p.InFinal) ? GamePhase.FinalWager : GamePhase.GameOver;
    }

    public Result SubmitFinalWager(Guid playerToken, int amount) => Run(() =>
    {
        var player = FindPlayer(playerToken);
        if (player is null) return Result.Fail("Only players can wager.");
        if (_phase != GamePhase.FinalWager) return Result.Fail("It isn't time to wager.");
        if (!player.InFinal) return Result.Fail("You didn't qualify for Final Jeopardy.");
        if (player.FinalWager is not null) return Result.Fail("You already locked in a wager.");
        if (amount < 0 || amount > player.Score) return Result.Fail($"Your wager must be between $0 and ${player.Score}.");

        player.FinalWager = amount;
        if (_players.Where(p => p.InFinal).All(p => p.FinalWager is not null)) _phase = GamePhase.FinalAnswer;
        return Result.Success;
    });

    public Result SubmitFinalAnswer(Guid playerToken, string answer) => Run(() =>
    {
        var player = FindPlayer(playerToken);
        if (player is null) return Result.Fail("Only players can answer.");
        if (_phase != GamePhase.FinalAnswer) return Result.Fail("It isn't time to answer.");
        if (!player.InFinal) return Result.Fail("You didn't qualify for Final Jeopardy.");
        if (player.FinalAnswer is not null) return Result.Fail("You already locked in an answer.");

        var clean = (answer ?? "").Trim();
        player.FinalAnswer = clean.Length > 100 ? clean[..100] : clean;
        if (_players.Where(p => p.InFinal).All(p => p.FinalAnswer is not null)) _phase = GamePhase.FinalJudging;
        return Result.Success;
    });

    /// <summary>
    /// Safety valve: if somebody's phone died mid-wager, the emcee can lock everyone in.
    /// Missing wagers count as $0 and missing answers count as blank.
    /// </summary>
    public Result ForceFinalAdvance(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");

        if (_phase == GamePhase.FinalWager)
        {
            foreach (var p in _players.Where(p => p.InFinal && p.FinalWager is null)) p.FinalWager = 0;
            _phase = GamePhase.FinalAnswer;
            return Result.Success;
        }
        if (_phase == GamePhase.FinalAnswer)
        {
            foreach (var p in _players.Where(p => p.InFinal && p.FinalAnswer is null)) p.FinalAnswer = "";
            _phase = GamePhase.FinalJudging;
            return Result.Success;
        }
        return Result.Fail("There's nothing to lock in right now.");
    });

    public Result JudgeFinal(Guid emceeToken, Guid playerId, bool correct) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can judge answers.");
        if (_phase != GamePhase.FinalJudging) return Result.Fail("It isn't time to judge Final Jeopardy.");

        var player = _players.FirstOrDefault(p => p.Id == playerId);
        if (player is null || !player.InFinal) return Result.Fail("That player isn't in Final Jeopardy.");
        if (player.FinalCorrect is not null) return Result.Fail("That answer was already judged.");

        var wager = player.FinalWager ?? 0;
        player.FinalCorrect = correct;
        player.Score += correct ? wager : -wager;

        if (_players.Where(p => p.InFinal).All(p => p.FinalCorrect is not null)) _phase = GamePhase.GameOver;
        return Result.Success;
    });

    // ---- Looking things up ---------------------------------------------------------------

    /// <summary>Works out who a device is from the secret token it saved when it joined.</summary>
    public Seat GetSeat(Guid token)
    {
        lock (_gate)
        {
            if (_emceeToken == token) return new Seat(Role.Emcee);
            var player = FindPlayer(token);
            return player is null ? new Seat(Role.Spectator) : new Seat(Role.Player, player.Id);
        }
    }

    public GameSnapshot Snapshot()
    {
        lock (_gate)
        {
            var inAnswerPhase = _phase is GamePhase.Answering or GamePhase.DailyDoubleWager or GamePhase.DailyDoubleAnswering;
            var inRoundPlay = _phase is GamePhase.Board or GamePhase.ClueOpen or GamePhase.Answering
                or GamePhase.DailyDoubleWager or GamePhase.DailyDoubleAnswering or GamePhase.Reveal;

            var players = _players.Select(p => new PlayerInfo(
                Id: p.Id,
                Name: p.Name,
                Score: p.Score,
                HasControl: inRoundPlay && p.Id == _controlId,
                IsAnswering: inAnswerPhase && p.Id == _answeringId,
                IsLockedOut: _lockedOut.Contains(p.Id),
                InFinal: p.InFinal,
                FinalWager: p.FinalWager,
                FinalAnswer: p.FinalAnswer,
                FinalCorrect: p.FinalCorrect)).ToList();

            var categories = _categories.Select(c => new CategoryInfo(
                c.Name,
                c.Cells.Select(cell => new CellInfo(cell.Value, cell.IsUsed)).ToList())).ToList();

            ClueInfo? clue = null;
            if (_currentCell is not null && inRoundPlay && _phase != GamePhase.Board)
            {
                // While a Daily Double wager is being made, nobody (not even the emcee) sees the clue yet.
                var hidden = _phase == GamePhase.DailyDoubleWager;
                var showWager = _currentCell.IsDailyDouble && _phase is GamePhase.DailyDoubleAnswering or GamePhase.Reveal;
                clue = new ClueInfo(
                    Category: _currentCategoryName ?? "",
                    Value: _currentCell.Value,
                    Text: hidden ? "" : _currentCell.Text,
                    Answer: hidden ? "" : _currentCell.Answer,
                    IsDailyDouble: _currentCell.IsDailyDouble,
                    Wager: showWager ? _wager : null);
            }

            var min = 0;
            var max = 0;
            if (_phase == GamePhase.DailyDoubleWager && _players.FirstOrDefault(p => p.Id == _controlId) is { } controlPlayer)
                (min, max) = WagerRange(controlPlayer);

            var inFinal = _phase is GamePhase.FinalWager or GamePhase.FinalAnswer or GamePhase.FinalJudging or GamePhase.GameOver;
            var finalClueVisible = _phase is GamePhase.FinalAnswer or GamePhase.FinalJudging or GamePhase.GameOver;
            var finalAnswerVisible = _phase is GamePhase.FinalJudging or GamePhase.GameOver;

            var winners = new List<Guid>();
            if (_phase == GamePhase.GameOver && _players.Count > 0)
            {
                var best = _players.Max(p => p.Score);
                winners = _players.Where(p => p.Score == best).Select(p => p.Id).ToList();
            }

            return new GameSnapshot(
                Code: Code,
                Phase: _phase,
                Round: _round,
                EmceeName: _emceeName,
                Players: players,
                Categories: categories,
                Clue: clue,
                RevealMessage: _revealMessage,
                MinWager: min,
                MaxWager: max,
                FinalCategory: inFinal ? _data?.Final.Category : null,
                FinalText: finalClueVisible ? _data?.Final.Text : null,
                FinalAnswer: finalAnswerVisible ? _data?.Final.Answer : null,
                Winners: winners,
                CanStart: _phase == GamePhase.Lobby && IsFull());
        }
    }

    // ---- Private helpers -----------------------------------------------------------------

    // Runs an action under the lock, then (outside the lock) tells the pages if something changed.
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

    private static bool TryCleanName(string? name, out string clean)
    {
        clean = (name ?? "").Trim();
        if (clean.Length == 0) return false;
        if (clean.Length > NameMaxLength) clean = clean[..NameMaxLength].Trim();
        return true;
    }

    private static void ValidateData(GameData data)
    {
        foreach (var round in new[] { data.Round1, data.Round2 })
        {
            if (round.Count != CategoriesPerRound)
                throw new InvalidOperationException($"Each round needs exactly {CategoriesPerRound} categories.");
            foreach (var category in round)
                if (category.Clues.Count != CluesPerCategory)
                    throw new InvalidOperationException($"Category '{category.Name}' needs exactly {CluesPerCategory} clues.");
        }
    }

    private void BuildBoard(IReadOnlyList<CategoryData> source, int baseValue, int dailyDoubles)
    {
        _categories = source.Select(c => new CategoryState
        {
            Name = c.Name,
            Cells = c.Clues.Select((clue, i) => new Cell
            {
                Value = baseValue * (i + 1),
                Text = clue.Text,
                Answer = clue.Answer
            }).ToArray()
        }).ToList();

        // Like the show: Daily Doubles hide in different categories, never on the cheapest row.
        var chosen = Enumerable.Range(0, _categories.Count).OrderBy(_ => _rng.Next()).Take(dailyDoubles);
        foreach (var categoryIndex in chosen)
            _categories[categoryIndex].Cells[_rng.Next(1, CluesPerCategory)].IsDailyDouble = true;
    }
}
