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

/// <summary>
/// Builds questions out of the same category bank Jeopardy uses. A Jeopardy clue is already an open-ended
/// question with a one-word or one-name answer, and the OTHER answers in its category make natural decoys
/// ("Also a Game": Risk, Go, Othello...). Round 1 uses the easier rows of a category, round 2 the harder ones.
/// </summary>
public sealed class JsonPylQuestionSource : IPylQuestionSource
{
    private readonly IReadOnlyList<CategoryRecord> _categories;

    public JsonPylQuestionSource() : this(QuestionBank.LoadCategories()) { }

    public JsonPylQuestionSource(IReadOnlyList<CategoryRecord> categories) => _categories = categories;

    public IReadOnlyList<PylQuestion> CreateQuestions(Random rng, int rounds, int perRound, GameOptions? options = null)
    {
        options ??= GameOptions.Default;
        var total = rounds * perRound;
        var used = new HashSet<CategoryRecord>();
        var holidayIndex = options.Holiday ? rng.Next(total) : -1;
        var result = new List<PylQuestion>();

        for (var round = 0; round < rounds; round++)
        {
            var tagsThisRound = new HashSet<string>();
            for (var i = 0; i < perRound; i++)
            {
                var index = round * perRound + i;
                var wantHoliday = index == holidayIndex;

                var candidates = _categories.Where(c => !used.Contains(c) && c.Clues.Length >= 3
                    && (wantHoliday ? c.Tag == JsonGameDataSource.Holiday : c.Tag != JsonGameDataSource.Holiday)
                    && (wantHoliday || !tagsThisRound.Contains(c.Tag))).ToList();
                if (candidates.Count == 0)   // a tiny bank: relax the "different subject" rule
                    candidates = _categories.Where(c => !used.Contains(c) && c.Tag != JsonGameDataSource.Holiday).ToList();
                if (candidates.Count == 0) throw new InvalidOperationException("The question bank has too few categories.");

                var category = candidates[rng.Next(candidates.Count)];
                used.Add(category);
                tagsThisRound.Add(category.Tag);

                // Rows run easiest to hardest: round 1 draws from the first three, round 2 from the last three.
                var rowStart = rounds <= 1 ? 0 : (int)Math.Round((double)round / (rounds - 1) * Math.Max(0, category.Clues.Length - 3));
                var row = Math.Min(category.Clues.Length - 1, rowStart + rng.Next(3));
                result.Add(Build(category, row, rng));
            }
        }
        return result;
    }

    private static PylQuestion Build(CategoryRecord category, int row, Random rng)
    {
        var clue = category.Clues[row];
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { clue.Answer };
        foreach (var a in clue.Alt ?? []) taken.Add(a);

        var decoys = category.Clues.Where(c => !taken.Contains(c.Answer)).Select(c => c.Answer)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(_ => rng.Next()).Take(2).ToList();

        return new PylQuestion(
            category.Title.ToUpperInvariant(), clue.Clue, clue.Answer, decoys,
            clue.Alt is { Length: > 0 } ? string.Join(", ", clue.Alt) : null);
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
