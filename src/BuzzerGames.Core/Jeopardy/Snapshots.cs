namespace BuzzerGames.Core.Jeopardy;

// A snapshot is a frozen, read-only picture of the game at one moment.
// The pages draw themselves from snapshots instead of reading the live game,
// so a buzz arriving mid-render can never give a page half-old, half-new data.

public sealed record PlayerInfo(
    Guid Id,
    string Name,
    int Score,
    bool HasControl,       // picks the next clue
    bool IsAnswering,      // currently has the floor
    bool IsLockedOut,      // already missed the current clue
    bool InFinal,          // qualified for Final Jeopardy (positive score going in)
    int? FinalWager,
    string? FinalAnswer,
    bool? FinalCorrect);

public sealed record CellInfo(int Value, bool IsUsed);

public sealed record CategoryInfo(string Name, IReadOnlyList<CellInfo> Cells);

public sealed record ClueInfo(
    string Category,
    int Value,
    string Text,
    string Answer,
    bool IsDailyDouble,
    int? Wager);

public sealed record GameSnapshot(
    string Code,
    GamePhase Phase,
    int Round,
    string? EmceeName,
    IReadOnlyList<PlayerInfo> Players,
    IReadOnlyList<CategoryInfo> Categories,
    ClueInfo? Clue,
    string? RevealMessage,
    int MinWager,
    int MaxWager,
    string? FinalCategory,
    string? FinalText,
    string? FinalAnswer,
    IReadOnlyList<Guid> Winners,
    bool CanStart)
{
    public PlayerInfo? FindPlayer(Guid? id) => id is null ? null : Players.FirstOrDefault(p => p.Id == id);

    public PlayerInfo? ControlPlayer => Players.FirstOrDefault(p => p.HasControl);

    public PlayerInfo? AnsweringPlayer => Players.FirstOrDefault(p => p.IsAnswering);
}
