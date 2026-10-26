namespace BuzzerGames.Core.Jeopardy;

/// <summary>
/// Deals a game out of the question bank. Instead of picking 12 categories purely at random it fills
/// "slots" so a board is always a mix a party will enjoy:
///
///   each round (6 categories) = 2 wordplay + 2 pop culture + 2 others (never the same tag twice)
///
/// If the creator ticked "holiday", one of the "other" slots in a random round becomes a holiday
/// category. Holiday categories never appear otherwise. No category appears twice in a game, and the
/// "double" categories (a bit harder) go to round 2 when there is a choice.
/// </summary>
public sealed class JsonGameDataSource : IGameDataSource
{
    public const string Wordplay = "wordplay";
    public const string Pop = "pop";
    public const string Holiday = "holiday";

    private readonly IReadOnlyList<CategoryRecord> _categories;
    private readonly IReadOnlyList<FinalRecord> _finals;

    public JsonGameDataSource() : this(QuestionBank.LoadCategories(), QuestionBank.LoadFinals()) { }

    public JsonGameDataSource(IReadOnlyList<CategoryRecord> categories, IReadOnlyList<FinalRecord> finals)
    {
        _categories = categories;
        _finals = finals;
    }

    public GameData CreateGame(Random rng, GameOptions? options = null)
    {
        options ??= GameOptions.Default;
        var used = new HashSet<CategoryRecord>();

        // Which round gets the holiday category (0 = none).
        var holidayRound = options.Holiday ? rng.Next(1, 3) : 0;

        var round1 = DealRound(1, holidayRound == 1, used, rng);
        var round2 = DealRound(2, holidayRound == 2, used, rng);

        var finals = _finals.Where(f => options.Holiday || !f.Holiday).ToList();
        var f = finals[rng.Next(finals.Count)];
        return new GameData(round1, round2, new FinalClueData(f.Category.ToUpperInvariant(), f.Clue, WithAlternates(f.Answer, f.Alt)));
    }

    private List<CategoryData> DealRound(int round, bool includeHoliday, HashSet<CategoryRecord> used, Random rng)
    {
        var picked = new List<CategoryRecord>();

        void Take(int count, Func<CategoryRecord, bool> filter)
        {
            for (var i = 0; i < count; i++)
            {
                var choice = Choose(round, rng, used, picked, filter);
                if (choice is null) return;
                picked.Add(choice);
                used.Add(choice);
            }
        }

        Take(2, c => c.Tag == Wordplay);
        Take(2, c => c.Tag == Pop);
        if (includeHoliday) Take(1, c => c.Tag == Holiday);

        // The "other" slots: any tag except the three special ones, and never the same tag twice in a round.
        while (picked.Count < 6)
        {
            var tagsInRound = picked.Select(p => p.Tag).ToHashSet();
            var choice = Choose(round, rng, used, picked,
                c => c.Tag is not (Wordplay or Pop or Holiday) && !tagsInRound.Contains(c.Tag));
            // If a quota can't be met (a tiny question bank), relax it rather than fail to deal a game.
            choice ??= Choose(round, rng, used, picked, c => c.Tag != Holiday);
            if (choice is null) throw new InvalidOperationException("The question bank has too few categories to deal a game.");
            picked.Add(choice);
            used.Add(choice);
        }

        return picked.OrderBy(_ => rng.Next()).Select(ToData).ToList();
    }

    /// <summary>Picks one unused category that passes the filter, preferring ones that fit this round.</summary>
    private CategoryRecord? Choose(int round, Random rng, HashSet<CategoryRecord> used, List<CategoryRecord> picked, Func<CategoryRecord, bool> filter)
    {
        var candidates = _categories.Where(c => !used.Contains(c) && !picked.Contains(c) && filter(c)).ToList();
        if (candidates.Count == 0) return null;

        var preferred = round == 1 ? "single" : "double";
        var fits = candidates.Where(c => c.Round.Equals(preferred, StringComparison.OrdinalIgnoreCase)
                                        || c.Round.Equals("any", StringComparison.OrdinalIgnoreCase)).ToList();
        var pool = fits.Count > 0 ? fits : candidates;
        return pool[rng.Next(pool.Count)];
    }

    private static CategoryData ToData(CategoryRecord c) =>
        new(c.Title.ToUpperInvariant(), c.Clues.Select(x => new ClueData(x.Clue, WithAlternates(x.Answer, x.Alt))).ToList());

    // The emcee sees the answer on their phone, so alternates are shown right there.
    private static string WithAlternates(string answer, string[]? alt) =>
        alt is { Length: > 0 } ? $"{answer} (also accept: {string.Join(", ", alt)})" : answer;
}
