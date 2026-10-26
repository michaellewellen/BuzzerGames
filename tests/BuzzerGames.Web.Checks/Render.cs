using System.Text.RegularExpressions;
using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Web.Components.Jeopardy;
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

    /// <summary>The lobby, as seen by a device with the given seat.</summary>
    public static string Lobby(JeopardyGame game, Seat seat) =>
        Html<LobbyView>(new() { ["Snapshot"] = game.Snapshot(), ["Seat"] = seat });

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

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
    }
}
