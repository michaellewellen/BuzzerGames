using BuzzerGames.Core.Floor;
using BuzzerGames.Core.Jeopardy;

/// <summary>
/// A Floor game with a fake clock. Players are Ann, Bob, Cy, ... and player i picks "Topic (i+1)". Time only moves when a check says so.
/// </summary>
sealed class FloorTable
{
    public FloorGame Game { get; }
    public Guid Emcee { get; }
    public List<Guid> Tokens { get; } = [];
    public List<Guid> Ids { get; } = [];
    public DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    public FixedFloorContent Content { get; }

    private static readonly string[] Names = ["Ann", "Bob", "Cy", "Dee", "Eve", "Fay", "Gus", "Hal", "Ivy", "Jo", "Kai", "Lee", "Mia", "Ned", "Oz", "Pam", "Quin", "Ray", "Sue", "Tom"];

    public FloorTable(int players = 5, int topics = 30, int promptsEach = 120, int seed = 1, bool build = true, FixedFloorContent? content = null, bool pick = true)
    {
        Content = content ?? new FixedFloorContent(topics, promptsEach);
        Game = new FloorGame("TEST", Content, new Random(seed), () => Now);
        Emcee = Game.JoinEmcee("Host").Token;
        for (var i = 0; i < players; i++)
        {
            Tokens.Add(Game.JoinPlayer(Names[i]).Token);
            Ids.Add(Game.GetSeat(Tokens[i]).PlayerId!.Value);
        }
        if (pick) for (var i = 0; i < players; i++) Check.Ok(Game.PickTopic(Tokens[i], $"Topic {i + 1:00}"));
        if (build) Check.Ok(Game.Build(Emcee));
    }

    public FloorSnapshot S => Game.Snapshot();

    public void Advance(int ms)
    {
        Now = Now.AddMilliseconds(ms);
        Game.Tick();
    }

    /// <summary>The challenger picks the first neighbor offered, and the emcee starts the duel.</summary>
    public void StartDuel()
    {
        var s = S;
        Check.Equal(FloorPhase.Choose, s.Phase, "choosing an opponent");
        Check.Ok(Game.Challenge(Emcee, s.Candidates[0]));
        if (S.Phase == FloorPhase.Repick) Check.Ok(Game.AssignRandomTopic(Emcee, S.RepickPlayerId!.Value));
        Check.Ok(Game.StartDuel(Emcee));
    }

    public Guid Active => S.Duel!.ActiveId;
    public Guid Other => S.Duel!.ActiveId == S.Duel.ChallengerId ? S.Duel.DefenderId : S.Duel.ChallengerId;

    /// <summary>Plays a duel the challenger wins: they answer, and the defender's clock runs out.</summary>
    public void ChallengerWinsDuel()
    {
        var challenger = S.Duel!.ChallengerId;
        Check.Equal(challenger, Active, "challenger starts");
        Check.Ok(Game.Correct(Emcee));
        Advance(S.Duel!.DefenderMs + 1);
    }

    /// <summary>Plays a duel the challenger loses: their own clock runs out.</summary>
    public void ChallengerLosesDuel() => Advance(S.Duel!.ChallengerMs + 1);

    public static FloorShape Shape(int n) => FloorShape.For(n);
}

static class FloorChecks
{
    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine("The Floor");

        // ---- shape ----
        run("floor shape: every player count makes one connected block with one square each", () =>
        {
            for (var n = 1; n <= 40; n++)
            {
                var shape = FloorShape.For(n);
                Check.Equal(n, shape.Cells.Count, $"{n} players get {n} squares");
                Check.True(shape.Cells.Distinct().Count() == n, "no two squares share a place");
                var seen = new HashSet<int> { 0 };
                var todo = new Queue<int>([0]);
                while (todo.Count > 0)
                    foreach (var next in shape.Neighbors(todo.Dequeue()))
                        if (seen.Add(next)) todo.Enqueue(next);
                Check.Equal(n, seen.Count, $"{n} squares are all connected");
                Check.True(shape.Cells.All(c => c.Row >= 0 && c.Row < shape.Rows && c.Col >= 0 && c.Col < shape.Cols), "inside the grid");
            }
        });

        run("floor shape: sensible blocks for the sizes a party will have", () =>
        {
            var four = FloorShape.For(4);
            Check.True(four.Rows * four.Cols == 4, "4 is a full rectangle");
            var twelve = FloorShape.For(12);
            Check.Equal(12, twelve.Rows * twelve.Cols, "12 is a full rectangle");
            Check.True(twelve.Cols > twelve.Rows, "wider than tall for a TV");
            var thirteen = FloorShape.For(13);
            Check.True(thirteen.Rows * thirteen.Cols - 13 <= thirteen.Cols - 1, "13 has one ragged row, not a mess");
            // Neighbors share an edge, not a corner.
            var shape = FloorShape.For(12);
            foreach (var cell in Enumerable.Range(0, 12))
                foreach (var n in shape.Neighbors(cell))
                    Check.Equal(1, shape.Distance(cell, n), "neighbors are one step away");
        });

        // ---- text helpers ----
        run("answers come from file names: dashes, case, duplicate markers and alternates", () =>
        {
            Check.Equal("Golden Retriever", FloorText.AnswerFromFileName("golden-retriever.jpg").Answer, "dashes become spaces and words are capitalized");
            Check.Equal("Golden Retriever", FloorText.AnswerFromFileName("golden-retriever_2.png").Answer, "_2 is only a duplicate marker");
            Check.Equal("Golden Retriever", FloorText.AnswerFromFileName("golden-retriever (3).png").Answer, "(3) is only a duplicate marker");
            Check.Equal("iPhone", FloorText.AnswerFromFileName("iPhone.png").Answer, "a name with a capital is left alone");
            var (answer, alts) = FloorText.AnswerFromFileName("lord-of-the-rings~lotr~the-fellowship.jpg");
            Check.Equal("Lord Of The Rings", answer, "first part is the answer");
            Check.Equal(2, alts.Count, "the rest are alternates");
            Check.Equal("Lotr", alts[0], "alternates are tidied the same way");
            Check.Equal("star-wars-a-new-hope", FloorText.Slugify("Star Wars: A New Hope!"), "slugs are file-safe");
        });

        run("text prompts parse: clue | answer | alternates, comments, topic header, bad lines counted", () =>
        {
            var list = FloorText.ParseTextPrompts("# Topic: Capitals\n\n# a comment\nFrance | Paris\nSpain | Madrid | Madrid, Spain; Madrid Spain\nnonsense\n | missing clue\n", out var topic, out var skipped);
            Check.Equal("Capitals", topic ?? "", "topic header");
            Check.Equal(2, list.Count, "two good lines");
            Check.Equal(2, list[1].Alternates.Count, "two alternates");
            Check.Equal(2, skipped, "two bad lines");
        });

        run("topic names are cleaned for use as folder names", () =>
        {
            Check.Equal("Dog Breeds", FloorText.CleanTopicName("  Dog   Breeds  ") ?? "", "spaces tidy");
            Check.True(FloorText.CleanTopicName("../..") is null || !FloorText.CleanTopicName("../..")!.Contains('/'), "no path tricks");
            Check.True(FloorText.CleanTopicName("   ") is null, "blank is nothing");
            Check.True(FloorText.CleanTopicName("a/b\\c") == "abc", "slashes removed");
            Check.True(FloorText.CleanTopicName("..") is null, "dots only is nothing");
        });

        run("pictures are recognised by their bytes, not their names", () =>
        {
            Check.Equal(".jpg", FloorText.DetectImageExtension([0xFF, 0xD8, 0xFF, 0xE0, 0, 0]) ?? "", "jpeg");
            Check.Equal(".png", FloorText.DetectImageExtension([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]) ?? "", "png");
            Check.True(FloorText.DetectImageExtension("<html>nope</html>"u8) is null, "html is not a picture");
            Check.True(FloorText.DetectImageExtension("MZ.exe......"u8) is null, "an exe is not a picture");
        });

        // ---- the topic folder ----
        run("topic folder: create, upload, list, serve, delete; starters ship built in", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "floor-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FolderFloorContent(dir, minPrompts: 3);
                var names = store.ListTopics().Select(t => t.Name).ToList();
                Check.True(names.Contains("Acronyms") && names.Contains("Finish the Book Title") && names.Contains("Name the Movie from the Quote"), "the three starter topics are there");

                Check.Ok(store.CreateTopic("Dog Breeds").Ok ? Result.Success : Result.Fail("create"));
                Check.True(!store.CreateTopic("dog breeds").Ok, "no duplicate topic, ignoring case");
                var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6 };
                Check.True(store.SaveImage("Dog Breeds", "Golden Retriever", "", jpeg).Ok, "save a picture");
                Check.True(store.SaveImage("Dog Breeds", "Golden Retriever", "", jpeg).Ok, "same answer twice is fine");
                Check.True(store.SaveImage("Dog Breeds", "Poodle", "Standard Poodle; Toy Poodle", jpeg).Ok, "alternates");
                Check.True(!store.SaveImage("Dog Breeds", "Fake", "", "<html>"u8.ToArray()).Ok, "not a picture");
                Check.True(!store.SaveImage("Dog Breeds", "", "", jpeg).Ok, "needs an answer");
                Check.True(!store.SaveImage("Nope", "X", "", jpeg).Ok, "unknown topic");

                var prompts = store.LoadPrompts("dog breeds");
                Check.Equal(3, prompts.Count, "three pictures");
                Check.True(prompts.Count(p => p.Answer == "Golden Retriever") == 2, "the duplicate keeps its answer");
                Check.True(prompts.Single(p => p.Answer == "Poodle").Alternates.Count == 2, "alternates come back");
                Check.True(prompts.All(p => p.IsImage && !p.ImageKey!.Contains("poodle", StringComparison.OrdinalIgnoreCase) && !p.ImageKey.Contains("retriever", StringComparison.OrdinalIgnoreCase)), "keys don't reveal the answer");
                Check.True(store.TryGetImage(prompts[0].ImageKey!, out var path, out var type) && File.Exists(path) && type == "image/jpeg", "the key finds the file");
                Check.True(!store.TryGetImage("../../etc/passwd", out _, out _), "no path tricks through the key");

                var info = store.ListTopics().Single(t => t.Name == "Dog Breeds");
                Check.Equal(3, info.Images, "image count");

                // Used topics survive a restart.
                store.MarkUsed("Dog Breeds");
                Check.True(new FolderFloorContent(dir, 3).ListTopics().Single(t => t.Name == "Dog Breeds").Used, "used list is saved to disk");
                store.ClearUsed();
                Check.True(!store.ListTopics().Single(t => t.Name == "Dog Breeds").Used, "cleared");

                // Adding to a built-in topic copies it out first, so nothing is lost.
                var before = store.ListTopics().Single(t => t.Name == "Acronyms").Total;
                var (added, n, _) = store.AddTextPrompts("Acronyms", "SPAM | A lunch meat | Spiced Ham");
                Check.True(added.Ok && n == 1, "added a text prompt");
                Check.Equal(before + 1, store.ListTopics().Single(t => t.Name == "Acronyms").Total, "starters plus the new one");

                var firstFile = store.ListImages("Dog Breeds")[0].File;
                Check.True(store.DeleteImage("Dog Breeds", firstFile).Ok, "delete a picture");
                Check.True(!store.DeleteImage("Dog Breeds", "../x.jpg").Ok, "no path tricks when deleting");
                Check.Equal(2, store.LoadPrompts("Dog Breeds").Count, "one fewer");
                Check.True(store.DeleteTopic("Dog Breeds").Ok, "delete a topic");
                Check.True(store.ListTopics().All(t => t.Name != "Dog Breeds"), "gone");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        });

        run("starter topics are big enough to play and free of things we promised to leave out", () =>
        {
            var store = new FolderFloorContent(Path.Combine(Path.GetTempPath(), "floor-check-" + Guid.NewGuid().ToString("N")), 20);
            try
            {
                foreach (var name in new[] { "Acronyms", "Finish the Book Title", "Name the Movie from the Quote" })
                {
                    var prompts = store.LoadPrompts(name);
                    Check.True(prompts.Count >= 40, $"{name} has {prompts.Count} prompts");
                    Check.True(prompts.All(p => p.Text is { Length: > 0 } && p.Answer.Length > 0), $"{name}: every prompt has a clue and an answer");
                    Check.True(prompts.Select(p => p.Text).Distinct().Count() >= prompts.Count - 2, $"{name}: no big duplicates");
                    Check.True(!prompts.Any(p => new[] { "beer", "wine", "vodka", "whiskey", "whisky", "coffee", "damn", "hell " }
                        .Any(w => (p.Text + " " + p.Answer).Contains(w, StringComparison.OrdinalIgnoreCase))), $"{name}: nothing off-limits");
                }
            }
            finally
            {
                var d = store.DataDirectory;
                if (Directory.Exists(d)) Directory.Delete(d, true);
            }
        });

        // ---- lobby ----
        run("lobby: one emcee, unique names, topics picked once, min players, then build", () =>
        {
            var game = new FloorGame("TEST", new FixedFloorContent(), new Random(1));
            Check.Ok(game.JoinEmcee("Host"));
            Check.Fails(game.JoinEmcee("Other"), "a second emcee");
            var emcee = game.JoinEmcee("x").Token; // fails, token default
            var host = new FloorTable(0, build: false);
            var ann = host.Game.JoinPlayer("Ann");
            Check.Ok(ann);
            Check.Fails(host.Game.JoinPlayer("ann"), "duplicate name");
            Check.Fails(host.Game.JoinPlayer("  "), "blank name");
            Check.Fails(host.Game.Build(host.Emcee), "build with too few players");

            var t = new FloorTable(5, build: false);
            Check.Fails(t.Game.PickTopic(t.Tokens[1], "Topic 01"), "someone already has Topic 01");
            Check.Fails(t.Game.PickTopic(t.Tokens[1], "No such topic"), "unknown topic");
            Check.Ok(t.Game.PickTopic(t.Tokens[0], "topic 01"));
            Check.Ok(t.Game.PickTopic(t.Tokens[0], "Topic 20"));
            Check.Ok(t.Game.PickTopic(t.Tokens[1], "Topic 01"));
            Check.Fails(t.Game.Build(t.Tokens[0]), "players can't build");
            Check.Ok(t.Game.Build(t.Emcee));
            Check.Fails(t.Game.Build(t.Emcee), "build twice");
            Check.Fails(t.Game.JoinPlayer("Late"), "joining after build");
            Check.Fails(t.Game.PickTopic(t.Tokens[0], "Topic 02"), "changing topic after build");
            _ = emcee;
        });

        run("lobby: build waits for everyone's topic, and the emcee can assign random ones", () =>
        {
            var t = new FloorTable(5, build: false);
            var late = t.Game.JoinPlayer("Zed");
            Check.Fails(t.Game.Build(t.Emcee), "Zed hasn't picked");
            Check.Fails(t.Game.AssignRandomTopic(late.Token, t.S.Players.Last().Id), "a player can't assign");
            Check.Ok(t.Game.AssignRandomTopic(t.Emcee, t.S.Players.Last().Id));
            Check.True(t.S.Players.Last().Topics.Count == 1, "Zed has a topic");
            Check.Ok(t.Game.Build(t.Emcee));
            Check.Equal(6, t.S.Squares.Count, "one square per player");
        });

        run("lobby: remove a player, capacity is 20", () =>
        {
            var t = new FloorTable(4, build: false);
            Check.Ok(t.Game.RemovePlayer(t.Emcee, t.Ids[3]));
            Check.Fails(t.Game.Build(t.Emcee), "only 3 left");
            var full = new FloorTable(0, build: false);
            for (var i = 0; i < FloorGame.MaxPlayers; i++) Check.Ok(full.Game.JoinPlayer("P" + i));
            Check.Fails(full.Game.JoinPlayer("P-too-many"), "the 21st player");
        });

        run("topics used in one game are dead in the next until the emcee clears them", () =>
        {
            var t = new FloorTable(4);
            Check.Ok(t.Game.BackToLobby(t.Emcee));
            Check.True(t.S.Players.All(p => p.Topics.Count == 0), "everyone must pick again");
            Check.Fails(t.Game.PickTopic(t.Tokens[0], "Topic 01"), "Topic 01 was used last game");
            Check.Ok(t.Game.PickTopic(t.Tokens[0], "Topic 05"));
            Check.Equal(4, t.S.DeadTopicCount, "four dead topics");
            Check.Ok(t.Game.ClearUsedTopics(t.Emcee));
            Check.Ok(t.Game.PickTopic(t.Tokens[1], "Topic 01"));
            Check.Fails(t.Game.ClearUsedTopics(t.Tokens[0]), "a player can't clear");
        });

        run("build: a topic that is too small is refused with its name", () =>
        {
            var small = new FixedFloorContent(new Dictionary<string, int> { ["Big A"] = 50, ["Big B"] = 50, ["Big C"] = 50, ["Tiny"] = 3 });
            var t = new FloorTable(4, build: false, content: small, pick: false);
            var r = t.Game.PickTopic(t.Tokens[0], "Big A");
            Check.Ok(r); Check.Ok(t.Game.PickTopic(t.Tokens[1], "Big B")); Check.Ok(t.Game.PickTopic(t.Tokens[2], "Big C"));
            Check.Ok(t.Game.PickTopic(t.Tokens[3], "Tiny"));
            Check.Fails(t.Game.Build(t.Emcee), "Tiny has only 3");
        });

        // ---- the floor ----
        run("build: squares are dealt one per player, the first challenger can only pick neighbors", () =>
        {
            for (var n = 4; n <= 20; n++)
            {
                var t = new FloorTable(n, seed: n);
                var s = t.S;
                Check.Equal(FloorPhase.Choose, s.Phase, "choosing");
                Check.Equal(n, s.Squares.Count, "squares");
                Check.True(s.Players.All(p => p.Squares == 1), "one square each");
                Check.True(s.Candidates.Count >= 1 && s.Candidates.Count <= 4, "1-4 neighbors");
                Check.True(!s.Candidates.Contains(s.ChallengerId!.Value), "can't challenge yourself");
                var far = s.Players.First(p => p.Id != s.ChallengerId && !s.Candidates.Contains(p.Id));
                if (n > 5) Check.Fails(t.Game.Challenge(t.Emcee, far.Id), "challenging a non-neighbor");
                Check.Fails(t.Game.Challenge(t.Tokens.First(tok => t.Game.GetSeat(tok).PlayerId != s.ChallengerId), s.Candidates[0]), "someone else's turn");
            }
        });

        run("the first challenger really is random", () =>
        {
            var firsts = new HashSet<Guid>();
            var spots = new HashSet<int>();
            for (var seed = 0; seed < 60; seed++)
            {
                var t = new FloorTable(6, seed: seed);
                var s = t.S;
                firsts.Add(s.ChallengerId!.Value);
                spots.Add(s.Squares.Single(q => q.OwnerId == s.ChallengerId).Index);
            }
            Check.True(spots.Count >= 4, $"challenger starts from several different squares ({spots.Count})");
        });

        // ---- duels ----
        run("duel: challenger starts, clocks run one at a time, correct hands over the clock", () =>
        {
            var t = new FloorTable(5);
            t.StartDuel();
            var d = t.S.Duel!;
            Check.Equal(d.ChallengerId, d.ActiveId, "challenger starts");
            Check.Equal(45000, d.ChallengerMs, "full clock");
            Check.Equal(45000, d.DefenderMs, "full clock");
            Check.True(d.Running && !d.Paused, "running");

            t.Now = t.Now.AddMilliseconds(10_000);
            d = t.S.Duel!;
            Check.Equal(35000, d.ChallengerMs, "challenger spent 10s");
            Check.Equal(45000, d.DefenderMs, "defender hasn't started");

            var firstAnswer = d.Answer;
            Check.Ok(t.Game.Correct(t.Emcee));
            d = t.S.Duel!;
            Check.Equal(d.DefenderId, d.ActiveId, "the clock passes");
            Check.Equal(1, d.ChallengerCorrect, "scored");
            Check.True(d.Answer != firstAnswer, "a new picture");
            t.Now = t.Now.AddMilliseconds(5_000);
            d = t.S.Duel!;
            Check.Equal(35000, d.ChallengerMs, "challenger's clock is stopped");
            Check.Equal(40000, d.DefenderMs, "defender's clock is running");
        });

        run("duel: only the emcee judges; players and spectators can't", () =>
        {
            var t = new FloorTable(5);
            t.StartDuel();
            Check.Fails(t.Game.Correct(t.Tokens[0]), "a player judging");
            Check.Fails(t.Game.Pass(Guid.NewGuid()), "a stranger judging");
            Check.Fails(t.Game.StartDuel(t.Emcee), "starting twice");
        });

        run("duel: the prompts come from the defender's topic only, and never repeat", () =>
        {
            var t = new FloorTable(6, seed: 4);
            var defenderId = t.S.Candidates[0];
            Check.Ok(t.Game.Challenge(t.Emcee, defenderId));
            var defenderTopic = t.S.Players.Single(p => p.Id == defenderId).HomeTopic!;
            Check.Ok(t.Game.StartDuel(t.Emcee));
            var seen = new HashSet<string>();
            for (var i = 0; i < 40; i++)
            {
                var d = t.S.Duel!;
                Check.Equal(defenderTopic, d.Topic, "defender's topic");
                Check.True(seen.Add(d.Answer), "no repeat: " + d.Answer);
                Check.Ok(t.Game.Correct(t.Emcee));
            }
        });

        run("pass: the answer shows for 3 seconds while the clock keeps running, then the same player continues", () =>
        {
            var t = new FloorTable(5);
            t.StartDuel();
            var before = t.S.Duel!;
            Check.Ok(t.Game.Pass(t.Emcee));
            var d = t.S.Duel!;
            Check.True(d.RevealAnswer && d.RevealMs == 3000, "answer showing for 3s");
            Check.Equal(before.Answer, d.Answer, "the passed picture's answer");
            Check.Fails(t.Game.Correct(t.Emcee), "no judging during the reveal");
            Check.Fails(t.Game.Pass(t.Emcee), "no second pass during the reveal");
            Check.Fails(t.Game.Pause(t.Emcee), "no pause during the reveal");
            t.Advance(2000);
            Check.True(t.S.Duel!.RevealAnswer, "still showing");
            t.Advance(1000);
            d = t.S.Duel!;
            Check.True(!d.RevealAnswer, "reveal over");
            Check.True(d.Answer != before.Answer, "a new picture");
            Check.Equal(before.ActiveId, d.ActiveId, "same player is still on the clock");
            Check.Equal(42000, d.ChallengerMs, "the pass cost 3 seconds of clock");
            Check.Equal(0, d.ChallengerCorrect, "a pass doesn't score");
        });

        run("clock: time up turns the screen red, names the loser, and nothing more can be judged", () =>
        {
            var t = new FloorTable(5);
            t.StartDuel();
            var changes = 0;
            t.Game.Changed += () => changes++;
            t.Advance(44_000);
            Check.Equal(FloorPhase.Duel, t.S.Phase, "1s left");
            t.Advance(1_001);
            var s = t.S;
            Check.Equal(FloorPhase.TimeUp, s.Phase, "time up");
            Check.Equal(FloorEndReason.Clock, s.EndReason!.Value, "clock reason");
            Check.Equal(s.Duel!.ChallengerId, s.LoserId!.Value, "the player on the clock loses");
            Check.Equal(0, s.Duel.ChallengerMs, "clock at zero");
            Check.True(changes >= 1, "screens were told");
            Check.Fails(t.Game.Correct(t.Emcee), "judging after time up");
            Check.Fails(t.Game.Pass(t.Emcee), "passing after time up");
        });

        run("clock: noticing expiry during a refused action still tells the screens", () =>
        {
            var t = new FloorTable(5);
            t.StartDuel();
            var changes = 0;
            t.Game.Changed += () => changes++;
            t.Now = t.Now.AddSeconds(60); // no Tick: the timer hasn't fired yet
            Check.Fails(t.Game.Correct(t.Emcee), "too late");
            Check.Equal(FloorPhase.TimeUp, t.S.Phase, "time up");
            Check.True(changes == 1, "one redraw for the settle");
        });

        run("clock: a clock that runs out during a pass reveal still ends the duel", () =>
        {
            var t = new FloorTable(5);
            t.StartDuel();
            t.Advance(43_000);
            Check.Ok(t.Game.Pass(t.Emcee));
            t.Advance(2_500);
            Check.Equal(FloorPhase.TimeUp, t.S.Phase, "2s left minus a 3s reveal");
            Check.Equal(0, t.S.Duel!.ChallengerMs, "zero");
        });

        run("pause freezes the clock and resume restarts it", () =>
        {
            var t = new FloorTable(5);
            t.StartDuel();
            t.Advance(5_000);
            Check.Ok(t.Game.Pause(t.Emcee));
            Check.Fails(t.Game.Pause(t.Emcee), "pause twice");
            Check.Fails(t.Game.Correct(t.Emcee), "judging while paused");
            t.Advance(600_000);
            Check.Equal(FloorPhase.Duel, t.S.Phase, "ten minutes paused is fine");
            Check.Equal(40000, t.S.Duel!.ChallengerMs, "still 40s");
            Check.True(t.S.Duel!.Paused && !t.S.Duel.Running, "shown as paused");
            Check.Ok(t.Game.Resume(t.Emcee));
            Check.Fails(t.Game.Resume(t.Emcee), "resume twice");
            t.Advance(10_000);
            Check.Equal(30000, t.S.Duel!.ChallengerMs, "running again");
        });

        run("undo: takes back a Correct (and the time) or a Pass", () =>
        {
            var t = new FloorTable(5);
            t.StartDuel();
            Check.Fails(t.Game.UndoLast(t.Emcee), "nothing yet");
            var first = t.S.Duel!;
            t.Advance(4_000);
            Check.Ok(t.Game.Correct(t.Emcee));
            t.Advance(6_000);
            Check.Ok(t.Game.UndoLast(t.Emcee));
            var d = t.S.Duel!;
            Check.Equal(first.ActiveId, d.ActiveId, "back to the first player");
            Check.Equal(first.Answer, d.Answer, "back to the same picture");
            Check.Equal(0, d.ChallengerCorrect, "score restored");
            Check.Equal(41000, d.ChallengerMs, "the clock is back to when Correct was tapped");
            Check.Equal(45000, d.DefenderMs, "the other clock too");
            Check.Fails(t.Game.UndoLast(t.Emcee), "only one level");
            Check.Ok(t.Game.Correct(t.Emcee));
            Check.True(t.S.Duel!.Answer != first.Answer, "moves on again");

            Check.Ok(t.Game.Pass(t.Emcee));
            Check.Ok(t.Game.UndoLast(t.Emcee));
            Check.True(!t.S.Duel!.RevealAnswer, "reveal cancelled");
            Check.True(t.S.Duel.CanUndo == false, "used up");
        });

        run("undo never loses a picture: the one that was skipped over comes back", () =>
        {
            var t = new FloorTable(5, seed: 3);
            t.StartDuel();
            var seen = new List<string> { t.S.Duel!.Answer };
            Check.Ok(t.Game.Correct(t.Emcee));
            seen.Add(t.S.Duel!.Answer);
            Check.Ok(t.Game.UndoLast(t.Emcee));
            Check.Ok(t.Game.Correct(t.Emcee));
            Check.Equal(seen[1], t.S.Duel!.Answer, "the picture after undo is the one we were about to see");
        });

        // ---- takeovers ----
        run("takeover: winner gets every square and, with inheritance on, every topic", () =>
        {
            var t = new FloorTable(5, seed: 2);
            t.StartDuel();
            var d = t.S.Duel!;
            var challenger = t.S.Players.Single(p => p.Id == d.ChallengerId);
            var defender = t.S.Players.Single(p => p.Id == d.DefenderId);
            t.ChallengerWinsDuel();
            var s = t.S;
            Check.Equal(FloorPhase.TimeUp, s.Phase, "time up");
            Check.Equal(defender.Id, s.LoserId!.Value, "defender lost");
            Check.True(s.Squares.Count(q => q.OwnerId == defender.Id) == 1, "squares not moved until Continue");
            Check.Ok(t.Game.Continue(t.Emcee));
            s = t.S;
            Check.Equal(FloorPhase.Takeover, s.Phase, "takeover");
            Check.Equal(challenger.Id, s.Takeover!.WinnerId, "winner");
            Check.Equal(defender.Id, s.Takeover.LoserId, "loser");
            Check.Equal(1, s.Takeover.Cells.Count, "one square changes hands");
            var w = s.Players.Single(p => p.Id == challenger.Id);
            Check.Equal(2, w.Squares, "winner has two squares");
            Check.Equal(2, w.Topics.Count, "winner has both topics");
            Check.Equal(challenger.HomeTopic!, w.Topics[0], "own topic stays first");
            Check.Equal(defender.HomeTopic!, w.Topics[1], "won topic follows");
            Check.True(!s.Players.Single(p => p.Id == defender.Id).Alive, "loser is out");
            Check.Equal(4, s.Alive, "four left");

            Check.Ok(t.Game.Continue(t.Emcee));
            s = t.S;
            Check.Equal(FloorPhase.Choose, s.Phase, "back to choosing");
            Check.Equal(challenger.Id, s.ChallengerId!.Value, "the winner challenges next");
            Check.True(s.Candidates.All(id => s.Player(id)!.Alive), "only the living can be challenged");
        });

        run("takeover: the defender can win, and then challenges next", () =>
        {
            var t = new FloorTable(5, seed: 2);
            t.StartDuel();
            var d = t.S.Duel!;
            t.ChallengerLosesDuel();
            Check.Equal(d.ChallengerId, t.S.LoserId!.Value, "challenger lost");
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(d.DefenderId, t.S.Takeover!.WinnerId, "defender wins");
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(d.DefenderId, t.S.ChallengerId!.Value, "defender now challenges");
        });

        run("inheritance off: the winner takes the territory but not the topics", () =>
        {
            var t = new FloorTable(5, seed: 2);
            Check.Ok(t.Game.SetInherit(t.Emcee, false));
            Check.Fails(t.Game.SetInherit(t.Tokens[0], true), "a player can't change it");
            t.StartDuel();
            var winnerId = t.S.Duel!.ChallengerId;
            t.ChallengerWinsDuel();
            Check.Fails(t.Game.SetInherit(t.Emcee, true), "not mid-ending");
            Check.Ok(t.Game.Continue(t.Emcee));
            var w = t.S.Players.Single(p => p.Id == winnerId);
            Check.Equal(2, w.Squares, "territory taken");
            Check.Equal(1, w.Topics.Count, "no topics inherited");
            Check.Equal(0, t.S.Takeover!.TopicsGained.Count, "nothing gained");
        });

        run("inherited topics mix into the questions, home topic a little more often", () =>
        {
            // Give one defender three topics by chaining wins, then count where the pictures come from.
            var t = new FloorTable(5, seed: 11, promptsEach: 400);
            t.StartDuel();
            var champ = t.S.Duel!.ChallengerId;
            t.ChallengerWinsDuel();
            Check.Ok(t.Game.Continue(t.Emcee)); Check.Ok(t.Game.Continue(t.Emcee));
            t.StartDuel(); t.ChallengerWinsDuel();
            Check.Ok(t.Game.Continue(t.Emcee)); Check.Ok(t.Game.Continue(t.Emcee));
            var me = t.S.Players.Single(p => p.Id == champ);
            Check.Equal(3, me.Topics.Count, "three topics");

            // Make the champion the defender of a duel in the final stretch: win once more, then the last two play with the bigger as defender.
            t.StartDuel(); t.ChallengerWinsDuel();
            Check.Ok(t.Game.Continue(t.Emcee)); Check.Ok(t.Game.Continue(t.Emcee));
            Check.True(t.S.FinalDuel, "two left");
            Check.Equal(champ, t.S.DefenderId!.Value, "the bigger player defends the final duel");
            Check.Ok(t.Game.StartDuel(t.Emcee));
            var counts = new Dictionary<string, int>();
            for (var i = 0; i < 300; i++)
            {
                var topic = t.S.Duel!.Topic;
                counts[topic] = counts.GetValueOrDefault(topic) + 1;
                t.Now = t.Now.AddMilliseconds(5);
                if (t.Game.Correct(t.Emcee).Ok == false) break;
                if (t.S.Phase != FloorPhase.Duel) break;
            }
            var champ4 = t.S.Players.Single(p => p.Id == champ);
            Check.True(counts.Count >= 3, $"all topics show up ({counts.Count})");
            Check.True(counts[champ4.Topics[0]] > counts[champ4.Topics[1]], $"home topic leads: {string.Join(", ", counts.Select(kv => kv.Key + "=" + kv.Value))}");
        });

        run("final duel: with two left there is no choosing, and the bigger floor defends", () =>
        {
            var t = new FloorTable(4, seed: 5);
            for (var round = 0; round < 2; round++)
            {
                t.StartDuel();
                t.ChallengerWinsDuel();
                Check.Ok(t.Game.Continue(t.Emcee));
                Check.Ok(t.Game.Continue(t.Emcee));
            }
            var s = t.S;
            Check.Equal(2, s.Alive, "two left");
            Check.Equal(FloorPhase.Ready, s.Phase, "straight to the duel");
            Check.True(s.FinalDuel, "marked as final");
            Check.Fails(t.Game.ChangeOpponent(t.Emcee), "no changing the final duel");
            var defenderSquares = s.Player(s.DefenderId)!.Squares;
            var challengerSquares = s.Player(s.ChallengerId)!.Squares;
            Check.True(defenderSquares >= challengerSquares, "the bigger floor defends");
            Check.Ok(t.Game.StartDuel(t.Emcee));
            t.ChallengerWinsDuel();
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Ok(t.Game.Continue(t.Emcee));
            s = t.S;
            Check.Equal(FloorPhase.GameOver, s.Phase, "game over");
            Check.Equal(4, s.Player(s.WinnerId)!.Squares, "the winner owns everything");
        });

        run("change opponent: back out before the duel and pick someone else", () =>
        {
            var t = new FloorTable(9, seed: 6);
            var s = t.S;
            Check.True(s.Candidates.Count >= 2, "at least two neighbors in a 3x3 block");
            Check.Ok(t.Game.Challenge(t.Emcee, s.Candidates[0]));
            Check.Equal(FloorPhase.Ready, t.S.Phase, "ready");
            Check.Fails(t.Game.Challenge(t.Emcee, s.Candidates[1]), "already challenged someone");
            Check.Ok(t.Game.ChangeOpponent(s.ChallengerId == null ? t.Emcee : t.Tokens[t.Ids.IndexOf(s.ChallengerId.Value)]));
            Check.Equal(FloorPhase.Choose, t.S.Phase, "choosing again");
            Check.Ok(t.Game.Challenge(t.Emcee, s.Candidates[1]));
        });

        // ---- running out ----
        run("a topic that runs dry mid-duel: whoever has more time wins", () =>
        {
            var small = new FixedFloorContent(30, 10);
            var t = new FloorTable(5, content: small);
            t.StartDuel();
            var d = t.S.Duel!;
            // Challenger answers fast, defender slowly, so the defender has less time left when the pictures run out.
            for (var i = 0; i < 9; i++)
            {
                t.Now = t.Now.AddMilliseconds(i % 2 == 0 ? 1000 : 3000);
                Check.Ok(t.Game.Correct(t.Emcee));
            }
            Check.Equal(FloorPhase.Duel, t.S.Phase, "one picture left");
            t.Now = t.Now.AddMilliseconds(1000);
            Check.Ok(t.Game.Correct(t.Emcee));
            var s = t.S;
            Check.Equal(FloorPhase.TimeUp, s.Phase, "no pictures left");
            Check.Equal(FloorEndReason.OutOfPrompts, s.EndReason!.Value, "reason");
            Check.Equal(d.DefenderId, s.LoserId!.Value, "the defender spent more time, so loses");
        });

        // ---- whole games ----
        run("fuzz: random whole games always reach a winner and keep the floor consistent", () =>
        {
            var repicks = 0;
            for (var seed = 1; seed <= 120; seed++)
            {
                var players = 4 + seed % 17;
                var t = new FloorTable(players, topics: 60, promptsEach: 12 + seed % 30, seed: seed);
                var rng = new Random(seed);
                if (seed % 3 == 0) Check.Ok(t.Game.SetInherit(t.Emcee, false));
                var displayed = new HashSet<string>();
                var lastShown = "";
                var steps = 0;

                while (t.S.Phase != FloorPhase.GameOver && steps++ < 20_000)
                {
                    var s = t.S;
                    CheckConsistent(t, s);
                    switch (s.Phase)
                    {
                        case FloorPhase.Choose:
                            Check.Ok(t.Game.Challenge(rng.Next(2) == 0 ? t.Emcee : t.Tokens[t.Ids.IndexOf(s.ChallengerId!.Value)], s.Candidates[rng.Next(s.Candidates.Count)]));
                            break;
                        case FloorPhase.Repick:
                            repicks++;
                            var needy = s.RepickPlayerId!.Value;
                            var free = s.Topics.Where(x => x.Available && x.Count >= FloorGame.MinLivePrompts).ToList();
                            if (free.Count == 0) { Check.Ok(t.Game.ClearUsedTopics(t.Emcee)); free = t.S.Topics.Where(x => x.Available).ToList(); }
                            if (rng.Next(2) == 0) Check.Ok(t.Game.PickTopic(t.Tokens[t.Ids.IndexOf(needy)], free[rng.Next(free.Count)].Name));
                            else Check.Ok(t.Game.AssignRandomTopic(t.Emcee, needy));
                            break;
                        case FloorPhase.Ready:
                            if (!s.FinalDuel && rng.Next(8) == 0) { Check.Ok(t.Game.ChangeOpponent(t.Emcee)); break; }
                            Check.Ok(t.Game.StartDuel(t.Emcee));
                            break;
                        case FloorPhase.Duel:
                            var d = s.Duel!;
                            var shown = d.Topic + "/" + d.Answer;
                            if (shown != lastShown)
                            {
                                Check.True(displayed.Add(shown), "a picture was shown twice: " + d.Answer);
                                lastShown = shown;
                            }
                            t.Now = t.Now.AddMilliseconds(rng.Next(200, 4000));
                            var roll = rng.Next(10);
                            if (roll < 6) t.Game.Correct(t.Emcee);
                            else if (roll < 8) t.Game.Pass(t.Emcee);
                            else if (roll == 8) { t.Game.Pause(t.Emcee); t.Now = t.Now.AddSeconds(30); t.Game.Resume(t.Emcee); }
                            else t.Game.Tick();
                            break;
                        case FloorPhase.TimeUp:
                        case FloorPhase.Takeover:
                            Check.Ok(t.Game.Continue(t.Emcee));
                            break;
                    }
                }

                var end = t.S;
                Check.Equal(FloorPhase.GameOver, end.Phase, $"seed {seed} ({players} players) finished in {steps} steps");
                Check.Equal(1, end.Alive, "one player left");
                Check.Equal(players, end.Player(end.WinnerId)!.Squares, "the winner owns every square");
            }
            Check.True(repicks > 0, "at least one game needed a re-pick, so that path was exercised");
        });

        run("fuzz: undo mixed in never breaks a duel", () =>
        {
            for (var seed = 1; seed <= 60; seed++)
            {
                var t = new FloorTable(5 + seed % 6, topics: 60, promptsEach: 40, seed: seed);
                var rng = new Random(seed * 7);
                var steps = 0;
                while (t.S.Phase != FloorPhase.GameOver && steps++ < 20_000)
                {
                    var s = t.S;
                    CheckConsistent(t, s);
                    switch (s.Phase)
                    {
                        case FloorPhase.Choose: Check.Ok(t.Game.Challenge(t.Emcee, s.Candidates[rng.Next(s.Candidates.Count)])); break;
                        case FloorPhase.Repick: Check.Ok(t.Game.AssignRandomTopic(t.Emcee, s.RepickPlayerId!.Value)); break;
                        case FloorPhase.Ready: Check.Ok(t.Game.StartDuel(t.Emcee)); break;
                        case FloorPhase.Duel:
                            t.Now = t.Now.AddMilliseconds(rng.Next(100, 3000));
                            switch (rng.Next(6))
                            {
                                case 0: t.Game.UndoLast(t.Emcee); break;
                                case 1: t.Game.Pass(t.Emcee); break;
                                default: t.Game.Correct(t.Emcee); break;
                            }
                            break;
                        default: Check.Ok(t.Game.Continue(t.Emcee)); break;
                    }
                }
                Check.Equal(FloorPhase.GameOver, t.S.Phase, $"seed {seed} finished");
            }
        });

        run("rooms: four-letter codes with no vowels, found by code in any case", () =>
        {
            var rooms = new FloorRoomManager(new FixedFloorContent());
            for (var i = 0; i < 50; i++)
            {
                var game = rooms.Create();
                Check.Equal(4, game.Code.Length, "length");
                Check.True(game.Code.All(c => !"AEIOU".Contains(c)), "no vowels");
                Check.True(rooms.Find(game.Code.ToLowerInvariant()) == game, "found in lowercase");
            }
            Check.True(rooms.Find("NOPE") is null, "unknown code");
            Check.True(rooms.Find(null) is null, "no code");
        });
    }

    /// <summary>Facts that must hold at every moment of every game.</summary>
    private static void CheckConsistent(FloorTable t, FloorSnapshot s)
    {
        var cells = FloorShape.For(s.Players.Count).Cells.Count;
        Check.Equal(cells, s.Squares.Count, "every square has an owner");
        Check.Equal(cells, s.Players.Sum(p => p.Squares), "squares add up");
        foreach (var p in s.Players)
        {
            Check.True(p.Alive == (p.Squares > 0), p.Name + ": alive exactly when holding squares");
            Check.True(p.Topics.Distinct(StringComparer.OrdinalIgnoreCase).Count() == p.Topics.Count, p.Name + ": no topic twice");
        }
        Check.True(s.Squares.All(q => s.Player(q.OwnerId)!.Alive || s.Phase is FloorPhase.TimeUp), "no square belongs to the dead");
        var all = s.Players.Where(p => p.Alive).SelectMany(p => p.Topics).ToList();
        Check.True(all.Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Count, "no topic held by two living players");

        if (s.Duel is { } d)
        {
            Check.True(d.ActiveId == d.ChallengerId || d.ActiveId == d.DefenderId, "the clock belongs to a duelist");
            Check.True(d.ChallengerMs >= 0 && d.ChallengerMs <= s.DuelSeconds * 1000, "challenger clock in range");
            Check.True(d.DefenderMs >= 0 && d.DefenderMs <= s.DuelSeconds * 1000, "defender clock in range");
            if (s.Phase == FloorPhase.Duel) Check.True(d.Answer.Length > 0, "a prompt is showing");
        }
        if (s.Phase == FloorPhase.Choose)
            Check.True(s.Candidates.Count > 0 && s.Candidates.All(id => id != s.ChallengerId), "someone to challenge");
    }
}
