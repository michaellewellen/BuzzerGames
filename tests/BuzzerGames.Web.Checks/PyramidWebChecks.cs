using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Pyramid;
using BuzzerGames.Web;
using BuzzerGames.Web.Components.Pyramid;
using BuzzerGames.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

/// <summary>What each Pyramid screen may show. The TV must never show a hidden topic, an unrevealed word, the emcee's script, or an unseen bonus subject.</summary>
static class PyramidWebChecks
{
    private static (PyramidGame Game, Guid Emcee) New()
    {
        var game = new PyramidGame("TEST", new FixedPyramidContent(), new Random(4));
        var emcee = game.JoinEmcee().Token;
        return (game, emcee);
    }


    public static string Emcee(PyramidGame game, Guid token) =>
        Render.Html<PyramidEmceeView>(new() { ["Game"] = game, ["Snapshot"] = game.Snapshot(), ["Token"] = token });

    public static string Lobby(PyramidGame game, Seat seat, bool board) =>
        Render.Html<PyramidLobbyView>(new() { ["Snapshot"] = game.Snapshot(), ["Seat"] = seat, ["BoardMode"] = board });

    public static string Topics(FolderPyramidContent store, FloorAdminAccess access)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime, Render.NoJs>().AddSingleton(store).AddSingleton(access).BuildServiceProvider();
        using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<PyramidTopics>(ParameterView.Empty)).ToHtmlString()).GetAwaiter().GetResult();
    }

    /// <summary>Plays to the chosen point and returns the topic number (1 to 6) that was picked. mystery: true picks the Mystery 7, false picks an ordinary one.</summary>
    private static int PickKind(PyramidGame game, Guid emcee, bool mystery)
    {
        while (game.Phase == PyramidPhase.Reveal) game.RevealNext(emcee);
        for (var i = 0; i < PyramidRules.TileCount; i++)
        {
            game.PickTile(emcee, i);
            if (game.Snapshot().Play!.Mystery == mystery) return i + 1;
            game.StartRound(emcee);
            for (var w = 0; w < 7; w++) game.Mark(emcee, WordMark.Wrong);
            game.Continue(emcee);
        }
        throw new InvalidOperationException("no such topic");
    }

    private static string Word(int topic, int n) => $"T{topic}-{n}";

    public static string Tv(PyramidGame game) =>
        Render.Html<PyramidTvView>(new() { ["Game"] = game, ["Snapshot"] = game.Snapshot() });

    /// <summary>Set PYRAMID_DUMP_DIR and every screen these checks draw is also saved there as a page, so it can be looked at in a browser.</summary>
    public static void Dump(string name, string html)
    {
        var dir = Environment.GetEnvironmentVariable("PYRAMID_DUMP_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var css = File.ReadAllText(FindCss());
        File.WriteAllText(Path.Combine(dir, name + ".html"),
            $"<!doctype html><html><head><meta charset=\"utf-8\"><style>{css}</style></head><body>{html}</body></html>");
    }

    private static string FindCss()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "src", "BuzzerGames.Web", "wwwroot", "app.css"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? ".", "src", "BuzzerGames.Web", "wwwroot", "app.css");
    }

    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine("$100,000 Pyramid screens");

        run("pyramid tv: the lobby shows the title and nothing else", () =>
        {
            var (game, _) = New();
            var html = Tv(game);
            Dump("lobby", html);
            Check.True(Render.Has(html, "$100,000") && Render.Has(html, "PYRAMID"), "the title");
        });

        run("pyramid tv: face-down squares show no titles; turned-over squares show theirs", () =>
        {
            var (game, emcee) = New();
            game.Start(emcee);
            var html = Tv(game);
            Dump("reveal-0", html);
            Check.Equal(6, Render.Count(html, "pyr-cell"), "six squares");
            Check.True(!Render.Has(html, "Topic 1"), "no titles before the reveal");
            game.RevealNext(emcee);
            game.RevealNext(emcee);
            html = Tv(game);
            Dump("reveal-2", html);
            Check.True(Render.Has(html, "Topic 1") && Render.Has(html, "Topic 2") && !Render.Has(html, "Topic 3"), "just the two turned over");
            while (game.Phase == PyramidPhase.Reveal) game.RevealNext(emcee);
            Dump("choosing", Tv(game));
            game.PickTile(emcee, 1);
            Dump("picked", Tv(game));
            game.StartRound(emcee);
            Dump("playing", Tv(game));
            for (var i = 0; i < 7; i++) game.Mark(emcee, i < 5 ? WordMark.Correct : WordMark.Wrong);
            Dump("roundover", Tv(game));
        });

        run("pyramid tv: after a pick nothing but the pyramid shows: no title, description or words", () =>
        {
            var (game, emcee) = New();
            game.Start(emcee);
            while (game.Phase == PyramidPhase.Reveal) game.RevealNext(emcee);
            game.PickTile(emcee, 2);
            var topic = int.Parse(game.Snapshot().Play!.Label.Split(' ')[1]);
            var html = Tv(game);
            Check.True(!Render.Has(html, "Name these things") && !Render.Has(html, Word(topic, 1)) && !Render.Has(html, "pyr-clock"), "no script, word or clock");
            Check.Equal(1, Render.Count(html, "pyr-s-gone"), "the chosen square has flipped away");
        });

        run("pyramid tv: while the clock runs it shows the clock, the current word and the score, and no other word", () =>
        {
            var (game, emcee) = New();
            game.Start(emcee);
            var topic = PickKind(game, emcee, mystery: false);
            game.StartRound(emcee);
            game.Mark(emcee, WordMark.Correct);
            var html = Tv(game);
            Dump("playing-2", html);
            Check.True(Render.Has(html, "pyr-clock") && Render.Has(html, "data-clock-end"), "a clock");
            Check.True(Render.Has(html, Word(topic, 2)), "the second word is up");
            foreach (var n in new[] { 1, 3, 4, 5, 6, 7 }) Check.True(!Render.Has(html, Word(topic, n)), $"word {n} must not be on the TV");
            Check.True(!Render.Has(html, "Name these things"), "no script on the TV");
            Check.True(Render.Has(html, "pyr-total") && Render.Has(html, "<b>1</b>"), "the running score");
        });

        run("pyramid tv: after a round it lists the words; a mystery's secret only appears once the emcee reveals it", () =>
        {
            var (game, emcee) = New();
            game.Start(emcee);
            var topic = PickKind(game, emcee, mystery: false);
            game.StartRound(emcee);
            for (var i = 0; i < 7; i++) game.Mark(emcee, i < 4 ? WordMark.Correct : WordMark.Wrong);
            var html = Tv(game);
            Dump("roundover", html);
            for (var n = 1; n <= 7; n++) Check.True(Render.Has(html, Word(topic, n)), $"word {n} is listed");
            Check.True(!Render.Has(html, "Name these things"), "an ordinary topic's description never goes on the TV");

            var (g2, e2) = New();
            g2.Start(e2);
            var m = PickKind(g2, e2, mystery: true);
            Check.True(!Render.Has(Tv(g2), "Name these things"), "ready: nothing");
            Check.True(Render.Has(Tv(g2), "pyr-mystery"), "the pyramid flashes for a mystery");
            g2.StartRound(e2);
            Check.True(!Render.Has(Tv(g2), "Name these things"), "playing: nothing");
            for (var i = 0; i < 7; i++) g2.Mark(e2, WordMark.Correct);
            html = Tv(g2);
            Dump("mystery-won", html);
            Check.True(!Render.Has(html, "Name these things"), "round over, before the reveal: still hidden");
            Check.True(Render.Has(html, "SPECIAL PRIZE"), "but the prize is announced");
            g2.RevealDescription(e2);
            Check.True(Render.Has(Tv(g2), "Name these things from topic " + m), "revealed");
        });

        run("pyramid tv: the tiebreaker and game-won screens", () =>
        {
            var (game, emcee) = New();
            game.Start(emcee);
            while (game.Phase == PyramidPhase.Reveal) game.RevealNext(emcee);
            for (var round = 0; round < 3; round++)
                for (var turn = 0; turn < 2; turn++)
                {
                    game.PickTile(emcee, game.Snapshot().Tiles.First(t => t.State == PyramidTileState.Revealed).Index);
                    game.StartRound(emcee);
                    for (var w = 0; w < 7; w++) game.Mark(emcee, w < 4 ? WordMark.Correct : WordMark.Wrong);
                    game.Continue(emcee);
                }
            var tie = game.Snapshot().Tiebreak!;
            var html = Tv(game);
            Dump("tiebreak", html);
            Check.True(Render.Has(html, "TIEBREAKER") && Render.Has(html, PyramidRules.TeamNames[tie.Chooser] + " chooses"), "who chooses");
            Check.Equal(2, Render.Count(html, "class=\"pyr-letter\""), "two letters");
        });

        run("pyramid tv: the bonus never shows a subject before it has come up, and shows the dollars on valid squares", () =>
        {
            var (game, emcee) = New();
            game.Start(emcee);
            while (game.Phase == PyramidPhase.Reveal) game.RevealNext(emcee);
            for (var r = 0; r < 3; r++)
                for (var t = 0; t < 2; t++)
                {
                    game.PickTile(emcee, game.Snapshot().Tiles.First(x => x.State == PyramidTileState.Revealed).Index);
                    game.StartRound(emcee);
                    for (var w = 0; w < 7; w++) game.Mark(emcee, game.Snapshot().Up == 0 ? WordMark.Correct : WordMark.Wrong);
                    game.Continue(emcee);
                }
            Check.Equal(PyramidPhase.GameWon, game.Phase, "game won");
            game.Continue(emcee);
            var html = Tv(game);
            Dump("bonus-ready", html);
            Check.True(Render.Has(html, "$25,000") && Render.Has(html, "plays for"), "the prize");
            Check.True(!Render.Has(html, "Bonus subject"), "no subjects yet");
            game.StartBonus(emcee);
            game.MarkBonus(emcee, BonusMark.Valid);
            game.MarkBonus(emcee, BonusMark.Pass);
            html = Tv(game);
            Dump("bonus-play", html);
            Check.True(Render.Has(html, "pyr-subject") && Render.Has(html, "Bonus subject 3"), "the current subject");
            Check.True(!Render.Has(html, "Bonus subject 4") && !Render.Has(html, "Bonus subject 5") && !Render.Has(html, "Bonus subject 6"), "later subjects stay secret");
            Check.True(Render.Has(html, "$1,000"), "the first square turned to dollars");
            Check.True(Render.Has(html, "Bonus subject 2"), "the passed square is still showing");
            game.MarkBonus(emcee, BonusMark.Valid); game.MarkBonus(emcee, BonusMark.Valid); game.MarkBonus(emcee, BonusMark.Valid); game.MarkBonus(emcee, BonusMark.Valid);
            game.MarkBonus(emcee, BonusMark.Valid);
            html = Tv(game);
            Dump("bonus-over", html);
            Check.True(Render.Has(html, "ALL SIX"), "all six announced");
        });

        run("pyramid lobby: the TV shows a QR code; a phone gets the emcee button, then Start; nobody else can take the seat", () =>
        {
            var (game, _) = New();
            var tv = Lobby(game, new Seat(Role.Spectator), board: true);
            Dump("lobby-tv", tv);
            Check.True(Render.Has(tv, "id=\"qr-TEST\""), "a QR code for the emcee");
            Check.True(!Render.Has(tv, "Start Game"), "no buttons on the TV");
            var g = new PyramidGame("TEST", new FixedPyramidContent(), new Random(1));
            var phone = Lobby(g, new Seat(Role.Spectator), board: false);
            Check.True(Render.Has(phone, "m the emcee</button>") && !Render.Has(phone, "Start Game"), "an empty room offers the emcee seat");
            var token = g.JoinEmcee().Token;
            Check.True(Render.Has(Lobby(g, g.GetSeat(token), false), "Start Game"), "the emcee gets Start");
            var other = Lobby(g, new Seat(Role.Spectator), false);
            Check.True(!Render.Has(other, "Start Game") && !Render.Has(other, "m the emcee</button>"), "everyone else just watches");
        });

        run("pyramid emcee phone: has the script and the right buttons for each phase", () =>
        {
            var (game, emcee) = New();
            game.Start(emcee);
            Check.True(Render.Has(Emcee(game, emcee), "Reveal topic 1 of 6"), "reveal button");
            while (game.Phase == PyramidPhase.Reveal) game.RevealNext(emcee);
            Check.True(Render.Has(Emcee(game, emcee), "Tap the topic they choose"), "choose");
            var topic = PickKind(game, emcee, mystery: false);
            var html = Emcee(game, emcee);
            Check.True(Render.Has(html, "Name these things from topic " + topic) && Render.Has(html, "Start the clock"), "the script and the start button");
            game.StartRound(emcee);
            html = Emcee(game, emcee);
            Dump("emcee-playing", html);
            Check.True(Render.Has(html, Word(topic, 1)) && Render.Has(html, "Pass") && Render.Has(html, "✓") && Render.Has(html, "✗"), "word and marks");
            for (var w = 0; w < 7; w++) game.Mark(emcee, WordMark.Correct);
            html = Emcee(game, emcee);
            Check.True(Render.Has(html, "Tap a word to change it") && Render.Has(html, "Next") && Render.Has(html, "Undo") == game.Snapshot().CanUndo, "fix words and move on");
        });

        run("pyramid topics page: locked without the passcode, and lists nothing to a stranger", () =>
        {
            using var dir = new TempDir();
            var store = new FolderPyramidContent(dir.Path);
            var gate = Topics(store, new FloorAdminAccess("secret", false));
            Check.True(Render.Has(gate, "Passcode"), "asks for the passcode");
            Check.True(!Render.Has(gate, "Press Your Luck") && !Render.Has(gate, "toaster"), "no topics before unlocking");
            var locked = Topics(store, new FloorAdminAccess(null, false));
            Check.True(Render.Has(locked, "FLOOR_ADMIN_PASSCODE") && !Render.Has(locked, "Press Your Luck"), "tells you how to unlock it");
        });
    }
}