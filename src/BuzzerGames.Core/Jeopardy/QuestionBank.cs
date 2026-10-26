using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuzzerGames.Core.Jeopardy;

// The shapes below mirror the JSON files in Data/Jeopardy. One file holds many categories.

/// <summary>One clue as written in a JSON file. "Alt" lists other spellings of the SAME answer.</summary>
public sealed record ClueRecord(
    [property: JsonPropertyName("clue")] string Clue,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("alt")] string[]? Alt = null);

/// <summary>
/// A category as written in a JSON file.
/// Tags decide how the shuffle balances a board: wordplay, pop, geography, history, science,
/// books, arts, food, sports, potpourri, and holiday.
/// Round says where it fits: "single", "double" (a bit harder), or "any".
/// </summary>
public sealed record CategoryRecord(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("tag")] string Tag,
    [property: JsonPropertyName("round")] string Round,
    [property: JsonPropertyName("clues")] ClueRecord[] Clues);

/// <summary>A Final Jeopardy clue as written in finals.json.</summary>
public sealed record FinalRecord(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("clue")] string Clue,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("alt")] string[]? Alt = null,
    [property: JsonPropertyName("holiday")] bool Holiday = false);

/// <summary>Loads the JSON files that ship inside the assembly.</summary>
public static class QuestionBank
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Every category from every Data/Jeopardy/categories-*.json file.</summary>
    public static IReadOnlyList<CategoryRecord> LoadCategories() =>
        ReadResources("categories-").SelectMany(json => JsonSerializer.Deserialize<CategoryRecord[]>(json, Json) ?? []).ToList();

    public static IReadOnlyList<FinalRecord> LoadFinals() =>
        ReadResources("finals").SelectMany(json => JsonSerializer.Deserialize<FinalRecord[]>(json, Json) ?? []).ToList();

    private static IEnumerable<string> ReadResources(string filePrefix)
    {
        var assembly = typeof(QuestionBank).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Order())
        {
            // Resource names look like "BuzzerGames.Core.Data.Jeopardy.categories-wordplay.json".
            var file = name[(name.LastIndexOf('.', name.Length - 6) + 1)..];
            if (!file.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".json")) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return reader.ReadToEnd();
        }
    }
}
