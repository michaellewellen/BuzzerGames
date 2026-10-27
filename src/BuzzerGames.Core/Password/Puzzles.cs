using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuzzerGames.Core.Password;

/// <summary>
/// One Password Plus puzzle: a mystery "big word" and five password words that each hint at it.
/// Kind is what the TV announces ("A Person", "A Place", "A Thing", "A Title", "An Idea").
/// </summary>
public sealed record PasswordPuzzle(
    string Kind,
    string Answer,
    IReadOnlyList<string> Words,
    IReadOnlyList<string> AlsoAccept);

/// <summary>Where Password Plus puzzles come from.</summary>
public interface IPasswordPuzzleSource
{
    /// <summary>Returns <paramref name="count"/> different puzzles, in the order they will be played.</summary>
    IReadOnlyList<PasswordPuzzle> CreatePuzzles(Random rng, int count);
}

/// <summary>One puzzle as written in Data/Password/puzzles.json.</summary>
public sealed record PasswordPuzzleRecord(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("words")] string[] Words,
    [property: JsonPropertyName("alsoAccept")] string[]? AlsoAccept = null);

/// <summary>
/// Deals puzzles from Data/Password/puzzles.json. Every puzzle in a game is different, and the deal avoids
/// two of the same kind (two people in a row, say) back to back whenever the bank allows it.
/// </summary>
public sealed class JsonPasswordPuzzleSource : IPasswordPuzzleSource
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyList<PasswordPuzzleRecord> _bank;

    public JsonPasswordPuzzleSource() : this(LoadBank()) { }

    public JsonPasswordPuzzleSource(IReadOnlyList<PasswordPuzzleRecord> bank) => _bank = bank;

    /// <summary>Reads Data/Password/puzzles.json from inside the assembly.</summary>
    public static IReadOnlyList<PasswordPuzzleRecord> LoadBank()
    {
        var assembly = typeof(JsonPasswordPuzzleSource).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(".Password.puzzles.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The Password Plus puzzle bank (puzzles.json) is missing.");
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return JsonSerializer.Deserialize<PasswordPuzzleRecord[]>(reader.ReadToEnd(), Json) ?? [];
    }

    public IReadOnlyList<PasswordPuzzle> CreatePuzzles(Random rng, int count)
    {
        if (_bank.Count < count)
            throw new InvalidOperationException("The puzzle bank has too few puzzles to deal a game.");

        // Shuffle (Fisher-Yates), then take the first `count` and spread out the kinds.
        var pool = _bank.ToList();
        for (var i = pool.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        pool = pool.Take(count).ToList();

        // Each step draws from whichever kind has the most puzzles left (other than the one just played), so a run of
        // one kind is never stranded at the end. Ties go to whichever came first in the shuffle.
        var ordered = new List<PasswordPuzzleRecord>();
        while (pool.Count > 0)
        {
            var last = ordered.Count == 0 ? null : ordered[^1].Kind;
            var candidates = pool.Where(p => !string.Equals(p.Kind, last, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0) candidates = pool;

            var best = candidates.Max(c => pool.Count(p => string.Equals(p.Kind, c.Kind, StringComparison.OrdinalIgnoreCase)));
            var next = candidates.First(c => pool.Count(p => string.Equals(p.Kind, c.Kind, StringComparison.OrdinalIgnoreCase)) == best);
            pool.Remove(next);
            ordered.Add(next);
        }

        return ordered
            .Select(r => new PasswordPuzzle(r.Kind, r.Answer, r.Words, r.AlsoAccept ?? []))
            .ToList();
    }
}

/// <summary>A fixed set of puzzles for tests, so the engine can be checked without the big bank.</summary>
public sealed class FixedPasswordPuzzleSource : IPasswordPuzzleSource
{
    public static readonly string[] Words = ["Anchor", "Banjo", "Cactus", "Dolphin", "Engine"];

    public IReadOnlyList<PasswordPuzzle> CreatePuzzles(Random rng, int count) =>
        Enumerable.Range(1, count)
            .Select(i => new PasswordPuzzle("Thing", $"Mystery{i}", Words, [$"Alt{i}"]))
            .ToList();
}
