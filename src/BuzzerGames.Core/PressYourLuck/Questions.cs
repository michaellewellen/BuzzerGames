using System.Text.Json;
using System.Text.Json.Serialization;
using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.PressYourLuck;

/// <summary>
/// One open-ended question. Decoys are plausible wrong answers, used to build the three-choice
/// follow-up that the other contestants pick from.
/// </summary>
public sealed record PylQuestion(
    string Category,
    string Text,
    string Answer,
    IReadOnlyList<string> Decoys,
    string? AlsoAccept = null);

/// <summary>Where Press Your Luck questions come from.</summary>
public interface IPylQuestionSource
{
    /// <summary>Returns rounds x perRound questions, round by round. Later rounds should be a bit harder.</summary>
    IReadOnlyList<PylQuestion> CreateQuestions(Random rng, int rounds, int perRound, GameOptions? options = null);
}

/// <summary>One question as written in Data/PressYourLuck/questions.json.</summary>
public sealed record PylQuestionRecord(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("wrong")] string[] Wrong,
    [property: JsonPropertyName("level")] int Level = 1,
    [property: JsonPropertyName("holiday")] bool Holiday = false);

/// <summary>
/// Deals Press Your Luck questions from its own bank, written for this game: general-knowledge questions with
/// one short spoken answer, and two wrong answers chosen by hand because people really might say them.
/// A game gets six questions, each from a different category. Round 1 uses the easier ones (level 1); round 2
/// leans toward the harder ones (level 2). Holiday questions only appear when the holiday option is on, and then
/// exactly one does.
/// </summary>
public sealed class JsonPylQuestionSource : IPylQuestionSource
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyList<PylQuestionRecord> _bank;

    public JsonPylQuestionSource() : this(LoadBank()) { }

    public JsonPylQuestionSource(IReadOnlyList<PylQuestionRecord> bank) => _bank = bank;

    /// <summary>Reads Data/PressYourLuck/questions.json from inside the assembly.</summary>
    public static IReadOnlyList<PylQuestionRecord> LoadBank()
    {
        var assembly = typeof(JsonPylQuestionSource).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(".PressYourLuck.questions.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The Press Your Luck question bank (questions.json) is missing.");
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return JsonSerializer.Deserialize<PylQuestionRecord[]>(reader.ReadToEnd(), Json) ?? [];
    }

    public IReadOnlyList<PylQuestion> CreateQuestions(Random rng, int rounds, int perRound, GameOptions? options = null)
    {
        options ??= GameOptions.Default;
        var total = rounds * perRound;
        var holidayIndex = options.Holiday ? rng.Next(total) : -1;
        var usedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedQuestions = new HashSet<PylQuestionRecord>();
        var result = new List<PylQuestion>();

        for (var round = 0; round < rounds; round++)
        {
            for (var i = 0; i < perRound; i++)
            {
                var wantHoliday = round * perRound + i == holidayIndex;
                var pool = _bank.Where(q => q.Holiday == wantHoliday && !usedQuestions.Contains(q)
                    && (wantHoliday || !usedCategories.Contains(q.Category))).ToList();
                if (!wantHoliday && round == 0 && pool.Any(q => q.Level <= 1)) pool = pool.Where(q => q.Level <= 1).ToList();
                if (pool.Count == 0) throw new InvalidOperationException("The question bank has too few questions to deal a game.");

                // Round 2 favors the harder questions: they count triple.
                var pick = round == 0 ? pool[rng.Next(pool.Count)] : WeightedPick(pool, rng);
                usedQuestions.Add(pick);
                usedCategories.Add(pick.Category);
                result.Add(new PylQuestion(pick.Category, pick.Question, pick.Answer, pick.Wrong));
            }
        }
        return result;
    }

    private static PylQuestionRecord WeightedPick(List<PylQuestionRecord> pool, Random rng)
    {
        var weights = pool.Select(q => q.Level >= 2 ? 3 : 1).ToArray();
        var roll = rng.Next(weights.Sum());
        for (var i = 0; i < pool.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0) return pool[i];
        }
        return pool[^1];
    }
}

/// <summary>A fixed set of questions for tests, so the engine can be checked without the big bank.</summary>
public sealed class FixedPylQuestionSource : IPylQuestionSource
{
    public IReadOnlyList<PylQuestion> CreateQuestions(Random rng, int rounds, int perRound, GameOptions? options = null) =>
        Enumerable.Range(1, rounds * perRound)
            .Select(i => new PylQuestion($"CATEGORY {i}", $"Clue number {i}", $"Answer{i}", [$"Decoy{i}A", $"Decoy{i}B"]))
            .ToList();
}
