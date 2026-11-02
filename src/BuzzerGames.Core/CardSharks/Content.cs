using System.Reflection;
using System.Text.Json;

namespace BuzzerGames.Core.CardSharks;

/// <summary>A survey question and its real result: "We asked 100 people...", and the answer is how many said yes (0 to 100).</summary>
public sealed record CardQuestion(string Text, int Answer);

public interface ICardSharksContent
{
    /// <summary>A shuffled run of questions, with none repeated until the bank runs out.</summary>
    IReadOnlyList<CardQuestion> CreateQuestions(Random rng, int count);
}

/// <summary>The survey bank, loaded from the embedded card-sharks.json (comments allowed in the JSON).</summary>
public sealed class JsonCardSharksContent : ICardSharksContent
{
    private sealed record Raw(List<RawQuestion>? Questions);
    private sealed record RawQuestion(string Text, int Answer);

    private readonly IReadOnlyList<CardQuestion> _questions;

    public JsonCardSharksContent()
        : this(ReadEmbedded())
    {
    }

    public JsonCardSharksContent(string json)
    {
        var raw = JsonSerializer.Deserialize<Raw>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new Raw(null);

        _questions = (raw.Questions ?? [])
            .Where(q => !string.IsNullOrWhiteSpace(q.Text) && q.Answer is >= 0 and <= 100)
            .Select(q => new CardQuestion(q.Text.Trim(), q.Answer))
            .ToList();
        if (_questions.Count == 0) throw new InvalidOperationException("The Card Sharks bank has no usable questions.");
    }

    private static string ReadEmbedded()
    {
        var assembly = typeof(JsonCardSharksContent).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".card-sharks.json", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    public IReadOnlyList<CardQuestion> CreateQuestions(Random rng, int count)
    {
        var result = new List<CardQuestion>();
        while (result.Count < count) result.AddRange(_questions.OrderBy(_ => rng.Next()));
        return result.Take(count).ToList();
    }
}

/// <summary>Questions for tests: "Question N", with answers 10, 20, 30...</summary>
public sealed class FixedCardSharksContent : ICardSharksContent
{
    public IReadOnlyList<CardQuestion> CreateQuestions(Random rng, int count) =>
        Enumerable.Range(1, count).Select(i => new CardQuestion($"Question {i}", (i * 10 - 1) % 100 + 1)).ToList();
}
