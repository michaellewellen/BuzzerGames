using System.Text.Json;
using System.Text.RegularExpressions;

namespace BuzzerGames.Core.Pyramid;

/// <summary>
/// A main-game topic: a title shown on the pyramid ("Press Your Luck"), what the emcee says out loud ("Name these things that you press"),
/// and seven words, in the order they will appear. The title is a tease and need not match the description exactly.
/// </summary>
public sealed record PyramidTopic(string Id, string Title, string Description, IReadOnlyList<string> Words);

/// <summary>A bonus-round topic. The clue giver sees the subject ("Things a toaster might say") and lists examples; the guesser names the subject. Tier 1 is easiest, tier 6 hardest.</summary>
public sealed record PyramidBonusTopic(string Id, int Tier, string Subject);

/// <summary>The words for one tiebreaker letter. Any seven of them are dealt.</summary>
public sealed record PyramidLetterList(char Letter, IReadOnlyList<string> Words);

public sealed record PyramidCounts(int Main, int MainUnused, int Bonus, int BonusUnused, IReadOnlyList<int> BonusPerTier, int LettersReady);

public readonly record struct PyramidOpResult(bool Ok, string? Error = null, string? Id = null, bool Duplicate = false, IReadOnlyList<string>? Warnings = null)
{
    public static PyramidOpResult Success(string? id = null, IReadOnlyList<string>? warnings = null) => new(true, null, id, false, warnings);
    public static PyramidOpResult Fail(string error, bool duplicate = false) => new(false, error, null, duplicate);
}

public sealed record PyramidImportReport(int Added, int Duplicates, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings);

public interface IPyramidContent
{
    PyramidCounts Counts();

    /// <summary>Six topics for one game, fresh ones first. Fails only if the bank has fewer than six.</summary>
    bool TryCreateMain(Random rng, out IReadOnlyList<PyramidTopic> topics, out string error);

    /// <summary>Six bonus topics, one from each difficulty tier (fresh first), easiest to hardest. Fails if a tier is empty.</summary>
    bool TryCreateBonus(Random rng, out IReadOnlyList<PyramidBonusTopic> topics, out string error);

    /// <summary>Every tiebreaker letter that has its words.</summary>
    IReadOnlyList<PyramidLetterList> Letters();

    void MarkUsed(IEnumerable<string> ids);
}

/// <summary>What the admin page checks. Warnings are for a human to judge and never block anything.</summary>
public static class PyramidContentRules
{
    private static readonly string[] PartyRuleWords =
        ["BEER", "WINE", "VODKA", "WHISKEY", "WHISKY", "TEQUILA", "COCKTAIL", "MARTINI", "CHAMPAGNE", "COFFEE", "LATTE", "ESPRESSO", "CAPPUCCINO"];

    /// <summary>
    /// Collapses spaces, drops control characters and the separators the import box uses, and trims to a maximum length.
    /// Commas are kept in titles and descriptions ("Lights, Camera, Action") but dropped from single words, where the import box uses them to split a list.
    /// </summary>
    public static string Clean(string? raw, int max, bool keepCommas = false)
    {
        var chars = (raw ?? "").Where(c => !char.IsControl(c) && c != '|' && c != '#' && (keepCommas || c != ',')).ToArray();
        var line = string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length > max ? line[..max].Trim() : line;
    }

    public static IReadOnlyList<string> Warnings(params string[] texts)
    {
        var warnings = new List<string>();
        var haystack = string.Join(" ", texts).ToUpperInvariant();
        foreach (var word in PartyRuleWords)
            if (Regex.IsMatch(haystack, @"\b" + word + @"S?\b")) warnings.Add($"Mentions {word.ToLowerInvariant()}: this party's rules say no alcohol or coffee content.");
        return warnings;
    }

    /// <summary>The first letter of a word, upper-cased, ignoring anything that isn't a letter at the start ("X-ray" is X).</summary>
    public static char? FirstLetter(string word)
    {
        foreach (var c in word)
            if (char.IsLetter(c)) return char.ToUpperInvariant(c);
        return null;
    }
}

/// <summary>
/// The bank, kept in one file (<c>pyramid/topics.json</c>) inside the data folder. On Railway that folder is the Volume, so topics survive a
/// redeploy. The first time it runs it fills the file with a starter set so there is something to play; after that the file is yours.
/// </summary>
public sealed class FolderPyramidContent : IPyramidContent
{
    private sealed class Bank
    {
        public List<PyramidTopic> Main { get; set; } = [];
        public List<PyramidBonusTopic> Bonus { get; set; } = [];
        public Dictionary<string, List<string>> Letters { get; set; } = [];
        public List<string> Used { get; set; } = [];
    }

    private sealed record RawMain(string Title, string Description, List<string> Words);
    private sealed record RawBonus(int Tier, string Subject);
    private sealed record RawStarter(List<RawMain>? Main, List<RawBonus>? Bonus, Dictionary<string, List<string>>? Letters);

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

    public FolderPyramidContent(string dataDirectory, bool seedStarters = true)
    {
        var folder = Path.Combine(Path.GetFullPath(dataDirectory), "pyramid");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "topics.json");

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

    public IReadOnlyList<PyramidTopic> Main() { lock (_gate) return [.. _bank.Main]; }
    public IReadOnlyList<PyramidBonusTopic> Bonus() { lock (_gate) return [.. _bank.Bonus.OrderBy(b => b.Tier)]; }
    public bool IsUsed(string id) { lock (_gate) return _bank.Used.Contains(id); }

    public IReadOnlyList<PyramidLetterList> Letters()
    {
        lock (_gate)
            return _bank.Letters
                .Where(kv => kv.Key.Length == 1 && kv.Value.Count >= PyramidRules.WordsPerTopic)
                .OrderBy(kv => kv.Key)
                .Select(kv => new PyramidLetterList(kv.Key[0], [.. kv.Value]))
                .ToList();
    }

    /// <summary>The words stored for a letter, ready for the admin page's edit box (empty if there are none yet).</summary>
    public IReadOnlyList<string> LetterWords(char letter)
    {
        lock (_gate) return _bank.Letters.TryGetValue(char.ToUpperInvariant(letter).ToString(), out var words) ? [.. words] : [];
    }

    public PyramidCounts Counts()
    {
        lock (_gate)
        {
            var used = _bank.Used.ToHashSet();
            var perTier = Enumerable.Range(1, PyramidRules.TileCount).Select(t => _bank.Bonus.Count(b => b.Tier == t)).ToList();
            return new PyramidCounts(
                _bank.Main.Count, _bank.Main.Count(t => !used.Contains(t.Id)),
                _bank.Bonus.Count, _bank.Bonus.Count(b => !used.Contains(b.Id)),
                perTier,
                _bank.Letters.Count(kv => kv.Key.Length == 1 && kv.Value.Count >= PyramidRules.WordsPerTopic));
        }
    }

    // ---- Adding and removing -------------------------------------------------------------

    public PyramidOpResult AddMain(string? title, string? description, IReadOnlyList<string?> words) => Locked(() => Persist(TryAddMain(title, description, words)));

    public PyramidOpResult AddBonus(int tier, string? subject) => Locked(() => Persist(TryAddBonus(tier, subject)));

    /// <summary>Replaces the words for one letter. Every word must start with that letter, and there must be at least seven.</summary>
    public PyramidOpResult SetLetter(char letter, IReadOnlyList<string?> words) => Locked(() => Persist(TrySetLetter(letter, words)));

    public PyramidOpResult Delete(string id) => Locked(() =>
    {
        var removed = _bank.Main.RemoveAll(t => t.Id == id) + _bank.Bonus.RemoveAll(b => b.Id == id);
        if (removed == 0) return PyramidOpResult.Fail("That one is already gone.");
        _bank.Used.Remove(id);
        Save();
        return PyramidOpResult.Success();
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
    /// Adds many at once from pasted text. A line starting with <c># main</c>, <c># bonus</c> or <c># letters</c> switches the kind (it starts as main);
    /// any other line starting with # is a comment. Main: <c>title | description | word, word, word, word, word, word, word</c>.
    /// Bonus: <c>tier | subject</c> (tier 1 to 6). Letters: <c>A | word, word, ...</c>. Lines that can't be used are reported, not fatal.
    /// </summary>
    public PyramidImportReport Import(string? pasted)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        var added = 0;
        var duplicates = 0;
        var kind = "main";
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
                    if (body.StartsWith("main")) kind = "main";
                    else if (body.StartsWith("bonus")) kind = "bonus";
                    else if (body.StartsWith("letter")) kind = "letters";
                    continue;
                }

                var parts = line.Split('|', StringSplitOptions.TrimEntries);
                PyramidOpResult result;
                if (kind == "main")
                {
                    result = parts.Length == 3
                        ? TryAddMain(parts[0], parts[1], parts[2].Split(',', StringSplitOptions.TrimEntries).Cast<string?>().ToList())
                        : PyramidOpResult.Fail("A main line needs three parts: title | description | seven words separated by commas");
                }
                else if (kind == "bonus")
                {
                    result = parts.Length == 2 && int.TryParse(parts[0], out var tier)
                        ? TryAddBonus(tier, parts[1])
                        : PyramidOpResult.Fail("A bonus line needs a tier number and a subject: 3 | Things with wheels");
                }
                else
                {
                    result = parts.Length == 2 && parts[0].Length == 1
                        ? TrySetLetter(parts[0][0], parts[1].Split(',', StringSplitOptions.TrimEntries).Cast<string?>().ToList())
                        : PyramidOpResult.Fail("A letters line needs one letter and its words: A | Apple, Anchor, ...");
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
        return new PyramidImportReport(added, duplicates, problems, warnings);
    }

    // ---- Dealing -------------------------------------------------------------------------

    public bool TryCreateMain(Random rng, out IReadOnlyList<PyramidTopic> topics, out string error)
    {
        lock (_gate)
        {
            topics = [];
            error = "";
            var used = _bank.Used.ToHashSet();
            var picked = new List<PyramidTopic>();

            // Fresh topics first, then used ones; never the same title twice in one game.
            foreach (var t in Shuffled(_bank.Main.Where(t => !used.Contains(t.Id)), rng).Concat(Shuffled(_bank.Main.Where(t => used.Contains(t.Id)), rng)))
            {
                if (picked.Count == PyramidRules.TileCount) break;
                if (picked.Any(p => string.Equals(p.Title, t.Title, StringComparison.OrdinalIgnoreCase))) continue;
                picked.Add(t);
            }

            if (picked.Count < PyramidRules.TileCount)
            {
                error = $"The bank has {_bank.Main.Count} topics; a game needs {PyramidRules.TileCount}. Add some on the topics page.";
                return false;
            }
            topics = picked;
            return true;
        }
    }

    public bool TryCreateBonus(Random rng, out IReadOnlyList<PyramidBonusTopic> topics, out string error)
    {
        lock (_gate)
        {
            topics = [];
            error = "";
            var used = _bank.Used.ToHashSet();
            var picked = new List<PyramidBonusTopic>();
            for (var tier = 1; tier <= PyramidRules.TileCount; tier++)
            {
                var inTier = _bank.Bonus.Where(b => b.Tier == tier).ToList();
                var choice = Shuffled(inTier.Where(b => !used.Contains(b.Id)), rng).Concat(Shuffled(inTier.Where(b => used.Contains(b.Id)), rng)).FirstOrDefault();
                if (choice is null)
                {
                    error = $"The bonus round needs a topic in every difficulty tier, and tier {tier} is empty. Add one on the topics page.";
                    return false;
                }
                picked.Add(choice);
            }
            topics = picked;
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

    private T Locked<T>(Func<T> action)
    {
        lock (_gate) return action();
    }

    private PyramidOpResult Persist(PyramidOpResult result)
    {
        if (result.Ok) Save();
        return result;
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];

    private PyramidOpResult TryAddMain(string? rawTitle, string? rawDescription, IReadOnlyList<string?> rawWords)
    {
        var title = PyramidContentRules.Clean(rawTitle, PyramidRules.TitleMaxLength, keepCommas: true);
        var description = PyramidContentRules.Clean(rawDescription, PyramidRules.DescriptionMaxLength, keepCommas: true);
        if (title.Length == 0) return PyramidOpResult.Fail("Give the topic a title.");
        if (description.Length == 0) return PyramidOpResult.Fail("Give the topic a description for the emcee to read (\"Name these things that you press\").");

        var words = rawWords.Select(w => PyramidContentRules.Clean(w, PyramidRules.WordMaxLength)).Where(w => w.Length > 0).ToList();
        if (words.Count != PyramidRules.WordsPerTopic) return PyramidOpResult.Fail($"A topic has exactly {PyramidRules.WordsPerTopic} words; this one has {words.Count}.");
        if (words.Select(w => w.ToUpperInvariant()).Distinct().Count() != words.Count) return PyramidOpResult.Fail("A word appears twice in this topic.");
        if (_bank.Main.Any(t => string.Equals(t.Title, title, StringComparison.OrdinalIgnoreCase))) return PyramidOpResult.Fail("A topic with that title is already in the bank.", duplicate: true);

        var topic = new PyramidTopic(NewId(), title, description, words);
        _bank.Main.Add(topic);
        return PyramidOpResult.Success(topic.Id, PyramidContentRules.Warnings([title, description, .. words]));
    }

    private PyramidOpResult TryAddBonus(int tier, string? rawSubject)
    {
        if (tier is < 1 or > PyramidRules.TileCount) return PyramidOpResult.Fail($"The difficulty tier is a number from 1 (easiest) to {PyramidRules.TileCount} (hardest).");
        var subject = PyramidContentRules.Clean(rawSubject, PyramidRules.SubjectMaxLength, keepCommas: true);
        if (subject.Length == 0) return PyramidOpResult.Fail("Give it a subject, like \"Things a toaster might say\".");
        if (_bank.Bonus.Any(b => string.Equals(b.Subject, subject, StringComparison.OrdinalIgnoreCase))) return PyramidOpResult.Fail("That subject is already in the bank.", duplicate: true);

        var topic = new PyramidBonusTopic(NewId(), tier, subject);
        _bank.Bonus.Add(topic);
        return PyramidOpResult.Success(topic.Id, PyramidContentRules.Warnings(subject));
    }

    private PyramidOpResult TrySetLetter(char letter, IReadOnlyList<string?> rawWords)
    {
        var key = char.ToUpperInvariant(letter);
        if (key is < 'A' or > 'Z') return PyramidOpResult.Fail("Letters A to Z only.");

        var words = rawWords.Select(w => PyramidContentRules.Clean(w, PyramidRules.WordMaxLength)).Where(w => w.Length > 0).ToList();
        if (words.Count < PyramidRules.WordsPerTopic) return PyramidOpResult.Fail($"{key} needs at least {PyramidRules.WordsPerTopic} words; this has {words.Count}.");
        var wrong = words.FirstOrDefault(w => PyramidContentRules.FirstLetter(w) != key);
        if (wrong is not null) return PyramidOpResult.Fail($"\"{wrong}\" doesn't start with {key}.");
        if (words.Select(w => w.ToUpperInvariant()).Distinct().Count() != words.Count) return PyramidOpResult.Fail("A word appears twice in this list.");

        _bank.Letters[key.ToString()] = words;
        return PyramidOpResult.Success(key.ToString(), PyramidContentRules.Warnings([.. words]));
    }

    private void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_bank, Json));
        File.Move(temp, _path, overwrite: true);
    }

    private void SeedStarters()
    {
        var assembly = typeof(FolderPyramidContent).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".pyramid-starter.json", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        var starter = JsonSerializer.Deserialize<RawStarter>(reader.ReadToEnd(), Json) ?? new RawStarter(null, null, null);

        foreach (var t in starter.Main ?? []) TryAddMain(t.Title, t.Description, t.Words.Cast<string?>().ToList());
        foreach (var b in starter.Bonus ?? []) TryAddBonus(b.Tier, b.Subject);
        foreach (var (key, words) in starter.Letters ?? []) TrySetLetter(key.Length > 0 ? key[0] : ' ', words.Cast<string?>().ToList());
    }
}

/// <summary>A bank for tests: topics named Topic 1, Topic 2... whose words are "T1-1" to "T1-7", so a check can say exactly what the TV should show.</summary>
public sealed class FixedPyramidContent : IPyramidContent
{
    private readonly List<PyramidTopic> _main;
    private readonly List<PyramidBonusTopic> _bonus;
    private readonly List<PyramidLetterList> _letters;

    public FixedPyramidContent(int mainTopics = 12, int lettersCount = 26)
    {
        _main = Enumerable.Range(1, mainTopics)
            .Select(i => new PyramidTopic($"m{i}", $"Topic {i}", $"Name these things from topic {i}", Enumerable.Range(1, PyramidRules.WordsPerTopic).Select(w => $"T{i}-{w}").ToList()))
            .ToList();
        _bonus = Enumerable.Range(1, PyramidRules.TileCount).Select(t => new PyramidBonusTopic($"b{t}", t, $"Bonus subject {t}")).ToList();
        _letters = Enumerable.Range(0, lettersCount)
            .Select(i => (char)('A' + i))
            .Select(c => new PyramidLetterList(c, Enumerable.Range(1, PyramidRules.WordsPerTopic).Select(w => $"{c}word{w}").ToList()))
            .ToList();
    }

    public List<string> Used { get; } = [];

    public PyramidCounts Counts() => new(_main.Count, _main.Count, _bonus.Count, _bonus.Count, Enumerable.Repeat(1, PyramidRules.TileCount).ToList(), _letters.Count);

    public bool TryCreateMain(Random rng, out IReadOnlyList<PyramidTopic> topics, out string error)
    {
        topics = [];
        error = "";
        if (_main.Count < PyramidRules.TileCount)
        {
            error = "Not enough topics.";
            return false;
        }
        // Rotate through the bank so back-to-back games in a check get different topics.
        var fresh = _main.Where(t => !Used.Contains(t.Id)).ToList();
        if (fresh.Count < PyramidRules.TileCount) fresh = _main;
        topics = fresh.Take(PyramidRules.TileCount).ToList();
        return true;
    }

    public bool TryCreateBonus(Random rng, out IReadOnlyList<PyramidBonusTopic> topics, out string error)
    {
        topics = _bonus;
        error = "";
        return true;
    }

    public IReadOnlyList<PyramidLetterList> Letters() => _letters;

    public void MarkUsed(IEnumerable<string> ids) => Used.AddRange(ids);
}
