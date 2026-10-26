namespace BuzzerGames.Core.Jeopardy;

/// <summary>
/// Where a game's questions come from. The engine only knows about this interface,
/// so swapping the dummy data for a real dataset later means writing one new class
/// and changing one line in Program.cs. Nothing else has to move.
/// </summary>
public interface IGameDataSource
{
    /// <summary>Builds one complete game: 6 categories for each round and one final clue.</summary>
    GameData CreateGame(Random rng, GameOptions? options = null);
}
