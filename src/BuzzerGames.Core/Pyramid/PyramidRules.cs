namespace BuzzerGames.Core.Pyramid;

/// <summary>The numbers that define a game of $100,000 Pyramid. Kept in one place so the engine, the screens and the checks agree.</summary>
public static class PyramidRules
{
    public const int Teams = 2;
    public const int TileCount = 6;            // topics on the pyramid, three picked by each team
    public const int WordsPerTopic = 7;        // every topic and every letter round has seven words
    public const int RoundsPerGame = 3;        // each round, both teams play one topic: 3 rounds = 6 topics
    public const int GamesPerMatch = 2;
    public const double RoundSeconds = 30;     // seven words in thirty seconds
    public const double BonusSeconds = 60;     // the six bonus topics in sixty

    public const int LowPrize = 25_000;
    public const int HighPrize = 100_000;

    /// <summary>What each of the six bonus squares shows when it turns over. They climb, and they add up to far less than the prize for clearing all six.</summary>
    public static readonly int[] LowTiles = [1_000, 1_000, 1_000, 2_000, 2_000, 5_000];      // 12,000
    public static readonly int[] HighTiles = [3_000, 3_000, 4_000, 6_000, 8_000, 16_000];    // 40,000

    public static readonly string[] TeamNames = ["Team A", "Team B"];

    public static int[] TileValues(int prize) => prize == HighPrize ? HighTiles : LowTiles;

    /// <summary>The main pyramid is drawn as rows of 1, 2 and 3 squares. Slot numbers read left to right, top to bottom.</summary>
    public static readonly int[] MainRows = [1, 2, 3];

    /// <summary>The bonus pyramid climbs: squares 0 to 2 are the bottom row, 3 and 4 the middle, 5 the top (the hardest topic, and the biggest dollar amount).</summary>
    public static readonly int[][] BonusRowsTopToBottom = [[5], [3, 4], [0, 1, 2]];

    public const int WordMaxLength = 40;
    public const int TitleMaxLength = 40;
    public const int DescriptionMaxLength = 90;
    public const int SubjectMaxLength = 60;
}
