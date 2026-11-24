using BuzzerGames.Core.Feud;
using BuzzerGames.Web;
using BuzzerGames.Web.Components.Feud;

/// <summary>What each Family Feud screen may show. The TV must never put an unrevealed answer (or the question before it is read) in the page.</summary>
static class FeudWebChecks
{
    private static (FeudGame Game, Guid Emcee) New()
    {
        var game = new FeudGame("TEST", new FixedFeudContent(), new Random(2));
        var emcee = game.JoinEmcee().Token;
        game.Start(emcee);
        return (game, emcee);
    }

    private static string Tv(FeudGame game, bool interactive = false) =>
        Render.Html<FeudTvView>(new() { ["Game"] = game, ["Snapshot"] = game.Snapshot(), ["Interactive"] = interactive });

    private static string Emcee(FeudGame game, Guid token) =>
        Render.Html<FeudEmceeView>(new() { ["Game"] = game, ["Snapshot"] = game.Snapshot(), ["Token"] = token });

    /// <summary>Set FEUD_DUMP_DIR and the screens drawn here are saved as pages, to look at in a browser.</summary>
    private static void Dump(string name, string html)
    {
        var dir = Environment.GetEnvironmentVariable("FEUD_DUMP_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".html"),
            "<!doctype html><html><head><meta charset=utf-8><link rel=stylesheet href=app.css></head><body>" + html + "</body></html>");
    }

    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine("Family Feud screens");

        run("feud tv: the lobby shows the logo and a legend for the mouse buzzers", () =>
        {
            var game = new FeudGame("TEST", new FixedFeudContent(), new Random(2));
            var html = Render.Html<FeudLobbyView>(new() { ["Snapshot"] = game.Snapshot(), ["BoardMode"] = true });
            Check.True(Render.Has(html, "family-feud.jpg"), "the logo");
            Check.True(Render.Has(html, "fd-mouse-help"), "the left/right click legend");
        });

        run("feud tv: before the question is read, neither the question nor any answer is in the page", () =>
        {
            var (game, _) = New();
            var html = Tv(game);
            Check.True(!Render.Has(html, "Question 1"), "no question yet");
            Check.True(!Render.Has(html, "Q1 answer"), "no answers");
            Check.True(Render.Has(html, "family-feud.jpg"), "the logo header");
        });

        run("feud tv: only revealed answers are written into the page", () =>
        {
            var (game, emcee) = New();
            game.ReadQuestion(emcee);
            Check.Ok(game.Buzz(0));
            Check.Ok(game.Right(emcee, 0));
            var html = Tv(game);
            Dump("play", html);
            Check.True(Render.Has(html, "Q1 answer 1"), "the revealed answer");
            Check.True(!Render.Has(html, "Q1 answer 2") && !Render.Has(html, "Q1 answer 6"), "the hidden ones stay out of the page");
            Check.True(!Render.Has(html, "Question 1"), "the question is spoken, never shown on the TV");
            Check.True(Render.Has(html, "ROUND 1"), "the round is shown");
            Check.Equal(8, Render.Count(html, "fd-slot "), "eight slots on the board");
            Check.Equal(2, Render.Count(html, "fd-slot empty"), "two of them unused for a six-answer board");
            Check.Equal(1, Render.Count(html, "fd-slot  open"), "one slid open");
        });

        run("feud emcee: sees every answer, and the revealed one is disabled", () =>
        {
            var (game, emcee) = New();
            game.ReadQuestion(emcee);
            Check.Ok(game.Buzz(1));
            var html = Emcee(game, emcee);
            Check.True(Render.Has(html, "Q1 answer 1") && Render.Has(html, "Q1 answer 4") && Render.Has(html, "Q1 answer 6"), "the whole list");
            Check.True(Render.Has(html, "Question 1"), "the question");
            Check.Ok(game.Right(emcee, 0));
            html = Emcee(game, emcee);
            Check.True(Render.Has(html, "Q1 answer 6"), "the list is still there after a reveal");
        });

        run("feud home: the picker has a Family Feud tile that is playable", () =>
        {
            var tile = BuzzerGames.Web.Games.GameCatalog.All.Single(g => g.Name == "Family Feud");
            Check.True(tile.IsPlayable && tile.Url == "/family-feud", "playable, at /family-feud");
        });
    }
}
