using System.Text.RegularExpressions;
using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Password;
using BuzzerGames.Core.PressYourLuck;
using BuzzerGames.Web.Components.Jeopardy;
using BuzzerGames.Web.Components.Password;
using BuzzerGames.Web.Components.PressYourLuck;
using BuzzerGames.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

/// <summary>
/// Draws the game's Razor components to an HTML string, the same markup a browser would receive,
/// so checks can ask "what would this screen actually show right now?"
/// </summary>
static class Render
{
    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .AddSingleton<IJSRuntime, NoJs>()
        .BuildServiceProvider();

    private static string Html<T>(Dictionary<string, object?> parameters) where T : IComponent
    {
        using var renderer = new HtmlRenderer(Services, Services.GetRequiredService<ILoggerFactory>());
        return renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        }).GetAwaiter().GetResult();
    }

    /// <summary>The big screen.</summary>
    public static string Tv(Table table) =>
        Html<BoardView>(new() { ["Snapshot"] = table.S });

    /// <summary>The emcee's phone.</summary>
    public static string Emcee(Table table) =>
        Html<EmceeView>(new() { ["Game"] = table.Game, ["Snapshot"] = table.S, ["Token"] = table.Emcee });

    /// <summary>One player's phone (0 = Ann, 1 = Bob, 2 = Cy).</summary>
    public static string Player(Table table, int player) =>
        Html<PlayerView>(new()
        {
            ["Game"] = table.Game,
            ["Snapshot"] = table.S,
            ["Token"] = table.Tokens[player],
            ["PlayerId"] = table.Ids[player]
        });

    public static string PylTv(PylTable t) => Html<PylTvView>(new() { ["Snapshot"] = t.S });

    public static string PylEmcee(PylTable t) =>
        Html<PylEmceeView>(new() { ["Game"] = t.Game, ["Snapshot"] = t.S, ["Token"] = t.Emcee });

    public static string PylPlayer(PylTable t, int i) =>
        Html<PylPlayerView>(new() { ["Game"] = t.Game, ["Snapshot"] = t.S, ["Token"] = t.Tokens[i], ["PlayerId"] = t.Ids[i] });

    // ---- Password Plus ----

    public static string PwTv(PasswordTable t) => Html<PasswordTvView>(new() { ["Snapshot"] = t.S });

    public static string PwEmcee(PasswordTable t) =>
        Html<PasswordEmceeView>(new() { ["Game"] = t.Game, ["Snapshot"] = t.S, ["Token"] = t.Emcee });

    /// <summary>One player's phone (0 = Ann, 1 = Bob on Team A; 2 = Cy, 3 = Dee on Team B).</summary>
    public static string PwPlayer(PasswordTable t, int i) =>
        Html<PasswordPlayerView>(new() { ["Game"] = t.Game, ["Snapshot"] = t.S, ["Token"] = t.Tokens[i], ["PlayerId"] = t.Ids[i] });

    public static string PwLobby(PasswordGame game, Seat seat, bool boardMode = false) =>
        Html<PasswordLobbyView>(new() { ["Snapshot"] = game.Snapshot(), ["Seat"] = seat, ["BoardMode"] = boardMode });

    /// <summary>The whole Password Plus room page, at the phone address or the big-screen /board address.</summary>
    public static string PwRoom(PasswordRoomManager rooms, string code, bool board)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IJSRuntime, NoJs>()
            .AddSingleton(rooms)
            .AddSingleton<NavigationManager>(new FakeNav($"http://localhost/password-plus/{code}" + (board ? "/board" : "")))
            .BuildServiceProvider();
        using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<PasswordRoom>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Code"] = code }));
            return output.ToHtmlString();
        }).GetAwaiter().GetResult();
    }

    /// <summary>True if the text appears anywhere on the page, in any capitalization (raw or HTML-encoded).</summary>
    public static bool ShowsAnyCase(string html, string text) =>
        html.Contains(text, StringComparison.OrdinalIgnoreCase)
        || html.Contains(System.Net.WebUtility.HtmlEncode(text), StringComparison.OrdinalIgnoreCase)
        || html.Contains(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(text), StringComparison.OrdinalIgnoreCase);

    /// <summary>The lobby, as seen by a device with the given seat.</summary>
    public static string Lobby(JeopardyGame game, Seat seat, bool boardMode = false) =>
        Html<LobbyView>(new() { ["Snapshot"] = game.Snapshot(), ["Seat"] = seat, ["BoardMode"] = boardMode });

    /// <summary>
    /// The whole room page, at the phone address (/jeopardy/CODE) or the big-screen address (/jeopardy/CODE/board).
    /// Unlike the other helpers this goes through routing-level code: which view gets picked, and what
    /// parameters the page passes down. That is where the "_error" bug lived.
    /// </summary>
    public static string Room(GameRoomManager rooms, string code, bool board)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IJSRuntime, NoJs>()
            .AddSingleton(rooms)
            .AddSingleton<NavigationManager>(new FakeNav($"http://localhost/jeopardy/{code}" + (board ? "/board" : "")))
            .BuildServiceProvider();
        using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<JeopardyRoom>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Code"] = code }));
            return output.ToHtmlString();
        }).GetAwaiter().GetResult();
    }

    private sealed class FakeNav : NavigationManager
    {
        public FakeNav(string uri) => Initialize("http://localhost/", uri);
    }

    // ---- little helpers for asserting on HTML ----

    public static bool Has(string html, string text) => html.Contains(text, StringComparison.Ordinal);

    public static int Count(string html, string text) => Regex.Matches(html, Regex.Escape(text)).Count;

    /// <summary>True if the clue text or answer (raw or HTML-encoded) appears anywhere on the page.</summary>
    public static bool Shows(string html, string text) =>
        Has(html, text)
        || Has(html, System.Net.WebUtility.HtmlEncode(text))
        || Has(html, System.Text.Encodings.Web.HtmlEncoder.Default.Encode(text));

    /// <summary>True if the button labelled <paramref name="label"/> is disabled.</summary>
    public static bool ButtonDisabled(string html, string label) =>
        Regex.IsMatch(html, @"<button[^>]*\bdisabled\b[^>]*>\s*" + Regex.Escape(label));

    public sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
    }
}
