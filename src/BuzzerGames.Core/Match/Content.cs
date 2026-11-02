using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuzzerGames.Core.Match;

/// <summary>
/// A fill-in-the-blank for the celebrity panel. "{name}" is replaced with a name from the names list when the question is asked,
/// so "{name} is so dumb, he ___." becomes "Donald is so dumb, he ___." (or whoever is next in the family).
/// </summary>
public sealed record MatchQuestion(string Text)
{
    public string With(string name) => Text.Replace("{name}", name, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A Super Match blank like "Super ___". <see cref="Answers"/> are the three most popular answers, best first. Each one is a list of
/// spellings that count as that answer (the first spelling is the one shown on the board).
/// </summary>
public sealed record SuperQuestion(string Text, IReadOnlyList<IReadOnlyList<string>> Answers)
{
    public string Shown(int rank) => Answers[rank].Count > 0 ? Answers[rank][0] : "";
}

/// <summary>Where the game's questions and family names come from.</summary>
public interface IMatchContent
{
    IReadOnlyList<string> Names { get; }
    IReadOnlyList<MatchQuestion> CreateQuestions(Random rng, int count);
    IReadOnlyList<SuperQuestion> CreateSupers(Random rng, int count);
}

/// <summary>Helpers for comparing typed answers. The emcee always makes the real call; this only suggests.</summary>
public static class MatchText
{
    /// <summary>Lower case, "&amp;" as "and", no punctuation or spaces, no leading "a", "an" or "the".</summary>
    public static string Normalize(string? text)
    {
        var s = (text ?? "").ToLowerInvariant().Replace("&", " and ");
        var words = new string(s.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && words[0] is "a" or "an" or "the") words.RemoveAt(0);
        return string.Concat(words);
    }

    /// <summary>1 to 3 if the answer matches one of the top three (best first), or 0.</summary>
    public static int SuggestRank(SuperQuestion question, string? answer)
    {
        var typed = Normalize(answer);
        if (typed.Length == 0) return 0;
        for (var rank = 0; rank < question.Answers.Count; rank++)
            if (question.Answers[rank].Any(spelling => Normalize(spelling) == typed)) return rank + 1;
        return 0;
    }
}

/// <summary>Reads Data/Match/match-game.json (inside the assembly).</summary>
public sealed class JsonMatchContent : IMatchContent
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed class Bank
    {
        [JsonPropertyName("names")] public List<string> Names { get; set; } = [];
        [JsonPropertyName("questions")] public List<string> Questions { get; set; } = [];
        [JsonPropertyName("supers")] public List<SuperEntry> Supers { get; set; } = [];
    }

    private sealed class SuperEntry
    {
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("answers")] public List<List<string>> Answers { get; set; } = [];
    }

    private readonly IReadOnlyList<string> _names;
    private readonly IReadOnlyList<MatchQuestion> _questions;
    private readonly IReadOnlyList<SuperQuestion> _supers;

    public JsonMatchContent() : this(LoadJson()) { }

    public JsonMatchContent(string json)
    {
        var bank = JsonSerializer.Deserialize<Bank>(json, Json) ?? new Bank();
        _names = bank.Names.Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        _questions = bank.Questions.Select(q => q.Trim()).Where(q => q.Length > 0).Select(q => new MatchQuestion(q)).ToList();
        _supers = bank.Supers
            .Where(s => s.Text.Trim().Length > 0 && s.Answers.Count == 3 && s.Answers.All(a => a.Count > 0))
            .Select(s => new SuperQuestion(s.Text.Trim(), s.Answers.Select(a => (IReadOnlyList<string>)a.Select(x => x.Trim()).ToList()).ToList()))
            .ToList();
    }

    public static string LoadJson()
    {
        var assembly = typeof(JsonMatchContent).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(".Match.match-game.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The Match Game bank (match-game.json) is missing.");
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    public IReadOnlyList<string> Names => _names;

    public IReadOnlyList<MatchQuestion> CreateQuestions(Random rng, int count) => Deal(_questions, rng, count, "questions");

    public IReadOnlyList<SuperQuestion> CreateSupers(Random rng, int count) => Deal(_supers, rng, count, "Super Match blanks");

    private static IReadOnlyList<T> Deal<T>(IReadOnlyList<T> bank, Random rng, int count, string what)
    {
        if (bank.Count == 0) throw new InvalidOperationException($"The Match Game bank has no {what}.");
        var deck = bank.OrderBy(_ => rng.Next()).ToList();
        while (deck.Count < count) deck.AddRange(bank.OrderBy(_ => rng.Next()));
        return deck.Take(count).ToList();
    }
}

/// <summary>Predictable content for tests: "Question 1 about {name}", Super blanks whose top three are Alpha, Bravo and Charlie.</summary>
public sealed class FixedMatchContent : IMatchContent
{
    public IReadOnlyList<string> Names { get; } = ["Ann", "Bob", "Cy"];

    public IReadOnlyList<MatchQuestion> CreateQuestions(Random rng, int count) =>
        Enumerable.Range(1, count).Select(i => new MatchQuestion($"Question {i} about {{name}}: ___")).ToList();

    public IReadOnlyList<SuperQuestion> CreateSupers(Random rng, int count) =>
        Enumerable.Range(1, count).Select(i => new SuperQuestion($"Super {i} ___",
            [["Alpha", "The Alpha"], ["Bravo"], ["Charlie", "Charlie Brown"]])).ToList();
}
