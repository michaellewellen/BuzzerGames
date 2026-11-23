using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.Pyramid;

/// <summary>
/// One match of $100,000 Pyramid: two teams, one emcee, a TV. The emcee's phone runs everything; the TV only shows. Two games make a match,
/// each followed by a bonus round for the winner.
///
/// A game is a pyramid of six topics. Each round both teams play one topic (seven words in thirty seconds), and the team with the lower
/// score goes first. After three rounds the higher score wins; a tie goes to letter rounds until someone is ahead. Like the other games, this
/// class knows nothing about web pages, and every public method takes one lock and returns a Result instead of throwing. Family rules apply:
/// the emcee's word is final, and every emcee action can be undone.
/// </summary>
public sealed class PyramidGame
{
    /// <summary>Everything that changes during play, so Undo can put it all back by swapping one object.</summary>
    private sealed class State
    {
        public PyramidPhase Phase = PyramidPhase.Lobby;
        public int GameNumber;
        public List<int> GameWinners = [];
        public List<PyramidWinning> Winnings = [];
        public int GameFirst;                                   // which team went first in this game's first round
        public int[] Banked = new int[PyramidRules.Teams];      // points from finished rounds of this game
        public PyramidTileState[] Tiles = new PyramidTileState[PyramidRules.TileCount];
        public int Revealed;
        public int MysteryTile;
        public int Round;
        public int[] Order = [0, 1];                            // who plays first and second this round
        public int Turn;
        public int Up;
        public bool Tiebreak;

        // The round in play: a topic or a letter.
        public PlayKind Kind;
        public string Label = "";
        public string Description = "";
        public bool Mystery;
        public bool DescriptionShown;
        public string[] Words = [];
        public WordResult[] Results = [];
        public List<int> Queue = [];                            // indexes into Words; the first is the word on screen, passed words go to the back
        public DateTime? StartedUtc;
        public bool TimeIsUp;

        // The tiebreaker.
        public int TieRounds;
        public char[] LetterChoices = [];
        public int Chooser;
        public char[] TeamLetter = new char[PyramidRules.Teams];
        public int[] TieScores = new int[PyramidRules.Teams];
        public bool[] TiePlayed = new bool[PyramidRules.Teams];

        // The bonus round.
        public int BonusTeam;
        public int BonusPrize = PyramidRules.LowPrize;
        public BonusTileState[] BonusTiles = new BonusTileState[PyramidRules.TileCount];
        public int BonusCurrent;
        public DateTime? BonusStartedUtc;
        public bool BonusTimeUp;

        public string? Message;

        public State Clone()
        {
            var copy = (State)MemberwiseClone();
            copy.GameWinners = [.. GameWinners];
            copy.Winnings = [.. Winnings];
            copy.Banked = (int[])Banked.Clone();
            copy.Tiles = (PyramidTileState[])Tiles.Clone();
            copy.Order = (int[])Order.Clone();
            copy.Results = (WordResult[])Results.Clone();
            copy.Queue = [.. Queue];
            copy.LetterChoices = (char[])LetterChoices.Clone();
            copy.TeamLetter = (char[])TeamLetter.Clone();
            copy.TieScores = (int[])TieScores.Clone();
            copy.TiePlayed = (bool[])TiePlayed.Clone();
            copy.BonusTiles = (BonusTileState[])BonusTiles.Clone();
            return copy;
        }
    }

    private const int HistoryLimit = 80;
    private const string NotEmcee = "Only the emcee can do that.";

    private readonly object _gate = new();
    private readonly IPyramidContent _content;
    private readonly Random _rng;
    private readonly double _roundSeconds;
    private readonly double _bonusSeconds;
    private readonly List<State> _history = [];
    private readonly HashSet<char> _usedLetters = [];           // letters already played in this match's tiebreakers
    private Guid? _emceeToken;
    private IReadOnlyList<PyramidTopic> _topics = [];
    private IReadOnlyList<PyramidBonusTopic> _bonus = [];
    private State _s = new();

    public PyramidGame(string code, IPyramidContent content, Random? rng = null, double roundSeconds = PyramidRules.RoundSeconds, double bonusSeconds = PyramidRules.BonusSeconds)
    {
        Code = code;
        _content = content;
        _rng = rng ?? new Random();
        _roundSeconds = roundSeconds;
        _bonusSeconds = bonusSeconds;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string Code { get; }
    public DateTime CreatedAtUtc { get; }
    public event Action? Changed;

    public PyramidPhase Phase { get { lock (_gate) return _s.Phase; } }

    // ---- The lobby -----------------------------------------------------------------------

    /// <summary>Whoever taps the button first is the emcee. There is no password: the QR code is only on the TV.</summary>
    public JoinResult JoinEmcee() => Run(() =>
    {
        if (_emceeToken is not null) return JoinResult.Fail("This game already has an emcee.");
        _emceeToken = Guid.NewGuid();
        return JoinResult.Success(_emceeToken.Value);
    });

    public Result Start(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.Lobby) return Result.Fail("The game has already started.");
        return BeginGame(1, _rng.Next(PyramidRules.Teams), remember: false);
    });

    // ---- A game: reveal, choose, play ----------------------------------------------------

    /// <summary>Turns over the next topic. The emcee reads each one aloud, so they come one tap at a time.</summary>
    public Result RevealNext(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.Reveal) return Result.Fail("The topics are already showing.");
        Remember();
        _s.Tiles[_s.Revealed] = PyramidTileState.Revealed;
        _s.Revealed++;
        if (_s.Revealed == PyramidRules.TileCount)
        {
            _s.Phase = PyramidPhase.Choosing;
            _s.Up = _s.Order[0];
            _s.Message = $"{TeamName(_s.Up)} is up. Tap the topic they pick.";
        }
        else
        {
            _s.Message = $"Topic {_s.Revealed} is showing. Read it, then turn over the next.";
        }
        return Result.Success;
    });

    /// <summary>The emcee taps the topic the team up has chosen. The TV only shows the pyramid with that square gone; if it's the Mystery 7, it lights up first.</summary>
    public Result PickTile(Guid token, int index) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.Choosing) return Result.Fail("It isn't time to pick a topic.");
        if (index is < 0 or >= PyramidRules.TileCount) return Result.Fail("There's no such topic.");
        if (_s.Tiles[index] != PyramidTileState.Revealed) return Result.Fail("That topic has already been played.");

        Remember();
        var topic = _topics[index];
        _s.Tiles[index] = PyramidTileState.Used;
        SetPlay(PlayKind.Topic, topic.Title, topic.Description, index == _s.MysteryTile, topic.Words, _s.Up);
        _s.Message = _s.Mystery
            ? "MYSTERY 7! Don't read the description. Line them up, then start the clock."
            : $"Read: \"{topic.Description}\". Line them up, then start the clock.";
        return Result.Success;
    });

    /// <summary>"Your thirty seconds starts now." The first word and the clock appear on the TV.</summary>
    public Result StartRound(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.Ready) return Result.Fail("There's no round waiting to start.");
        Remember();
        _s.StartedUtc = DateTime.UtcNow;
        _s.TimeIsUp = false;
        _s.Phase = PyramidPhase.Playing;
        _s.Message = null;
        return Result.Success;
    });

    /// <summary>
    /// The emcee's ruling on the word on screen. Correct is a point. Wrong (the clue giver said the answer, or broke some other rule) kills the word for good.
    /// Pass puts it at the back of the line, and it comes round again if there's time. Words are always in order.
    /// </summary>
    public Result Mark(Guid token, WordMark mark) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.Playing) return Result.Fail("There's no clock running.");
        if (Elapsed(_s.StartedUtc) >= _roundSeconds) return Result.Fail("Time is up!");
        if (_s.Queue.Count == 0) return Result.Fail("Every word is done.");
        if (mark == WordMark.Pass && _s.Queue.Count < 2) return Result.Fail("That's the last word, so there's nothing to pass to.");

        Remember();
        var word = _s.Queue[0];
        switch (mark)
        {
            case WordMark.Correct:
                _s.Results[word] = WordResult.Correct;
                _s.Queue.RemoveAt(0);
                break;
            case WordMark.Wrong:
                _s.Results[word] = WordResult.Wrong;
                _s.Queue.RemoveAt(0);
                break;
            case WordMark.Pass:
                _s.Queue.RemoveAt(0);
                _s.Queue.Add(word);
                break;
        }
        if (_s.Queue.Count == 0) EndPlay(timeIsUp: false);
        return Result.Success;
    });

    /// <summary>
    /// The TV (and the emcee's phone, in case the TV is closed) calls this when the clock reaches zero. It is safe to call from both: the first
    /// one ends the round and the second is told so. Nothing is recorded for Undo, because going back into an expired clock would be pointless.
    /// </summary>
    public Result TimeUp() => Run(() =>
    {
        if (_s.Phase == PyramidPhase.Playing && _s.StartedUtc is not null)
        {
            if (Elapsed(_s.StartedUtc) < _roundSeconds - Tolerance(_roundSeconds)) return Result.Fail("There is still time on the clock.");
            EndPlay(timeIsUp: true);
            return Result.Success;
        }
        if (_s.Phase == PyramidPhase.BonusPlay && _s.BonusStartedUtc is not null)
        {
            if (Elapsed(_s.BonusStartedUtc) < _bonusSeconds - Tolerance(_bonusSeconds)) return Result.Fail("There is still time on the clock.");
            EndBonus(timeUp: true);
            return Result.Success;
        }
        return Result.Fail("No clock is running.");
    });

    /// <summary>After a Mystery 7, the emcee reveals what the words had in common.</summary>
    public Result RevealDescription(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.RoundOver || !_s.Mystery) return Result.Fail("There's nothing to reveal.");
        if (_s.DescriptionShown) return Result.Fail("It's already showing.");
        _s.DescriptionShown = true;
        return Result.Success;
    });

    /// <summary>After the round, tap a word to change how it was scored (the emcee's thumb slipped, or the clock cut off a ruling). Not recorded for Undo.</summary>
    public Result FixWord(Guid token, int index, WordResult result) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.RoundOver) return Result.Fail("Words can only be fixed once the round is over.");
        if (index < 0 || index >= _s.Results.Length) return Result.Fail("There's no such word.");
        _s.Results[index] = result;
        _s.Message = RoundSummary();
        return Result.Success;
    });

    /// <summary>The emcee's "next": from a finished round to the next topic, from a won game to its bonus, from a finished bonus on to game two or the end.</summary>
    public Result Continue(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        return _s.Phase switch
        {
            PyramidPhase.RoundOver => ContinueFromRound(),
            PyramidPhase.GameWon => ContinueFromGameWon(),
            PyramidPhase.BonusOver => ContinueFromBonus(),
            PyramidPhase.BetweenGames => BeginGame(2, 1 - _s.GameFirst, remember: true),
            PyramidPhase.MatchOver => NewMatch(),
            _ => Result.Fail("There's nothing to continue from.")
        };
    });

    // ---- The tiebreaker ------------------------------------------------------------------

    /// <summary>The team that won the coin flip chooses one of the two letters; the other team gets the other one.</summary>
    public Result PickLetter(Guid token, char letter) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.LetterPick) return Result.Fail("It isn't time to pick a letter.");
        var c = char.ToUpperInvariant(letter);
        if (!_s.LetterChoices.Contains(c)) return Result.Fail("That letter isn't one of the two on offer.");
        if (_content.Letters().Any(l => l.Letter == _s.LetterChoices[0]) is false || _content.Letters().Any(l => l.Letter == _s.LetterChoices[1]) is false)
            return Result.Fail("One of those letters has lost its words. Check the topics page.");

        Remember();
        var other = _s.LetterChoices.First(x => x != c);
        _s.TeamLetter[_s.Chooser] = c;
        _s.TeamLetter[1 - _s.Chooser] = other;
        return SetLetterPlay(_s.Chooser);
    });

    // ---- The bonus round -----------------------------------------------------------------

    /// <summary>Normally $25,000, or $100,000 for a team that won both games. The emcee can change it, because family rules apply.</summary>
    public Result SetBonusPrize(Guid token, int prize) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.BonusReady) return Result.Fail("The prize can only be changed before the bonus starts.");
        if (prize != PyramidRules.LowPrize && prize != PyramidRules.HighPrize) return Result.Fail("The prize is $25,000 or $100,000.");
        _s.BonusPrize = prize;
        _s.Message = BonusReadyMessage();
        return Result.Success;
    });

    /// <summary>The pyramid flips around to the first subject and the sixty seconds begin.</summary>
    public Result StartBonus(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.BonusReady) return Result.Fail("The bonus round isn't ready.");
        if (!_content.TryCreateBonus(_rng, out var topics, out var error)) return Result.Fail(error);

        Remember();
        _bonus = topics;
        _content.MarkUsed(topics.Select(t => t.Id));
        _s.BonusTiles = new BonusTileState[PyramidRules.TileCount];
        _s.BonusCurrent = 0;
        _s.BonusStartedUtc = DateTime.UtcNow;
        _s.BonusTimeUp = false;
        _s.Phase = PyramidPhase.BonusPlay;
        _s.Message = null;
        return Result.Success;
    });

    /// <summary>
    /// Valid: the guesser named the subject, and its square turns over to show the money. Invalid: it's lost for good. Pass: on to the next, and the
    /// old one stays on the pyramid to be gone back to. Every square starts out waiting, and they come in order, easiest first.
    /// </summary>
    public Result MarkBonus(Guid token, BonusMark mark) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.BonusPlay) return Result.Fail("The bonus clock isn't running.");
        if (Elapsed(_s.BonusStartedUtc) >= _bonusSeconds) return Result.Fail("Time is up!");
        var current = _s.BonusCurrent;
        if (mark == BonusMark.Pass && !_s.BonusTiles.Where((t, i) => i != current && t is BonusTileState.Waiting or BonusTileState.Passed).Any())
            return Result.Fail("There's nothing else to pass to.");

        Remember();
        _s.BonusTiles[current] = mark switch
        {
            BonusMark.Valid => BonusTileState.Valid,
            BonusMark.Invalid => BonusTileState.Invalid,
            _ => BonusTileState.Passed
        };

        if (_s.BonusTiles.All(t => t is BonusTileState.Valid or BonusTileState.Invalid)) EndBonus(timeUp: false);
        else _s.BonusCurrent = NextBonusTile(current);
        return Result.Success;
    });

    /// <summary>Go back to a passed topic (in any order) by tapping its square. The topic that was on screen stays on the pyramid as passed.</summary>
    public Result SelectBonus(Guid token, int index) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.BonusPlay) return Result.Fail("The bonus clock isn't running.");
        if (index < 0 || index >= PyramidRules.TileCount) return Result.Fail("There's no such square.");
        if (index == _s.BonusCurrent) return Result.Fail("That one is already on screen.");
        if (_s.BonusTiles[index] is not (BonusTileState.Waiting or BonusTileState.Passed)) return Result.Fail("That square is already done.");

        Remember();
        if (_s.BonusTiles[_s.BonusCurrent] == BonusTileState.Waiting) _s.BonusTiles[_s.BonusCurrent] = BonusTileState.Passed;
        _s.BonusCurrent = index;
        return Result.Success;
    });

    /// <summary>After the clock stops, change a square to valid, invalid or not-guessed. Not recorded for Undo.</summary>
    public Result FixBonus(Guid token, int index, BonusTileState state) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase != PyramidPhase.BonusOver) return Result.Fail("Squares can only be fixed once the bonus is over.");
        if (index < 0 || index >= PyramidRules.TileCount) return Result.Fail("There's no such square.");
        if (state is not (BonusTileState.Valid or BonusTileState.Invalid or BonusTileState.Waiting)) return Result.Fail("A square can be valid, invalid or not guessed.");
        _s.BonusTiles[index] = state;
        _s.Message = BonusOverMessage();
        return Result.Success;
    });

    // ---- Undo, seats, snapshots ----------------------------------------------------------

    /// <summary>Steps back one emcee action. Once a round or bonus is over, use the tap-to-fix buttons instead, because the clock can't be wound back.</summary>
    public Result Undo(Guid token) => Run(() =>
    {
        if (!IsEmcee(token)) return Result.Fail(NotEmcee);
        if (_s.Phase is PyramidPhase.RoundOver or PyramidPhase.BonusOver) return Result.Fail("Tap a word or square to change how it was scored instead.");
        if (_history.Count == 0) return Result.Fail("There's nothing to undo.");
        _s = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        return Result.Success;
    });

    public Seat GetSeat(Guid token)
    {
        lock (_gate) return _emceeToken == token ? new Seat(Role.Emcee) : new Seat(Role.Spectator);
    }

    public PyramidSnapshot Snapshot()
    {
        lock (_gate)
        {
            var s = _s;
            var teams = Enumerable.Range(0, PyramidRules.Teams)
                .Select(i => new PyramidTeamInfo(i, TeamName(i), TeamScore(i), s.GameWinners.Count(w => w == i)))
                .ToList();

            var tiles = Enumerable.Range(0, PyramidRules.TileCount)
                .Select(i => new PyramidTileInfo(i, s.Tiles[i], s.Tiles[i] == PyramidTileState.Hidden || _topics.Count == 0 ? "" : _topics[i].Title))
                .ToList();

            int? gameWinner = s.GameNumber > 0 && s.GameWinners.Count >= s.GameNumber ? s.GameWinners[s.GameNumber - 1] : null;
            var canUndo = _history.Count > 0 && s.Phase is not (PyramidPhase.Lobby or PyramidPhase.RoundOver or PyramidPhase.BonusOver);

            return new PyramidSnapshot(
                Code, s.Phase, _emceeToken is not null, s.GameNumber, s.Tiebreak ? 0 : s.Round, s.Turn, teams, s.Up, tiles, s.Revealed,
                PlayInfo(), TiebreakInfo(), BonusInfo(), gameWinner, [.. s.Winnings], s.Message, canUndo);
        }
    }

    // ---- Moving through the game ---------------------------------------------------------

    /// <summary>Deals six topics and sets up a fresh pyramid. Checks the bank first, so a thin bank leaves the game exactly where it was.</summary>
    private Result BeginGame(int number, int first, bool remember)
    {
        if (!_content.TryCreateMain(_rng, out var topics, out var error)) return Result.Fail(error);

        if (remember) Remember();
        _topics = topics;
        _content.MarkUsed(topics.Select(t => t.Id));

        _s.GameNumber = number;
        _s.GameFirst = first;
        _s.Banked = new int[PyramidRules.Teams];
        _s.Tiles = new PyramidTileState[PyramidRules.TileCount];
        _s.Revealed = 0;
        _s.MysteryTile = _rng.Next(PyramidRules.TileCount);
        _s.Round = 1;
        _s.Order = [first, 1 - first];
        _s.Turn = 0;
        _s.Up = first;
        _s.Tiebreak = false;
        _s.TieRounds = 0;
        ClearPlay();
        _s.Phase = PyramidPhase.Reveal;
        _s.Message = number == 1
            ? $"{TeamName(first)} won the coin flip and picks first. Tap to turn over the first topic."
            : $"Game {number}. {TeamName(first)} starts. Tap to turn over the first topic.";
        return Result.Success;
    }

    private Result NewMatch()
    {
        if (!_content.TryCreateMain(_rng, out _, out var error)) return Result.Fail(error);
        Remember();
        _s.GameWinners = [];
        _s.Winnings = [];
        _usedLetters.Clear();
        return BeginGame(1, _rng.Next(PyramidRules.Teams), remember: false);
    }

    private Result ContinueFromRound()
    {
        Remember();
        var team = _s.Up;
        var score = RoundScore();

        if (_s.Kind == PlayKind.Letter)
        {
            _s.TieScores[team] = score;
            _s.TiePlayed[team] = true;
            var other = 1 - team;
            if (!_s.TiePlayed[other]) return SetLetterPlay(other);
            if (_s.TieScores[0] != _s.TieScores[1])
            {
                WinGame(_s.TieScores[0] > _s.TieScores[1] ? 0 : 1);
                return Result.Success;
            }
            return BeginLetterPick();
        }

        _s.Banked[team] += score;
        ClearPlay();
        if (_s.Turn == 0)
        {
            _s.Turn = 1;
            _s.Up = _s.Order[1];
            _s.Phase = PyramidPhase.Choosing;
            _s.Message = $"{TeamName(_s.Up)} is up. Tap the topic they pick.";
            return Result.Success;
        }

        if (_s.Round < PyramidRules.RoundsPerGame)
        {
            // The team with the lower score goes first. On a tie the order stays as it was.
            var first = _s.Order[0];
            var second = _s.Order[1];
            _s.Order = _s.Banked[first] > _s.Banked[second] ? [second, first] : [first, second];
            _s.Round++;
            _s.Turn = 0;
            _s.Up = _s.Order[0];
            _s.Phase = PyramidPhase.Choosing;
            _s.Message = _s.Banked[0] == _s.Banked[1]
                ? $"Tied at {_s.Banked[0]}, so the order stays: {TeamName(_s.Up)} is up."
                : $"{TeamName(_s.Up)} is behind, so they pick first. Tap the topic they pick.";
            return Result.Success;
        }

        if (_s.Banked[0] != _s.Banked[1])
        {
            WinGame(_s.Banked[0] > _s.Banked[1] ? 0 : 1);
            return Result.Success;
        }
        return BeginLetterPick();
    }

    private void WinGame(int winner)
    {
        _s.GameWinners.Add(winner);
        ClearPlay();
        _s.Phase = PyramidPhase.GameWon;
        _s.Message = $"{TeamName(winner)} wins game {_s.GameNumber}! Continue to the bonus round.";
    }

    private Result ContinueFromGameWon()
    {
        Remember();
        var winner = _s.GameWinners[_s.GameNumber - 1];
        _s.BonusTeam = winner;
        _s.BonusPrize = _s.GameNumber == PyramidRules.GamesPerMatch && _s.GameWinners[0] == _s.GameWinners[1] ? PyramidRules.HighPrize : PyramidRules.LowPrize;
        _s.BonusTiles = new BonusTileState[PyramidRules.TileCount];
        _s.BonusCurrent = 0;
        _s.BonusStartedUtc = null;
        _s.BonusTimeUp = false;
        _s.Phase = PyramidPhase.BonusReady;
        _s.Message = BonusReadyMessage();
        return Result.Success;
    }

    private string BonusReadyMessage() =>
        $"{TeamName(_s.BonusTeam)} plays the pyramid for ${_s.BonusPrize:N0}. Change the prize if you like, then start the clock.";

    private Result ContinueFromBonus()
    {
        Remember();
        var amount = BonusWinnings(_s);
        _s.Winnings.Add(new PyramidWinning(_s.GameNumber, _s.BonusTeam, _s.BonusPrize, amount, AllSix(_s)));
        _s.Phase = _s.GameNumber < PyramidRules.GamesPerMatch ? PyramidPhase.BetweenGames : PyramidPhase.MatchOver;
        _s.Message = _s.Phase == PyramidPhase.BetweenGames
            ? "Game one is done. Swap the celebrities, then start game two."
            : "That's the match! Start a new one whenever you like.";
        return Result.Success;
    }

    // ---- Playing a round -----------------------------------------------------------------

    private void SetPlay(PlayKind kind, string label, string description, bool mystery, IReadOnlyList<string> words, int team)
    {
        _s.Kind = kind;
        _s.Label = label;
        _s.Description = description;
        _s.Mystery = mystery;
        _s.DescriptionShown = false;
        _s.Words = [.. words];
        _s.Results = new WordResult[words.Count];
        _s.Queue = Enumerable.Range(0, words.Count).ToList();
        _s.StartedUtc = null;
        _s.TimeIsUp = false;
        _s.Up = team;
        _s.Phase = PyramidPhase.Ready;
    }

    private void ClearPlay()
    {
        _s.Label = "";
        _s.Description = "";
        _s.Mystery = false;
        _s.DescriptionShown = false;
        _s.Words = [];
        _s.Results = [];
        _s.Queue = [];
        _s.StartedUtc = null;
        _s.TimeIsUp = false;
    }

    private void EndPlay(bool timeIsUp)
    {
        _s.Phase = PyramidPhase.RoundOver;
        _s.TimeIsUp = timeIsUp;
        _s.Message = RoundSummary();
    }

    private int RoundScore() => _s.Results.Count(r => r == WordResult.Correct);

    private string RoundSummary()
    {
        var score = RoundScore();
        var text = $"{TeamName(_s.Up)} got {score} of {_s.Words.Length}.";
        if (_s.Mystery && score == _s.Words.Length) text += " A PERFECT MYSTERY 7: they win the special prize!";
        else if (_s.Mystery) text += " Tap \"Reveal\" to show what the words had in common.";
        return text;
    }

    // ---- The tiebreaker ------------------------------------------------------------------

    private Result BeginLetterPick()
    {
        var letters = _content.Letters().Where(l => l.Words.Count >= PyramidRules.WordsPerTopic).ToList();
        if (letters.Count < 2)
        {
            // A bank with fewer than two letters can't run a tiebreaker. A coin flip decides rather than leaving the game stuck.
            WinGame(_rng.Next(PyramidRules.Teams));
            _s.Message += " (There weren't two letters to play, so a coin flip decided it.)";
            return Result.Success;
        }

        var available = letters.Where(l => !_usedLetters.Contains(l.Letter)).ToList();
        if (available.Count < 2)
        {
            _usedLetters.Clear();
            available = letters;
        }
        var pair = available.OrderBy(_ => _rng.Next()).Take(2).Select(l => l.Letter).OrderBy(c => c).ToArray();
        foreach (var c in pair) _usedLetters.Add(c);

        _s.Tiebreak = true;
        _s.TieRounds++;
        _s.LetterChoices = pair;
        _s.Chooser = _rng.Next(PyramidRules.Teams);
        _s.TeamLetter = new char[PyramidRules.Teams];
        _s.TieScores = new int[PyramidRules.Teams];
        _s.TiePlayed = new bool[PyramidRules.Teams];
        ClearPlay();
        _s.Up = _s.Chooser;
        _s.Phase = PyramidPhase.LetterPick;
        _s.Message = $"{(_s.TieRounds == 1 ? "Tied!" : "Still tied!")} A coin flip says {TeamName(_s.Chooser)} chooses: {pair[0]} or {pair[1]}?";
        return Result.Success;
    }

    private Result SetLetterPlay(int team)
    {
        var letter = _s.TeamLetter[team];
        var list = _content.Letters().FirstOrDefault(l => l.Letter == letter);
        if (list is null) return Result.Fail($"There are no words for the letter {letter}.");

        var words = list.Words.OrderBy(_ => _rng.Next()).Take(PyramidRules.WordsPerTopic).ToList();
        SetPlay(PlayKind.Letter, $"The letter {letter}", "", mystery: false, words, team);
        _s.Message = $"{TeamName(team)} has the letter {letter}: seven words that start with it. Line them up, then start the clock.";
        return Result.Success;
    }

    // ---- The bonus ------------------------------------------------------------------------

    private int NextBonusTile(int from)
    {
        // Squares not yet shown come first, in order; then the passed ones. Never the one just dealt with.
        for (var pass = 0; pass < 2; pass++)
        {
            var wanted = pass == 0 ? BonusTileState.Waiting : BonusTileState.Passed;
            for (var step = 1; step < PyramidRules.TileCount; step++)
            {
                var i = (from + step) % PyramidRules.TileCount;
                if (_s.BonusTiles[i] == wanted) return i;
            }
        }
        return from;
    }

    private void EndBonus(bool timeUp)
    {
        _s.Phase = PyramidPhase.BonusOver;
        _s.BonusTimeUp = timeUp;
        _s.Message = BonusOverMessage();
    }

    private string BonusOverMessage() => AllSix(_s)
        ? $"ALL SIX! {TeamName(_s.BonusTeam)} wins ${_s.BonusPrize:N0}!"
        : $"{TeamName(_s.BonusTeam)} wins ${BonusWinnings(_s):N0}.";

    private static bool AllSix(State s) => s.BonusTiles.All(t => t == BonusTileState.Valid);

    /// <summary>Clearing all six pays the full prize. Anything less pays what the squares that turned over show, added up.</summary>
    private static int BonusWinnings(State s)
    {
        if (AllSix(s)) return s.BonusPrize;
        var values = PyramidRules.TileValues(s.BonusPrize);
        return s.BonusTiles.Select((t, i) => t == BonusTileState.Valid ? values[i] : 0).Sum();
    }

    // ---- Snapshot pieces -----------------------------------------------------------------

    private string TeamName(int team) => PyramidRules.TeamNames[team];

    private int TeamScore(int team)
    {
        var live = _s.Phase is PyramidPhase.Playing or PyramidPhase.RoundOver && _s.Kind == PlayKind.Topic && _s.Up == team ? RoundScore() : 0;
        return _s.Banked[team] + live;
    }

    private PyramidPlayInfo? PlayInfo()
    {
        var s = _s;
        if (s.Phase is not (PyramidPhase.Ready or PyramidPhase.Playing or PyramidPhase.RoundOver)) return null;

        var seconds = _roundSeconds;
        double? left = s.Phase == PyramidPhase.Playing && s.StartedUtc is { } started ? Math.Max(0, seconds - Elapsed(started)) : null;
        var words = s.Words.Select((w, i) => new PyramidWordInfo(w, s.Results[i])).ToList();
        return new PyramidPlayInfo(
            s.Kind, s.Label, s.Description, s.Mystery, s.DescriptionShown, s.Up, seconds, left,
            s.StartedUtc?.Ticks.ToString() ?? "", s.TimeIsUp,
            s.Phase == PyramidPhase.Playing && s.Queue.Count > 0 ? s.Words[s.Queue[0]] : null,
            s.Results.Count(r => r == WordResult.Correct), s.Results.Count(r => r == WordResult.Wrong), s.Queue.Count, words);
    }

    private PyramidTiebreakInfo? TiebreakInfo()
    {
        var s = _s;
        if (!s.Tiebreak || s.Phase is not (PyramidPhase.LetterPick or PyramidPhase.Ready or PyramidPhase.Playing or PyramidPhase.RoundOver)) return null;
        return new PyramidTiebreakInfo(s.LetterChoices, s.Chooser, s.TeamLetter, s.TieScores, s.TieRounds);
    }

    private PyramidBonusInfo? BonusInfo()
    {
        var s = _s;
        if (s.Phase is not (PyramidPhase.BonusReady or PyramidPhase.BonusPlay or PyramidPhase.BonusOver)) return null;

        var values = PyramidRules.TileValues(s.BonusPrize);
        var tiles = Enumerable.Range(0, PyramidRules.TileCount).Select(i =>
        {
            // A subject stays secret until its square has been shown, and everything is revealed once the clock has stopped.
            var known = s.Phase == PyramidPhase.BonusOver || (s.Phase == PyramidPhase.BonusPlay && (i == s.BonusCurrent || s.BonusTiles[i] != BonusTileState.Waiting));
            return new PyramidBonusTileInfo(i, s.BonusTiles[i], known && _bonus.Count == PyramidRules.TileCount ? _bonus[i].Subject : "", values[i]);
        }).ToList();

        double? left = s.Phase == PyramidPhase.BonusPlay && s.BonusStartedUtc is { } started ? Math.Max(0, _bonusSeconds - Elapsed(started)) : null;
        var current = s.Phase == PyramidPhase.BonusPlay && _bonus.Count == PyramidRules.TileCount ? _bonus[s.BonusCurrent].Subject : "";
        return new PyramidBonusInfo(
            s.BonusTeam, s.BonusPrize, tiles, s.BonusCurrent, current, _bonusSeconds, left,
            s.BonusStartedUtc?.Ticks.ToString() ?? "", s.BonusTimeUp, BonusWinnings(s), AllSix(s));
    }

    // ---- Plumbing ------------------------------------------------------------------------

    private static double Elapsed(DateTime? started) => started is { } t ? (DateTime.UtcNow - t).TotalSeconds : 0;

    /// <summary>The TV's clock and the server's can differ by a hair, so the TV may report time up a moment early. Anything within this is accepted.</summary>
    private static double Tolerance(double seconds) => Math.Min(0.25, seconds / 4);

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
