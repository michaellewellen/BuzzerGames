using System.Text.Json;
using System.Text.RegularExpressions;
using BuzzerGames.Core.Pyramid;

namespace BuzzerGames.Core.Feud;

/// <summary>One survey answer and how many of the hundred people said it.</summary>
public sealed record FeudAnswer(string Text, int Points);

/// <summary>A survey question with its answers, best first. Three to eight answers.</summary>
public sealed record FeudBoard(string Id, string Question, IReadOnlyList<FeudAnswer> Answers);

public sealed record FeudCounts(int Total, int Fresh);

public readonly record struct FeudOpResult(bool Ok, string? Error = null, string? Id = null, bool Duplicate = false, IReadOnlyList<string>? Warnings = null)
{
    public static FeudOpResult Success(string? id = null, IReadOnlyList<string>? warnings = null) => new(true, null, id, false, warnings);
    public static FeudOpResult Fail(string error, bool duplicate = false) => new(false, error, null, duplicate);
}

public sealed record FeudImportReport(int Added, int Duplicates, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings);

public interface IFeudContent
{
    FeudCounts Counts();

    /// <summary>Up to <paramref name="count"/> boards for one game, fresh ones first. Fails only if the bank has fewer than a game needs.</summary>
    bool TryDeal(Random rng, int count, out IReadOnlyList<FeudBoard> boards, out string error);

    void MarkUsed(IEnumerable<string> ids);
}

/// <summary>Cleaning, matching and the import format.</summary>
public static class FeudContentRules
{
    /// <summary>Lower-case, letters and digits only, no leading "a/an/the" and a plural "s" dropped, so "The Dogs" matches "dog".</summary>
    public static string Normalize(string? text)
    {
        var chars = (text ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        var words = new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && words[0] is "a" or "an" or "the") words.RemoveAt(0);
        var joined = string.Join(' ', words);
        return joined.Length > 3 && joined.EndsWith('s') && !joined.EndsWith("ss") ? joined[..^1] : joined;
    }

    /// <summary>One line: <c>Question | Answer: 38 | Answer: 25 | ...</c> (a colon, an equals sign or brackets before the number all work).</summary>
    public static bool TryParseLine(string line, out string question, out List<(string Text, int Points)> answers, out string error)
    {
        question = "";
        answers = [];
        error = "";
        var parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) { error = "Use: question | answer: points | answer: points ..."; return false; }
        question = parts[0];
        foreach (var part in parts.Skip(1))
        {
            var m = Regex.Match(part, @"^(?<a>.+?)\s*(?::|=|\()\s*(?<p>\d{1,3})\s*\)?$");
            if (!m.Success) { error = $"\"{part}\" needs its points after a colon, like \"Mustard: 34\"."; return false; }
            answers.Add((m.Groups["a"].Value, int.Parse(m.Groups["p"].Value)));
        }
        return true;
    }
}

/// <summary>
/// The bank, kept in one file (<c>feud/boards.json</c>) inside the data folder. On Railway that folder is the Volume, so questions survive a
/// redeploy. The first run fills it with a starter set; after that the file is yours.
/// </summary>
public sealed class FolderFeudContent : IFeudContent
{
    private sealed class Bank
    {
        public List<FeudBoard> Boards { get; set; } = [];
        public List<string> Used { get; set; } = [];
    }

    private sealed record RawBoard(string Question, List<FeudAnswer> Answers);
    private sealed record RawStarter(List<RawBoard>? Boards);

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

    public FolderFeudContent(string dataDirectory, bool seedStarters = true)
    {
        var folder = Path.Combine(Path.GetFullPath(dataDirectory), "feud");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "boards.json");

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

        if (seedStarters) SeedStarters();
        Save();
    }

    public string FilePath => _path;

    // ---- Reading -------------------------------------------------------------------------

    public IReadOnlyList<FeudBoard> Boards() { lock (_gate) return [.. _bank.Boards]; }
    public bool IsUsed(string id) { lock (_gate) return _bank.Used.Contains(id); }

    public FeudCounts Counts()
    {
        lock (_gate)
        {
            var used = _bank.Used.ToHashSet();
            return new FeudCounts(_bank.Boards.Count, _bank.Boards.Count(b => !used.Contains(b.Id)));
        }
    }

    // ---- Adding and removing -------------------------------------------------------------

    public FeudOpResult Add(string? question, IReadOnlyList<(string? Text, int Points)> answers) => Locked(() => Persist(TryAdd(question, answers)));

    public FeudOpResult Delete(string id) => Locked(() =>
    {
        if (_bank.Boards.RemoveAll(b => b.Id == id) == 0) return FeudOpResult.Fail("That question isn't in the bank any more.");
        _bank.Used.Remove(id);
        Save();
        return FeudOpResult.Success();
    });

    public void ClearUsed()
    {
        lock (_gate)
        {
            _bank.Used.Clear();
            Save();
        }
    }

    /// <summary>Adds many at once, one per line: <c>Question | Answer: 38 | Answer: 25</c>. Lines starting with # are ignored.</summary>
    public FeudImportReport Import(string? pasted)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        int added = 0, duplicates = 0, lineNumber = 0;
        lock (_gate)
        {
            foreach (var raw in (pasted ?? "").Split('\n'))
            {
                lineNumber++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                if (!FeudContentRules.TryParseLine(line, out var question, out var answers, out var parseError))
                {
                    problems.Add($"Line {lineNumber}: {parseError}");
                    continue;
                }
                var result = TryAdd(question, answers.Select(a => ((string?)a.Text, a.Points)).ToList());
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
        return new FeudImportReport(added, duplicates, problems, warnings);
    }

    // ---- Dealing -------------------------------------------------------------------------

    public bool TryDeal(Random rng, int count, out IReadOnlyList<FeudBoard> boards, out string error)
    {
        lock (_gate)
        {
            error = "";
            var used = _bank.Used.ToHashSet();
            var picked = Shuffled(_bank.Boards.Where(b => !used.Contains(b.Id)), rng)
                .Concat(Shuffled(_bank.Boards.Where(b => used.Contains(b.Id)), rng))
                .Take(count)
                .ToList();
            boards = picked;
            if (picked.Count < FeudRules.MinBoardsPerGame)
            {
                error = $"The bank has {_bank.Boards.Count} questions; a game needs at least {FeudRules.MinBoardsPerGame}. Add some on the questions page.";
                return false;
            }
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

    private FeudOpResult Persist(FeudOpResult result)
    {
        if (result.Ok) Save();
        return result;
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];

    private FeudOpResult TryAdd(string? rawQuestion, IReadOnlyList<(string? Text, int Points)> rawAnswers)
    {
        var question = PyramidContentRules.Clean(rawQuestion, FeudRules.QuestionMaxLength, keepCommas: true);
        if (question.Length == 0) return FeudOpResult.Fail("Give it a question.");

        var answers = rawAnswers
            .Select(a => (Text: PyramidContentRules.Clean(a.Text, FeudRules.AnswerMaxLength, keepCommas: true), a.Points))
            .Where(a => a.Text.Length > 0)
            .ToList();
        if (answers.Count < FeudRules.MinAnswers) return FeudOpResult.Fail($"A board needs at least {FeudRules.MinAnswers} answers; this has {answers.Count}.");
        if (answers.Count > FeudRules.MaxAnswers) return FeudOpResult.Fail($"A board has room for {FeudRules.MaxAnswers} answers; this has {answers.Count}.");
        var bad = answers.FirstOrDefault(a => a.Points is < 1 or > FeudRules.MaxPoints);
        if (bad.Text is not null) return FeudOpResult.Fail($"\"{bad.Text}\" is worth {bad.Points}: points run from 1 to {FeudRules.MaxPoints}.");
        if (answers.Select(a => FeudContentRules.Normalize(a.Text)).Distinct().Count() != answers.Count) return FeudOpResult.Fail("An answer appears twice on this board.");
        if (_bank.Boards.Any(b => string.Equals(b.Question, question, StringComparison.OrdinalIgnoreCase))) return FeudOpResult.Fail("That question is already in the bank.", duplicate: true);

        var sorted = answers.OrderByDescending(a => a.Points).Select(a => new FeudAnswer(a.Text, a.Points)).ToList();
        var board = new FeudBoard(NewId(), question, sorted);
        _bank.Boards.Add(board);

        var warnings = new List<string>(PyramidContentRules.Warnings([question, .. sorted.Select(a => a.Text)]));
        var total = sorted.Sum(a => a.Points);
        if (total > 100) warnings.Add($"The points add up to {total}; a survey of 100 people can't give more than 100.");
        return FeudOpResult.Success(board.Id, warnings);
    }

    private void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_bank, Json));
        File.Move(temp, _path, overwrite: true);
    }

    private void SeedStarters()
    {
        var assembly = typeof(FolderFeudContent).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".feud-starter.json", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        var starter = JsonSerializer.Deserialize<RawStarter>(reader.ReadToEnd(), Json) ?? new RawStarter(null);
        foreach (var b in starter.Boards ?? [])
            TryAdd(b.Question, b.Answers.Select(a => ((string?)a.Text, a.Points)).ToList());
    }
}

/// <summary>
/// A bank for tests. Question n is "Question n" with answers "Qn answer 1" to "Qn answer 6", worth 30, 25, 20, 12, 8, 5, so a check can say
/// exactly what a screen should (and should not) be showing.
/// </summary>
public sealed class FixedFeudContent : IFeudContent
{
    private readonly List<FeudBoard> _boards;

    public FixedFeudContent(int boards = 20, int answersPerBoard = 6)
    {
        int[] points = [30, 25, 20, 12, 8, 5, 3, 2];
        _boards = Enumerable.Range(1, boards)
            .Select(n => new FeudBoard($"q{n}", $"Question {n}",
                Enumerable.Range(1, answersPerBoard).Select(a => new FeudAnswer($"Q{n} answer {a}", points[a - 1])).ToList()))
            .ToList();
    }

    public List<string> Used { get; } = [];
    public IReadOnlyList<FeudBoard> Boards => _boards;

    public FeudCounts Counts() => new(_boards.Count, _boards.Count(b => !Used.Contains(b.Id)));

    public bool TryDeal(Random rng, int count, out IReadOnlyList<FeudBoard> boards, out string error)
    {
        // In order, so a check knows which board each round gets.
        boards = _boards.Take(count).ToList();
        error = boards.Count < FeudRules.MinBoardsPerGame ? "Not enough boards." : "";
        return error.Length == 0;
    }

    public void MarkUsed(IEnumerable<string> ids) => Used.AddRange(ids);
}
