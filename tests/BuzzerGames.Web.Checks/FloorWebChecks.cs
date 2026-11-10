using System.Text.RegularExpressions;
using BuzzerGames.Core.Floor;
using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Web;
using Microsoft.Extensions.DependencyInjection;

/// <summary>The Floor's screens: the TV must never show an answer (except just after a pass), and every phase must draw.</summary>
static class FloorWebChecks
{
    private static FloorTable TextTable(int players = 5, int seed = 1) => new(players, seed: seed);

    private static FloorTable ImageTable(int players = 5, int seed = 1) =>
        new(players, seed: seed, content: new FixedFloorContent(30, 120, images: true));

    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine("The Floor screens");

        run("floor lobby: join choices, QR on the TV only, emcee build button waits for topics", () =>
        {
            var game = new FloorGame("TEST", new FixedFloorContent(), new Random(1));
            var guest = Render.FlLobby(game, new Seat(Role.Spectator));
            Check.True(Render.Has(guest, "Join as a player") && Render.Shows(guest, "I'm the emcee"), "a new phone can join either way");
            var tv = Render.FlLobby(game, new Seat(Role.Spectator), boardMode: true);
            Check.True(Render.Has(tv, "Scan to join"), "the TV shows the QR");
            Check.True(!Render.Shows(tv, "I'm the emcee"), "the TV never takes a seat");

            var emcee = game.JoinEmcee("Host").Token;
            var t = new FloorTable(0, build: false);
            var host = Render.FlLobby(t.Game, new Seat(Role.Spectator));
            Check.True(Render.Has(host, "Join as a player"), "still offers a seat to players");

            var table = new FloorTable(5, build: false);
            var emceePhone = Render.FlLobby(table.Game, new Seat(Role.Emcee), table.Emcee);
            Check.True(Render.Has(emceePhone, "Build the floor") && Render.Has(emceePhone, "Winner inherits topics"), "emcee controls");
            Check.True(!Render.ButtonDisabled(emceePhone, "Build the floor"), "all five picked, so Build is enabled");
            var short4 = new FloorTable(3, build: false);
            Check.True(Render.ButtonDisabled(Render.FlLobby(short4.Game, new Seat(Role.Emcee), short4.Emcee), "Build the floor"), "three players is too few");
            _ = emcee;
        });

        run("floor lobby: a player sees the topics to pick and which are taken", () =>
        {
            var t = new FloorTable(4, build: false, pick: false);
            Check.Ok(t.Game.PickTopic(t.Tokens[0], "Topic 03"));
            var phone = Render.FlLobby(t.Game, new Seat(Role.Player, t.Ids[1]), t.Tokens[1]);
            Check.True(Render.Has(phone, "Pick your expertise") && Render.Has(phone, "Topic 07"), "the picker is there");
            Check.True(Render.Has(phone, "Ann has it"), "Topic 03 is marked as taken");
            Check.True(Render.Has(Render.FlLobby(t.Game, new Seat(Role.Player, t.Ids[0]), t.Tokens[0]), "your pick"), "your own pick is marked");
        });

        run("floor wiring: the real service setup builds the game and the starter topics are pickable", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "floor-web-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("FLOOR_DATA_DIR", dir);
            Environment.SetEnvironmentVariable("FLOOR_ADMIN_PASSCODE", "secret");
            try
            {
                var services = new ServiceCollection()
                    .AddLogging()
                    .AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new Env())
                    .AddFloor().BuildServiceProvider();
                var game = services.GetRequiredService<FloorRoomManager>().Create();
                Check.True(game.Snapshot().Phase == FloorPhase.Lobby, "a new room is in the lobby");
                var store = services.GetRequiredService<FolderFloorContent>();
                Check.Equal(Path.GetFullPath(dir), store.DataDirectory, "the data folder comes from FLOOR_DATA_DIR");
                Check.True(ReferenceEquals(store, services.GetRequiredService<IFloorContent>()), "one store for the game and the topics page");
                var access = services.GetRequiredService<FloorAdminAccess>();
                Check.True(access.IsAllowed("secret") && !access.IsAllowed("wrong") && !access.IsAllowed(""), "passcode is enforced");
            }
            finally
            {
                Environment.SetEnvironmentVariable("FLOOR_DATA_DIR", null);
                Environment.SetEnvironmentVariable("FLOOR_ADMIN_PASSCODE", null);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        });

        run("floor admin access: no passcode means open on a dev machine and locked everywhere else", () =>
        {
            Check.True(new FloorAdminAccess(null, true).IsAllowed(null), "open in development");
            Check.True(!new FloorAdminAccess(null, false).IsAllowed("anything"), "locked in production without a passcode");
            Check.True(new FloorAdminAccess(null, false).Problem is not null, "and it says why");
            Check.True(!new FloorAdminAccess("pw", true).IsAllowed(null), "a set passcode applies even in development");
        });

        run("topics page: locked without the passcode, and it never lists topics to a stranger", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "floor-web-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FolderFloorContent(dir, 20);
                var gate = Render.FlTopics(store, new FloorAdminAccess("secret", false));
                Check.True(Render.Has(gate, "Passcode"), "asks for the passcode");
                Check.True(!Render.Has(gate, "Acronyms"), "no topics before unlocking");
                var locked = Render.FlTopics(store, new FloorAdminAccess(null, false));
                Check.True(Render.Has(locked, "FLOOR_ADMIN_PASSCODE"), "tells you how to unlock it");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        });

        run("floor TV: the map shows every player's name and ALL their topics", () =>
        {
            var t = TextTable(6);
            var map = Render.FlMap(t.S);
            foreach (var p in t.S.Players)
            {
                Check.True(Render.Has(map, p.Name), p.Name + " is on the map");
                Check.True(Render.Has(map, p.HomeTopic!), p.HomeTopic + " is on the map");
            }
            Check.Equal(6, Regex.Matches(map, "fl-square ").Count, "one square each");

            // After a win the winner's territory shows both topics.
            t.StartDuel();
            var winner = t.S.Duel!.ChallengerId;
            t.ChallengerWinsDuel();
            Check.Ok(t.Game.Continue(t.Emcee));
            var after = t.S;
            var w = after.Player(winner)!;
            Check.Equal(2, w.Topics.Count, "two topics");
            var html = Render.FlTv(after);
            Check.True(Render.Has(html, w.Topics[0]) && Render.Has(html, w.Topics[1]), "both topics are written on the territory");
            Check.True(Render.Has(html, "takes") && Render.Has(html, "territory"), "the takeover is announced");
            Check.True(Regex.Matches(html, "fl-square [^\"]*taken").Count == after.Takeover!.Cells.Count, "the taken squares are animated");
        });

        run("floor TV: during a duel it shows the picture and clocks but never the answer", () =>
        {
            var t = ImageTable();
            t.StartDuel();
            var d = t.S.Duel!;
            var tv = Render.FlTv(t.S);
            Check.True(Regex.IsMatch(tv, "<img[^>]+src=\"/the-floor/i/[^\"]+\""), "the picture is on screen");
            Check.True(!Render.Shows(tv, d.Answer), "answer not on the TV");
            Check.True(!Regex.IsMatch(tv, "src=\"[^\"]*answer", RegexOptions.IgnoreCase), "no answer in a picture address");
            Check.True(Render.Has(tv, "data-fl-ms=\"45000\""), "clocks are on screen");
            Check.True(Regex.Matches(tv, "fl-preload").Count == 1 && Regex.Matches(tv, "<img").Count > 1, "upcoming pictures are preloaded");
            Check.True(Render.Has(Render.FlEmcee(t), d.Answer), "the emcee sees the answer");
        });

        run("floor TV: text prompts show the clue and hide the answer, until a pass shows it", () =>
        {
            var t = TextTable();
            t.StartDuel();
            var d = t.S.Duel!;
            var tv = Render.FlTv(t.S);
            Check.True(Render.Shows(tv, d.PromptText!), "the clue is shown");
            Check.True(!Render.Shows(tv, d.Answer), "the answer isn't");
            Check.Ok(t.Game.Pass(t.Emcee));
            Check.True(Render.Shows(Render.FlTv(t.S), d.Answer), "after a pass the answer is shown to the room");
            t.Advance(3000);
            Check.True(!Render.Shows(Render.FlTv(t.S), d.Answer), "and it goes away");
        });

        run("floor TV: time up goes red and names the loser", () =>
        {
            var t = ImageTable();
            t.StartDuel();
            var loser = t.S.Player(t.S.Duel!.ChallengerId)!;
            t.ChallengerLosesDuel();
            var tv = Render.FlTv(t.S);
            Check.True(Render.Has(tv, "phase-timeup") && Render.Shows(tv, "TIME'S UP"), "the red time-up screen");
            Check.True(Render.Has(tv, loser.Name + " ran out of time"), "names who ran out");
            Check.True(!Regex.IsMatch(tv, "<img[^>]+class=\"fl-pic\""), "no picture once time is up");
        });

        run("floor TV: paused duels say so, and the final duel is announced", () =>
        {
            var t = TextTable(4, seed: 5);
            for (var round = 0; round < 2; round++)
            {
                t.StartDuel(); t.ChallengerWinsDuel();
                Check.Ok(t.Game.Continue(t.Emcee)); Check.Ok(t.Game.Continue(t.Emcee));
            }
            Check.True(Render.Has(Render.FlTv(t.S), "FINAL DUEL"), "final duel on the TV before it starts");
            Check.Ok(t.Game.StartDuel(t.Emcee));
            Check.Ok(t.Game.Pause(t.Emcee));
            Check.True(Render.Has(Render.FlTv(t.S), "PAUSED"), "paused");
            Check.True(Render.Has(Render.FlEmcee(t), "Resume"), "the emcee can resume");
        });

        run("floor emcee phone: Correct and Pass, undo only when there is something to undo", () =>
        {
            var t = TextTable();
            t.StartDuel();
            var phone = Render.FlEmcee(t);
            Check.True(Render.Has(phone, "Correct") && Render.Has(phone, "Pass") && Render.Has(phone, "Pause"), "judging buttons");
            Check.True(Render.ButtonDisabled(phone, "Undo"), "nothing to undo yet");
            Check.Ok(t.Game.Correct(t.Emcee));
            Check.True(!Render.ButtonDisabled(Render.FlEmcee(t), "Undo"), "now there is");
            Check.Ok(t.Game.Pass(t.Emcee));
            Check.True(Render.ButtonDisabled(Render.FlEmcee(t), "Correct"), "no judging while the answer shows");
        });

        run("floor player phones: the challenger gets neighbors to pick, others just wait", () =>
        {
            var t = TextTable(6);
            var s = t.S;
            var challengerIdx = t.Ids.IndexOf(s.ChallengerId!.Value);
            var mine = Render.FlPlayer(t, challengerIdx);
            Check.True(Render.Has(mine, "Your move!"), "the challenger is told");
            foreach (var id in s.Candidates) Check.True(Render.Has(mine, s.Player(id)!.Name), "can pick " + s.Player(id)!.Name);
            var other = Enumerable.Range(0, 6).First(i => i != challengerIdx);
            var theirs = Render.FlPlayer(t, other);
            Check.True(!Render.Has(theirs, "Your move!"), "others aren't told to move");
            Check.True(Render.Has(theirs, "is choosing"), "they see who is choosing");
        });

        run("floor player phones: during a duel the player on the clock is told, and never sees a picture or answer", () =>
        {
            var t = ImageTable();
            t.StartDuel();
            var d = t.S.Duel!;
            var up = Render.FlPlayer(t, t.Ids.IndexOf(d.ActiveId));
            Check.True(Render.Shows(up, "You're up!"), "told it's their turn");
            Check.True(!Render.Shows(up, d.Answer) && !up.Contains("<img"), "no picture or answer on a player's phone");
            var waiting = Render.FlPlayer(t, t.Ids.IndexOf(d.ActiveId == d.ChallengerId ? d.DefenderId : d.ChallengerId));
            Check.True(Render.Has(waiting, "Wait for your turn"), "the other duelist waits");
        });

        run("floor room page: board address watches, phone address asks to join", () =>
        {
            var rooms = new FloorRoomManager(new FixedFloorContent());
            var game = rooms.Create();
            var board = Render.FlRoom(rooms, game.Code, board: true);
            Check.True(Render.Has(board, "Scan to join"), "the board shows the QR in the lobby");
            Check.True(Render.Has(Render.FlRoom(rooms, "NOPE", board: false), "Game not found"), "an unknown room says so");
            game.JoinEmcee("Host");
            Check.True(Render.Has(board, "THE FLOOR"), "the logo");
        });

        run("floor screens draw in every phase through fuzzed games, and the TV never leaks an answer", () =>
        {
            for (var seed = 1; seed <= 12; seed++)
            {
                var t = seed % 2 == 0 ? ImageTable(4 + seed % 9, seed) : TextTable(4 + seed % 9, seed);
                var rng = new Random(seed);
                var steps = 0;
                while (t.S.Phase != FloorPhase.GameOver && steps++ < 3000)
                {
                    var s = t.S;
                    var tv = Render.FlTv(s);
                    Render.FlEmcee(t);
                    for (var i = 0; i < t.Tokens.Count; i += 2) Render.FlPlayer(t, i);
                    if (s.Phase == FloorPhase.Duel && !s.Duel!.RevealAnswer)
                        Check.True(!Render.Shows(tv, s.Duel.Answer), $"the TV leaked '{s.Duel.Answer}' in seed {seed}");
                    switch (s.Phase)
                    {
                        case FloorPhase.Choose: Check.Ok(t.Game.Challenge(t.Emcee, s.Candidates[rng.Next(s.Candidates.Count)])); break;
                        case FloorPhase.Repick: Check.Ok(t.Game.AssignRandomTopic(t.Emcee, s.RepickPlayerId!.Value)); break;
                        case FloorPhase.Ready: Check.Ok(t.Game.StartDuel(t.Emcee)); break;
                        case FloorPhase.Duel:
                            t.Now = t.Now.AddMilliseconds(rng.Next(300, 3500));
                            if (rng.Next(5) == 0) t.Game.Pass(t.Emcee); else t.Game.Correct(t.Emcee);
                            break;
                        default: Check.Ok(t.Game.Continue(t.Emcee)); break;
                    }
                }
                var end = Render.FlTv(t.S);
                Check.True(Render.Has(end, "THE FLOOR IS YOURS"), "the winner screen");
            }
        });

        run("floor home page: The Floor is listed as playable", () =>
        {
            var tile = BuzzerGames.Web.Games.GameCatalog.All.Single(g => g.Name == "The Floor");
            Check.True(tile.IsPlayable && tile.Url == "/the-floor", "the tile points at /the-floor");
        });
    }

    private sealed class Env : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
