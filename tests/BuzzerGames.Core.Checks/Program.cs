using BuzzerGames.Core.Jeopardy;

// Run with:  dotnet run --project tests/BuzzerGames.Core.Checks
// Each check plays part of a game and asserts the rules held. Exit code is 0 only if all pass.

var passed = 0;
var failed = 0;

void Run(string name, Action body)
{
    try
    {
        body();
        passed++;
        Console.WriteLine($"  ok    {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}\n          {ex.Message}");
    }
}

Console.WriteLine("Lobby");

Run("a game needs 1 emcee and 3 players before it can start", () =>
{
    var game = new JeopardyGame("TEST", new DummyGameDataSource(), new Random(1));
    Check.Fails(game.Start(), "starting an empty lobby");
    Check.Ok(game.JoinEmcee("Host"));
    Check.Ok(game.JoinPlayer("Ann"));
    Check.Ok(game.JoinPlayer("Bob"));
    Check.True(!game.Snapshot().CanStart, "start should be hidden with only 2 players");
    Check.Fails(game.Start(), "starting with 2 players");
    Check.Ok(game.JoinPlayer("Cy"));
    Check.True(game.Snapshot().CanStart, "start should appear once the table is full");
    Check.Ok(game.Start());
    Check.Fails(game.Start(), "starting twice");
});

Run("only one emcee, only three players, no duplicate or blank names", () =>
{
    var game = new JeopardyGame("TEST", new DummyGameDataSource(), new Random(1));
    Check.Ok(game.JoinEmcee("Host"));
    Check.Fails(game.JoinEmcee("Second Host"), "a second emcee");
    Check.Fails(game.JoinPlayer("   "), "a blank name");
    Check.Ok(game.JoinPlayer("Ann"));
    Check.Fails(game.JoinPlayer("ann"), "a duplicate name, ignoring case");
    Check.Ok(game.JoinPlayer("Bob"));
    Check.Ok(game.JoinPlayer("Cy"));
    Check.Fails(game.JoinPlayer("Dee"), "a fourth player");
});

Run("long names are trimmed, nobody can join after the game starts", () =>
{
    var game = new JeopardyGame("TEST", new DummyGameDataSource(), new Random(1));
    Check.Ok(game.JoinEmcee("  Host  "));
    Check.Ok(game.JoinPlayer(new string('x', 50)));
    Check.Equal(JeopardyGame.NameMaxLength, game.Snapshot().Players[0].Name.Length, "trimmed name length");
    Check.Equal("Host", game.Snapshot().EmceeName, "emcee name is trimmed of spaces");
    game.JoinPlayer("Bob");
    game.JoinPlayer("Cy");
    Check.Ok(game.Start());
    Check.Fails(game.JoinEmcee("Late"), "joining a started game");
});

Run("a device's secret token maps back to its seat", () =>
{
    var table = Table.Started();
    Check.Equal(Role.Emcee, table.Game.GetSeat(table.Emcee).Role, "emcee token");
    Check.Equal(Role.Player, table.Game.GetSeat(table.Tokens[1]).Role, "player token");
    Check.Equal(table.Ids[1], table.Game.GetSeat(table.Tokens[1]).PlayerId!.Value, "player id");
    Check.Equal(Role.Spectator, table.Game.GetSeat(Guid.NewGuid()).Role, "unknown token");
});

Console.WriteLine("The board");

Run("round 1 is 6 categories of 5 clues, $200 to $1000, with one player in control", () =>
{
    var s = Table.Started().S;
    Check.Equal(GamePhase.Board, s.Phase, "phase");
    Check.Equal(1, s.Round, "round");
    Check.Equal(6, s.Categories.Count, "categories");
    foreach (var category in s.Categories)
        Check.True(category.Cells.Select(c => c.Value).SequenceEqual([200, 400, 600, 800, 1000]), $"values in {category.Name}");
    Check.True(s.ControlPlayer is not null, "someone should be in control");
});

Run("only the emcee can pick clues, and a clue can only be played once", () =>
{
    var table = Table.Started();
    Check.Fails(table.Game.SelectClue(table.Tokens[0], 0, 0), "a player picking");
    Check.Fails(table.Game.SelectClue(Guid.NewGuid(), 0, 0), "a stranger picking");
    Check.Fails(table.Game.SelectClue(table.Emcee, 9, 0), "a category that doesn't exist");
    Check.Fails(table.Game.SelectClue(table.Emcee, 0, 9), "a row that doesn't exist");
    table.PickNext();
    Check.True(table.S.Categories[0].Cells[0].IsUsed, "the picked clue is marked used");
    table.Game.GiveUp(table.Emcee);
    table.Game.Continue(table.Emcee);
    Check.Fails(table.Game.SelectClue(table.Emcee, 0, 0), "replaying a used clue");
});

Run("only the emcee can judge, and only players can buzz", () =>
{
    var table = Table.Started();
    table.OpenNormalClue();
    Check.Fails(table.Game.Buzz(table.Emcee), "the emcee buzzing");
    Check.Fails(table.Game.Buzz(Guid.NewGuid()), "a stranger buzzing");
    Check.Ok(table.Game.Buzz(table.Tokens[0]));
    Check.Fails(table.Game.Judge(table.Tokens[0], true), "a player judging themselves");
    Check.Fails(table.Game.Judge(Guid.NewGuid(), true), "a stranger judging");
});

Console.WriteLine("Answering a clue");

Run("correct answer: score goes up, the answerer takes control, Continue returns to the board", () =>
{
    var table = Table.Started();
    table.OpenNormalClue();
    var value = table.S.Clue!.Value;
    var before = table.Score(1);

    Check.Ok(table.Game.Buzz(table.Tokens[1]));
    Check.Equal(GamePhase.Answering, table.S.Phase, "phase after a buzz");
    Check.Equal(table.Ids[1], table.S.AnsweringPlayer!.Id, "who is answering");

    Check.Ok(table.Game.Judge(table.Emcee, true));
    Check.Equal(before + value, table.Score(1), "score after a correct answer");
    Check.Equal(table.Ids[1], table.S.ControlPlayer!.Id, "control after a correct answer");
    Check.Equal(GamePhase.Reveal, table.S.Phase, "phase after judging");
    Check.True(table.S.RevealMessage!.Contains("Bob"), "reveal message names the winner");

    Check.Ok(table.Game.Continue(table.Emcee));
    Check.Equal(GamePhase.Board, table.S.Phase, "phase after Continue");
    Check.True(table.S.Clue is null, "the clue is cleared from the screen");
});

Run("wrong answer: points come off, that player is locked out, the clue goes live again", () =>
{
    var table = Table.Started();
    table.OpenNormalClue();
    var value = table.S.Clue!.Value;
    var before = new[] { table.Score(0), table.Score(1), table.Score(2) };
    var controlBefore = table.S.ControlPlayer!.Id;

    Check.Ok(table.Game.Buzz(table.Tokens[0]));
    Check.Ok(table.Game.Judge(table.Emcee, false));

    Check.Equal(before[0] - value, table.Score(0), "score after a wrong answer");
    Check.Equal(GamePhase.ClueOpen, table.S.Phase, "the clue should be live again");
    Check.True(table.S.Players[0].IsLockedOut, "the wrong player is locked out");
    Check.Fails(table.Game.Buzz(table.Tokens[0]), "buzzing again after a miss");
    Check.Equal(controlBefore, table.S.ControlPlayer!.Id, "control doesn't move on a miss");

    Check.Ok(table.Game.Buzz(table.Tokens[2]));
    Check.Ok(table.Game.Judge(table.Emcee, true));
    Check.Equal(before[2] + value, table.Score(2), "the next player can still steal it");
    Check.Equal(before[1], table.Score(1), "the bystander's score is untouched");
});

Run("all three miss: everyone loses the value, the answer is revealed, control stays put", () =>
{
    var table = Table.Started();
    table.OpenNormalClue();
    var value = table.S.Clue!.Value;
    var before = new[] { table.Score(0), table.Score(1), table.Score(2) };
    var controlBefore = table.S.ControlPlayer!.Id;

    for (var i = 0; i < 3; i++)
    {
        Check.Ok(table.Game.Buzz(table.Tokens[i]));
        Check.Ok(table.Game.Judge(table.Emcee, false));
    }

    Check.Equal(GamePhase.Reveal, table.S.Phase, "phase after everyone misses");
    Check.Equal("Nobody got it.", table.S.RevealMessage, "reveal message");
    for (var i = 0; i < 3; i++) Check.Equal(before[i] - value, table.Score(i), $"player {i} score");
    Check.Equal(controlBefore, table.S.ControlPlayer!.Id, "control");
});

Run("emcee gives up: no points change, control stays, only valid while the clue is open", () =>
{
    var table = Table.Started();
    Check.Fails(table.Game.GiveUp(table.Emcee), "giving up with no clue showing");
    table.OpenNormalClue();
    var before = new[] { table.Score(0), table.Score(1), table.Score(2) };
    var controlBefore = table.S.ControlPlayer!.Id;

    Check.Fails(table.Game.GiveUp(table.Tokens[0]), "a player giving up for the table");
    Check.Ok(table.Game.GiveUp(table.Emcee));
    Check.Equal(GamePhase.Reveal, table.S.Phase, "phase");
    for (var i = 0; i < 3; i++) Check.Equal(before[i], table.Score(i), $"player {i} score");
    Check.Equal(controlBefore, table.S.ControlPlayer!.Id, "control");
    Check.Fails(table.Game.Buzz(table.Tokens[0]), "buzzing after the answer is revealed");
});

Run("first buzz wins; a second buzz is told it was too slow", () =>
{
    var table = Table.Started();
    table.OpenNormalClue();
    Check.Ok(table.Game.Buzz(table.Tokens[2]));
    Check.Fails(table.Game.Buzz(table.Tokens[0]), "a buzz that arrives second");
    Check.Equal(table.Ids[2], table.S.AnsweringPlayer!.Id, "the first buzzer keeps the floor");
});

Run("three phones buzzing at the exact same instant: exactly one wins (200 trials)", () =>
{
    for (var trial = 0; trial < 200; trial++)
    {
        var table = Table.Started(seed: trial);
        table.OpenNormalClue();

        var barrier = new Barrier(3);
        var wins = 0;
        var tasks = Enumerable.Range(0, 3).Select(i => Task.Run(() =>
        {
            barrier.SignalAndWait();
            if (table.Game.Buzz(table.Tokens[i]).Ok) Interlocked.Increment(ref wins);
        })).ToArray();
        Task.WaitAll(tasks);

        Check.Equal(1, wins, $"winners in trial {trial}");
    }
});

Console.WriteLine("Daily Doubles");

Run("Daily Double: hidden until the wager, only the player in control can wager within the limits", () =>
{
    var table = Table.Started();
    table.OpenDailyDouble();

    var control = table.S.ControlPlayer!;
    var controlToken = table.TokenOf(control.Id);
    var other = table.Tokens.First(t => t != controlToken);

    Check.True(table.S.Clue!.IsDailyDouble, "flagged as a Daily Double");
    Check.Equal("", table.S.Clue!.Text, "clue text is hidden during the wager");
    Check.Equal("", table.S.Clue!.Answer, "answer is hidden during the wager");
    Check.Equal(5, table.S.MinWager, "minimum wager");
    Check.Equal(Math.Max(control.Score, 1000), table.S.MaxWager, "maximum wager");

    Check.Fails(table.Game.SubmitDailyDoubleWager(other, 100), "a player who isn't in control wagering");
    Check.Fails(table.Game.SubmitDailyDoubleWager(controlToken, 4), "a wager under $5");
    Check.Fails(table.Game.SubmitDailyDoubleWager(controlToken, table.S.MaxWager + 1), "a wager over the limit");
    Check.Ok(table.Game.SubmitDailyDoubleWager(controlToken, 300));

    Check.Equal(GamePhase.DailyDoubleAnswering, table.S.Phase, "phase after the wager");
    Check.True(table.S.Clue!.Text.Length > 0, "clue text appears after the wager");
    Check.Equal(300, table.S.Clue!.Wager, "wager shown");
    Check.Fails(table.Game.Buzz(other), "anyone else buzzing on a Daily Double");
});

Run("Daily Double: a right answer wins the wager, a wrong one loses it, and control stays either way", () =>
{
    foreach (var correct in new[] { true, false })
    {
        var table = Table.Started();
        table.OpenDailyDouble();
        var controlId = table.S.ControlPlayer!.Id;
        var before = table.S.ControlPlayer!.Score;

        Check.Ok(table.Game.SubmitDailyDoubleWager(table.TokenOf(controlId), 300));
        Check.Ok(table.Game.Judge(table.Emcee, correct));

        Check.Equal(before + (correct ? 300 : -300), table.S.FindPlayer(controlId)!.Score, $"score after correct={correct}");
        Check.Equal(GamePhase.Reveal, table.S.Phase, "phase");
        Check.Equal(controlId, table.S.ControlPlayer!.Id, "control stays with the Daily Double player");
    }
});

Console.WriteLine("Whole games");

Run("round 1 has 1 Daily Double, round 2 has 2, and Round 2 starts with whoever is behind", () =>
{
    var table = Table.Started();

    var round1DailyDoubles = table.PlayRound(_ => 0);   // Ann answers everything
    Check.Equal(1, round1DailyDoubles, "Daily Doubles in round 1");
    Check.Equal(2, table.S.Round, "round after clearing the board");
    Check.Equal(GamePhase.Board, table.S.Phase, "phase");
    Check.True(table.S.Categories.All(c => c.Cells.Select(x => x.Value).SequenceEqual([400, 800, 1200, 1600, 2000])), "round 2 values");
    Check.Equal(table.Ids[1], table.S.ControlPlayer!.Id, "control goes to the first player with the lowest score");

    var round2DailyDoubles = table.PlayRound(_ => 0);
    Check.Equal(2, round2DailyDoubles, "Daily Doubles in round 2");
});

Run("Final Jeopardy with all three players: wagers, answers, judging, winner", () =>
{
    var table = Table.Started(seed: 7);
    table.PlayRound(i => i % 3);
    table.PlayRound(i => i % 3);

    var s = table.S;
    Check.Equal(GamePhase.FinalWager, s.Phase, "phase after round 2");
    Check.True(s.Players.All(p => p.InFinal), "everyone with a positive score qualifies");
    Check.True(s.FinalCategory is not null, "the category is shown for wagering");
    Check.True(s.FinalText is null, "the clue is hidden during wagering");
    Check.True(s.FinalAnswer is null, "the answer is hidden during wagering");

    var before = s.Players.Select(p => p.Score).ToArray();

    Check.Fails(table.Game.SubmitFinalWager(table.Tokens[0], -1), "a negative wager");
    Check.Fails(table.Game.SubmitFinalWager(table.Tokens[0], before[0] + 1), "betting more than you have");
    Check.Ok(table.Game.SubmitFinalWager(table.Tokens[0], 100));
    Check.Fails(table.Game.SubmitFinalWager(table.Tokens[0], 50), "changing a locked-in wager");
    Check.Ok(table.Game.SubmitFinalWager(table.Tokens[1], 0));
    Check.Equal(GamePhase.FinalWager, table.S.Phase, "still waiting on the third wager");
    Check.Ok(table.Game.SubmitFinalWager(table.Tokens[2], before[2]));

    s = table.S;
    Check.Equal(GamePhase.FinalAnswer, s.Phase, "phase once all wagers are in");
    Check.True(s.FinalText is not null, "the clue appears for answering");
    Check.True(s.FinalAnswer is null, "the correct answer is still hidden");

    Check.Ok(table.Game.SubmitFinalAnswer(table.Tokens[2], "Some answer"));
    Check.Fails(table.Game.SubmitFinalAnswer(table.Tokens[2], "A second answer"), "changing a locked-in answer");
    Check.Ok(table.Game.SubmitFinalAnswer(table.Tokens[0], "Another"));
    Check.Ok(table.Game.SubmitFinalAnswer(table.Tokens[1], ""));

    Check.Equal(GamePhase.FinalJudging, table.S.Phase, "phase once all answers are in");
    Check.True(table.S.FinalAnswer is not null, "the correct answer is revealed for judging");

    Check.Fails(table.Game.JudgeFinal(table.Tokens[0], table.Ids[0], true), "a player judging");
    Check.Ok(table.Game.JudgeFinal(table.Emcee, table.Ids[0], true));    // Ann right: + 100
    Check.Fails(table.Game.JudgeFinal(table.Emcee, table.Ids[0], false), "judging the same answer twice");
    Check.Ok(table.Game.JudgeFinal(table.Emcee, table.Ids[1], false));   // Bob wrong: - 0
    Check.Equal(GamePhase.FinalJudging, table.S.Phase, "still judging");
    Check.Ok(table.Game.JudgeFinal(table.Emcee, table.Ids[2], false));   // Cy wrong: loses everything

    s = table.S;
    Check.Equal(GamePhase.GameOver, s.Phase, "phase after the last judgment");
    Check.Equal(before[0] + 100, s.Players[0].Score, "Ann's final score");
    Check.Equal(before[1], s.Players[1].Score, "Bob's final score");
    Check.Equal(0, s.Players[2].Score, "Cy bet everything and missed");

    var best = s.Players.Max(p => p.Score);
    Check.True(s.Winners.SequenceEqual(s.Players.Where(p => p.Score == best).Select(p => p.Id)), "winners are the top scorers");
});

Run("Final Jeopardy: only players with a positive score qualify", () =>
{
    var table = Table.Started();
    table.PlayRound(_ => 0);   // Ann answers everything in both rounds, Bob and Cy stay at $0
    table.PlayRound(_ => 0);

    var s = table.S;
    Check.Equal(GamePhase.FinalWager, s.Phase, "phase");
    Check.True(s.Players[0].InFinal, "Ann qualifies");
    Check.True(!s.Players[1].InFinal && !s.Players[2].InFinal, "Bob and Cy are out");
    Check.Fails(table.Game.SubmitFinalWager(table.Tokens[1], 0), "a non-qualifier wagering");

    Check.Ok(table.Game.SubmitFinalWager(table.Tokens[0], s.Players[0].Score));
    Check.Equal(GamePhase.FinalAnswer, table.S.Phase, "the only qualifier's wager moves things on");
    Check.Fails(table.Game.SubmitFinalAnswer(table.Tokens[1], "hi"), "a non-qualifier answering");
    Check.Ok(table.Game.SubmitFinalAnswer(table.Tokens[0], "x"));
    Check.Ok(table.Game.JudgeFinal(table.Emcee, table.Ids[0], true));
    Check.Equal(GamePhase.GameOver, table.S.Phase, "game over");
    Check.True(table.S.Winners.SequenceEqual([table.Ids[0]]), "Ann wins");
});

Run("Final Jeopardy: if nobody has a positive score the game just ends", () =>
{
    var table = Table.Started();
    table.PlayRound(_ => 0, giveUp: true);
    table.PlayRound(_ => 0, giveUp: true);
    Check.Equal(GamePhase.GameOver, table.S.Phase, "phase");
    Check.True(table.S.Players.All(p => p.Score <= 0), "nobody scored");
});

Run("emcee can lock in missing Final Jeopardy wagers and answers if a phone dies", () =>
{
    var table = Table.Started(seed: 3);
    table.PlayRound(i => i % 3);
    table.PlayRound(i => i % 3);
    var before = table.S.Players.Select(p => p.Score).ToArray();

    Check.Ok(table.Game.SubmitFinalWager(table.Tokens[0], 50));
    Check.Fails(table.Game.ForceFinalAdvance(table.Tokens[0]), "a player forcing things along");
    Check.Ok(table.Game.ForceFinalAdvance(table.Emcee));
    Check.Equal(GamePhase.FinalAnswer, table.S.Phase, "phase after locking wagers");
    Check.Equal(0, table.S.Players[1].FinalWager!.Value, "a missing wager counts as $0");

    Check.Ok(table.Game.SubmitFinalAnswer(table.Tokens[0], "typed"));
    Check.Ok(table.Game.ForceFinalAdvance(table.Emcee));
    Check.Equal(GamePhase.FinalJudging, table.S.Phase, "phase after locking answers");
    Check.Equal("", table.S.Players[2].FinalAnswer, "a missing answer is blank");

    for (var i = 0; i < 3; i++) Check.Ok(table.Game.JudgeFinal(table.Emcee, table.Ids[i], true));
    Check.Equal(GamePhase.GameOver, table.S.Phase, "game over");
    Check.Equal(before[0] + 50, table.S.Players[0].Score, "Ann's winnings");
    Check.Equal(before[1], table.S.Players[1].Score, "Bob's score is unchanged by a $0 wager");
});

Console.WriteLine("Plumbing");

Run("Changed fires on every successful action and stays quiet on failures", () =>
{
    var game = new JeopardyGame("TEST", new DummyGameDataSource(), new Random(1));
    var fired = 0;
    game.Changed += () => fired++;

    Check.Ok(game.JoinEmcee("Host"));
    Check.Equal(1, fired, "after a successful join");
    Check.Fails(game.JoinEmcee("Another"), "a second emcee");
    Check.Fails(game.JoinPlayer(""), "a blank name");
    Check.Fails(game.Start(), "starting early");
    Check.Equal(1, fired, "failures shouldn't notify anyone");
});

Run("room codes are unique, 4 letters, and found regardless of case", () =>
{
    var manager = new GameRoomManager(new DummyGameDataSource());
    var games = Enumerable.Range(0, 100).Select(_ => manager.Create()).ToList();
    Check.Equal(100, games.Select(g => g.Code).Distinct().Count(), "distinct codes");
    Check.True(games.All(g => g.Code.Length == 4 && g.Code.All(char.IsUpper)), "codes are 4 capital letters");
    Check.True(ReferenceEquals(games[0], manager.Find(games[0].Code.ToLowerInvariant())), "lookup ignores case");
    Check.True(ReferenceEquals(games[0], manager.Find("  " + games[0].Code + "  ")), "lookup ignores spaces");
    Check.True(manager.Find("ZZZZ-nope") is null, "unknown code");
    Check.True(manager.Find(null) is null, "null code");
});

Run("the dummy data always makes a valid game (200 random games)", () =>
{
    var source = new DummyGameDataSource();
    for (var seed = 0; seed < 200; seed++)
    {
        var game = source.CreateGame(new Random(seed));
        Check.Equal(6, game.Round1.Count, "round 1 categories");
        Check.Equal(6, game.Round2.Count, "round 2 categories");
        foreach (var category in game.Round1.Concat(game.Round2))
        {
            Check.Equal(5, category.Clues.Count, $"clues in {category.Name}");
            Check.True(category.Clues.All(c => c.Text.Length > 0 && c.Answer.Length > 0), $"empty text in {category.Name}");
        }
        var names = game.Round1.Concat(game.Round2).Select(c => c.Name).ToList();
        Check.Equal(12, names.Distinct().Count(), "no category repeated within a game");
        Check.True(game.Final.Text.Length > 0 && game.Final.Answer.Length > 0, "final clue is filled in");
    }
});

Run("the engine refuses a data source that returns a broken board", () =>
{
    var game = new JeopardyGame("TEST", new BrokenSource(), new Random(1));
    game.JoinEmcee("Host");
    game.JoinPlayer("Ann");
    game.JoinPlayer("Bob");
    game.JoinPlayer("Cy");
    var threw = false;
    try { game.Start(); } catch (InvalidOperationException) { threw = true; }
    Check.True(threw, "Start should throw when a round has too few categories");
    Check.Equal(GamePhase.Lobby, game.Snapshot().Phase, "the game stays in the lobby");
});

Console.WriteLine("The question bank");

var bank = QuestionBank.LoadCategories();
var bankFinals = QuestionBank.LoadFinals();
var validTags = new[] { "wordplay", "pop", "geography", "history", "science", "books", "arts", "food", "sports", "potpourri", "holiday" };
var showReport = Environment.GetEnvironmentVariable("BANK_REPORT") == "1";

Run("bank: every category has 5 filled-in clues, a known tag and a known round", () =>
{
    Check.True(bank.Count >= 150, $"expected a few hundred categories, found {bank.Count}");
    foreach (var c in bank)
    {
        Check.True(c.Clues.Length == 5, $"'{c.Title}' has {c.Clues.Length} clues");
        Check.True(validTags.Contains(c.Tag), $"'{c.Title}' has unknown tag '{c.Tag}'");
        Check.True(c.Round is "single" or "double" or "any", $"'{c.Title}' has unknown round '{c.Round}'");
        foreach (var clue in c.Clues)
            Check.True(!string.IsNullOrWhiteSpace(clue.Clue) && !string.IsNullOrWhiteSpace(clue.Answer), $"'{c.Title}' has a blank clue or answer");
    }
    var duplicates = bank.GroupBy(c => c.Title.ToLowerInvariant()).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
    Check.True(duplicates.Count == 0, "duplicate category titles: " + string.Join(", ", duplicates));
});

Run("bank: no clue contains its own answer, and no category repeats an answer", () =>
{
    var stop = new HashSet<string> { "the", "and", "of", "a", "an", "to", "in", "on", "for", "de", "la", "van", "von", "jr", "ii", "iii" };
    var leaks = new List<string>();
    var soft = new List<string>();
    foreach (var c in bank)
    {
        var answers = c.Clues.Select(x => x.Answer.ToLowerInvariant()).ToList();
        Check.True(answers.Distinct().Count() == answers.Count, $"'{c.Title}' repeats an answer");
        foreach (var clue in c.Clues)
        {
            var clueWords = System.Text.RegularExpressions.Regex.Matches(clue.Clue.ToLowerInvariant(), "[a-z0-9']+").Select(m => m.Value.Trim('\'')).ToList();
            var answerWords = System.Text.RegularExpressions.Regex.Matches(clue.Answer.ToLowerInvariant(), "[a-z0-9]+").Select(m => m.Value).Where(w => !stop.Contains(w)).ToList();
            foreach (var aw in answerWords)
            {
                if (aw.Length >= 3 && clueWords.Contains(aw)) leaks.Add($"{c.Title}: '{clue.Answer}' appears in its clue");
                // Softer: a long answer word that shares a 4+ letter chunk with a clue word (like 'ship' in warship/Battleship).
                if (aw.Length >= 6)
                    foreach (var cw in clueWords.Where(w => w.Length >= 5))
                        for (var i = 0; i + 4 <= aw.Length; i++)
                            if (cw.Contains(aw.Substring(i, 4)) && !cw.Equals(aw))
                            { soft.Add($"{c.Title}: '{clue.Answer}' ~ '{cw}'"); i = aw.Length; }
            }
        }
    }
    if (showReport) foreach (var w in soft.Distinct()) Console.WriteLine("          maybe: " + w);
    if (showReport) foreach (var w in leaks.Distinct()) Console.WriteLine("          LEAK?: " + w);
    // Hard failure only when the WHOLE answer sits inside the clue; shared single words are reported above for a human to judge.
    var whole = bank.SelectMany(c => c.Clues.Select(x => (c, x)))
        .Where(t => t.x.Answer.Length >= 3 && System.Text.RegularExpressions.Regex.IsMatch(t.x.Clue, @"\b" + System.Text.RegularExpressions.Regex.Escape(t.x.Answer) + @"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        .Select(t => $"{t.c.Title}: '{t.x.Answer}'").ToList();
    Check.True(whole.Count == 0, "whole answer inside its clue: " + string.Join(" | ", whole));
});

Run("bank: finals are filled in, and there are enough of each kind", () =>
{
    Check.True(bankFinals.Count(f => !f.Holiday) >= 10, "at least 10 regular finals");
    Check.True(bankFinals.Any(f => f.Holiday), "at least one holiday final");
    foreach (var f in bankFinals)
        Check.True(!string.IsNullOrWhiteSpace(f.Category) && !string.IsNullOrWhiteSpace(f.Clue) && !string.IsNullOrWhiteSpace(f.Answer), "a final is blank");
});

Console.WriteLine("The shuffle");

Run("shuffle: 3,000 games always deal 2 wordplay + 2 pop + 2 different others per round, no repeats", () =>
{
    var source = new JsonGameDataSource();
    var byTitle = bank.ToDictionary(c => c.Title.ToUpperInvariant());
    var wrongRound = 0;
    for (var seed = 0; seed < 3000; seed++)
    {
        var holiday = seed % 2 == 0;
        var game = source.CreateGame(new Random(seed), new GameOptions(holiday));
        var all = game.Round1.Concat(game.Round2).ToList();
        Check.True(all.Select(c => c.Name).Distinct().Count() == 12, $"seed {seed}: a category repeated");
        var holidayCount = 0;
        foreach (var (round, number) in new[] { (game.Round1, 1), (game.Round2, 2) })
        {
            Check.True(round.Count == 6, $"seed {seed}: round {number} has {round.Count} categories");
            var records = round.Select(c => byTitle[c.Name]).ToList();
            Check.True(records.Count(r => r.Tag == "wordplay") == 2, $"seed {seed}: round {number} wordplay count");
            Check.True(records.Count(r => r.Tag == "pop") == 2, $"seed {seed}: round {number} pop count");
            var others = records.Where(r => r.Tag is not ("wordplay" or "pop")).Select(r => r.Tag).ToList();
            Check.True(others.Distinct().Count() == others.Count, $"seed {seed}: round {number} repeats a tag among the others: {string.Join(",", others)}");
            holidayCount += records.Count(r => r.Tag == "holiday");
            wrongRound += records.Count(r => r.Round == (number == 1 ? "double" : "single"));
        }
        Check.True(holidayCount == (holiday ? 1 : 0), $"seed {seed}: holiday option {holiday} but {holidayCount} holiday categories");
        var finalIsHoliday = bankFinals.Any(f => f.Holiday && f.Category.ToUpperInvariant() == game.Final.Category);
        Check.True(holiday || !finalIsHoliday, $"seed {seed}: a holiday final appeared in a regular game");
    }
    Check.True(wrongRound == 0, $"{wrongRound} categories landed in a round they weren't written for");
});

Run("shuffle: the holiday category lands in both rounds over time, and every category gets used", () =>
{
    var source = new JsonGameDataSource();
    var byTitle = bank.ToDictionary(c => c.Title.ToUpperInvariant());
    int inRound1 = 0, inRound2 = 0;
    var used = new HashSet<string>();
    for (var seed = 0; seed < 3000; seed++)
    {
        var game = source.CreateGame(new Random(seed), new GameOptions(true));
        if (game.Round1.Any(c => byTitle[c.Name].Tag == "holiday")) inRound1++; else inRound2++;
        foreach (var c in game.Round1.Concat(game.Round2)) used.Add(c.Name);
    }
    Check.True(inRound1 > 1000 && inRound2 > 1000, $"holiday split was {inRound1}/{inRound2}");
    var neverUsed = bank.Where(c => !used.Contains(c.Title.ToUpperInvariant()) && c.Tag != "holiday").Select(c => c.Title).ToList();
    Console.WriteLine($"          (holiday round split {inRound1}/{inRound2}; {used.Count} of {bank.Count} categories appeared across the games)");
    Check.True(neverUsed.Count == 0, "never dealt: " + string.Join(", ", neverUsed.Take(10)));
});

Run("shuffle: a regular game deals no holiday categories, with the same seeds", () =>
{
    var source = new JsonGameDataSource();
    var holidayTitles = bank.Where(c => c.Tag == "holiday").Select(c => c.Title.ToUpperInvariant()).ToHashSet();
    for (var seed = 0; seed < 2000; seed++)
    {
        var game = source.CreateGame(new Random(seed), GameOptions.Default);
        Check.True(!game.Round1.Concat(game.Round2).Any(c => holidayTitles.Contains(c.Name)), $"seed {seed}");
    }
});

Run("shuffle: real data plays through the engine (500 games started, 1 clue picked in each)", () =>
{
    var source = new JsonGameDataSource();
    for (var seed = 0; seed < 500; seed++)
    {
        var game = new JeopardyGame("TEST", source, new Random(seed), new GameOptions(seed % 2 == 0));
        game.JoinEmcee("Host"); game.JoinPlayer("Ann"); game.JoinPlayer("Bob"); game.JoinPlayer("Cy");
        Check.Ok(game.Start());
    }
});

PylChecks.Run(Run);

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

sealed class BrokenSource : IGameDataSource
{
    public GameData CreateGame(Random rng, GameOptions? options = null)
    {
        var good = new DummyGameDataSource().CreateGame(rng);
        return good with { Round1 = good.Round1.Take(5).ToList() };
    }
}
