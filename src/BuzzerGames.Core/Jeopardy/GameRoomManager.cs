using System.Collections.Concurrent;

namespace BuzzerGames.Core.Jeopardy;

/// <summary>
/// Keeps every running game in memory, findable by its short room code.
/// The web app registers exactly one of these, so every phone talks to the same set of games.
/// </summary>
public sealed class GameRoomManager(IGameDataSource source)
{
    // No vowels, so a room code can never spell a word you'd rather not show on the big screen.
    private const string CodeAlphabet = "BCDFGHJKLMNPQRSTVWXZ";
    private const int CodeLength = 4;

    private readonly ConcurrentDictionary<string, JeopardyGame> _games = new(StringComparer.OrdinalIgnoreCase);

    public JeopardyGame Create(GameOptions? options = null)
    {
        RemoveOldGames();
        while (true)
        {
            var code = NewCode();
            var game = new JeopardyGame(code, source, options: options);
            if (_games.TryAdd(code, game)) return game;   // on the (rare) chance of a clash, loop and try another code
        }
    }

    public JeopardyGame? Find(string? code) =>
        code is not null && _games.TryGetValue(code.Trim(), out var game) ? game : null;

    // A party lasts a few hours. Anything older than 12 is abandoned, so memory can't pile up.
    private void RemoveOldGames()
    {
        var cutoff = DateTime.UtcNow.AddHours(-12);
        foreach (var (code, game) in _games)
            if (game.CreatedAtUtc < cutoff) _games.TryRemove(code, out _);
    }

    private static string NewCode() =>
        string.Create(CodeLength, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)];
        });
}
