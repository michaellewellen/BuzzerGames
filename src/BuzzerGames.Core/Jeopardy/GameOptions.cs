namespace BuzzerGames.Core.Jeopardy;

/// <summary>Choices the person creating the game makes before the questions are dealt.</summary>
/// <param name="Holiday">When true, the deal includes one holiday category. When false, holiday categories never appear.</param>
public sealed record GameOptions(bool Holiday = false)
{
    public static GameOptions Default { get; } = new();
}
