using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BuzzerGames.Core.Wheel;

public enum WheelPuzzleKind { Regular, Quick, Bonus }

/// <summary>A regular or bonus puzzle: a category and the phrase. The phrase is stored in capitals, with "/" wherever the author forced a line break.</summary>
public sealed record WheelPuzzle(string Id, string Category, string Text);

/// <summary>A quick round: three puzzles that share a theme ("Things With Wheels"), played one after another.</summary>
public sealed record WheelQuickSet(string Id, string Theme, IReadOnlyList<string> Texts);

public sealed record WheelCounts(int Regular, int RegularUnused, int Quick, int QuickUnused, int Bonus, int BonusUnused, int BonusCategories);

/// <summary>What one game plays. The quick sets and bonus choices can be shorter than hoped if the bank is thin; only the regular puzzles are required.</summary>
public sealed record WheelDeal(IReadOnlyList<WheelPuzzle> Regular, IReadOnlyList<WheelQuickSet> Quick, IReadOnlyList<WheelPuzzle> BonusChoices);

public readonly record struct WheelOpResult(bool Ok, string? Error = null, string? Id = null, bool Duplicate = false, IReadOnlyList<string>? Warnings = null)
{
    public static WheelOpResult Success(string? id = null, IReadOnlyList<string>? warnings = null) => new(true, null, id, false, warnings);
    public static WheelOpResult Fail(string error, bool duplicate = false) => new(false, error, null, duplicate);
}

public sealed record WheelImportReport(int Added, int Duplicates, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings);

public interface IWheelContent
{
    WheelCounts Counts();

    /// <summary>Picks a game's puzzles, unused ones first. Fails only if there are too few regular puzzles.</summary>
    bool TryCreateDeal(Random rng, out WheelDeal deal, out string error);

    /// <summary>The ids of puzzles a game has dealt, so the next game starts with fresh ones.</summary>
    void MarkUsed(IEnumerable<string> ids);
}

/// <summary>What the admin page checks for besides "does it fit on the board". Everything here is a warning for a human to judge, never a block.</summary>
public static class WheelContentRules
{
    public const int CategoryMax = 40;

    private static readonly string[] PartyRuleWords =
        ["BEER", "WINE", "VODKA", "WHISKEY", "WHISKY", "TEQUILA", "COCKTAIL", "MARTINI", "CHAMPAGNE", "COFFEE", "LATTE", "ESPRESSO", "CAPPUCCINO"];

    private static readonly string[] CutesyBonusCategories = ["SAME NAME", "BEFORE & AFTER", "RHYME TIME", "CROSSWORD CLUE"];

    public static string CleanCategory(string? raw)
    {
        var chars = (raw ?? "").Where(c => !char.IsControl(c) && c != '|' && c != '#').ToArray();
        var line = string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length > CategoryMax ? line[..CategoryMax].Trim() : line;
    }

    public static IReadOnlyList<string> Warnings(WheelPuzzleKind kind, string category, string text)
    {
        var warnings = new List<string>();
        var haystack = (category + " " + text).ToUpperInvariant();
        foreach (var word in PartyRuleWords)
            if (Regex.IsMatch(haystack, @"\b" + word + @"S?\b")) warnings.Add($"Mentions {word.ToLowerInvariant()}: this party's rules say no alcohol or coffee content.");
        if (kind == WheelPuzzleKind.Bonus && CutesyBonusCategories.Contains(category.ToUpperInvariant()))
            warnings.Add($"\"{category}\" is a clue-style category; the bonus round uses plain ones (a person, a place, a thing).");
        return warnings;
    }
}

/// <summary>
/// The puzzle bank, kept in one file (<c>wheel/puzzles.json</c>) inside the data folder. On Railway that folder is the Volume, so puzzles
/// survive a redeploy. The first time it runs it fills the file with a small starter set so there is something to play; after that the file
/// is yours, and the admin page is how you change it.
/// </summary>
public sealed class FolderWheelContent : IWheelContent
{
    private sealed class Bank
    {
        public List<WheelPuzzle> Regular { get; set; } = [];
        public List<WheelQuickSet> Quick { get; set; } = [];
        public List<WheelPuzzle> Bonus { get; set; } = [];
        public List<string> Used { get; set; } = [];
    }

    private sealed record RawPuzzle(string Category, string Text);
    private sealed record RawQuick(string Theme, List<string> Texts);
    private sealed record RawStarter(List<RawPuzzle>? Regular, List<RawQuick>? Quick, List<RawPuzzle>? Bonus);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly object _gate = new();
    private readonly string _path;
    private Bank _bank = new();

    public FolderWheelContent(string dataDirectory, bool seedStarters = true)
    {
        var folder = Path.Combine(Path.GetFullPath(dataDirectory), "wheel");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "puzzles.json");

        if (File.Exists(_path))
        {
            try
            {
                _bank = JsonSerializer.Deserialize<Bank>(File.ReadAllText(_path), Json) ?? new Bank();
                return;
            }
            catch (JsonException)
            {
                // Keep the unreadable file for a human to look at instead of overwriting it.
                File.Move(_path, _path + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            }
        }

        if (seedStarters) SeedStarters();
        Save();
    }

    public string FilePath => _path;

    // ---- Reading -------------------------------------------------------------------------

    public IReadOnlyList<WheelPuzzle> Regular() { lock (_gate) return [.. _bank.Regular]; }
    public IReadOnlyList<WheelQuickSet> Quick() { lock (_gate) return [.. _bank.Quick]; }
    public IReadOnlyList<WheelPuzzle> Bonus() { lock (_gate) return [.. _bank.Bonus]; }
    public bool IsUsed(string id) { lock (_gate) return _bank.Used.Contains(id); }

    public WheelCounts Counts()
    {
        lock (_gate)
        {
            var used = _bank.Used.ToHashSet();
            return new WheelCounts(
                _bank.Regular.Count, _bank.Regular.Count(p => !used.Contains(p.Id)),
                _bank.Quick.Count, _bank.Quick.Count(q => !used.Contains(q.Id)),
                _bank.Bonus.Count, _bank.Bonus.Count(p => !used.Contains(p.Id)),
                _bank.Bonus.Select(p => p.Category.ToUpperInvariant()).Distinct().Count());
        }
    }

    // ---- Adding and removing -------------------------------------------------------------

    public WheelOpResult AddRegular(string? category, string? text) => Locked(() => Persist(TryAddSingle(WheelPuzzleKind.Regular, category, text)));

    public WheelOpResult AddBonus(string? category, string? text) => Locked(() => Persist(TryAddSingle(WheelPuzzleKind.Bonus, category, text)));

    public WheelOpResult AddQuick(string? theme, IReadOnlyList<string?> texts) => Locked(() => Persist(TryAddQuick(theme, texts)));

    public WheelOpResult Delete(string id) => Locked(() =>
    {
        var removed = _bank.Regular.RemoveAll(p => p.Id == id) + _bank.Bonus.RemoveAll(p => p.Id == id) + _bank.Quick.RemoveAll(q => q.Id == id);
        if (removed == 0) return WheelOpResult.Fail("That puzzle is already gone.");
        _bank.Used.Remove(id);
        Save();
        return WheelOpResult.Success();
    });

    public void ClearUsed()
    {
        lock (_gate)
        {
            _bank.Used.Clear();
            Save();
        }
    }

    /// <summary>
    /// Adds many puzzles at once from pasted text. A line starting with <c># regular</c>, <c># quick</c> or <c># bonus</c> switches the kind
    /// (it starts as regular); any other line starting with # is a comment. Regular and bonus lines are <c>category | puzzle</c>;
    /// quick lines are <c>theme | puzzle | puzzle | puzzle</c>. Lines that can't be used are reported, not fatal.
    /// </summary>
    public WheelImportReport Import(string? pasted)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        var added = 0;
        var duplicates = 0;
        var kind = WheelPuzzleKind.Regular;
        var lineNumber = 0;

        lock (_gate)
        {
            foreach (var raw in (pasted ?? "").Split('\n'))
            {
                lineNumber++;
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith('#'))
                {
                    var body = line.TrimStart('#').Trim().ToLowerInvariant();
                    if (body.StartsWith("regular")) kind = WheelPuzzleKind.Regular;
                    else if (body.StartsWith("quick")) kind = WheelPuzzleKind.Quick;
                    else if (body.StartsWith("bonus")) kind = WheelPuzzleKind.Bonus;
                    continue;
                }

                var parts = line.Split('|', StringSplitOptions.TrimEntries);
                WheelOpResult result;
                if (kind == WheelPuzzleKind.Quick)
                {
                    result = parts.Length == 4
                        ? TryAddQuick(parts[0], parts.Skip(1).ToList())
                        : WheelOpResult.Fail("A quick round line needs a theme and three puzzles: theme | one | two | three");
                }
                else
                {
                    result = parts.Length == 2
                        ? TryAddSingle(kind, parts[0], parts[1])
                        : WheelOpResult.Fail("This line needs a category and a puzzle: category | puzzle");
                }

                if (result.Ok)
                {
                    added++;
                    foreach (var w in result.Warnings ?? []) warnings.Add($"Line {lineNumber}: {w}");
                }
                else if (result.Duplicate) duplicates++;
                else problems.Add($"Line {lineNumber}: {result.Error}");
            }
            if (added > 0) Save();
        }
        return new WheelImportReport(added, duplicates, problems, warnings);
    }

    // ---- Dealing a game ------------------------------------------------------------------

    public bool TryCreateDeal(Random rng, out WheelDeal deal, out string error)
    {
        lock (_gate)
        {
            deal = new WheelDeal([], [], []);
            error = "";
            var used = _bank.Used.ToHashSet();

            var regular = Pick(_bank.Regular, p => p.Id, p => p.Category, WheelRules.RegularPuzzles, used, rng);
            if (regular.Count < WheelRules.RegularPuzzles)
            {
                error = $"The puzzle bank has {_bank.Regular.Count} regular puzzles; a game needs {WheelRules.RegularPuzzles}. Add some on the puzzles page.";
                return false;
            }

            var quick = Pick(_bank.Quick, q => q.Id, q => q.Theme, 2, used, rng);

            var chosenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bonus = new List<WheelPuzzle>();
            foreach (var p in Shuffled(_bank.Bonus.Where(p => !used.Contains(p.Id)), rng).Concat(Shuffled(_bank.Bonus.Where(p => used.Contains(p.Id)), rng)))
            {
                if (bonus.Count == 3) break;
                if (chosenCategories.Add(p.Category)) bonus.Add(p);
            }

            deal = new WheelDeal(regular, quick, bonus);
            return true;
        }
    }

    public void MarkUsed(IEnumerable<string> ids)
    {
        lock (_gate)
        {
            foreach (var id in ids)
                if (!_bank.Used.Contains(id)) _bank.Used.Add(id);
            Save();
        }
    }

    // ---- Internals -----------------------------------------------------------------------

    private static List<T> Shuffled<T>(IEnumerable<T> items, Random rng) => items.OrderBy(_ => rng.Next()).ToList();

    /// <summary>
    /// Picks <paramref name="count"/> items. In order of preference: fresh ones from a category not used yet in this pick, then any other fresh ones, then used
    /// ones from a new category, then anything. So with a decent bank no category appears twice and nothing repeats, and a thin
    /// bank still deals a game instead of failing.
    /// </summary>
    private static List<T> Pick<T>(List<T> all, Func<T, string> id, Func<T, string> group, int count, HashSet<string> used, Random rng)
    {
        var fresh = Shuffled(all.Where(x => !used.Contains(id(x))), rng);
        var old = Shuffled(all.Where(x => used.Contains(id(x))), rng);
        var picked = new List<T>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Take(List<T> from, bool newCategoryOnly)
        {
            foreach (var x in from)
            {
                if (picked.Count == count) return;
                if (picked.Contains(x)) continue;
                if (newCategoryOnly && seen.Contains(group(x).Trim())) continue;
                picked.Add(x);
                seen.Add(group(x).Trim());
            }
        }

        Take(fresh, newCategoryOnly: true);
        Take(fresh, newCategoryOnly: false);
        Take(old, newCategoryOnly: true);
        Take(old, newCategoryOnly: false);
        return picked;
    }

    private T Locked<T>(Func<T> action)
    {
        lock (_gate) return action();
    }

    private WheelOpResult Persist(WheelOpResult result)
    {
        if (result.Ok) Save();
        return result;
    }

    private HashSet<string> AllTexts() =>
        _bank.Regular.Select(p => p.Text)
            .Concat(_bank.Bonus.Select(p => p.Text))
            .Concat(_bank.Quick.SelectMany(q => q.Texts))
            .ToHashSet();

    private WheelOpResult TryAddSingle(WheelPuzzleKind kind, string? rawCategory, string? rawText)
    {
        var category = WheelContentRules.CleanCategory(rawCategory);
        if (category.Length == 0) return WheelOpResult.Fail("Give it a category.");
        if (!WheelBoard.TryLayout(rawText, out var layout, out var problem)) return WheelOpResult.Fail(problem);
        if (AllTexts().Contains(layout.Text)) return WheelOpResult.Fail("That puzzle is already in the bank.", duplicate: true);

        var puzzle = new WheelPuzzle(NewId(), category, layout.Text);
        (kind == WheelPuzzleKind.Bonus ? _bank.Bonus : _bank.Regular).Add(puzzle);
        return WheelOpResult.Success(puzzle.Id, WheelContentRules.Warnings(kind, category, layout.Text));
    }

    private WheelOpResult TryAddQuick(string? rawTheme, IReadOnlyList<string?> rawTexts)
    {
        var theme = WheelContentRules.CleanCategory(rawTheme);
        if (theme.Length == 0) return WheelOpResult.Fail("Give the quick round a theme.");
        if (rawTexts.Count != 3) return WheelOpResult.Fail("A quick round has exactly three puzzles.");

        var texts = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            if (!WheelBoard.TryLayout(rawTexts[i], out var layout, out var problem)) return WheelOpResult.Fail($"Puzzle {i + 1}: {problem}");
            texts.Add(layout.Text);
        }
        if (texts.Distinct().Count() < 3) return WheelOpResult.Fail("The three puzzles must be different.");
        var existing = AllTexts();
        if (texts.Any(existing.Contains)) return WheelOpResult.Fail("One of those puzzles is already in the bank.", duplicate: true);

        var set = new WheelQuickSet(NewId(), theme, texts);
        _bank.Quick.Add(set);
        return WheelOpResult.Success(set.Id, texts.SelectMany(t => WheelContentRules.Warnings(WheelPuzzleKind.Quick, theme, t)).ToList());
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];

    private void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_bank, Json));
        File.Move(temp, _path, overwrite: true);
    }

    private void SeedStarters()
    {
        var assembly = typeof(FolderWheelContent).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".wheel-starter.json", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        var starter = JsonSerializer.Deserialize<RawStarter>(reader.ReadToEnd(), Json) ?? new RawStarter(null, null, null);

        foreach (var p in starter.Regular ?? []) TryAddSingle(WheelPuzzleKind.Regular, p.Category, p.Text);
        foreach (var p in starter.Bonus ?? []) TryAddSingle(WheelPuzzleKind.Bonus, p.Category, p.Text);
        foreach (var q in starter.Quick ?? []) TryAddQuick(q.Theme, q.Texts.Cast<string?>().ToList());
    }
}

/// <summary>A puzzle bank for tests: the regular puzzles you give it, in order, plus a fixed quick set and bonus choices.</summary>
public sealed class FixedWheelContent : IWheelContent
{
    private readonly List<WheelPuzzle> _regular;

    public FixedWheelContent(IEnumerable<string>? regularTexts = null)
    {
        var texts = regularTexts ?? ["BREAK THE ICE", "A PIECE OF CAKE", "FRESH FALLEN SNOW", "THE GRAND CANYON", "HOMEMADE APPLE PIE", "A QUIET LAKE CABIN", "SHERLOCK HOLMES", "BETTER LATE THAN NEVER"];
        _regular = texts.Select((t, i) => new WheelPuzzle($"r{i}", "Phrase", t)).ToList();
    }

    public List<string> Used { get; } = [];

    public WheelCounts Counts() => new(_regular.Count, _regular.Count, 0, 0, 0, 0, 0);

    public bool TryCreateDeal(Random rng, out WheelDeal deal, out string error)
    {
        deal = new WheelDeal([], [], []);
        error = "";
        if (_regular.Count < WheelRules.RegularPuzzles)
        {
            error = "Not enough puzzles.";
            return false;
        }
        deal = new WheelDeal(_regular.Take(WheelRules.RegularPuzzles).ToList(), [], []);
        return true;
    }

    public void MarkUsed(IEnumerable<string> ids) => Used.AddRange(ids);
}
