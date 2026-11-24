using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.Feud;

/// <summary>
/// One game of Family Feud: two families, one emcee, a TV with a laptop mouse in front of it. Nobody but the emcee has a phone. The emcee has
/// the survey answers in his hand and taps the one a family said, or a strike; everything the families do is said out loud.
///
/// Each round: a face-off (one player from each family hits their mouse button, the left for family 1 and the right for family 2), the
/// winner plays or passes, and the family in control keeps naming answers until they clear the board (they win the bank) or collect three
/// strikes (the other family gets one guess to steal it). Rounds one and two are worth face value, round three double, and every round after
/// that triple, until a family reaches 300. Then that family plays Fast Money: two players, five questions each, 200 points to win.
///
/// Like the other games this class knows nothing about web pages, and every public method takes one lock and returns a Result instead of
/// throwing. Every emcee action can be undone.
/// </summary>
public sealed class FeudGame
{
    private const int None = -1;
    private const int Struck = -2;
    private const string NoAnswer = "(no answer)";

    private sealed record FastEntry(string Text, int Points, int Index);

    /// <summary>Everything that changes during play, so Undo can put it all back by swapping one object.</summary>
    private sealed class State
    {
        public FeudPhase Phase = FeudPhase.Lobby;
        public string[] Names = ["Family 1", "Family 2"];
        public int[] Scores = new int[FeudRules.Teams];

        public int Round;
        public int BoardIdx;                                    // index into the regular boards
        public int NextBoard;                                   // the next regular board not yet used
        public bool[] Revealed = [];
        public int Strikes;
        public int StrikeEvents;                                // every strike ever given this game, so a screen can tell a new one happened
        public int FirstBuzz = None;
        public int Turn = None;
        public int[] Face = [None, None];                       // each family's face-off result: None, Struck, or the rank they named
        public int FaceWinner = None;
        public int Control = None;
        public int Stealer = None;
        public int RoundWinner = None;

        public int FastTeam = None;
        public string[] FastNames = ["Player 1", "Player 2"];
        public int FastPlayer;
        public int FastQ;
        public FastEntry?[] FastEntries = new FastEntry?[FeudRules.FastQuestions * 2];   // [player * 5 + question]
        public DateTime? FastStartedUtc;
        public bool FastTimeUp;
        public int FastStep;

        public string? Message;

        public State Clone()
        {
            var copy = (State)MemberwiseClone();
            copy.Names = (string[])Names.Clone();
            copy.Scores = (int[])Scores.Clone();
            copy.Revealed = (bool[])Revealed.Clone();
            copy.Face = (int[])Face.Clone();
            copy.FastNames = (string[])FastNames.Clone();
            copy.FastEntries = (FastEntry?[])FastEntries.Clone();
            return copy;
        }
    }

    private const int HistoryLimit = 80;
    private const string NotEmcee = "Only the emcee can do that.";

    private readonly object _gate = new();
    private readonly IFeudContent _content;
    private readonly Random _rng;
    private readonly double _firstSeconds;
    private readonly double _secondSeconds;
    private readonly List<State> _history = [];
    private Guid? _emceeToken;
    private IReadOnlyList<FeudBoard> _rounds = [];
    private IReadOnlyList<FeudBoard> _fast = [];
    private State _s = new();

    public FeudGame(string code, IFeudContent content, Random? rng = null, double firstSeconds = FeudRules.FastSecondsFirst, double secondSeconds = FeudRules.FastSecondsSecond)
    {
        Code = code;
        _content = content;
        _rng = rng ?? new Random();
        _firstSeconds = firstSeconds;
        _secondSeconds = secondSeconds;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string Code { get; }
    public DateTime CreatedAtUtc { get; }
    public event Action? Changed;

    public FeudPhase Phase { get { lock (_gate) return _s.Phase; } }

    // ---- The lobby -----------------------------------------------------------------------

    /// <summary>Whoever taps the button first is the emcee. There is no password: the QR code is only on the TV.</summary>
    public JoinResult JoinEmcee() => Run(() =>
    {
        if (_emceeToken is not null) return JoinResult.Fail("This game already has an emcee.");
        _emceeToken = Guid.NewGuid();
        return JoinResult.Success(_emceeToken.Value);
    });

    public Result SetTeamName(Guid token, int team, string? name) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.Lobby) return Result.Fail("The names are set once the game starts.");
        if (team is < 0 or >= FeudRules.Teams) return Result.Fail("Which family?");
        var clean = Clean(name, FeudRules.NameMaxLength);
        _s.Names[team] = clean.Length > 0 ? clean : $"Family {team + 1}";
        return Result.Success;
    });

    public Result Start(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.Lobby) return Result.Fail("The game has already started.");
        if (!_content.TryDeal(_rng, FeudRules.BoardsPerGame, out var boards, out var error)) return Result.Fail(error);

        var fastCount = FeudRules.FastQuestions;
        _rounds = boards.Take(boards.Count - fastCount).ToList();
        _fast = boards.Skip(boards.Count - fastCount).ToList();
        _content.MarkUsed(boards.Select(b => b.Id));

        _s.Round = 1;
        _s.NextBoard = 1;
        BeginRound(0);
        return Result.Success;
    });

    // ---- A round -------------------------------------------------------------------------

    private FeudBoard Board => _rounds[_s.BoardIdx];

    private void BeginRound(int boardIdx)
    {
        _s.BoardIdx = boardIdx;
        _s.Revealed = new bool[Board.Answers.Count];
        _s.Strikes = 0;
        _s.FirstBuzz = None;
        _s.Turn = None;
        _s.Face = [None, None];
        _s.FaceWinner = None;
        _s.Control = None;
        _s.Stealer = None;
        _s.RoundWinner = None;
        _s.Phase = FeudPhase.RoundIntro;
        var label = FeudRules.MultiplierLabel(_s.Round);
        _s.Message = label.Length > 0 ? $"Round {_s.Round}: {label}!" : $"Round {_s.Round}.";
    }

    /// <summary>The emcee is about to read the question: the TV shows it and the two players get ready at the mouse.</summary>
    public Result ReadQuestion(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.RoundIntro) return Result.Fail("The question is already up.");
        Remember();
        _s.Phase = FeudPhase.FaceOffBuzz;
        _s.Message = "Face-off! Left click for family 1, right click for family 2.";
        return Result.Success;
    });

    /// <summary>
    /// A mouse click on the TV. The first click wins the buzz; anything later is ignored, which is a normal thing to happen at a face-off, so it
    /// is not an error either way. There is no token: the TV is the one thing that calls this.
    /// </summary>
    public Result Buzz(int team) => Run(() =>
    {
        if (team is < 0 or >= FeudRules.Teams) return Result.Fail("Which family?");
        if (_s.Phase != FeudPhase.FaceOffBuzz) return Result.Fail("It's not time to buzz.");
        Remember();
        TakeBuzz(team);
        return Result.Success;
    });

    /// <summary>The emcee's fallback if the mouse misbehaves: say who got there first.</summary>
    public Result AssignBuzz(Guid token, int team) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.FaceOffBuzz) return Result.Fail("It's not time to buzz.");
        if (team is < 0 or >= FeudRules.Teams) return Result.Fail("Which family?");
        Remember();
        TakeBuzz(team);
        return Result.Success;
    });

    private void TakeBuzz(int team)
    {
        _s.FirstBuzz = team;
        _s.Turn = team;
        _s.Phase = FeudPhase.FaceOffAnswer;
        _s.Message = $"{Name(team)} buzzed in first!";
    }

    /// <summary>The player's answer is on the board: reveal it. What it means depends on the phase (face-off, play or steal).</summary>
    public Result Right(Guid token, int index) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase is not (FeudPhase.FaceOffAnswer or FeudPhase.Play or FeudPhase.Steal)) return Result.Fail("There's nothing to mark right now.");
        if (index < 0 || index >= Board.Answers.Count) return Result.Fail("There's no such answer.");
        if (_s.Revealed[index]) return Result.Fail("That answer is already up.");
        Remember();
        _s.Revealed[index] = true;
        var answer = Board.Answers[index];

        switch (_s.Phase)
        {
            case FeudPhase.FaceOffAnswer:
                _s.Message = $"{answer.Text}! Number {index + 1}.";
                RecordFace(index);
                break;

            case FeudPhase.Play:
                _s.Message = $"{answer.Text}! {answer.Points}.";
                if (_s.Revealed.All(r => r)) EndRound(_s.Control, "cleared the board");
                break;

            case FeudPhase.Steal:
                EndRound(_s.Stealer, "stole it");
                break;
        }
        return Result.Success;
    });

    /// <summary>A wrong answer. In a face-off it only loses that player's chance; in play it is one of the three strikes.</summary>
    public Result Strike(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase is not (FeudPhase.FaceOffAnswer or FeudPhase.Play or FeudPhase.Steal)) return Result.Fail("There's nothing to mark right now.");
        Remember();

        switch (_s.Phase)
        {
            case FeudPhase.FaceOffAnswer:
                _s.Message = $"{Name(_s.Turn)}: strike!";
                _s.StrikeEvents++;
                RecordFace(Struck);
                break;

            case FeudPhase.Play:
                _s.StrikeEvents++;
                _s.Strikes++;
                if (_s.Strikes >= FeudRules.StrikesAllowed)
                {
                    _s.Stealer = 1 - _s.Control;
                    _s.Phase = FeudPhase.Steal;
                    _s.Message = $"Three strikes! {Name(_s.Stealer)} can steal {Bank()} points with one guess.";
                }
                else _s.Message = $"Strike {_s.Strikes}!";
                break;

            case FeudPhase.Steal:
                _s.StrikeEvents++;
                EndRound(_s.Control, "kept it");
                break;
        }
        return Result.Success;
    });

    /// <summary>Records what the family up in the face-off said (a rank, or <see cref="Struck"/>) and works out whether the face-off is decided.</summary>
    private void RecordFace(int result)
    {
        var team = _s.Turn;
        var other = 1 - team;
        _s.Face[team] = result;

        if (result == 0) { WinFaceOff(team); return; }               // the number one answer wins it outright
        if (_s.Face[other] == None) { _s.Turn = other; return; }      // the other player still gets a go

        var mine = _s.Face[team];
        var theirs = _s.Face[other];
        if (mine >= 0 && theirs >= 0) WinFaceOff(mine < theirs ? team : other);
        else if (mine >= 0) WinFaceOff(team);
        else if (theirs >= 0) WinFaceOff(other);
        else
        {
            // Both missed: back to the first player, with the question still open.
            _s.Face = [None, None];
            _s.Turn = _s.FirstBuzz;
            _s.Message = "Both families missed! Back to the first player.";
        }
    }

    private void WinFaceOff(int team)
    {
        _s.FaceWinner = team;
        _s.Turn = None;
        _s.Phase = FeudPhase.ControlChoice;
        _s.Message = $"{Name(team)} wins the face-off. Play or pass?";
    }

    /// <summary>The face-off winner plays the board, or passes it to the other family.</summary>
    public Result Choose(Guid token, bool play) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.ControlChoice) return Result.Fail("Nobody has won the face-off yet.");
        Remember();
        _s.Control = play ? _s.FaceWinner : 1 - _s.FaceWinner;
        _s.Strikes = 0;
        _s.Phase = FeudPhase.Play;
        _s.Message = $"{Name(_s.Control)} will play. Name an answer!";
        if (_s.Revealed.All(r => r)) EndRound(_s.Control, "cleared the board");
        return Result.Success;
    });

    private void EndRound(int winner, string how)
    {
        _s.RoundWinner = winner;
        _s.Phase = FeudPhase.RoundOver;
        _s.Message = $"{Name(winner)} {how} and wins {Bank()} points!";
    }

    /// <summary>Skips a question the emcee doesn't like, before anyone has scored on it.</summary>
    public Result SkipQuestion(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase is not (FeudPhase.RoundIntro or FeudPhase.FaceOffBuzz or FeudPhase.FaceOffAnswer))
            return Result.Fail("Too late to skip this one.");
        if (_s.Revealed.Any(r => r)) return Result.Fail("Too late to skip this one.");
        if (_s.NextBoard >= _rounds.Count) return Result.Fail("There are no spare questions left in this game.");
        Remember();
        BeginRound(_s.NextBoard++);
        _s.Message = "New question. " + _s.Message;
        return Result.Success;
    });

    /// <summary>The celebration is over: the round's points go to the winner, and the game moves on.</summary>
    public Result Continue(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.RoundOver) return Result.Fail("The round isn't over yet.");
        Remember();
        _s.Scores[_s.RoundWinner] += Bank();

        var leader = _s.Scores[0] == _s.Scores[1] ? None : _s.Scores[0] > _s.Scores[1] ? 0 : 1;
        var someoneHasIt = leader >= 0 && _s.Scores[leader] >= FeudRules.WinScore;
        var outOfBoards = _s.NextBoard >= _rounds.Count;

        if (someoneHasIt || outOfBoards)
        {
            // Out of questions with nobody at 300 (it takes a very long game): the leader goes, and a tie is settled by a coin.
            var winner = leader >= 0 ? leader : _rng.Next(FeudRules.Teams);
            BeginFast(winner, someoneHasIt);
            return Result.Success;
        }

        _s.Round++;
        BeginRound(_s.NextBoard++);
        return Result.Success;
    });

    // ---- Fast Money ----------------------------------------------------------------------

    private void BeginFast(int team, bool reached)
    {
        _s.FastTeam = team;
        _s.FastPlayer = 0;
        _s.FastQ = 0;
        _s.FastEntries = new FastEntry?[FeudRules.FastQuestions * 2];
        _s.FastStartedUtc = null;
        _s.FastTimeUp = false;
        _s.FastStep = 0;
        _s.Phase = FeudPhase.FastIntro;
        _s.Message = reached
            ? $"{Name(team)} wins the game with {_s.Scores[team]} points! Pick two players for Fast Money."
            : $"The questions are used up, and {Name(team)} is ahead. They play Fast Money!";
    }

    public Result SetFastPlayer(Guid token, int which, string? name) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.FastIntro) return Result.Fail("The players are named before Fast Money starts.");
        if (which is < 0 or > 1) return Result.Fail("Which player?");
        var clean = Clean(name, FeudRules.NameMaxLength);
        _s.FastNames[which] = clean.Length > 0 ? clean : $"Player {which + 1}";
        return Result.Success;
    });

    /// <summary>The first player steps up (the second is out of earshot).</summary>
    public Result BeginFastMoney(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.FastIntro) return Result.Fail("Fast Money has already started.");
        Remember();
        _s.Phase = FeudPhase.FastReady;
        _s.Message = $"{_s.FastNames[0]}, you have {_firstSeconds:0} seconds. Player 2 stays out of earshot!";
        return Result.Success;
    });

    public Result StartFastClock(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.FastReady) return Result.Fail("The clock can't start now.");
        Remember();
        _s.Phase = FeudPhase.FastPlay;
        _s.FastQ = 0;
        _s.FastStartedUtc = DateTime.UtcNow;
        _s.FastTimeUp = false;
        _s.Message = $"{_s.FastNames[_s.FastPlayer]}: go!";
        return Result.Success;
    });

    /// <summary>The player's answer matches one of the survey answers: tap it.</summary>
    public Result FastPick(Guid token, int index) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.FastPlay) return Result.Fail("Nobody is playing right now.");
        var board = _fast[_s.FastQ];
        if (index < 0 || index >= board.Answers.Count) return Result.Fail("There's no such answer.");
        var answer = board.Answers[index];
        var dup = FastDuplicate(answer.Text);
        if (dup is not null) return Result.Fail(dup);
        Remember();
        RecordFast(new FastEntry(answer.Text, answer.Points, index));
        return Result.Success;
    });

    /// <summary>The emcee typed what the player said. If it matches a survey answer it scores that answer's points; otherwise it scores nothing.</summary>
    public Result FastType(Guid token, string? text) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.FastPlay) return Result.Fail("Nobody is playing right now.");
        var clean = Clean(text, FeudRules.AnswerMaxLength);
        if (clean.Length == 0) return Result.Fail("Type what they said, or tap Pass.");
        var dup = FastDuplicate(clean);
        if (dup is not null) return Result.Fail(dup);

        var board = _fast[_s.FastQ];
        var key = FeudContentRules.Normalize(clean);
        var match = board.Answers.Select((a, i) => (a, i)).FirstOrDefault(x => FeudContentRules.Normalize(x.a.Text) == key);
        Remember();
        RecordFast(match.a is null ? new FastEntry(clean, 0, None) : new FastEntry(clean, match.a.Points, match.i));
        return Result.Success;
    });

    /// <summary>The player has no answer for this one.</summary>
    public Result FastPass(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.FastPlay) return Result.Fail("Nobody is playing right now.");
        Remember();
        RecordFast(new FastEntry(NoAnswer, 0, None));
        return Result.Success;
    });

    /// <summary>The second player can't say what the first said.</summary>
    private string? FastDuplicate(string text)
    {
        if (_s.FastPlayer != 1) return null;
        var first = _s.FastEntries[_s.FastQ];
        if (first is null || first.Text == NoAnswer) return null;
        return FeudContentRules.Normalize(first.Text) == FeudContentRules.Normalize(text)
            ? $"\"{first.Text}\" was already said. Ask for another answer." : null;
    }

    private void RecordFast(FastEntry entry)
    {
        _s.FastEntries[_s.FastPlayer * FeudRules.FastQuestions + _s.FastQ] = entry;
        _s.FastQ++;
        if (_s.FastQ >= FeudRules.FastQuestions) FinishFastTurn();
        else _s.Message = $"Question {_s.FastQ + 1}.";
    }

    /// <summary>Ends the player's turn early or after the buzzer: whatever hasn't been answered scores nothing.</summary>
    public Result EndFastTurn(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.FastPlay) return Result.Fail("Nobody is playing right now.");
        Remember();
        FinishFastTurn();
        return Result.Success;
    });

    private void FinishFastTurn()
    {
        for (var q = 0; q < FeudRules.FastQuestions; q++)
        {
            var i = _s.FastPlayer * FeudRules.FastQuestions + q;
            _s.FastEntries[i] ??= new FastEntry(NoAnswer, 0, None);
        }
        _s.FastStartedUtc = null;
        _s.FastTimeUp = false;

        if (_s.FastPlayer == 0)
        {
            _s.FastPlayer = 1;
            _s.FastQ = 0;
            _s.Phase = FeudPhase.FastReady;
            _s.Message = $"Bring in {_s.FastNames[1]}: {_secondSeconds:0} seconds, and no repeating {_s.FastNames[0]}'s answers.";
        }
        else
        {
            _s.Phase = FeudPhase.FastReveal;
            _s.FastStep = 0;
            _s.Message = "Let's see what we've got!";
        }
    }

    /// <summary>The TV reports the clock has run out. Cosmetic: the buzzer sounds and the emcee decides whether a last answer counts.</summary>
    public Result FastTimeUp() => Run(() =>
    {
        if (_s.Phase != FeudPhase.FastPlay || _s.FastTimeUp || _s.FastStartedUtc is not { } started) return Result.Fail("The clock isn't running.");
        var seconds = _s.FastPlayer == 0 ? _firstSeconds : _secondSeconds;
        if (Elapsed(started) < seconds - Math.Min(0.25, seconds / 4)) return Result.Fail("There's still time.");
        _s.FastTimeUp = true;
        _s.Message = "Time!";
        return Result.Success;
    });

    /// <summary>One tap reveals one thing: a player's answer, then its points, then the next answer. At the end the total is on the board.</summary>
    public Result RevealNext(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != FeudPhase.FastReveal) return Result.Fail("There's nothing to reveal.");
        Remember();
        _s.FastStep++;
        var step = _s.FastStep;
        var last = step >= RevealSteps;
        if (last)
        {
            _s.Phase = FeudPhase.FastOver;
            var total = FastTotal();
            _s.Message = total >= FeudRules.FastGoal
                ? $"{total} points! {Name(_s.FastTeam)} wins the big prize!"
                : $"{total} points. So close, but {Name(_s.FastTeam)} fell short of {FeudRules.FastGoal}.";
        }
        else
        {
            var p = (step - 1) / 10;
            var row = ((step - 1) % 10) / 2;
            var stage = (step - 1) % 2;
            var entry = _s.FastEntries[p * FeudRules.FastQuestions + row];
            _s.Message = stage == 0 ? $"{entry!.Text}…" : (entry!.Points > 0 ? $"{entry.Points}!" : "Zero.");
        }
        return Result.Success;
    });

    private const int RevealSteps = FeudRules.FastQuestions * 2 * 2;

    private int FastTotal() => _s.FastEntries.Sum(e => e?.Points ?? 0);

    /// <summary>Total of what has been fully revealed so far (answer and points both showing).</summary>
    private int FastTotalShown() => _s.FastEntries.Select((e, i) => (e, i)).Sum(x => x.e is not null && PointsShown(x.i) ? x.e.Points : 0);

    private bool TextShown(int i) => _s.Phase is FeudPhase.FastOver || (_s.Phase == FeudPhase.FastReveal && _s.FastStep >= RowStart(i) + 1);
    private bool PointsShown(int i) => _s.Phase is FeudPhase.FastOver || (_s.Phase == FeudPhase.FastReveal && _s.FastStep >= RowStart(i) + 2);
    private static int RowStart(int i) => (i / FeudRules.FastQuestions) * 10 + (i % FeudRules.FastQuestions) * 2;

    // ---- Undo ----------------------------------------------------------------------------

    public bool CanUndo { get { lock (_gate) return _history.Count > 0 && _s.Phase != FeudPhase.Lobby; } }

    public Result Undo(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_history.Count == 0 || _s.Phase == FeudPhase.Lobby) return Result.Fail("There's nothing to undo.");
        _s = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        return Result.Success;
    });

    // ---- Seats and the snapshot ----------------------------------------------------------

    public Seat GetSeat(Guid token)
    {
        lock (_gate) return _emceeToken == token ? new Seat(Role.Emcee) : new Seat(Role.Spectator);
    }

    public FeudSnapshot Snapshot()
    {
        lock (_gate)
        {
            var s = _s;
            var inRound = s.Phase is FeudPhase.RoundIntro or FeudPhase.FaceOffBuzz or FeudPhase.FaceOffAnswer or FeudPhase.ControlChoice
                or FeudPhase.Play or FeudPhase.Steal or FeudPhase.RoundOver;
            var board = inRound ? Board : null;
            var answers = board is null
                ? []
                : board.Answers.Select((a, i) => new FeudAnswerInfo(i + 1, s.Revealed[i], a.Text, a.Points)).ToList();

            return new FeudSnapshot(
                Code, s.Phase, _emceeToken is not null,
                Enumerable.Range(0, FeudRules.Teams).Select(i => new FeudTeamInfo(i, s.Names[i], s.Scores[i])).ToList(),
                s.Round, FeudRules.Multiplier(s.Round),
                board is null ? "" : board.Question,
                answers, inRound ? Bank() : 0, s.Strikes, s.StrikeEvents, s.Turn, s.Control, s.FaceWinner, s.Stealer, s.RoundWinner,
                FastInfo(), s.Message, _history.Count > 0 && s.Phase != FeudPhase.Lobby);
        }
    }

    private FeudFastInfo? FastInfo()
    {
        var s = _s;
        if (s.Phase is not (FeudPhase.FastIntro or FeudPhase.FastReady or FeudPhase.FastPlay or FeudPhase.FastReveal or FeudPhase.FastOver)) return null;

        var seconds = s.FastPlayer == 0 ? _firstSeconds : _secondSeconds;
        double? left = s.Phase == FeudPhase.FastPlay && s.FastStartedUtc is { } started ? Math.Max(0, seconds - Elapsed(started)) : null;
        var playing = s.Phase == FeudPhase.FastPlay && s.FastQ < FeudRules.FastQuestions;
        var entries = Enumerable.Range(0, 2).Select(p => (IReadOnlyList<FeudFastEntryInfo>)Enumerable.Range(0, FeudRules.FastQuestions).Select(q =>
        {
            var i = p * FeudRules.FastQuestions + q;
            var e = s.FastEntries[i];
            return new FeudFastEntryInfo(e is not null, e?.Text ?? "", e?.Points ?? 0, TextShown(i), PointsShown(i));
        }).ToList()).ToList();

        IReadOnlyList<FeudAnswerInfo> boardInfo = playing
            ? _fast[s.FastQ].Answers.Select((a, i) => new FeudAnswerInfo(i + 1, false, a.Text, a.Points)).ToList()
            : [];
        var total = FastTotal();
        return new FeudFastInfo(
            s.FastTeam, s.FastNames, s.FastPlayer, s.FastQ, playing ? _fast[s.FastQ].Question : "", boardInfo, entries,
            seconds, left, s.FastStartedUtc?.Ticks.ToString() ?? "", s.FastTimeUp,
            s.FastStep, s.Phase is FeudPhase.FastReveal or FeudPhase.FastOver ? FastTotalShown() : 0, total, FeudRules.FastGoal,
            s.Phase == FeudPhase.FastOver && total >= FeudRules.FastGoal);
    }

    // ---- Plumbing ------------------------------------------------------------------------

    /// <summary>The bank for this round: the revealed points, doubled or tripled by the round.</summary>
    private int Bank() => _s.Revealed.Select((r, i) => r ? Board.Answers[i].Points : 0).Sum() * FeudRules.Multiplier(_s.Round);

    private string Name(int team) => _s.Names[team];

    private static string Clean(string? raw, int max)
    {
        var chars = (raw ?? "").Where(c => !char.IsControl(c)).ToArray();
        var line = string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length > max ? line[..max].Trim() : line;
    }

    private static double Elapsed(DateTime started) => (DateTime.UtcNow - started).TotalSeconds;

    private void Remember()
    {
        _history.Add(_s.Clone());
        if (_history.Count > HistoryLimit) _history.RemoveAt(0);
    }

    private bool IsEmcee(Guid token) => _emceeToken is not null && _emceeToken == token;

    private T Run<T>(Func<T> action) where T : IResult
    {
        T result;
        lock (_gate) result = action();
        if (result.Ok) Changed?.Invoke();
        return result;
    }
}
