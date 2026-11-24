namespace BuzzerGames.Web.Games;

/// <summary>One tile on the "pick a game" page.</summary>
/// <param name="Logo">File under wwwroot/images/games shown on the tile.</param>
/// <param name="Fit">"cover" fills the tile (full-frame pictures); "contain" floats a logo on <paramref name="Backdrop"/>.</param>
public sealed record GameInfo(string Name, string Url, string Blurb, bool IsPlayable, string? Logo = null, string Fit = "cover", string Backdrop = "#000");

/// <summary>
/// Every game the site knows about. Adding the next game show is two steps:
/// build its pages under /yourgame, then flip IsPlayable to true on its line here.
/// </summary>
public static class GameCatalog
{
    public static readonly IReadOnlyList<GameInfo> All =
    [
        new("Jeopardy", "/jeopardy", "Three players, one emcee, and a board full of clues.", IsPlayable: true, Logo: "jeopardy.jpg"),
        new("Press Your Luck", "/press-your-luck", "Earn spins, then press your luck on the Big Board. No Whammies!", IsPlayable: true, Logo: "press-your-luck.jpg", Fit: "contain", Backdrop: "#111"),
        new("Password Plus", "/password-plus", "One-word clues, two teams, five secret words, one big word.", IsPlayable: true, Logo: "password-plus.jpg", Fit: "contain", Backdrop: "#111"),
        new("Match Game", "/match-game", "Fill in the blank like a celebrity panel. Match them, then go for the Super Match.", IsPlayable: true, Logo: "match-game.jpg"),
        new("Card Sharks", "/card-sharks", "Higher or lower: a survey duel for control, then race across your row of cards.", IsPlayable: true, Logo: "card-sharks.png", Fit: "contain", Backdrop: "radial-gradient(circle at 50% 40%, #2a6f4b, #0d2a1c)"),
        new("The Floor", "/the-floor", "Win the floor one picture duel at a time: name it before your clock runs out and take their territory.", IsPlayable: true, Logo: "the-floor.jpg"),
        new("Wheel of Fortune", "/wheel-of-fortune", "Spin, solve, and buy a vowel. Three players spin from their phones; the emcee runs the board.", IsPlayable: true, Logo: "wheel-of-fortune.png", Fit: "contain", Backdrop: "radial-gradient(circle at 50% 40%, #2b3fd0, #070b3c)"),
        new("$100,000 Pyramid", "/pyramid", "Two teams, six topics, seven words in thirty seconds. Win the game and take the bonus pyramid.", IsPlayable: true, Logo: "pyramid.jpg"),
    ];
}
