using System.Collections.Concurrent;

namespace BuzzerGames.Core.CardSharks;

/// <summary>Keeps every running Card Sharks game in memory, findable by its short room code.</summary>
public sealed class CardSharksRoomManager(ICardSharksContent content)
{
    // No vowels, so a room code can never spell a word you'd rather not show on the big screen.
    private const string CodeAlphabet = "BCDFGHJKLMNPQRSTVWXZ";
    private const int CodeLength = 4;

    private readonly ConcurrentDictionary<string, CardSharksGame> _games = new(StringComparer.OrdinalIgnoreCase);

    public CardSharksGame Create()
    {
        RemoveOldGames();
        while (true)
        {
            var code = string.Create(CodeLength, 0, (span, _) =>
            {
                for (var i = 0; i < span.Length; i++) span[i] = CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)];
            });
            var game = new CardSharksGame(code, content);
            if (_games.TryAdd(code, game)) return game;
        }
    }

    public CardSharksGame? Find(string? code) =>
        code is not null && _games.TryGetValue(code.Trim(), out var game) ? game : null;

    private void RemoveOldGames()
    {
        var cutoff = DateTime.UtcNow.AddHours(-12);
        foreach (var (code, game) in _games)
            if (game.CreatedAtUtc < cutoff) _games.TryRemove(code, out _);
    }
}
