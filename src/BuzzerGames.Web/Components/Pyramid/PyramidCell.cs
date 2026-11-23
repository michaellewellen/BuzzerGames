namespace BuzzerGames.Web.Components.Pyramid;

/// <summary>
/// One square on a drawn pyramid. State is the CSS word for how it looks:
/// face (red, gold emblem), title (turned over, shows text), gone (flipped off the screen), money (turned over to a dollar amount),
/// passed (turned over, still live), dead (turned over to nothing).
/// </summary>
public sealed record PyramidCell(int Index, string State, string Text, bool Current = false);
