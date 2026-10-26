namespace BuzzerGames.Web.Games;

/// <summary>One tile on the "pick a game" page.</summary>
public sealed record GameInfo(string Name, string Url, string Blurb, bool IsPlayable);

/// <summary>
/// Every game the site knows about. Adding the next game show is two steps:
/// build its pages under /yourgame, then flip IsPlayable to true on its line here.
/// </summary>
public static class GameCatalog
{
    public static readonly IReadOnlyList<GameInfo> All =
    [
        new("Jeopardy", "/jeopardy", "Three players, one emcee, and a board full of clues.", IsPlayable: true),
        new("Wheel of Fortune", "/wheel-of-fortune", "Spin, solve, and buy a vowel.", IsPlayable: false),
        new("Classic Concentration", "/classic-concentration", "Match the prizes to reveal the rebus.", IsPlayable: false),
        new("Super Password", "/super-password", "One-word clues, two teams, one secret word.", IsPlayable: false),
        new("The Price Is Right", "/price-is-right", "Come on down!", IsPlayable: false),
    ];
}
