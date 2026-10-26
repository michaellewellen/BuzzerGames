namespace BuzzerGames.Core.Jeopardy;

// These records describe a game's *questions*, before anyone starts playing.
// A data source (dummy data today, a real dataset later) hands one of these to the engine.

/// <summary>One clue and its answer. Its dollar value comes from its position in the category.</summary>
public sealed record ClueData(string Text, string Answer);

/// <summary>A category with exactly five clues, ordered easiest (top) to hardest (bottom).</summary>
public sealed record CategoryData(string Name, IReadOnlyList<ClueData> Clues);

/// <summary>The single Final Jeopardy clue.</summary>
public sealed record FinalClueData(string Category, string Text, string Answer);

/// <summary>Everything needed to play one whole game: six categories per round, plus the final clue.</summary>
public sealed record GameData(
    IReadOnlyList<CategoryData> Round1,
    IReadOnlyList<CategoryData> Round2,
    FinalClueData Final);
