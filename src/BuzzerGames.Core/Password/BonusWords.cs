namespace BuzzerGames.Core.Password;

/// <summary>
/// One bonus round's worth of words. <see cref="Words"/> has one word per letter, in alphabetical order starting at
/// <see cref="StartLetter"/> and wrapping from Z back to A, so Words[0] begins with StartLetter, Words[1] with the next letter, and so on.
/// </summary>
public sealed record BonusRound(char StartLetter, IReadOnlyList<string> Words)
{
    public char LetterAt(int index) => LetterFor(StartLetter, index);

    public static char LetterFor(char start, int index) => (char)('A' + (char.ToUpperInvariant(start) - 'A' + index) % 26);
}

/// <summary>Where the bonus round gets its words. Swap it to change them (a test uses a fixed one).</summary>
public interface IBonusWordSource
{
    BonusRound CreateRound(Random rng);
}

/// <summary>
/// Words from Data/Password/bonus-words.txt (one word per line, any order). A random starting letter is picked and one word per
/// letter is dealt from there. Words that were dealt recently are skipped, so a night of games doesn't keep repeating itself;
/// when a letter runs out of fresh words, only that letter starts over.
/// </summary>
public sealed class TextBonusWordSource : IBonusWordSource
{
    public const int WordsPerRound = 10;

    private readonly IReadOnlyDictionary<char, IReadOnlyList<string>> _byLetter;
    private readonly Dictionary<char, HashSet<string>> _used = new();
    private readonly object _gate = new();

    public TextBonusWordSource() : this(LoadBank()) { }

    public TextBonusWordSource(IEnumerable<string> words) => _byLetter = Group(words);

    /// <summary>Reads Data/Password/bonus-words.txt from inside the assembly.</summary>
    public static IReadOnlyList<string> LoadBank()
    {
        var assembly = typeof(TextBonusWordSource).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(".Password.bonus-words.txt", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The Password Plus bonus words (bonus-words.txt) are missing.");
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Splits text into words, dropping blank lines and # comments.</summary>
    public static IReadOnlyList<string> Parse(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

    public static IReadOnlyDictionary<char, IReadOnlyList<string>> Group(IEnumerable<string> words) =>
        words.Select(w => w.Trim()).Where(w => w.Length > 0)
            .GroupBy(w => char.ToUpperInvariant(w[0]))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

    public IReadOnlyDictionary<char, IReadOnlyList<string>> Bank => _byLetter;

    public BonusRound CreateRound(Random rng)
    {
        lock (_gate)
        {
            // Only letters with words can be dealt, but the round needs ten in a row, so start somewhere that works.
            var starts = Enumerable.Range(0, 26)
                .Where(s => Enumerable.Range(0, WordsPerRound).All(i => _byLetter.ContainsKey(BonusRound.LetterFor((char)('A' + s), i))))
                .ToList();
            if (starts.Count == 0) throw new InvalidOperationException("The bonus word bank has no ten letters in a row to deal.");

            var start = (char)('A' + starts[rng.Next(starts.Count)]);
            var words = new List<string>();
            for (var i = 0; i < WordsPerRound; i++)
            {
                var letter = BonusRound.LetterFor(start, i);
                var all = _byLetter[letter];
                if (!_used.TryGetValue(letter, out var used)) _used[letter] = used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var fresh = all.Where(w => !used.Contains(w)).ToList();
                if (fresh.Count == 0)
                {
                    used.Clear();
                    fresh = all.ToList();
                }
                var pick = fresh[rng.Next(fresh.Count)];
                used.Add(pick);
                words.Add(pick);
            }
            return new BonusRound(start, words);
        }
    }
}

/// <summary>Predictable words for tests: for letter L the word is "Lest" (Aest, Best...), starting at the given letter.</summary>
public sealed class FixedBonusWordSource(char start = 'A') : IBonusWordSource
{
    public BonusRound CreateRound(Random rng) =>
        new(start, Enumerable.Range(0, TextBonusWordSource.WordsPerRound).Select(i => BonusRound.LetterFor(start, i) + "est").ToList());
}
