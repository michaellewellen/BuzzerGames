using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Pyramid;

/// <summary>
/// A Pyramid match with a fixed bank (topics named "Topic 1" and so on, words "T1-1" to "T1-7") and a seeded random, so a check can drive it
/// by scripting scores. Team numbers are 0 (Team A) and 1 (Team B); who goes first is decided by the coin flip, so helpers say "team N" rather than "the first team".
/// </summary>
sealed class PyramidTable
{
    public PyramidGame Game { get; }
    public Guid Emcee { get; }
    public FixedPyramidContent Content { get; }

    public PyramidTable(int seed = 1, bool start = true, double roundSeconds = 30, double bonusSeconds = 60, int mainTopics = 12, int letters = 26)
    {
        Content = new FixedPyramidContent(mainTopics, letters);
        Game = new PyramidGame("TEST", Content, new Random(seed), roundSeconds, bonusSeconds);
        Emcee = Game.JoinEmcee().Token;
        if (start) Check.Ok(Game.Start(Emcee));
    }

    public PyramidSnapshot S => Game.Snapshot();

    public void Continue() => Check.Ok(Game.Continue(Emcee));

    public void RevealAll()
    {
        while (S.Phase == PyramidPhase.Reveal) Check.Ok(Game.RevealNext(Emcee));
    }

    /// <summary>The emcee taps the first topic still showing.</summary>
    public void Pick() => Check.Ok(Game.PickTile(Emcee, S.Tiles.First(t => t.State == PyramidTileState.Revealed).Index));

    /// <summary>Start the clock and mark the first <paramref name="correct"/> words right and the rest wrong, which ends the round.</summary>
    public void PlayWords(int correct)
    {
        Check.Ok(Game.StartRound(Emcee));
        for (var i = 0; i < PyramidRules.WordsPerTopic; i++) Check.Ok(Game.Mark(Emcee, i < correct ? WordMark.Correct : WordMark.Wrong));
        Check.Equal(PyramidPhase.RoundOver, S.Phase, "the seven words are done");
    }

    /// <summary>Pick a topic, play it for <paramref name="correct"/> points, and look at the result (no Continue).</summary>
    public void PlayTopic(int correct)
    {
        Pick();
        PlayWords(correct);
    }

    /// <summary>One round: both teams play a topic, in whatever order the game says, and each scores what you give for that team.</summary>
    public void PlayRoundByTeam(int points0, int points1)
    {
        for (var turn = 0; turn < 2; turn++)
        {
            var team = S.Up;
            PlayTopic(team == 0 ? points0 : points1);
            Continue();
        }
    }

    /// <summary>A whole regulation game: all six topics turned over, then three rounds of these scores.</summary>
    public void PlayRegulation(params (int Team0, int Team1)[] rounds)
    {
        RevealAll();
        foreach (var (a, b) in rounds) PlayRoundByTeam(a, b);
    }

    /// <summary>A whole game that the given team wins 21 to 0.</summary>
    public void WinGame(int team)
    {
        var round = team == 0 ? (7, 0) : (0, 7);
        PlayRegulation(round, round, round);
        Check.Equal(PyramidPhase.GameWon, S.Phase, "the game is won");
        Check.Equal(team, S.GameWinner!.Value, "the winner");
    }

    /// <summary>Play the bonus round through and return the winnings, marking squares as told: true is valid, false is invalid.</summary>
    public void PlayBonus(params bool[] valids)
    {
        Check.Ok(Game.StartBonus(Emcee));
        foreach (var valid in valids) Check.Ok(Game.MarkBonus(Emcee, valid ? BonusMark.Valid : BonusMark.Invalid));
    }
}

static class PyramidChecks
{
    private static readonly Guid Stranger = Guid.NewGuid();

    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine("$100,000 Pyramid: the lobby");

        run("pyramid: the first device to take the seat is the emcee, nobody else can, and only the emcee can start", () =>
        {
            var game = new PyramidGame("TEST", new FixedPyramidContent(), new Random(1));
            Check.True(!game.Snapshot().HasEmcee, "no emcee yet");
            Check.Fails(game.Start(Stranger), "starting with no emcee");
            var join = game.JoinEmcee();
            Check.Ok(join);
            Check.True(game.Snapshot().HasEmcee, "an emcee now");
            Check.Fails(game.JoinEmcee(), "a second emcee");
            Check.Equal(Role.Emcee, game.GetSeat(join.Token).Role, "the emcee's token");
            Check.Equal(Role.Spectator, game.GetSeat(Stranger).Role, "a stranger's token");
            Check.Fails(game.Start(Stranger), "a stranger starting the game");
            Check.Ok(game.Start(join.Token));
            Check.Fails(game.Start(join.Token), "starting twice");
        });

        run("pyramid: starting deals six face-down topics whose titles are not on the snapshot, and the coin flip can go either way", () =>
        {
            var table = new PyramidTable();
            var s = table.S;
            Check.Equal(PyramidPhase.Reveal, s.Phase, "phase");
            Check.Equal(1, s.GameNumber, "game");
            Check.Equal(1, s.Round, "round");
            Check.Equal(PyramidRules.TileCount, s.Tiles.Count, "six squares");
            Check.True(s.Tiles.All(t => t.State == PyramidTileState.Hidden && t.Title == ""), "every title stays secret until its square is turned over");
            Check.True(s.Teams.All(t => t.Score == 0 && t.GamesWon == 0), "everyone starts at zero");

            var firsts = Enumerable.Range(0, 60).Select(seed => new PyramidTable(seed).S.Up).Distinct().ToList();
            Check.Equal(2, firsts.Count, "over 60 coin flips both teams go first at least once");
        });

        Console.WriteLine("$100,000 Pyramid: revealing and choosing");

        run("pyramid: the emcee turns the topics over one at a time, and only then does the team up choose", () =>
        {
            var table = new PyramidTable();
            var first = table.S.Up;
            Check.Fails(table.Game.RevealNext(Stranger), "a stranger revealing");
            Check.Fails(table.Game.PickTile(table.Emcee, 0), "picking before the reveal is done");
            for (var i = 1; i <= PyramidRules.TileCount; i++)
            {
                Check.Ok(table.Game.RevealNext(table.Emcee));
                var s = table.S;
                Check.Equal(i, s.Revealed, "squares turned over");
                Check.Equal(i, s.Tiles.Count(t => t.Title.Length > 0), "only the turned-over squares show a title");
                Check.True(s.Tiles.Take(i).All(t => t.State == PyramidTileState.Revealed && t.Title == $"Topic {t.Index + 1}" || t.Title.StartsWith("Topic")), "titles belong to the right squares");
                Check.Equal(i < PyramidRules.TileCount ? PyramidPhase.Reveal : PyramidPhase.Choosing, s.Phase, $"phase after {i} reveals");
            }
            Check.Equal(first, table.S.Up, "the coin-flip winner picks first");
            Check.Fails(table.Game.RevealNext(table.Emcee), "revealing a seventh");
        });

        run("pyramid: a picked topic leaves the pyramid and waits, showing no word and no clock; it can't be picked twice", () =>
        {
            var table = new PyramidTable();
            table.RevealAll();
            Check.Fails(table.Game.PickTile(Stranger, 0), "a stranger picking");
            Check.Fails(table.Game.PickTile(table.Emcee, 9), "a square that doesn't exist");
            var team = table.S.Up;
            Check.Ok(table.Game.PickTile(table.Emcee, 2));
            var s = table.S;
            Check.Equal(PyramidPhase.Ready, s.Phase, "phase after picking");
            Check.Equal(PyramidTileState.Used, s.Tiles[2].State, "the square is gone");
            Check.Equal(team, s.Play!.Team, "the team up is the team playing");
            Check.Equal("Topic 3", s.Play.Label, "the label is the topic title");
            Check.Equal("Name these things from topic 3", s.Play.Description, "the emcee's script is on the snapshot for his phone");
            Check.True(s.Play.CurrentWord is null && s.Play.SecondsLeft is null, "nothing for the TV to show until the clock starts");
            Check.Fails(table.Game.PickTile(table.Emcee, 2), "picking while a round is waiting");
            Check.Fails(table.Game.Mark(table.Emcee, WordMark.Correct), "marking before the clock starts");
        });

        Console.WriteLine("$100,000 Pyramid: playing a round");

        run("pyramid: words come one at a time in order; pass sends one to the back; wrong kills it; the round ends when all seven are settled", () =>
        {
            var table = new PyramidTable();
            table.RevealAll();
            table.Pick();
            Check.Ok(table.Game.StartRound(table.Emcee));
            Check.Fails(table.Game.StartRound(table.Emcee), "starting a running clock");
            var words = table.S.Play!.Words.Select(w => w.Text).ToList();
            Check.Equal(PyramidRules.WordsPerTopic, words.Count, "seven words");
            Check.Equal(words[0], table.S.Play.CurrentWord, "the first word is up");
            Check.True(table.S.Play.SecondsLeft is > 29 and <= 30, "thirty seconds on the clock");

            Check.Fails(table.Game.Mark(Stranger, WordMark.Correct), "a stranger marking");
            Check.Ok(table.Game.Mark(table.Emcee, WordMark.Pass));
            Check.Equal(words[1], table.S.Play!.CurrentWord, "after a pass the next word comes");
            Check.Ok(table.Game.Mark(table.Emcee, WordMark.Pass));
            for (var i = 2; i <= 6; i++)
            {
                Check.Equal(words[i], table.S.Play!.CurrentWord, $"word {i + 1} is up, in order");
                Check.Ok(table.Game.Mark(table.Emcee, WordMark.Correct));
            }
            Check.Equal(words[0], table.S.Play!.CurrentWord, "the passed words come round again, first pass first");
            Check.Equal(5, table.S.Teams[table.S.Up].Score, "the score climbs with each correct word");
            Check.Ok(table.Game.Mark(table.Emcee, WordMark.Wrong));
            Check.Equal(words[1], table.S.Play!.CurrentWord, "a wrong word is gone, not requeued");
            Check.Fails(table.Game.Mark(table.Emcee, WordMark.Pass), "passing the very last word");
            Check.Ok(table.Game.Mark(table.Emcee, WordMark.Correct));

            var over = table.S;
            Check.Equal(PyramidPhase.RoundOver, over.Phase, "all seven settled ends the round");
            Check.True(!over.Play!.TimeIsUp, "it ended early, not on the clock");
            Check.Equal(6, over.Teams[over.Up].Score, "six points");
            Check.True(over.Play.Words.Select(w => w.Result).SequenceEqual([WordResult.Wrong, WordResult.Correct, WordResult.Correct, WordResult.Correct, WordResult.Correct, WordResult.Correct, WordResult.Correct]), "each word's result");
            Check.True(over.Play.CurrentWord is null, "no word on screen once it's over");
            Check.Fails(table.Game.Mark(table.Emcee, WordMark.Correct), "marking after the round");
        });

        run("pyramid: the clock is the server's: time up ends the round with unplayed words counted as misses, and not a moment early", () =>
        {
            var table = new PyramidTable(roundSeconds: 0.3);
            table.RevealAll();
            table.Pick();
            Check.Fails(table.Game.TimeUp(), "time up before the clock starts");
            Check.Ok(table.Game.StartRound(table.Emcee));
            Check.Ok(table.Game.Mark(table.Emcee, WordMark.Correct));
            Thread.Sleep(450);
            Check.Fails(table.Game.Mark(table.Emcee, WordMark.Correct), "a ruling after the clock ran out");
            Check.Ok(table.Game.TimeUp());
            Check.Fails(table.Game.TimeUp(), "the second of two screens calling time up");

            var s = table.S;
            Check.Equal(PyramidPhase.RoundOver, s.Phase, "phase");
            Check.True(s.Play!.TimeIsUp, "ended on the clock");
            Check.Equal(1, s.Play.Correct, "one correct");
            Check.Equal(0, s.Play.Wrong, "none wrong");
            Check.Equal(6, s.Play.Words.Count(w => w.Result == WordResult.Pending), "six never got a ruling");
            Check.Equal(1, s.Teams[s.Up].Score, "one point");

            var early = new PyramidTable();
            early.RevealAll();
            early.Pick();
            Check.Ok(early.Game.StartRound(early.Emcee));
            Check.Fails(early.Game.TimeUp(), "a screen calling time up with 29 seconds left");
            Check.Equal(PyramidPhase.Playing, early.S.Phase, "the round carries on");
        });

        run("pyramid: after a round the emcee can tap a word to change its score, and the total follows", () =>
        {
            var table = new PyramidTable();
            table.RevealAll();
            Check.Fails(table.Game.FixWord(table.Emcee, 0, WordResult.Correct), "fixing a word when no round is over");
            table.PlayTopic(4);
            var team = table.S.Up;
            Check.Equal(4, table.S.Teams[team].Score, "four");
            Check.Fails(table.Game.FixWord(Stranger, 4, WordResult.Correct), "a stranger fixing");
            Check.Fails(table.Game.FixWord(table.Emcee, 7, WordResult.Correct), "a word that doesn't exist");
            Check.Ok(table.Game.FixWord(table.Emcee, 4, WordResult.Correct));
            Check.Ok(table.Game.FixWord(table.Emcee, 0, WordResult.Wrong));
            Check.Equal(4, table.S.Teams[team].Score, "one up, one down");
            Check.Ok(table.Game.FixWord(table.Emcee, 5, WordResult.Correct));
            Check.Equal(5, table.S.Teams[team].Score, "five");
            table.Continue();
            Check.Equal(5, table.S.Teams[team].Score, "the fixed score is the banked score");
        });

        Console.WriteLine("$100,000 Pyramid: a game");

        run("pyramid: the lower score starts each round, and a tie keeps the order (the example from the rules)", () =>
        {
            var table = new PyramidTable(seed: 5);
            table.RevealAll();
            var a = table.S.Up;       // wins the coin flip
            var b = 1 - a;

            table.PlayTopic(7); table.Continue();
            Check.Equal(b, table.S.Up, "the second team is up second");
            table.PlayTopic(6); table.Continue();
            Check.Equal(7, table.S.Teams[a].Score, "A has 7");
            Check.Equal(6, table.S.Teams[b].Score, "B has 6");
            Check.Equal(2, table.S.Round, "round two");
            Check.Equal(b, table.S.Up, "B is behind, so B starts round two");

            table.PlayTopic(7); table.Continue();        // B: 13
            Check.Equal(a, table.S.Up, "A plays second");
            table.PlayTopic(6); table.Continue();        // A: 13
            Check.Equal(13, table.S.Teams[a].Score, "A has 13");
            Check.Equal(13, table.S.Teams[b].Score, "B has 13");
            Check.Equal(3, table.S.Round, "round three");
            Check.Equal(b, table.S.Up, "tied, so the order stays and B starts again");
            Check.Equal(PyramidPhase.Choosing, table.S.Phase, "waiting for B to choose");
        });

        run("pyramid: whoever is behind goes first, whichever team that is", () =>
        {
            var table = new PyramidTable(seed: 9);
            table.RevealAll();
            var first = table.S.Up;
            table.PlayRoundByTeam(2, 5);
            var behind = table.S.Teams[0].Score < table.S.Teams[1].Score ? 0 : 1;
            Check.Equal(behind, table.S.Up, "the team on 2 goes first");
            table.PlayRoundByTeam(7, 0);
            var behind2 = table.S.Teams[0].Score < table.S.Teams[1].Score ? 0 : 1;
            Check.Equal(behind2, table.S.Up, "and it follows the standings again after round two");
            _ = first;
        });

        run("pyramid: each team plays three of the six topics, the pyramid empties, and the higher score wins", () =>
        {
            var table = new PyramidTable(seed: 3);
            table.RevealAll();
            var played = new Dictionary<int, int> { [0] = 0, [1] = 0 };
            for (var round = 1; round <= 3; round++)
            {
                Check.Equal(round, table.S.Round, "round number");
                for (var turn = 0; turn < 2; turn++)
                {
                    played[table.S.Up]++;
                    table.PlayTopic(5);
                    table.Continue();
                }
            }
            Check.Equal(3, played[0], "team A played three topics");
            Check.Equal(3, played[1], "team B played three topics");
        });

        run("pyramid: a won game names its winner and counts it; the winner can score at most 21", () =>
        {
            var table = new PyramidTable();
            table.PlayRegulation((7, 3), (4, 7), (7, 5));
            var s = table.S;
            Check.Equal(PyramidPhase.GameWon, s.Phase, "phase");
            Check.True(s.Tiles.All(t => t.State == PyramidTileState.Used), "all six squares played");
            Check.Equal(s.Teams.Max(t => t.Score), s.Teams[s.GameWinner!.Value].Score, "the winner has the higher score");
            Check.True(s.Teams.All(t => t.Score <= 21), "21 is a perfect score");
            Check.Equal(1, s.Teams[s.GameWinner.Value].GamesWon, "one game won");
            Check.True(s.Message!.Contains("wins game 1"), "the message says so");
        });

        run("pyramid: a perfect game is 21", () =>
        {
            var table = new PyramidTable();
            table.WinGame(0);
            Check.Equal(21, table.S.Teams[0].Score, "21 to 0");
            Check.Equal(0, table.S.Teams[1].Score, "and nothing for the other team");
        });

        Console.WriteLine("$100,000 Pyramid: the Mystery 7");

        run("pyramid: exactly one topic per game is the Mystery 7, and the emcee is told only when it is picked", () =>
        {
            foreach (var seed in new[] { 1, 2, 3, 4, 5 })
            {
                var table = new PyramidTable(seed);
                table.RevealAll();
                var mysteries = new List<int>();
                for (var i = 0; i < PyramidRules.TileCount; i++)
                {
                    Check.True(table.S.Play is null, "nothing about the mystery before a topic is picked");
                    Check.Ok(table.Game.PickTile(table.Emcee, i));
                    if (table.S.Play!.Mystery) mysteries.Add(i);
                    table.PlayWords(0);
                    table.Continue();
                }
                Check.Equal(1, mysteries.Count, $"seed {seed}: mystery topics in a game");
            }
            var spread = Enumerable.Range(0, 40).Select(seed =>
            {
                var t = new PyramidTable(seed);
                t.RevealAll();
                for (var i = 0; i < PyramidRules.TileCount; i++)
                {
                    t.Game.PickTile(t.Emcee, i);
                    if (t.S.Play!.Mystery) return i;
                    t.PlayWords(0);
                    t.Continue();
                }
                return -1;
            }).Distinct().Count();
            Check.True(spread >= 4, $"the mystery lands on different squares (saw {spread})");
        });

        run("pyramid: the Mystery 7's description stays hidden until the emcee reveals it after the round; seven out of seven wins the prize", () =>
        {
            var table = new PyramidTable(seed: 4);
            table.RevealAll();
            for (var i = 0; i < PyramidRules.TileCount; i++)
            {
                Check.Ok(table.Game.PickTile(table.Emcee, i));
                if (table.S.Play!.Mystery) break;
                table.PlayWords(0);
                table.Continue();
            }
            var play = table.S.Play!;
            Check.True(play.Mystery && !play.DescriptionShown, "mystery, and the description isn't showing");
            Check.True(table.S.Message!.Contains("MYSTERY 7"), "the emcee's phone says so");
            Check.Fails(table.Game.RevealDescription(table.Emcee), "revealing before the round is over");

            table.PlayWords(PyramidRules.WordsPerTopic);
            Check.True(table.S.Message!.Contains("special prize"), "a perfect round wins the special prize");
            Check.Fails(table.Game.RevealDescription(Stranger), "a stranger revealing");
            Check.Ok(table.Game.RevealDescription(table.Emcee));
            Check.True(table.S.Play!.DescriptionShown, "now it's showing");
            Check.Fails(table.Game.RevealDescription(table.Emcee), "revealing twice");
        });

        run("pyramid: an ordinary topic has no description to reveal, and a missed mystery asks for the reveal", () =>
        {
            var table = new PyramidTable(seed: 4);
            table.RevealAll();
            var sawOrdinary = false;
            var sawMystery = false;
            for (var i = 0; i < PyramidRules.TileCount; i++)
            {
                Check.Ok(table.Game.PickTile(table.Emcee, i));
                var mystery = table.S.Play!.Mystery;
                table.PlayWords(3);
                if (mystery)
                {
                    sawMystery = true;
                    Check.True(table.S.Message!.Contains("Reveal"), "the emcee is prompted to reveal what the words had in common");
                    Check.True(!table.S.Message.Contains("special prize"), "no prize for 3 of 7");
                }
                else
                {
                    sawOrdinary = true;
                    Check.Fails(table.Game.RevealDescription(table.Emcee), "nothing to reveal on an ordinary topic");
                }
                table.Continue();
            }
            Check.True(sawOrdinary && sawMystery, "both kinds played");
        });

        Console.WriteLine("$100,000 Pyramid: the tiebreaker");

        run("pyramid: a tie after three rounds goes to a coin flip and two letters, and the other team gets the other letter", () =>
        {
            var table = new PyramidTable(seed: 2);
            table.PlayRegulation((4, 4), (5, 5), (3, 3));
            var s = table.S;
            Check.Equal(PyramidPhase.LetterPick, s.Phase, "phase");
            Check.Equal(0, s.Round, "the tiebreaker isn't a numbered round");
            var tie = s.Tiebreak!;
            Check.Equal(2, tie.Choices.Count, "two letters on offer");
            Check.True(tie.Choices[0] != tie.Choices[1], "two different letters");
            Check.Equal(tie.Chooser, s.Up, "the coin-flip winner is up");
            Check.True(s.Message!.Contains(PyramidRules.TeamNames[tie.Chooser]), "the message names who chooses");

            Check.Fails(table.Game.PickLetter(Stranger, tie.Choices[0]), "a stranger picking");
            var offered = tie.Choices.ToHashSet();
            var notOffered = (char)('A' + Enumerable.Range(0, 26).First(i => !offered.Contains((char)('A' + i))));
            Check.Fails(table.Game.PickLetter(table.Emcee, notOffered), "a letter that isn't on offer");
            Check.Ok(table.Game.PickLetter(table.Emcee, char.ToLowerInvariant(tie.Choices[1])));

            s = table.S;
            Check.Equal(PyramidPhase.Ready, s.Phase, "the chooser's round is ready");
            Check.Equal(PlayKind.Letter, s.Play!.Kind, "a letter round");
            Check.Equal(tie.Chooser, s.Play.Team, "the chooser plays first");
            var mine = tie.Choices[1];
            var theirs = tie.Choices[0];
            Check.Equal(mine, s.Tiebreak!.TeamLetters[tie.Chooser], "the chooser has the letter they picked");
            Check.Equal(theirs, s.Tiebreak.TeamLetters[1 - tie.Chooser], "the other team has the other letter");
            Check.Equal(PyramidRules.WordsPerTopic, s.Play.Words.Select(w => w.Text).Distinct().Count(), "seven different words");
            Check.True(s.Play.Words.All(w => w.Text[0] == mine), "all of them start with the letter");
            Check.True(s.Play.Description == "", "a letter round has no description to read");
        });

        run("pyramid: letter rounds are played one after the other, and the higher score wins the game", () =>
        {
            var table = new PyramidTable(seed: 2);
            table.PlayRegulation((4, 4), (5, 5), (3, 3));
            var chooser = table.S.Tiebreak!.Chooser;
            Check.Ok(table.Game.PickLetter(table.Emcee, table.S.Tiebreak!.Choices[0]));
            table.PlayWords(5);
            table.Continue();
            var s = table.S;
            Check.Equal(PyramidPhase.Ready, s.Phase, "the other team's letter round is next");
            Check.Equal(1 - chooser, s.Play!.Team, "the other team plays");
            Check.True(s.Play.Words.All(w => w.Text[0] == s.Tiebreak!.TeamLetters[1 - chooser]), "their words start with their letter");
            table.PlayWords(3);
            table.Continue();
            s = table.S;
            Check.Equal(PyramidPhase.GameWon, s.Phase, "somebody is ahead");
            Check.Equal(chooser, s.GameWinner!.Value, "5 beats 3");
            Check.Equal(12 - 12 + s.Teams[0].Score, s.Teams[0].Score, "the game score is untouched by letter rounds");
            Check.Equal(12, s.Teams[0].Score, "still the tied 12 each");
        });

        run("pyramid: still tied after the letters means two fresh letters and another coin flip, until somebody wins", () =>
        {
            var table = new PyramidTable(seed: 6);
            table.PlayRegulation((4, 4), (5, 5), (3, 3));
            var seen = new HashSet<char>(table.S.Tiebreak!.Choices);
            for (var round = 2; round <= 5; round++)
            {
                Check.Ok(table.Game.PickLetter(table.Emcee, table.S.Tiebreak!.Choices[0]));
                table.PlayWords(3); table.Continue();
                table.PlayWords(3); table.Continue();
                var s = table.S;
                Check.Equal(PyramidPhase.LetterPick, s.Phase, $"tied again after letter round {round - 1}");
                Check.Equal(round, s.Tiebreak!.Rounds, "the tiebreaker round counts up");
                Check.True(s.Message!.Contains("Still tied"), "the message says it's still tied");
                foreach (var letter in s.Tiebreak.Choices) Check.True(seen.Add(letter), $"{letter} hasn't been used in this match's tiebreakers");
            }
            Check.Ok(table.Game.PickLetter(table.Emcee, table.S.Tiebreak!.Choices[0]));
            table.PlayWords(2); table.Continue();
            table.PlayWords(6); table.Continue();
            Check.Equal(PyramidPhase.GameWon, table.S.Phase, "somebody finally wins");
        });

        run("pyramid: the bank can run short of letters without getting the game stuck", () =>
        {
            var table = new PyramidTable(seed: 2, letters: 3);
            table.PlayRegulation((4, 4), (5, 5), (3, 3));
            for (var i = 0; i < 4; i++)
            {
                Check.Equal(PyramidPhase.LetterPick, table.S.Phase, "another tiebreaker");
                Check.Ok(table.Game.PickLetter(table.Emcee, table.S.Tiebreak!.Choices[0]));
                table.PlayWords(2); table.Continue();
                table.PlayWords(2); table.Continue();
            }
            var none = new PyramidTable(seed: 2, letters: 1);
            none.PlayRegulation((4, 4), (5, 5), (3, 3));
            Check.Equal(PyramidPhase.GameWon, none.S.Phase, "with fewer than two letters a coin flip settles it");
        });

        Console.WriteLine("$100,000 Pyramid: the bonus round");

        run("pyramid: the winner plays the bonus for $25,000, and a team that wins both games plays for $100,000", () =>
        {
            var table = new PyramidTable(seed: 7);
            table.WinGame(0);
            table.Continue();
            var s = table.S;
            Check.Equal(PyramidPhase.BonusReady, s.Phase, "phase");
            Check.Equal(0, s.Bonus!.Team, "the winner plays");
            Check.Equal(25_000, s.Bonus.Prize, "$25,000 the first time");
            Check.True(s.Bonus.Tiles.All(t => t.State == BonusTileState.Waiting && t.Subject == ""), "face down, subjects secret");
            Check.True(s.Bonus.Tiles.Select(t => t.Value).SequenceEqual(PyramidRules.LowTiles), "the $25,000 pyramid's amounts");

            table.PlayBonus(true, true, true, true, true, true);
            table.Continue();
            Check.Equal(PyramidPhase.BetweenGames, table.S.Phase, "game one is done");
            table.Continue();
            Check.Equal(2, table.S.GameNumber, "game two");
            table.WinGame(0);
            table.Continue();
            Check.Equal(100_000, table.S.Bonus!.Prize, "$100,000 for winning both games");
            Check.True(table.S.Bonus.Tiles.Select(t => t.Value).SequenceEqual(PyramidRules.HighTiles), "the $100,000 pyramid's amounts");
        });

        run("pyramid: if each team wins a game, both play for $25,000; the $100,000 needs the same team twice", () =>
        {
            var table = new PyramidTable(seed: 7);
            table.WinGame(0);
            table.Continue();
            table.PlayBonus(false, false, false, false, false, false);   // they don't need to win the first bonus to go for the big one
            table.Continue();
            table.Continue();
            table.WinGame(1);
            table.Continue();
            Check.Equal(1, table.S.Bonus!.Team, "the second winner plays");
            Check.Equal(25_000, table.S.Bonus.Prize, "$25,000 again, because it's a different team");

            var again = new PyramidTable(seed: 8);
            again.WinGame(1);
            again.Continue();
            again.PlayBonus(false, false, false, false, false, false);
            again.Continue();
            again.Continue();
            again.WinGame(1);
            again.Continue();
            Check.Equal(100_000, again.S.Bonus!.Prize, "the same team twice, even after losing the first bonus, plays for $100,000");
        });

        run("pyramid: the emcee can change the prize before the clock starts, and only to a real prize", () =>
        {
            var table = new PyramidTable();
            table.WinGame(1);
            table.Continue();
            Check.Fails(table.Game.SetBonusPrize(Stranger, 100_000), "a stranger changing it");
            Check.Fails(table.Game.SetBonusPrize(table.Emcee, 5), "a made-up prize");
            Check.Ok(table.Game.SetBonusPrize(table.Emcee, 100_000));
            Check.Equal(100_000, table.S.Bonus!.Prize, "changed");
            Check.True(table.S.Message!.Contains("$100,000"), "the message follows");
            Check.Ok(table.Game.StartBonus(table.Emcee));
            Check.Fails(table.Game.SetBonusPrize(table.Emcee, 25_000), "changing it once the clock runs");
        });

        run("pyramid: the bonus shows one subject at a time, in order, and the unseen ones stay secret", () =>
        {
            var table = new PyramidTable();
            table.WinGame(0);
            table.Continue();
            Check.Fails(table.Game.StartBonus(Stranger), "a stranger starting it");
            Check.Fails(table.Game.MarkBonus(table.Emcee, BonusMark.Valid), "marking before the start");
            Check.Ok(table.Game.StartBonus(table.Emcee));
            var b = table.S.Bonus!;
            Check.Equal(PyramidPhase.BonusPlay, table.S.Phase, "phase");
            Check.Equal(0, b.Current, "the easiest first");
            Check.Equal("Bonus subject 1", b.CurrentSubject, "its subject");
            Check.True(b.SecondsLeft is > 59 and <= 60, "sixty seconds");
            Check.Equal(1, b.Tiles.Count(t => t.Subject.Length > 0), "only the subject on screen is known");
            Check.Ok(table.Game.MarkBonus(table.Emcee, BonusMark.Valid));
            b = table.S.Bonus!;
            Check.Equal(1, b.Current, "then the next");
            Check.Equal(BonusTileState.Valid, b.Tiles[0].State, "the first square turned over");
            Check.Equal(2, b.Tiles.Count(t => t.Subject.Length > 0), "a turned-over square keeps its subject");
        });

        run("pyramid: pass, go back in any order, invalid kills a square, and the winnings add up", () =>
        {
            var table = new PyramidTable();
            table.WinGame(0);
            table.Continue();
            Check.Ok(table.Game.StartBonus(table.Emcee));
            var g = table.Game;
            var e = table.Emcee;
            Check.Ok(g.MarkBonus(e, BonusMark.Valid));        // square 1: $1,000
            Check.Ok(g.MarkBonus(e, BonusMark.Valid));        // square 2: $1,000
            Check.Ok(g.MarkBonus(e, BonusMark.Invalid));      // square 3: lost
            Check.Ok(g.MarkBonus(e, BonusMark.Pass));         // square 4: passed
            Check.Equal(4, table.S.Bonus!.Current, "on to the fifth");
            Check.Equal(BonusTileState.Passed, table.S.Bonus.Tiles[3].State, "the old one stays on the pyramid, passed");
            Check.Equal("Bonus subject 4", table.S.Bonus.Tiles[3].Subject, "and still shows what it was");
            Check.Ok(g.MarkBonus(e, BonusMark.Valid));        // square 5: $2,000
            Check.Equal(5, table.S.Bonus!.Current, "the last one is up");
            Check.Ok(g.MarkBonus(e, BonusMark.Pass));         // square 6: passed
            Check.Equal(3, table.S.Bonus!.Current, "everything left is a pass, so the first passed one comes back");

            Check.Fails(g.SelectBonus(e, 3), "the one already on screen");
            Check.Fails(g.SelectBonus(e, 0), "a square that's already done");
            Check.Fails(g.SelectBonus(e, 9), "a square that doesn't exist");
            Check.Fails(g.SelectBonus(Stranger, 5), "a stranger choosing");
            Check.Ok(g.SelectBonus(e, 5));
            Check.Equal(5, table.S.Bonus!.Current, "going back out of order");
            Check.Equal(BonusTileState.Passed, table.S.Bonus.Tiles[3].State, "the one left behind is still passed");
            Check.Ok(g.MarkBonus(e, BonusMark.Valid));        // square 6: $5,000
            Check.Equal(3, table.S.Bonus!.Current, "and the last passed one returns");
            Check.Fails(g.MarkBonus(e, BonusMark.Pass), "passing when there's nothing else left");
            Check.Ok(g.MarkBonus(e, BonusMark.Invalid));

            var over = table.S;
            Check.Equal(PyramidPhase.BonusOver, over.Phase, "every square is settled");
            Check.Equal(1_000 + 1_000 + 2_000 + 5_000, over.Bonus!.Winnings, "the winnings are the turned-over squares, added up");
            Check.True(!over.Bonus.AllSix, "not all six");
            Check.True(over.Bonus.Tiles.All(t => t.Subject.Length > 0), "once it's over every subject can be shown");
            Check.True(over.Message!.Contains("$9,000"), "the message says what they won");
        });

        run("pyramid: all six valid pays the full prize, which is much more than the squares add up to", () =>
        {
            var table = new PyramidTable();
            table.WinGame(0);
            table.Continue();
            table.PlayBonus(true, true, true, true, true, true);
            var b = table.S.Bonus!;
            Check.True(b.AllSix, "all six");
            Check.Equal(25_000, b.Winnings, "the full prize");
            Check.Equal(12_000, b.Tiles.Sum(t => t.Value), "the squares only add up to $12,000");
            Check.True(table.S.Message!.Contains("ALL SIX"), "announced");
            Check.Equal(40_000, PyramidRules.HighTiles.Sum(), "the big pyramid's squares add up to $40,000");
            for (var i = 1; i < PyramidRules.TileCount; i++)
            {
                Check.True(PyramidRules.LowTiles[i] >= PyramidRules.LowTiles[i - 1], "the $25,000 amounts never go down");
                Check.True(PyramidRules.HighTiles[i] >= PyramidRules.HighTiles[i - 1], "the $100,000 amounts never go down");
            }
        });

        run("pyramid: when the sixty seconds run out the bonus ends, and the emcee can still fix a square", () =>
        {
            var table = new PyramidTable(bonusSeconds: 0.3);
            table.WinGame(0);
            table.Continue();
            Check.Ok(table.Game.StartBonus(table.Emcee));
            Check.Ok(table.Game.MarkBonus(table.Emcee, BonusMark.Valid));
            Thread.Sleep(450);
            Check.Fails(table.Game.MarkBonus(table.Emcee, BonusMark.Valid), "a ruling after the clock ran out");
            Check.Ok(table.Game.TimeUp());
            var s = table.S;
            Check.Equal(PyramidPhase.BonusOver, s.Phase, "phase");
            Check.True(s.Bonus!.TimeUp, "ended on the clock");
            Check.Equal(1_000, s.Bonus.Winnings, "one square");

            Check.Fails(table.Game.FixBonus(Stranger, 1, BonusTileState.Valid), "a stranger fixing");
            Check.Fails(table.Game.FixBonus(table.Emcee, 1, BonusTileState.Passed), "a state a square can't end in");
            Check.Fails(table.Game.FixBonus(table.Emcee, 8, BonusTileState.Valid), "a square that doesn't exist");
            Check.Ok(table.Game.FixBonus(table.Emcee, 1, BonusTileState.Valid));
            Check.Ok(table.Game.FixBonus(table.Emcee, 3, BonusTileState.Valid));
            Check.Equal(1_000 + 1_000 + 2_000, table.S.Bonus!.Winnings, "the winnings follow");
            for (var i = 0; i < PyramidRules.TileCount; i++) Check.Ok(table.Game.FixBonus(table.Emcee, i, BonusTileState.Valid));
            Check.Equal(25_000, table.S.Bonus!.Winnings, "fix all six and it's the full prize");
        });

        Console.WriteLine("$100,000 Pyramid: a whole match");

        run("pyramid: two games and two bonus rounds make a match, game two starts with the other team, and a new match starts clean", () =>
        {
            var table = new PyramidTable(seed: 11);
            var firstOfGame1 = table.S.Up;
            table.WinGame(0);
            table.Continue();
            table.PlayBonus(true, true, true, true, true, true);
            table.Continue();
            var s = table.S;
            Check.Equal(PyramidPhase.BetweenGames, s.Phase, "between games");
            Check.True(s.Message!.Contains("Swap the celebrities"), "a reminder to swap the celebrities");
            Check.Equal(1, s.Winnings.Count, "one bonus result so far");
            Check.Equal(25_000, s.Winnings[0].Amount, "they cleared it");

            table.Continue();
            s = table.S;
            Check.Equal(2, s.GameNumber, "game two");
            Check.Equal(PyramidPhase.Reveal, s.Phase, "a fresh pyramid");
            Check.Equal(1 - firstOfGame1, s.Up, "the other team starts game two");
            Check.True(s.Teams.All(t => t.Score == 0), "scores start over");
            Check.Equal(1, s.Teams[0].GamesWon, "but the games won carry over");

            table.WinGame(1);
            table.Continue();
            table.PlayBonus(true, true, false, false, false, false);
            table.Continue();
            s = table.S;
            Check.Equal(PyramidPhase.MatchOver, s.Phase, "the match is over");
            Check.Equal(2, s.Winnings.Count, "both bonus rounds are on the board");
            Check.Equal(2_000, s.Winnings[1].Amount, "the second team's partial win");
            Check.Equal(1, s.Teams[0].GamesWon, "team A won one");
            Check.Equal(1, s.Teams[1].GamesWon, "team B won one");

            Check.Ok(table.Game.Continue(table.Emcee));
            s = table.S;
            Check.Equal(PyramidPhase.Reveal, s.Phase, "a new match begins");
            Check.Equal(1, s.GameNumber, "back to game one");
            Check.Equal(0, s.Winnings.Count, "with nothing carried over");
            Check.True(s.Teams.All(t => t.GamesWon == 0 && t.Score == 0), "everyone starts at zero");
        });

        run("pyramid: topics are marked used as they are dealt, so a match doesn't repeat itself while the bank has fresh ones", () =>
        {
            var table = new PyramidTable(seed: 3, mainTopics: 12);
            table.WinGame(0);
            var first = Enumerable.Range(0, 6).Select(i => table.S.Tiles[i].Title).ToList();
            table.Continue();
            table.PlayBonus(true, true, true, true, true, true);
            table.Continue();
            table.Continue();
            table.RevealAll();
            var second = Enumerable.Range(0, 6).Select(i => table.S.Tiles[i].Title).ToList();
            Check.True(first.Intersect(second).Count() == 0, "game two has six different topics from game one");
            Check.Equal(12, table.Content.Used.Count(id => id.StartsWith('m')), "twelve topics used");
        });

        run("pyramid: a bank with too few topics refuses to start and leaves the game in the lobby", () =>
        {
            var table = new PyramidTable(mainTopics: 3, start: false);
            var result = table.Game.Start(table.Emcee);
            Check.Fails(result, "starting with three topics");
            Check.True(result.Error!.Contains("needs 6") || result.Error.Contains("Not enough"), "says why: " + result.Error);
            Check.Equal(PyramidPhase.Lobby, table.S.Phase, "still in the lobby");
        });

        Console.WriteLine("$100,000 Pyramid: undo");

        run("pyramid: undo steps back one emcee action at a time, but not once a round is over", () =>
        {
            var table = new PyramidTable();
            Check.Fails(table.Game.Undo(table.Emcee), "nothing to undo yet");
            Check.True(!table.S.CanUndo, "the button is off");
            table.RevealAll();
            var first = table.S.Up;
            Check.Ok(table.Game.PickTile(table.Emcee, 1));
            Check.Ok(table.Game.Undo(table.Emcee));
            Check.Equal(PyramidPhase.Choosing, table.S.Phase, "back to choosing");
            Check.Equal(PyramidTileState.Revealed, table.S.Tiles[1].State, "the square is back");
            Check.Fails(table.Game.Undo(Stranger), "a stranger undoing");

            table.Pick();
            Check.Ok(table.Game.StartRound(table.Emcee));
            var word = table.S.Play!.CurrentWord;
            Check.Ok(table.Game.Mark(table.Emcee, WordMark.Correct));
            Check.Equal(1, table.S.Teams[first].Score, "a point");
            Check.Ok(table.Game.Undo(table.Emcee));
            Check.Equal(0, table.S.Teams[first].Score, "the point is gone");
            Check.Equal(word, table.S.Play!.CurrentWord, "the same word is back on screen");
            Check.Equal(PyramidPhase.Playing, table.S.Phase, "and the clock is still running");

            for (var i = 0; i < 7; i++) Check.Ok(table.Game.Mark(table.Emcee, WordMark.Wrong));
            Check.Equal(PyramidPhase.RoundOver, table.S.Phase, "round over");
            Check.Fails(table.Game.Undo(table.Emcee), "undoing a finished round (use the fix buttons)");

        });

        run("pyramid: undo after Continue returns to the finished round with the score still unbanked, and undo works in the bonus", () =>
        {
            var table = new PyramidTable();
            table.RevealAll();
            table.PlayTopic(5);
            var team = table.S.Up;
            table.Continue();
            Check.Ok(table.Game.Undo(table.Emcee));
            Check.Equal(PyramidPhase.RoundOver, table.S.Phase, "back to the finished round");
            Check.Equal(5, table.S.Teams[team].Score, "the score is still showing");
            table.Continue();
            Check.Equal(5, table.S.Teams[team].Score, "and counted once, not twice");

            var bonus = new PyramidTable();
            bonus.WinGame(0);
            bonus.Continue();
            Check.Ok(bonus.Game.StartBonus(bonus.Emcee));
            Check.Ok(bonus.Game.MarkBonus(bonus.Emcee, BonusMark.Valid));
            Check.Ok(bonus.Game.MarkBonus(bonus.Emcee, BonusMark.Invalid));
            Check.Ok(bonus.Game.Undo(bonus.Emcee));
            Check.Equal(BonusTileState.Waiting, bonus.S.Bonus!.Tiles[1].State, "the invalid is undone");
            Check.Equal(1, bonus.S.Bonus.Current, "and its subject is back on screen");
            Check.Ok(bonus.Game.Undo(bonus.Emcee));
            Check.Equal(BonusTileState.Waiting, bonus.S.Bonus!.Tiles[0].State, "so is the valid");
            Check.Ok(bonus.Game.Undo(bonus.Emcee));
            Check.Equal(PyramidPhase.BonusReady, bonus.S.Phase, "and the start itself");
        });

        Console.WriteLine("$100,000 Pyramid: plumbing");

        run("pyramid: Changed fires on every successful action and stays quiet on failures", () =>
        {
            var game = new PyramidGame("TEST", new FixedPyramidContent(), new Random(1));
            var fired = 0;
            game.Changed += () => fired++;
            var emcee = game.JoinEmcee().Token;
            Check.Equal(1, fired, "after the emcee joins");
            Check.Fails(game.JoinEmcee(), "a second emcee");
            Check.Fails(game.Start(Stranger), "a stranger starting");
            Check.Fails(game.RevealNext(emcee), "revealing in the lobby");
            Check.Equal(1, fired, "failures shouldn't notify anyone");
            Check.Ok(game.Start(emcee));
            Check.Equal(2, fired, "after starting");
        });

        run("pyramid: room codes are unique, four letters with no vowels, and found in any case", () =>
        {
            var manager = new PyramidRoomManager(new FixedPyramidContent());
            var games = Enumerable.Range(0, 100).Select(_ => manager.Create()).ToList();
            Check.Equal(100, games.Select(g => g.Code).Distinct().Count(), "distinct codes");
            Check.True(games.All(g => g.Code.Length == 4 && g.Code.All(c => char.IsUpper(c) && !"AEIOU".Contains(c))), "four capitals, no vowels");
            Check.True(ReferenceEquals(games[0], manager.Find(games[0].Code.ToLowerInvariant())), "lookup ignores case");
            Check.True(ReferenceEquals(games[0], manager.Find("  " + games[0].Code + "  ")), "lookup ignores spaces");
            Check.True(manager.Find("ZZZZ-nope") is null && manager.Find(null) is null, "unknown codes");
        });

        run("pyramid: fuzz: 150 matches of random legal and illegal actions never break a rule or throw", () =>
        {
            for (var seed = 0; seed < 150; seed++)
            {
                var rng = new Random(seed);
                var table = new PyramidTable(seed, start: false);
                var game = table.Game;
                var e = table.Emcee;
                for (var step = 0; step < 900; step++)
                {
                    var token = rng.Next(25) == 0 ? Stranger : e;
                    var result = rng.Next(20) switch
                    {
                        0 => game.Start(token),
                        1 or 2 => game.RevealNext(token),
                        3 or 4 => game.PickTile(token, rng.Next(-1, 8)),
                        5 or 6 => game.StartRound(token),
                        7 or 8 or 9 => game.Mark(token, (WordMark)rng.Next(3)),
                        10 => game.FixWord(token, rng.Next(-1, 8), (WordResult)rng.Next(3)),
                        11 or 12 => game.Continue(token),
                        13 => game.PickLetter(token, (char)('A' + rng.Next(-1, 27))),
                        14 => game.StartBonus(token),
                        15 => game.MarkBonus(token, (BonusMark)rng.Next(3)),
                        16 => game.SelectBonus(token, rng.Next(-1, 8)),
                        17 => game.Undo(token),
                        18 => rng.Next(3) == 0 ? game.SetBonusPrize(token, new[] { 25_000, 100_000, 5 }[rng.Next(3)]) : game.RevealDescription(token),
                        _ => game.FixBonus(token, rng.Next(-1, 8), (BonusTileState)rng.Next(4))
                    };
                    if (token == Stranger) Check.True(!result.Ok, "a stranger got something done");

                    var s = game.Snapshot();
                    Check.True(s.Teams.All(t => t.Score is >= 0 and <= 21), "a game score is between 0 and 21");
                    Check.True(s.Tiles.Count(t => t.State == PyramidTileState.Used) <= 6, "no more than six squares");
                    Check.True(s.Tiles.All(t => t.State != PyramidTileState.Hidden || t.Title == ""), "a hidden square never shows its title");
                    Check.True(s.Winnings.Count <= 2, "at most two bonus results");
                    if (s.Phase == PyramidPhase.Playing)
                    {
                        Check.True(s.Play is not null && s.Play.CurrentWord is not null, "a running clock has a word on screen");
                        Check.Equal(PyramidRules.WordsPerTopic, s.Play!.Correct + s.Play.Wrong + s.Play.Remaining, "every word is settled or waiting");
                    }
                    if (s.Phase is PyramidPhase.Reveal or PyramidPhase.Choosing or PyramidPhase.GameWon or PyramidPhase.Lobby) Check.True(s.Play is null, "no round in play");
                    if (s.Phase == PyramidPhase.BonusPlay)
                        Check.True(s.Bonus!.Tiles[s.Bonus.Current].State is BonusTileState.Waiting or BonusTileState.Passed, "the square on screen is still live");
                    if (s.Phase == PyramidPhase.BonusPlay)
                        Check.True(s.Bonus!.Tiles.Where(t => t.Index != s.Bonus.Current && t.State == BonusTileState.Waiting).All(t => t.Subject == ""), "unseen subjects stay secret");
                }
            }
        });

        Console.WriteLine("$100,000 Pyramid: the topic bank");

        run("pyramid bank: the starter set is complete: 32 topics, 5 bonus topics in each of 6 tiers, and all 26 letters", () =>
        {
            using var dir = new TempDir();
            var store = new FolderPyramidContent(dir.Path);
            var counts = store.Counts();
            Check.True(counts.Main >= 24, $"enough topics for a whole match without repeats (has {counts.Main})");
            Check.True(counts.BonusPerTier.All(n => n >= 4), "enough bonus topics in every tier for several matches");
            Check.Equal(26, counts.LettersReady, "every letter has its words");
            Check.True(File.Exists(store.FilePath), "saved to the data folder");
            foreach (var t in store.Main())
            {
                Check.Equal(PyramidRules.WordsPerTopic, t.Words.Count, $"{t.Title}: seven words");
                Check.Equal(PyramidRules.WordsPerTopic, t.Words.Select(w => w.ToUpperInvariant()).Distinct().Count(), $"{t.Title}: no repeats");
                Check.True(PyramidContentRules.Warnings([t.Title, t.Description, .. t.Words]).Count == 0, $"{t.Title}: nothing against the party rules");
            }
            foreach (var l in store.Letters())
            {
                Check.True(l.Words.Count >= PyramidRules.WordsPerTopic, $"{l.Letter} has seven words");
                Check.True(l.Words.All(w => PyramidContentRules.FirstLetter(w) == l.Letter), $"every {l.Letter} word starts with {l.Letter}");
            }
            Check.True(store.Main().Any(t => t.Title == "Lights, Camera, Action"), "commas in a title are kept");
        });

        run("pyramid bank: dealing uses fresh topics first, never repeats a title in one game, and gives one bonus topic per tier in order", () =>
        {
            using var dir = new TempDir();
            var store = new FolderPyramidContent(dir.Path);
            var rng = new Random(1);
            var seen = new HashSet<string>();
            for (var game = 0; game < 5; game++)
            {
                Check.True(store.TryCreateMain(rng, out var topics, out var error), error);
                Check.Equal(6, topics.Count, "six topics");
                Check.Equal(6, topics.Select(t => t.Title).Distinct().Count(), "six different titles");
                foreach (var t in topics) Check.True(seen.Add(t.Id), $"{t.Title} was dealt twice while the bank still had fresh ones");
                store.MarkUsed(topics.Select(t => t.Id));
            }
            Check.Equal(30, seen.Count, "five games, thirty different topics");

            for (var i = 0; i < 4; i++)
            {
                Check.True(store.TryCreateBonus(rng, out var bonus, out var error), error);
                Check.True(bonus.Select(b => b.Tier).SequenceEqual([1, 2, 3, 4, 5, 6]), "one from each tier, easiest first");
                store.MarkUsed(bonus.Select(b => b.Id));
            }
            Check.True(store.TryCreateBonus(rng, out _, out _), "when the fresh ones run out it deals used ones instead of failing");
            store.ClearUsed();
            Check.Equal(store.Counts().Main, store.Counts().MainUnused, "every topic is fresh again");
        });

        run("pyramid bank: adding a topic checks it, keeps commas, warns about party rules, and rejects what can't be played", () =>
        {
            using var dir = new TempDir();
            var store = new FolderPyramidContent(dir.Path, seedStarters: false);
            string?[] seven = ["One", "Two", "Three", "Four", "Five", "Six", "Seven"];
            Check.True(!store.AddMain("", "Things", seven).Ok, "no title");
            Check.True(!store.AddMain("Title", "", seven).Ok, "no description");
            Check.True(!store.AddMain("Title", "Things", seven.Take(6).ToArray()).Ok, "six words");
            Check.True(!store.AddMain("Title", "Things", [.. seven, "Eight"]).Ok, "eight words");
            Check.True(!store.AddMain("Title", "Things", ["One", "one", "Three", "Four", "Five", "Six", "Seven"]).Ok, "a repeated word");

            var ok = store.AddMain("Ready, Set, Go", "Name these things, quickly", seven);
            Check.True(ok.Ok, "a good topic: " + ok.Error);
            Check.Equal("Ready, Set, Go", store.Main().Single().Title, "the comma in the title survives");
            var dupe = store.AddMain("ready, set, go", "Other", seven);
            Check.True(!dupe.Ok && dupe.Duplicate, "the same title again is a duplicate");

            var warned = store.AddMain("Cold Ones", "Name these drinks", ["Beer", "Milk", "Water", "Juice", "Soda", "Cocoa", "Lemonade"]);
            Check.True(warned.Ok && warned.Warnings!.Count == 1, "saved, with a warning about the beer");
            Check.True(!store.AddBonus(0, "Things").Ok && !store.AddBonus(7, "Things").Ok, "a tier outside 1 to 6");
            Check.True(!store.AddBonus(2, " ").Ok, "a blank subject");
            Check.True(store.AddBonus(2, "Things that roll").Ok, "a good bonus topic");
            Check.True(store.AddBonus(2, "things that ROLL").Duplicate, "the same subject again");
            Check.True(store.Counts().BonusPerTier[1] == 1, "counted in tier 2");
        });

        run("pyramid bank: a letter's list needs seven words that all start with it", () =>
        {
            using var dir = new TempDir();
            var store = new FolderPyramidContent(dir.Path, seedStarters: false);
            Check.True(!store.SetLetter('M', ["Mouse", "Moon", "Map"]).Ok, "too few words");
            var wrong = store.SetLetter('M', ["Mouse", "Moon", "Map", "Mud", "Milk", "Nest", "Mop"]);
            Check.True(!wrong.Ok && wrong.Error!.Contains("Nest"), "names the word that doesn't fit: " + wrong.Error);
            Check.True(!store.SetLetter('M', ["Mouse", "mouse", "Map", "Mud", "Milk", "Mat", "Mop"]).Ok, "a repeated word");
            Check.True(!store.SetLetter('3', ["Mouse", "Moon", "Map", "Mud", "Milk", "Mat", "Mop"]).Ok, "not a letter");
            Check.True(store.SetLetter('m', ["Mouse", "Moon", "Map", "Mud", "Milk", "Mat", "Mop", "Mitten"]).Ok, "eight is fine");
            Check.Equal(8, store.LetterWords('M').Count, "stored");
            Check.True(store.SetLetter('X', ["X-ray", "Xylophone", "Xbox", "Xerox", "Xmas", "Xenon", "X-Men"]).Ok, "a word like X-ray starts with X");
            Check.Equal(2, store.Counts().LettersReady, "two letters ready");
            Check.True(store.Letters().First(l => l.Letter == 'M').Words.Count == 8, "the game can read it");
        });

        run("pyramid bank: pasting many at once adds the good lines and reports the rest", () =>
        {
            using var dir = new TempDir();
            var store = new FolderPyramidContent(dir.Path, seedStarters: false);
            var report = store.Import("""
                # main
                Bark Up the Tree | Name these things with bark | Dog, Tree, Seal, Fox, Cinnamon, Chocolate, Squirrel
                Bad one | Not enough | One, Two, Three
                Bark Up the Tree | Again | Dog, Tree, Seal, Fox, Cinnamon, Chocolate, Squirrel
                # just a comment
                # bonus
                3 | Things that are round
                9 | Out of range
                # letters
                Q | Queen, Quilt, Quarter, Quicksand, Quail, Quarterback, Quiz
                Q | Queen, Quilt, Nope, Quicksand, Quail, Quarterback, Quiz
                """);
            Check.Equal(3, report.Added, "three good lines");
            Check.Equal(1, report.Duplicates, "one duplicate");
            Check.Equal(3, report.Problems.Count, "three problems");
            Check.True(report.Problems.Any(p => p.StartsWith("Line 3")), "problems are numbered by their line");
            Check.Equal(1, store.Main().Count, "one topic");
            Check.Equal(1, store.Bonus().Count, "one bonus topic");
            Check.Equal(1, store.Counts().LettersReady, "one letter");
        });

        run("pyramid bank: topics survive a restart, deleting works, and an unreadable file is kept aside instead of overwritten", () =>
        {
            using var dir = new TempDir();
            var store = new FolderPyramidContent(dir.Path);
            var before = store.Counts();
            var id = store.Main()[0].Id;
            store.MarkUsed([id]);
            Check.True(store.Delete(store.Main()[1].Id).Ok, "deleted");
            Check.True(!store.Delete("nope").Ok, "deleting what isn't there");

            var reopened = new FolderPyramidContent(dir.Path);
            Check.Equal(before.Main - 1, reopened.Counts().Main, "the deletion was saved");
            Check.True(reopened.IsUsed(id), "and which topics were used");

            File.WriteAllText(reopened.FilePath, "{ this is not json");
            var rescued = new FolderPyramidContent(dir.Path);
            Check.True(rescued.Counts().Main > 0, "starts again from the starter set");
            Check.Equal(1, Directory.GetFiles(Path.GetDirectoryName(reopened.FilePath)!, "topics.json.broken-*").Length, "the unreadable file is kept");
        });
    }

}
