using System.Globalization;
using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.Match;

/// <summary>
/// One Match Game: an emcee, a panel of celebrities (two or more, no upper limit worth worrying about), and two teams.
/// The teams do not log in. They play out loud, and only the celebrities and the emcee use phones.
///
/// Each team gets three questions, alternating A, B, A, B, A, B. For a question the celebrities who are still in play for that team
/// type a secret answer to a fill-in-the-blank. The team says its own answer out loud. Then the celebrities reveal their answers one at a time,
/// in seat order, and the emcee rules on each: match or no match. Each match is a point for the team that is up, and a celebrity who
/// matched for a team sits out that team's remaining questions (the other team still has them).
///
/// If the teams are tied after six questions, each team gets one more question with every celebrity back in play, until one is ahead.
///
/// The team with more matches plays the Super Match. Part one: its contestant calls on three celebrities, who each write an answer to
/// a short blank ("Super ___"). The emcee ranks each answer against the three most popular answers, which pay $500, $250 and $100
/// (or $50 for no match at all); the best of the three sets the amount. Part two: the contestant picks one celebrity and tries to match
/// them exactly on one question, for ten times that amount.
///
/// Like the other games, this class knows nothing about web pages, and every public method takes one lock and returns a Result.
/// </summary>
public sealed class MatchGame
{
    public const int Teams = 2;
    public const int NameMaxLength = 20;
    public const int AnswerMaxLength = 40;
    public const int GuessMaxLength = 60;
    public const int QuestionsPerTeam = 3;
    public const int MinCelebrities = 2;
    public const int MaxCelebrities = 30;
    public const int SuperPicks = 3;
    public const int FinalMultiplier = 10;
    public const int SuperFloor = 50;

    /// <summary>What the three most popular answers to a Super Match blank pay, best first.</summary>
    public static readonly IReadOnlyList<int> SuperPayouts = [500, 250, 100];

    private sealed class Celebrity
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid Token { get; } = Guid.NewGuid();
        public required string Name { get; init; }
    }

    private readonly object _gate = new();
    private readonly IMatchContent _content;
    private readonly Random _rng;

    private readonly List<Celebrity> _celebs = [];
    private Guid? _emceeToken;
    private string? _emceeName;
    private MatchPhase _phase = MatchPhase.Lobby;
    private MatchStage _stage = MatchStage.Regular;
    private string? _message;
    private readonly string[] _teamNames = ["Team A", "Team B"];

    private readonly int[] _scores = new int[Teams];
    private readonly HashSet<Guid>[] _matched = [[], []];
    private int _turn;                  // 0 to 5 in the regular game: turn % 2 is the team, turn / 2 + 1 is the question number
    private bool _tiebreak;
    private int _tieTeam;
    private int _team = -1;
    private string _label = "";

    private List<Guid> _participants = [];
    private readonly Dictionary<Guid, string> _answers = [];
    private readonly Dictionary<Guid, RevealState> _revealed = [];
    private readonly Dictionary<Guid, int> _outcomes = [];
    private string _questionText = "";
    private string _guess = "";
    private int _revealIndex;

    private List<MatchQuestion> _questionDeck = [];
    private int _questionPos;
    private List<string> _nameDeck = [];
    private int _namePos;

    private int _superTeam = -1;
    private SuperQuestion? _super;
    private readonly bool[] _superRevealed = new bool[SuperPicks];   // posted on the board (the emcee reveals them $100, $250, $500)
    private readonly bool[] _superLit = new bool[SuperPicks];        // a celebrity's answer matched this one
    private int _superAmount;
    private Guid? _finalCelebrity;
    private bool? _finalWon;

    public MatchGame(string code, IMatchContent content, Random? rng = null)
    {
        Code = code;
        _content = content;
        _rng = rng ?? new Random();
    }

    public string Code { get; }

    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;

    /// <summary>Raised after every successful change. Pages listen to this and redraw.</summary>
    public event Action? Changed;

    public static string Money(int amount) => "$" + amount.ToString("N0", CultureInfo.InvariantCulture);

    // ---- Lobby ---------------------------------------------------------------------------

    public JoinResult JoinEmcee(string name) => Run(() =>
    {
        if (_phase != MatchPhase.Lobby) return JoinResult.Fail("The game has already started.");
        if (_emceeToken is not null) return JoinResult.Fail("This game already has an emcee.");
        if (!TryCleanText(name, NameMaxLength, out var clean)) return JoinResult.Fail("Please enter a name.");
        if (_celebs.Any(c => SameName(c.Name, clean))) return JoinResult.Fail("A celebrity is already using that name.");

        _emceeToken = Guid.NewGuid();
        _emceeName = clean;
        return JoinResult.Success(_emceeToken.Value);
    });

    /// <summary>Takes a seat on the panel. Seat order is join order, and it is the order the celebrities reveal in. Latecomers are welcome: they play from the next question.</summary>
    public JoinResult JoinCelebrity(string name) => Run(() =>
    {
        if (_celebs.Count >= MaxCelebrities) return JoinResult.Fail("The panel is full.");
        if (!TryCleanText(name, NameMaxLength, out var clean)) return JoinResult.Fail("Please enter a name.");
        if (_celebs.Any(c => SameName(c.Name, clean)) || (_emceeName is not null && SameName(_emceeName, clean)))
            return JoinResult.Fail("Someone is already using that name.");

        var celeb = new Celebrity { Name = clean };
        _celebs.Add(celeb);
        return JoinResult.Success(celeb.Token);
    });

    public Result RemoveCelebrity(Guid emceeToken, Guid celebrityId) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != MatchPhase.Lobby) return Result.Fail("Celebrities can only be removed before the game starts.");
        return _celebs.RemoveAll(c => c.Id == celebrityId) > 0 ? Result.Success : Result.Fail("That celebrity is already gone.");
    });

    public Result SetTeamNames(Guid emceeToken, string? teamA, string? teamB) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != MatchPhase.Lobby) return Result.Fail("Team names can only change before the game starts.");
        if (!TryCleanText(teamA, NameMaxLength, out var a) || !TryCleanText(teamB, NameMaxLength, out var b))
            return Result.Fail("Each team needs a name.");
        if (SameName(a, b)) return Result.Fail("The teams need different names.");
        _teamNames[0] = a;
        _teamNames[1] = b;
        return Result.Success;
    });

    public Result Start(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can start the game.");
        if (_phase != MatchPhase.Lobby) return Result.Fail("The game has already started.");
        if (_celebs.Count < MinCelebrities) return Result.Fail($"You need at least {MinCelebrities} celebrities.");
        BeginGame();
        return Result.Success;
    });

    /// <summary>After the game: play again with the same panel.</summary>
    public Result Restart(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != MatchPhase.GameOver) return Result.Fail("The game isn't over yet.");
        BeginGame();
        return Result.Success;
    });

    // ---- A question ----------------------------------------------------------------------

    /// <summary>Don't like this question? Draw another one before it is asked.</summary>
    public Result SwapQuestion(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != MatchPhase.Ready) return Result.Fail("There's no question waiting to be asked.");
        DrawQuestion();
        return Result.Success;
    });

    /// <summary>The emcee reads the question out and the celebrities start typing.</summary>
    public Result AskQuestion(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != MatchPhase.Ready) return Result.Fail("There's no question waiting to be asked.");
        _phase = MatchPhase.Answering;
        _message = "Celebrities: type your answers!";
        return Result.Success;
    });

    /// <summary>A celebrity in play types (or retypes) their secret answer.</summary>
    public Result Answer(Guid token, string? text) => Run(() =>
    {
        if (_phase != MatchPhase.Answering) return Result.Fail("Answers aren't open right now.");
        var celeb = FindCelebrity(token);
        if (celeb is null) return Result.Fail("Only a celebrity can do that.");
        if (!_participants.Contains(celeb.Id)) return Result.Fail("You're sitting this one out.");
        if (!TryCleanText(text, AnswerMaxLength, out var clean)) return Result.Fail("Type your answer first.");
        _answers[celeb.Id] = clean;
        return Result.Success;
    });

    /// <summary>
    /// The emcee closes the answers. Celebrities who typed nothing sit this question out. Then the team (or the contestant) says
    /// its answer out loud; in the first part of the Super Match there is nothing to guess, so it goes straight to the reveal.
    /// </summary>
    public Result CloseAnswers(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != MatchPhase.Answering) return Result.Fail("Answers aren't open right now.");
        var answered = _participants.Where(_answers.ContainsKey).ToList();
        if (answered.Count == 0) return Result.Fail("Nobody has answered yet.");
        _participants = answered;

        if (_stage == MatchStage.Super)
        {
            BeginReveal();
            _message = "Reveal the answers one at a time, and rank each against the popular answers.";
        }
        else
        {
            _phase = MatchPhase.Guessing;
            _message = _stage == MatchStage.Final
                ? "The contestant says their answer out loud."
                : $"{_teamNames[_team]}: say your answer out loud!";
        }
        return Result.Success;
    });

    /// <summary>Optional: the emcee types in what the team or contestant said, so the room can see it next to the celebrities' answers.</summary>
    public Result SetGuess(Guid emceeToken, string? text) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase is not (MatchPhase.Answering or MatchPhase.Guessing or MatchPhase.Reveal) || _stage == MatchStage.Super)
            return Result.Fail("There's no team answer to record right now.");
        TryCleanText(text, GuessMaxLength, out var clean);
        _guess = clean;
        return Result.Success;
    });

    /// <summary>The team has spoken: time for the celebrities to reveal.</summary>
    public Result StartReveal(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != MatchPhase.Guessing) return Result.Fail("It isn't time to reveal yet.");
        BeginReveal();
        return Result.Success;
    });

    /// <summary>The celebrity whose turn it is shows their answer (and gets to explain it, out loud).</summary>
    public Result RevealAnswer(Guid token) => Run(() =>
    {
        var celeb = FindCelebrity(token);
        if (celeb is null) return Result.Fail("Only a celebrity can do that.");
        return RevealCurrent(celeb.Id);
    });

    /// <summary>The emcee reveals for the celebrity whose turn it is (their phone isn't working, or they're taking too long).</summary>
    public Result RevealFor(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != MatchPhase.Reveal) return Result.Fail("It isn't time to reveal.");
        return RevealCurrent(_participants[_revealIndex]);
    });

    /// <summary>The emcee rules on the answer that was just revealed.</summary>
    public Result Judge(Guid emceeToken, bool match) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can rule on answers.");
        if (_phase != MatchPhase.Reveal) return Result.Fail("There's nothing to rule on.");
        if (_stage == MatchStage.Super) return Result.Fail("Rank this answer against the popular answers instead.");
        if (CurrentState() != RevealState.Shown) return Result.Fail("The answer hasn't been revealed yet.");

        var id = _participants[_revealIndex];
        _revealed[id] = RevealState.Judged;
        _outcomes[id] = match ? 1 : 0;
        if (match && _stage is MatchStage.Regular or MatchStage.Tiebreak)
        {
            _scores[_team]++;
            if (_stage == MatchStage.Regular) _matched[_team].Add(id);
        }
        AdvanceReveal();
        return Result.Success;
    });

    /// <summary>
    /// First part of the Super Match: which of the three popular answers (1 to 3, best first) the answer just revealed is, or 0 for none of them.
    /// </summary>
    public Result Rank(Guid emceeToken, int rank) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can rule on answers.");
        if (_phase != MatchPhase.Reveal || _stage != MatchStage.Super) return Result.Fail("There's nothing to rank.");
        if (rank is < 0 or > SuperPicks) return Result.Fail("Pick one of the three popular answers, or none.");
        if (CurrentState() != RevealState.Shown) return Result.Fail("The answer hasn't been revealed yet.");

        var id = _participants[_revealIndex];
        _revealed[id] = RevealState.Judged;
        _outcomes[id] = rank;
        if (rank > 0) _superLit[rank - 1] = true;
        AdvanceReveal();
        return Result.Success;
    });

    /// <summary>
    /// After the celebrities' answers are ranked, the emcee posts the three popular answers one at a time, lowest first ($100, $250, $500).
    /// Every row is posted even if nobody matched it, because the room is curious. A row a celebrity matched lights up.
    /// </summary>
    public Result RevealSuperRow(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != MatchPhase.SuperResult) return Result.Fail("There's no board to reveal.");
        var next = Enumerable.Range(0, SuperPicks).Reverse().Cast<int?>().FirstOrDefault(i => !_superRevealed[i.Value]);
        if (next is null) return Result.Fail("The whole board is already up.");
        _superRevealed[next.Value] = true;
        if (_superRevealed.All(r => r))
        {
            var best = _participants.Select(id => _outcomes[id]).Where(r => r > 0).DefaultIfEmpty(0).Min();
            _message = best > 0
                ? $"Popular answer number {best}! That's {Money(_superAmount)}, so the final match is for {Money(FinalStake)}."
                : $"No popular answers this time. A consolation {Money(_superAmount)}, so the final match is for {Money(FinalStake)}.";
        }
        return Result.Success;
    });

    /// <summary>The emcee changes their mind about a ruling made earlier on this question (a misclick happens at parties).</summary>
    public Result Rejudge(Guid emceeToken, Guid celebrityId) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase is not (MatchPhase.Reveal or MatchPhase.QuestionDone) || _stage is not (MatchStage.Regular or MatchStage.Tiebreak))
            return Result.Fail("There's no ruling to change.");
        if (!_revealed.TryGetValue(celebrityId, out var state) || state != RevealState.Judged)
            return Result.Fail("That answer hasn't been ruled on yet.");

        var wasMatch = _outcomes[celebrityId] == 1;
        _outcomes[celebrityId] = wasMatch ? 0 : 1;
        _scores[_team] += wasMatch ? -1 : 1;
        if (_stage == MatchStage.Regular)
        {
            if (wasMatch) _matched[_team].Remove(celebrityId); else _matched[_team].Add(celebrityId);
        }
        return Result.Success;
    });

    /// <summary>Moves on: from a finished question to the next one, or from the posted Super Match answers to the final match.</summary>
    public Result Continue(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        switch (_phase)
        {
            case MatchPhase.QuestionDone:
                if (_tiebreak) _tieTeam++; else _turn++;
                PrepareNext();
                return Result.Success;

            case MatchPhase.SuperResult:
                if (!_superRevealed.All(r => r)) return Result.Fail("Reveal the whole board first.");
                _phase = MatchPhase.FinalPick;
                _message = $"Playing for {Money(FinalStake)}! The contestant picks one celebrity to match exactly.";
                return Result.Success;

            default:
                return Result.Fail("There's nothing to continue from.");
        }
    });

    // ---- The Super Match -----------------------------------------------------------------

    /// <summary>The contestant calls on three celebrities (or all of them, if there are fewer than three). Any celebrity can be picked.</summary>
    public Result PickSuperCelebrities(Guid emceeToken, IEnumerable<Guid> celebrityIds) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != MatchPhase.SuperPick) return Result.Fail("It isn't time to pick celebrities.");
        var ids = celebrityIds.Distinct().ToList();
        var needed = Math.Min(SuperPicks, _celebs.Count);
        if (ids.Count != needed) return Result.Fail($"Pick {needed} different celebrities.");
        if (ids.Any(id => _celebs.All(c => c.Id != id))) return Result.Fail("Pick celebrities from the panel.");

        _participants = _celebs.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToList();
        ClearQuestionState();
        _questionText = _super!.Text;
        _phase = MatchPhase.Answering;
        _message = "Celebrities: type your answers!";
        return Result.Success;
    });

    /// <summary>The contestant picks the one celebrity they will try to match exactly.</summary>
    public Result PickFinalCelebrity(Guid emceeToken, Guid celebrityId) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != MatchPhase.FinalPick) return Result.Fail("It isn't time to pick a celebrity.");
        if (_celebs.All(c => c.Id != celebrityId)) return Result.Fail("Pick a celebrity from the panel.");

        _stage = MatchStage.Final;
        _finalCelebrity = celebrityId;
        _participants = [celebrityId];
        ClearQuestionState();
        _questionText = NextQuestion().With(NextName());
        _phase = MatchPhase.Answering;
        _message = "One celebrity, one question. Match exactly!";
        return Result.Success;
    });

    public int FinalStake { get { lock (_gate) return _superAmount * FinalMultiplier; } }

    // ---- Seats and snapshots -------------------------------------------------------------

    public Seat GetSeat(Guid token)
    {
        lock (_gate)
        {
            if (_emceeToken == token) return new Seat(Role.Emcee);
            var celeb = FindCelebrity(token);
            return celeb is null ? new Seat(Role.Spectator) : new Seat(Role.Player, celeb.Id);
        }
    }

    public MatchSnapshot Snapshot()
    {
        lock (_gate)
        {
            var inReveal = _phase is MatchPhase.Reveal or MatchPhase.QuestionDone or MatchPhase.SuperResult or MatchPhase.GameOver;
            var celebs = _celebs.Select((c, i) =>
            {
                var state = inReveal ? _revealed.GetValueOrDefault(c.Id, RevealState.Hidden) : RevealState.Hidden;
                var answer = _answers.GetValueOrDefault(c.Id, "");
                return new CelebrityInfo(
                    c.Id, i + 1, c.Name,
                    Playing: _participants.Contains(c.Id) && _phase != MatchPhase.Lobby,
                    Submitted: _answers.ContainsKey(c.Id),
                    Answer: answer,
                    Reveal: state,
                    Outcome: _outcomes.GetValueOrDefault(c.Id, 0),
                    SuggestedRank: _stage == MatchStage.Super && _super is not null ? MatchText.SuggestRank(_super, answer) : 0,
                    OutForTeam: _stage == MatchStage.Regular && _team >= 0 && _matched[_team].Contains(c.Id),
                    MatchedA: _matched[0].Contains(c.Id),
                    MatchedB: _matched[1].Contains(c.Id));
            }).ToList();

            var rows = _super is null
                ? []
                : Enumerable.Range(0, SuperPicks).Select(i => new SuperRowInfo(i + 1, SuperPayouts[i], _super.Shown(i), _superRevealed[i], _superRevealed[i] && _superLit[i])).ToList();

            // Until the board is up the stake would give away which popular answer was matched.
            var boardHidden = _phase == MatchPhase.SuperResult && !_superRevealed.All(r => r);

            return new MatchSnapshot(
                Code: Code,
                Phase: _phase,
                Stage: _stage,
                EmceeName: _emceeName,
                Celebrities: celebs,
                TeamNames: _teamNames.ToList(),
                Scores: _scores.ToList(),
                Team: _team,
                Label: _label,
                QuestionText: _questionText,
                Guess: _guess,
                RevealIndex: _revealIndex,
                SuperTeam: _superTeam,
                SuperRows: rows,
                SuperAmount: boardHidden ? 0 : _superAmount,
                FinalStake: boardHidden ? 0 : _superAmount * FinalMultiplier,
                FinalCelebrityId: _finalCelebrity,
                FinalWon: _finalWon,
                Message: _message,
                CanStart: _phase == MatchPhase.Lobby && _emceeToken is not null && _celebs.Count >= MinCelebrities);
        }
    }

    // ---- Inside the game -----------------------------------------------------------------

    private void BeginGame()
    {
        Array.Clear(_scores);
        foreach (var set in _matched) set.Clear();
        _turn = 0;
        _tiebreak = false;
        _tieTeam = 0;
        _team = -1;
        _stage = MatchStage.Regular;
        _superTeam = -1;
        _super = null;
        Array.Clear(_superRevealed);
        Array.Clear(_superLit);
        _superAmount = 0;
        _finalCelebrity = null;
        _finalWon = null;
        _questionDeck = _content.CreateQuestions(_rng, 30).ToList();
        _questionPos = 0;
        _nameDeck = [];
        _namePos = 0;
        ClearQuestionState();
        PrepareNext(note: null);
    }

    /// <summary>Works out whose question is next, or that the questions are over, and sets up the "ready" screen.</summary>
    private void PrepareNext(string? note = null)
    {
        while (!_tiebreak && _turn < QuestionsPerTeam * Teams)
        {
            var team = _turn % Teams;
            var pool = _celebs.Where(c => !_matched[team].Contains(c.Id)).Select(c => c.Id).ToList();
            if (pool.Count == 0)
            {
                note = $"{_teamNames[team]} has matched everyone, so its question is skipped.";
                _turn++;
                continue;
            }
            BeginReady(team, $"Question {_turn / Teams + 1} of {QuestionsPerTeam}", pool, MatchStage.Regular, note);
            return;
        }

        if (!_tiebreak)
        {
            if (_scores[0] != _scores[1]) { BeginSuper(); return; }
            _tiebreak = true;
            _tieTeam = 0;
        }

        while (true)
        {
            if (_tieTeam < Teams)
            {
                BeginReady(_tieTeam, "Tiebreaker", _celebs.Select(c => c.Id).ToList(), MatchStage.Tiebreak,
                    note: $"It's tied at {_scores[0]}! Everyone is back in play.");
                return;
            }
            if (_scores[0] != _scores[1]) { BeginSuper(); return; }
            _tieTeam = 0;      // still tied: another pair of tiebreakers
        }
    }

    private void BeginReady(int team, string label, List<Guid> pool, MatchStage stage, string? note)
    {
        _stage = stage;
        _team = team;
        _label = label;
        _participants = pool;
        ClearQuestionState();
        DrawQuestion();
        _phase = MatchPhase.Ready;
        _message = (note is null ? "" : note + " ") + $"{_teamNames[team]} is up: {label.ToLowerInvariant()}.";
    }

    private void BeginSuper()
    {
        _stage = MatchStage.Super;
        _superTeam = _scores[0] > _scores[1] ? 0 : 1;
        _team = _superTeam;
        _label = "Super Match";
        _super = _content.CreateSupers(_rng, 1)[0];
        Array.Clear(_superRevealed);
        Array.Clear(_superLit);
        _participants = [];
        ClearQuestionState();
        _phase = MatchPhase.SuperPick;
        _message = $"{_teamNames[_superTeam]} wins {_scores[_superTeam]} to {_scores[1 - _superTeam]} and plays the Super Match! "
            + $"The contestant calls on {Math.Min(SuperPicks, _celebs.Count)} celebrities.";
    }

    private void BeginReveal()
    {
        _phase = MatchPhase.Reveal;
        _revealIndex = 0;
        _revealed.Clear();
        _outcomes.Clear();
        foreach (var id in _participants) _revealed[id] = RevealState.Hidden;
    }

    private Result RevealCurrent(Guid celebrityId)
    {
        if (_phase != MatchPhase.Reveal) return Result.Fail("It isn't time to reveal.");
        if (_participants[_revealIndex] != celebrityId) return Result.Fail("It isn't your turn to reveal.");
        if (CurrentState() != RevealState.Hidden) return Result.Fail("That answer is already showing.");
        _revealed[celebrityId] = RevealState.Shown;
        return Result.Success;
    }

    private RevealState CurrentState() => _revealed.GetValueOrDefault(_participants[_revealIndex], RevealState.Hidden);

    private void AdvanceReveal()
    {
        _revealIndex++;
        if (_revealIndex < _participants.Count) return;
        _revealIndex = _participants.Count;       // past the end: nobody is up

        switch (_stage)
        {
            case MatchStage.Super:
                var best = _participants.Select(id => _outcomes[id]).Where(r => r > 0).DefaultIfEmpty(0).Min();
                _superAmount = best > 0 ? SuperPayouts[best - 1] : SuperFloor;
                _phase = MatchPhase.SuperResult;
                _message = "Now the board! Reveal the popular answers, lowest first.";
                break;

            case MatchStage.Final:
                _finalWon = _outcomes[_participants[0]] == 1;
                _phase = MatchPhase.GameOver;
                _message = _finalWon == true
                    ? $"A MATCH! {_teamNames[_superTeam]} wins {Money(FinalStake)}!"
                    : $"No match. {_teamNames[_superTeam]} goes home with {Money(_superAmount)}.";
                break;

            default:
                var matches = _participants.Count(id => _outcomes.GetValueOrDefault(id) == 1);
                _phase = MatchPhase.QuestionDone;
                _message = $"{_teamNames[_team]} matched {matches} of {_participants.Count}. "
                    + $"{_teamNames[0]} {_scores[0]}, {_teamNames[1]} {_scores[1]}.";
                break;
        }
    }

    private void ClearQuestionState()
    {
        _answers.Clear();
        _revealed.Clear();
        _outcomes.Clear();
        _guess = "";
        _revealIndex = 0;
        _questionText = "";
    }

    private void DrawQuestion() => _questionText = NextQuestion().With(NextName());

    private MatchQuestion NextQuestion()
    {
        if (_questionPos >= _questionDeck.Count)
        {
            _questionDeck = _content.CreateQuestions(_rng, 30).ToList();
            _questionPos = 0;
        }
        return _questionDeck[_questionPos++];
    }

    /// <summary>Family names come out in a shuffled order, so nobody repeats until everybody has had a turn.</summary>
    private string NextName()
    {
        var names = _content.Names;
        if (names.Count == 0) return "Donald";
        if (_namePos >= _nameDeck.Count)
        {
            _nameDeck = names.OrderBy(_ => _rng.Next()).ToList();
            _namePos = 0;
        }
        return _nameDeck[_namePos++];
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

    private Celebrity? FindCelebrity(Guid token) => _celebs.FirstOrDefault(c => c.Token == token);

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool TryCleanText(string? text, int max, out string clean)
    {
        // Collapse whitespace and drop control characters, so a pasted tab or newline can't mess up a screen.
        var chars = (text ?? "").Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray();
        clean = string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > max) clean = clean[..max].Trim();
        return clean.Length > 0;
    }
}
