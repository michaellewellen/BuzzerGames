using System.Collections.Concurrent;
using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.PressYourLuck;

/// <summary>Keeps every running Press Your Luck game in memory, findable by its short room code.</summary>
public sealed class PylRoomManager(IPylQuestionSource source)
{
    // No vowels, so a room code can never spell a word you'd rather not show on the big screen.
    private const string CodeAlphabet = "BCDFGHJKLMNPQRSTVWXZ";
    private const int CodeLength = 4;

    private readonly ConcurrentDictionary<string, PressYourLuckGame> _games = new(StringComparer.OrdinalIgnoreCase);

    public PressYourLuckGame Create(GameOptions? options = null)
    {
        RemoveOldGames();
        while (true)
        {
            var code = string.Create(CodeLength, 0, (span, _) =>
            {
                for (var i = 0; i < span.Length; i++) span[i] = CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)];
            });
            var game = new PressYourLuckGame(code, source, options: options);
            if (_games.TryAdd(code, game)) return game;
        }
    }

    public PressYourLuckGame? Find(string? code) =>
        code is not null && _games.TryGetValue(code.Trim(), out var game) ? game : null;

    private void RemoveOldGames()
    {
        var cutoff = DateTime.UtcNow.AddHours(-12);
        foreach (var (code, game) in _games)
            if (game.CreatedAtUtc < cutoff) _games.TryRemove(code, out _);
    }
}
