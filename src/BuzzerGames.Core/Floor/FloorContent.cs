using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BuzzerGames.Core.Floor;

/// <summary>
/// One thing to name in a duel. It is either a picture (<see cref="ImageKey"/>) or a line of text (<see cref="Text"/>).
/// The picture is found by an opaque key rather than its file name, so the web address of a picture never gives the answer away.
/// </summary>
public sealed record FloorPrompt(string Topic, string Answer, IReadOnlyList<string> Alternates, string? Text, string? ImageKey)
{
    public bool IsImage => ImageKey is not null;
}

/// <summary>A topic and what is in it. <see cref="Used"/> means it was picked in an earlier game and stays dead until the emcee clears it.</summary>
public sealed record FloorTopicInfo(string Name, int Images, int Texts, bool Used)
{
    public int Total => Images + Texts;
}

/// <summary>A picture in a topic, for the topics page.</summary>
public sealed record FloorImageInfo(string File, string Answer, string Key);

/// <summary>The outcome of changing a topic on disk (create, upload, delete).</summary>
public readonly record struct FloorOpResult(bool Ok, string? Error = null, string? File = null)
{
    public static FloorOpResult Success(string? file = null) => new(true, null, file);
    public static FloorOpResult Fail(string error) => new(false, error);
}

/// <summary>A line of text to name: a clue and what it is.</summary>
public sealed record FloorTextPrompt(string Clue, string Answer, IReadOnlyList<string> Alternates);

/// <summary>Where The Floor's topics come from, and which of them have already been used up.</summary>
public interface IFloorContent
{
    /// <summary>A topic needs at least this many prompts before players can choose it.</summary>
    int MinPrompts { get; }

    /// <summary>Every topic there is, with its size and whether it is dead.</summary>
    IReadOnlyList<FloorTopicInfo> ListTopics();

    /// <summary>Everything in one topic, in no particular order.</summary>
    IReadOnlyList<FloorPrompt> LoadPrompts(string topic);

    /// <summary>A topic was picked by a player, so it can't be picked again until <see cref="ClearUsed"/>.</summary>
    void MarkUsed(string topic);

    /// <summary>Make every topic available again.</summary>
    void ClearUsed();
}

/// <summary>Small helpers for names, answers and the text prompt format. Pure functions, so they are easy to check.</summary>
public static class FloorText
{
    private static readonly char[] BadNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>A topic name that is safe to use as a folder name, or null if nothing usable is left.</summary>
    public static string? CleanTopicName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var chars = raw.Where(c => !char.IsControl(c) && Array.IndexOf(BadNameChars, c) < 0).ToArray();
        var name = string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.', ' ');
        if (name.Length > 40) name = name[..40].Trim().TrimEnd('.', ' ');
        if (name.Length == 0 || name.All(c => c == '.')) return null;
        return name;
    }

    /// <summary>A single line of text with control characters and the "|" separator taken out.</summary>
    public static string CleanLine(string? raw, int max)
    {
        var chars = (raw ?? "").Where(c => !char.IsControl(c) && c != '|' && c != '~').ToArray();
        var line = string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length > max ? line[..max].Trim() : line;
    }

    /// <summary>
    /// What a picture's file name says the answer is. "golden-retriever.jpg" is "Golden Retriever". A trailing "_2" or " (2)" only
    /// keeps two files with the same answer apart. "lord-of-the-rings~lotr.jpg" also accepts "lotr".
    /// </summary>
    public static (string Answer, IReadOnlyList<string> Alternates) AnswerFromFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        stem = Regex.Replace(stem, @"\s*\(\d{1,2}\)$", "");
        stem = Regex.Replace(stem, @"_\d{1,2}$", "");

        var pieces = stem.Split('~', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Tidy).Where(p => p.Length > 0).ToList();
        if (pieces.Count == 0) return (Tidy(stem).Length > 0 ? Tidy(stem) : fileName, []);
        return (pieces[0], pieces.Skip(1).ToList());
    }

    private static string Tidy(string piece)
    {
        var spaced = string.Join(' ', piece.Replace('_', ' ').Replace('-', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        // Lower-case file names look like file names; make them look like answers. Anything with a capital is left exactly as typed.
        if (spaced.Length > 0 && !spaced.Any(char.IsUpper))
            spaced = string.Join(' ', spaced.Split(' ').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        return spaced;
    }

    /// <summary>A file-name-safe version of an answer: "Star Wars: A New Hope" becomes "star-wars-a-new-hope".</summary>
    public static string Slugify(string answer)
    {
        var sb = new StringBuilder();
        foreach (var c in answer.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 50) slug = slug[..50].Trim('-');
        return slug.Length == 0 ? "picture" : slug;
    }

    /// <summary>
    /// Reads text prompts. One per line: <c>clue | answer | other accepted answers separated by ;</c>. Blank lines are skipped, and a line
    /// starting with # is a comment, except <c># Topic: Name</c>, which names the topic.
    /// </summary>
    public static IReadOnlyList<FloorTextPrompt> ParseTextPrompts(string text, out string? topicName, out int skipped)
    {
        topicName = null;
        skipped = 0;
        var result = new List<FloorTextPrompt>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#'))
            {
                var body = line.TrimStart('#').Trim();
                if (body.StartsWith("Topic:", StringComparison.OrdinalIgnoreCase)) topicName = CleanTopicName(body["Topic:".Length..]);
                continue;
            }

            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                skipped++;
                continue;
            }
            var alternates = parts.Length > 2
                ? parts[2].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [];
            result.Add(new FloorTextPrompt(parts[0], parts[1], alternates));
        }
        return result;
    }

    /// <summary>Which kind of picture these bytes are (by their first bytes, not by what the file is called), or null if it isn't one.</summary>
    public static string? DetectImageExtension(ReadOnlySpan<byte> b)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
        if (b.Length > 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return ".gif";
        return null;
    }

    public static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };
}

/// <summary>
/// Topics kept in a folder. Each topic is a sub-folder of <c>topics/</c> holding its pictures (the file name is the answer) and, optionally,
/// <c>prompts.txt</c> (text prompts) and <c>answers.txt</c> (answers that a file name can't spell, like "Star Wars: A New Hope"). Which topics
/// have been used up lives in <c>used-topics.json</c> beside it. On Railway the folder is the Volume, so nothing is lost on a redeploy.
/// A few text-only starter topics ship inside the program, so there is something to play before any pictures exist.
/// </summary>
public sealed class FolderFloorContent : IFloorContent
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private const string TextFile = "prompts.txt";
    private const string AnswersFile = "answers.txt";
    private const string UsedFile = "used-topics.json";
    private const int MaxImageBytes = 6 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly string _topicsDir;
    private readonly string _usedPath;
    private readonly Dictionary<string, IReadOnlyList<FloorTextPrompt>> _starters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Path, string ContentType)> _imageIndex = new();
    private IReadOnlyList<FloorTopicInfo>? _listCache;
    private DateTime _listCacheAt;
    private readonly Func<DateTime> _now;

    public FolderFloorContent(string dataDirectory, int minPrompts = 20, Func<DateTime>? clock = null, bool includeStarters = true)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        MinPrompts = minPrompts;
        _now = clock ?? (() => DateTime.UtcNow);
        _topicsDir = Path.Combine(DataDirectory, "topics");
        _usedPath = Path.Combine(DataDirectory, UsedFile);
        Directory.CreateDirectory(_topicsDir);
        if (includeStarters) LoadStarters();
    }

    public string DataDirectory { get; }
    public int MinPrompts { get; }

    // ---- Reading ---------------------------------------------------------------------------

    public IReadOnlyList<FloorTopicInfo> ListTopics()
    {
        lock (_gate)
        {
            // Phones all ask at once when someone joins, so a couple of seconds of memory saves a lot of folder scanning.
            if (_listCache is not null && (_now() - _listCacheAt).TotalSeconds < 2) return _listCache;
            _listCache = Scan();
            _listCacheAt = _now();
            return _listCache;
        }
    }

    public IReadOnlyList<FloorPrompt> LoadPrompts(string topic)
    {
        lock (_gate)
        {
            if (!TryResolve(topic, out var name, out var dir)) return [];
            var prompts = new List<FloorPrompt>();

            if (dir is not null)
            {
                var sidecar = ReadAnswers(dir);
                foreach (var file in EnumerateImages(dir))
                {
                    var fileName = Path.GetFileName(file);
                    var (answer, alternates) = sidecar.TryGetValue(fileName, out var over) ? over : FloorText.AnswerFromFileName(fileName);
                    var key = KeyFor(name, fileName);
                    _imageIndex[key] = (file, FloorText.ContentTypeFor(file));
                    prompts.Add(new FloorPrompt(name, answer, alternates, null, key));
                }
            }

            foreach (var t in TextPromptsFor(name, dir))
                prompts.Add(new FloorPrompt(name, t.Answer, t.Alternates, t.Clue, null));
            return prompts;
        }
    }

    /// <summary>The pictures in a topic, newest changes included, for the topics page.</summary>
    public IReadOnlyList<FloorImageInfo> ListImages(string topic)
    {
        lock (_gate)
        {
            if (!TryResolve(topic, out var name, out var dir) || dir is null) return [];
            var sidecar = ReadAnswers(dir);
            var list = new List<FloorImageInfo>();
            foreach (var file in EnumerateImages(dir))
            {
                var fileName = Path.GetFileName(file);
                var answer = sidecar.TryGetValue(fileName, out var over) ? over.Answer : FloorText.AnswerFromFileName(fileName).Answer;
                var key = KeyFor(name, fileName);
                _imageIndex[key] = (file, FloorText.ContentTypeFor(file));
                list.Add(new FloorImageInfo(fileName, answer, key));
            }
            return list;
        }
    }

    /// <summary>Finds a picture by its opaque key, for the web address that serves it.</summary>
    public bool TryGetImage(string key, out string path, out string contentType)
    {
        lock (_gate)
        {
            if (!_imageIndex.TryGetValue(key, out var hit) || !File.Exists(hit.Path))
            {
                RebuildImageIndex();
                if (!_imageIndex.TryGetValue(key, out hit) || !File.Exists(hit.Path))
                {
                    path = "";
                    contentType = "";
                    return false;
                }
            }
            path = hit.Path;
            contentType = hit.ContentType;
            return true;
        }
    }

    // ---- Dead topics -----------------------------------------------------------------------

    public void MarkUsed(string topic)
    {
        lock (_gate)
        {
            var used = ReadUsed();
            var name = TryResolve(topic, out var canonical, out _) ? canonical : topic;
            if (!used.Add(name)) return;
            WriteUsed(used);
            _listCache = null;
        }
    }

    public void ClearUsed()
    {
        lock (_gate)
        {
            WriteUsed([]);
            _listCache = null;
        }
    }

    // ---- Changing topics (the topics page) -------------------------------------------------

    public FloorOpResult CreateTopic(string? rawName)
    {
        var name = FloorText.CleanTopicName(rawName);
        if (name is null) return FloorOpResult.Fail("Give the topic a name.");
        lock (_gate)
        {
            if (TryResolve(name, out var existing, out _)) return FloorOpResult.Fail($"There is already a topic called {existing}.");
            return Guard(() =>
            {
                Directory.CreateDirectory(Path.Combine(_topicsDir, name));
                _listCache = null;
                return FloorOpResult.Success(name);
            });
        }
    }

    public FloorOpResult DeleteTopic(string topic)
    {
        lock (_gate)
        {
            if (!TryResolve(topic, out _, out var dir) || dir is null) return FloorOpResult.Fail("That topic has no folder to delete (it is built in).");
            return Guard(() =>
            {
                Directory.Delete(dir, recursive: true);
                _listCache = null;
                RebuildImageIndex();
                return FloorOpResult.Success();
            });
        }
    }

    /// <summary>Saves one picture. The answer is what the emcee sees; alternates are other answers to accept.</summary>
    public FloorOpResult SaveImage(string topic, string? answer, string? alternates, byte[] data)
    {
        lock (_gate)
        {
            if (!TryResolve(topic, out var name, out var dir)) return FloorOpResult.Fail("That topic doesn't exist.");
            if (data.Length == 0) return FloorOpResult.Fail("That file is empty.");
            if (data.Length > MaxImageBytes) return FloorOpResult.Fail("That picture is too big (6 MB is the limit).");
            var extension = FloorText.DetectImageExtension(data);
            if (extension is null) return FloorOpResult.Fail("That doesn't look like a picture (JPG, PNG, WEBP or GIF).");
            var cleanAnswer = FloorText.CleanLine(answer, 80);
            if (cleanAnswer.Length == 0) return FloorOpResult.Fail("Type the answer for this picture.");
            var alts = (alternates ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => FloorText.CleanLine(a, 80)).Where(a => a.Length > 0).ToList();

            return Guard(() =>
            {
                dir ??= Directory.CreateDirectory(Path.Combine(_topicsDir, name)).FullName;
                var slug = FloorText.Slugify(cleanAnswer);
                var fileName = slug + extension;
                for (var n = 2; File.Exists(Path.Combine(dir, fileName)); n++) fileName = $"{slug}_{n}{extension}";
                File.WriteAllBytes(Path.Combine(dir, fileName), data);

                // The file name can only spell so much ("Star Wars: A New Hope" has a colon). Anything it can't say goes in the sidecar.
                var fromName = FloorText.AnswerFromFileName(fileName);
                if (fromName.Answer != cleanAnswer || alts.Count > 0)
                    File.AppendAllText(Path.Combine(dir, AnswersFile), $"{fileName} | {cleanAnswer} | {string.Join("; ", alts)}{Environment.NewLine}");

                _listCache = null;
                _imageIndex[KeyFor(name, fileName)] = (Path.Combine(dir, fileName), FloorText.ContentTypeFor(fileName));
                return FloorOpResult.Success(fileName);
            });
        }
    }

    public FloorOpResult DeleteImage(string topic, string file)
    {
        lock (_gate)
        {
            if (!TryResolve(topic, out _, out var dir) || dir is null) return FloorOpResult.Fail("That topic doesn't exist.");
            if (file != Path.GetFileName(file) || !IsImage(file)) return FloorOpResult.Fail("That isn't a picture in this topic.");
            var path = Path.Combine(dir, file);
            if (!File.Exists(path)) return FloorOpResult.Fail("That picture is already gone.");
            return Guard(() =>
            {
                File.Delete(path);
                RemoveAnswerLine(dir, file);
                _listCache = null;
                RebuildImageIndex();
                return FloorOpResult.Success();
            });
        }
    }

    /// <summary>Adds text prompts (one per line, "clue | answer | alternates") to a topic.</summary>
    public (FloorOpResult Result, int Added, int Skipped) AddTextPrompts(string topic, string? lines)
    {
        lock (_gate)
        {
            if (!TryResolve(topic, out var name, out var dir)) return (FloorOpResult.Fail("That topic doesn't exist."), 0, 0);
            var parsed = FloorText.ParseTextPrompts(lines ?? "", out _, out var skipped);
            if (parsed.Count == 0) return (FloorOpResult.Fail("No usable lines. Each one needs a clue and an answer, like: Capital of France | Paris"), 0, skipped);

            var result = Guard(() =>
            {
                dir ??= Directory.CreateDirectory(Path.Combine(_topicsDir, name)).FullName;
                var path = Path.Combine(dir, TextFile);
                // A built-in topic has its prompts inside the program. The first time one is extended, copy them out so they aren't replaced.
                if (!File.Exists(path) && _starters.TryGetValue(name, out var starter))
                    File.WriteAllText(path, $"# Topic: {name}{Environment.NewLine}" + string.Concat(starter.Select(FormatLine)));
                File.AppendAllText(path, string.Concat(parsed.Select(FormatLine)));
                _listCache = null;
                return FloorOpResult.Success();
            });
            return (result, result.Ok ? parsed.Count : 0, skipped);
        }
    }

    // ---- Internals -------------------------------------------------------------------------

    private static string FormatLine(FloorTextPrompt p) =>
        $"{p.Clue} | {p.Answer}" + (p.Alternates.Count > 0 ? $" | {string.Join("; ", p.Alternates)}" : "") + Environment.NewLine;

    private static FloorOpResult Guard(Func<FloorOpResult> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FloorOpResult.Fail("The topics folder couldn't be written to: " + ex.Message);
        }
    }

    private void LoadStarters()
    {
        var assembly = typeof(FolderFloorContent).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.Contains(".Data.Floor.", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            var prompts = FloorText.ParseTextPrompts(reader.ReadToEnd(), out var topicName, out _);
            if (topicName is not null && prompts.Count > 0) _starters[topicName] = prompts;
        }
    }

    private IReadOnlyList<FloorTopicInfo> Scan()
    {
        var used = ReadUsed();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var starter in _starters.Keys) names[starter] = starter;
        foreach (var dir in Directory.EnumerateDirectories(_topicsDir)) names[Path.GetFileName(dir)] = Path.GetFileName(dir);

        var list = new List<FloorTopicInfo>();
        foreach (var name in names.Values.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var dir = Path.Combine(_topicsDir, name);
            var onDisk = Directory.Exists(dir);
            var images = onDisk ? EnumerateImages(dir).Count() : 0;
            var texts = TextPromptsFor(name, onDisk ? dir : null).Count;
            list.Add(new FloorTopicInfo(name, images, texts, used.Contains(name)));
        }
        return list;
    }

    private bool TryResolve(string? topic, out string name, out string? dir)
    {
        name = "";
        dir = null;
        var clean = FloorText.CleanTopicName(topic);
        if (clean is null) return false;

        foreach (var d in Directory.EnumerateDirectories(_topicsDir))
        {
            if (!string.Equals(Path.GetFileName(d), clean, StringComparison.OrdinalIgnoreCase)) continue;
            name = Path.GetFileName(d);
            dir = d;
            return true;
        }
        if (_starters.Keys.FirstOrDefault(k => string.Equals(k, clean, StringComparison.OrdinalIgnoreCase)) is { } starter)
        {
            name = starter;
            return true;
        }
        return false;
    }

    private IReadOnlyList<FloorTextPrompt> TextPromptsFor(string name, string? dir)
    {
        if (dir is not null)
        {
            var path = Path.Combine(dir, TextFile);
            if (File.Exists(path)) return FloorText.ParseTextPrompts(File.ReadAllText(path), out _, out _);
        }
        return _starters.TryGetValue(name, out var starter) ? starter : [];
    }

    private static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateImages(string dir) =>
        Directory.EnumerateFiles(dir).Where(IsImage).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    // A secret that only exists while the program runs, so nobody can work out a picture's web address from its topic and answer.
    private static readonly byte[] Salt = RandomNumberGenerator.GetBytes(16);

    private static string KeyFor(string topic, string fileName) =>
        Convert.ToHexString(HMACSHA256.HashData(Salt, Encoding.UTF8.GetBytes(topic.ToLowerInvariant() + "/" + fileName.ToLowerInvariant())))[..16].ToLowerInvariant();

    private void RebuildImageIndex()
    {
        _imageIndex.Clear();
        foreach (var dir in Directory.EnumerateDirectories(_topicsDir))
            foreach (var file in EnumerateImages(dir))
                _imageIndex[KeyFor(Path.GetFileName(dir), Path.GetFileName(file))] = (file, FloorText.ContentTypeFor(file));
    }

    private static Dictionary<string, (string Answer, IReadOnlyList<string> Alternates)> ReadAnswers(string dir)
    {
        var map = new Dictionary<string, (string, IReadOnlyList<string>)>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(dir, AnswersFile);
        if (!File.Exists(path)) return map;
        foreach (var raw in File.ReadAllLines(path))
        {
            var parts = raw.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) continue;
            var alternates = parts.Length > 2
                ? parts[2].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [];
            map[parts[0]] = (parts[1], alternates);
        }
        return map;
    }

    private static void RemoveAnswerLine(string dir, string file)
    {
        var path = Path.Combine(dir, AnswersFile);
        if (!File.Exists(path)) return;
        var kept = File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith(file + " |", StringComparison.OrdinalIgnoreCase)).ToList();
        File.WriteAllLines(path, kept);
    }

    private HashSet<string> ReadUsed()
    {
        try
        {
            if (!File.Exists(_usedPath)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_usedPath)) ?? [];
            return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void WriteUsed(IEnumerable<string> names)
    {
        // Write beside the file and swap it in, so a restart in the middle can't leave half a list behind.
        var temp = _usedPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()));
        File.Move(temp, _usedPath, overwrite: true);
    }
}

/// <summary>Topics made up for tests: "Topic 01", "Topic 02"..., each with the same number of prompts, as text or as pictures.</summary>
public sealed class FixedFloorContent : IFloorContent
{
    private readonly Dictionary<string, int> _topics = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _images;

    public FixedFloorContent(int topics = 30, int promptsEach = 120, bool images = false)
    {
        _images = images;
        for (var i = 1; i <= topics; i++) _topics[$"Topic {i:00}"] = promptsEach;
    }

    public FixedFloorContent(IDictionary<string, int> topics, bool images = false)
    {
        _images = images;
        foreach (var (name, count) in topics) _topics[name] = count;
    }

    public int MinPrompts => 1;

    public IReadOnlyList<FloorTopicInfo> ListTopics() =>
        _topics.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .Select(t => new FloorTopicInfo(t.Key, _images ? t.Value : 0, _images ? 0 : t.Value, _used.Contains(t.Key)))
            .ToList();

    public IReadOnlyList<FloorPrompt> LoadPrompts(string topic)
    {
        if (!_topics.TryGetValue(topic, out var count)) return [];
        var name = _topics.Keys.First(k => string.Equals(k, topic, StringComparison.OrdinalIgnoreCase));
        return Enumerable.Range(1, count)
            .Select(i => _images
                ? new FloorPrompt(name, $"{name} answer {i}", [], null, FloorText.Slugify($"{name} {i}"))
                : new FloorPrompt(name, $"{name} answer {i}", [], $"{name} clue {i}", null))
            .ToList();
    }

    public void MarkUsed(string topic) => _used.Add(topic);

    public void ClearUsed() => _used.Clear();
}
