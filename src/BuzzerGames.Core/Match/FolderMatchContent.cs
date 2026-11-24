using System.Text.Json;

namespace BuzzerGames.Core.Match;

public sealed record MatchStoredQuestion(string Id, string Text);
public sealed record MatchStoredSuper(string Id, string Text, IReadOnlyList<IReadOnlyList<string>> Answers);
public sealed record MatchBankCounts(int Questions, int Supers, int Names);
public sealed record MatchOpResult(bool Ok, string? Error = null)
{
    public static MatchOpResult Success { get; } = new(true);
    public static MatchOpResult Fail(string error) => new(false, error);
}
public sealed record MatchImportReport(int Added, int Duplicates, IReadOnlyList<string> Problems);

/// <summary>
/// The Match Game bank, kept in <c>match/bank.json</c> in the app's data folder so questions added on the questions page survive a restart.
/// The first time it runs it is filled with the starter family names and Super Match blanks; there are no starter questions.
/// </summary>
public sealed class FolderMatchContent : IMatchContent
{
    public const int QuestionMaxLength = 220;
    public const int NameMaxLength = 20;
    public const int AnswerMaxLength = 40;
    public const int MaxNames = 60;

    private sealed class StoredQuestion { public string Id { get; set; } = ""; public string Text { get; set; } = ""; }
    private sealed class StoredSuper { public string Id { get; set; } = ""; public string Text { get; set; } = ""; public List<List<string>> Answers { get; set; } = []; }
    private sealed class Bank
    {
        public List<string> Names { get; set; } = [];
        public List<StoredQuestion> Questions { get; set; } = [];
        public List<StoredSuper> Supers { get; set; } = [];
    }

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

    public FolderMatchContent(string dataDirectory, bool seedStarters = true)
    {
        var folder = Path.Combine(Path.GetFullPath(dataDirectory), "match");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "bank.json");

        if (File.Exists(_path))
        {
            try
            {
                _bank = JsonSerializer.Deserialize<Bank>(File.ReadAllText(_path), Json) ?? new Bank();
                return;
            }
            catch (JsonException)
            {
                File.Move(_path, _path + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            }
        }

        if (seedStarters)
        {
            var starter = new JsonMatchContent();
            _bank.Names = [.. starter.Names];
            _bank.Questions = starter.Questions.Select(q => new StoredQuestion { Id = NewId(), Text = q.Text }).ToList();
            _bank.Supers = starter.Supers.Select(x => new StoredSuper { Id = NewId(), Text = x.Text, Answers = x.Answers.Select(a => a.ToList()).ToList() }).ToList();
        }
        Save();
    }

    public string FilePath => _path;

    // ---- IMatchContent -------------------------------------------------------------------

    public IReadOnlyList<string> Names { get { lock (_gate) return [.. _bank.Names]; } }
    public int QuestionCount { get { lock (_gate) return _bank.Questions.Count; } }
    public int SuperCount { get { lock (_gate) return _bank.Supers.Count; } }

    public IReadOnlyList<MatchQuestion> CreateQuestions(Random rng, int count)
    {
        lock (_gate) return Deal(_bank.Questions.Select(q => new MatchQuestion(q.Text)).ToList(), rng, count, "questions");
    }

    public IReadOnlyList<SuperQuestion> CreateSupers(Random rng, int count)
    {
        lock (_gate) return Deal(_bank.Supers.Select(ToSuper).ToList(), rng, count, "Super Match blanks");
    }

    private static SuperQuestion ToSuper(StoredSuper s) =>
        new(s.Text, s.Answers.Select(a => (IReadOnlyList<string>)[.. a]).ToList());

    /// <summary>A shuffled deck; with fewer than <paramref name="count"/> in the bank it is reshuffled and dealt again, so a small bank still plays.</summary>
    private static IReadOnlyList<T> Deal<T>(IReadOnlyList<T> bank, Random rng, int count, string what)
    {
        if (bank.Count == 0) throw new InvalidOperationException($"The Match Game bank has no {what}.");
        var deck = bank.OrderBy(_ => rng.Next()).ToList();
        while (deck.Count < count) deck.AddRange(bank.OrderBy(_ => rng.Next()));
        return deck.Take(count).ToList();
    }

    // ---- Reading for the questions page --------------------------------------------------

    public MatchBankCounts Counts()
    {
        lock (_gate) return new(_bank.Questions.Count, _bank.Supers.Count, _bank.Names.Count);
    }

    public IReadOnlyList<MatchStoredQuestion> Questions()
    {
        lock (_gate) return _bank.Questions.Select(q => new MatchStoredQuestion(q.Id, q.Text)).ToList();
    }

    public IReadOnlyList<MatchStoredSuper> Supers()
    {
        lock (_gate) return _bank.Supers.Select(s => new MatchStoredSuper(s.Id, s.Text, s.Answers.Select(a => (IReadOnlyList<string>)[.. a]).ToList())).ToList();
    }

    // ---- Questions -----------------------------------------------------------------------

    /// <summary>A fill-in-the-blank for the panel. It needs a blank (___); "{name}" is replaced with the next family name, or write a name straight in.</summary>
    public MatchOpResult AddQuestion(string? text)
    {
        lock (_gate)
        {
            var result = TryAddQuestion(text);
            if (result.Ok) Save();
            return result;
        }
    }

    public MatchImportReport ImportQuestions(string? text)
    {
        lock (_gate)
        {
            int added = 0, dups = 0;
            var problems = new List<string>();
            var number = 0;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                number++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var result = TryAddQuestion(line);
                if (result.Ok) added++;
                else if (result.Error == Duplicate) dups++;
                else problems.Add($"Line {number}: {result.Error}");
            }
            if (added > 0) Save();
            return new(added, dups, problems);
        }
    }

    private const string Duplicate = "That question is already in the bank.";

    private MatchOpResult TryAddQuestion(string? text)
    {
        var clean = Clean(text);
        if (clean.Length == 0) return MatchOpResult.Fail("Type a question.");
        if (clean.Length > QuestionMaxLength) return MatchOpResult.Fail($"Questions are at most {QuestionMaxLength} characters.");
        if (!clean.Contains("___")) return MatchOpResult.Fail("It needs a blank for the panel to fill in: write ___ (three underscores).");
        if (_bank.Questions.Any(q => string.Equals(q.Text, clean, StringComparison.OrdinalIgnoreCase))) return MatchOpResult.Fail(Duplicate);
        _bank.Questions.Add(new StoredQuestion { Id = NewId(), Text = clean });
        return MatchOpResult.Success;
    }

    public MatchOpResult DeleteQuestion(string id)
    {
        lock (_gate)
        {
            if (_bank.Questions.RemoveAll(q => q.Id == id) == 0) return MatchOpResult.Fail("That question isn't in the bank.");
            Save();
            return MatchOpResult.Success;
        }
    }

    // ---- Names ---------------------------------------------------------------------------

    /// <summary>Replaces the family names ("Donald", "Dolly"...). Separate them with commas or new lines.</summary>
    public MatchOpResult SetNames(string? text)
    {
        var names = (text ?? "").Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Clean).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) return MatchOpResult.Fail("Give at least one name.");
        if (names.Any(n => n.Length > NameMaxLength)) return MatchOpResult.Fail($"Names are at most {NameMaxLength} characters.");
        if (names.Count > MaxNames) return MatchOpResult.Fail($"At most {MaxNames} names.");
        lock (_gate)
        {
            _bank.Names = names;
            Save();
            return MatchOpResult.Success;
        }
    }

    // ---- Super Match blanks --------------------------------------------------------------

    /// <summary>A "Super ___" blank with its three most popular answers, best first. Each answer is a list of spellings that count; the first is shown.</summary>
    public MatchOpResult AddSuper(string? text, IReadOnlyList<IReadOnlyList<string>> answers)
    {
        var clean = Clean(text);
        if (clean.Length == 0) return MatchOpResult.Fail("Type the blank, like \"Super ___\".");
        if (!clean.Contains("___")) return MatchOpResult.Fail("The blank needs ___ (three underscores) where the answer goes.");
        if (clean.Length > QuestionMaxLength) return MatchOpResult.Fail($"At most {QuestionMaxLength} characters.");
        if (answers.Count != 3) return MatchOpResult.Fail("A Super Match blank needs exactly three answers.");
        var cleaned = answers.Select(a => a.Select(Clean).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList()).ToList();
        if (cleaned.Any(a => a.Count == 0)) return MatchOpResult.Fail("Fill in all three answers.");
        if (cleaned.Any(a => a.Any(x => x.Length > AnswerMaxLength))) return MatchOpResult.Fail($"Answers are at most {AnswerMaxLength} characters.");
        lock (_gate)
        {
            if (_bank.Supers.Any(s => string.Equals(s.Text, clean, StringComparison.OrdinalIgnoreCase))) return MatchOpResult.Fail("That blank is already in the bank.");
            _bank.Supers.Add(new StoredSuper { Id = NewId(), Text = clean, Answers = cleaned });
            Save();
            return MatchOpResult.Success;
        }
    }

    public MatchOpResult DeleteSuper(string id)
    {
        lock (_gate)
        {
            if (_bank.Supers.RemoveAll(s => s.Id == id) == 0) return MatchOpResult.Fail("That blank isn't in the bank.");
            Save();
            return MatchOpResult.Success;
        }
    }

    // ---- Plumbing ------------------------------------------------------------------------

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];

    private static string Clean(string? raw)
    {
        var chars = (raw ?? "").Where(c => !char.IsControl(c)).ToArray();
        return string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_bank, Json));
        File.Move(temp, _path, overwrite: true);
    }
}
