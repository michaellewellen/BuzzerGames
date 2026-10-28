using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.Price;

// Plinko, the first pricing game.
//
// The contestant on stage is shown four small grocery items, each with a price tag that is NOT the real price. For each one they say
// whether the real price is higher or lower. Every right answer earns a chip, and they start with one free chip. Then each chip is
// dropped from one of nine slots at the top of a pegboard and bounces down to a slot at the bottom that pays out money.

/// <summary>A small item for the hi-lo part of Plinko. Prices are in cents.</summary>
public sealed record PlinkoItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("cents")] int Cents);

public interface IPlinkoItemSource
{
    /// <summary>Returns <paramref name="count"/> different items.</summary>
    IReadOnlyList<PlinkoItem> CreateItems(Random rng, int count);
}

/// <summary>Deals items from Data/Price/plinko-items.json (inside the assembly).</summary>
public sealed class JsonPlinkoItemSource : IPlinkoItemSource
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyList<PlinkoItem> _bank;

    public JsonPlinkoItemSource() : this(LoadBank()) { }

    public JsonPlinkoItemSource(IReadOnlyList<PlinkoItem> bank) => _bank = bank;

    public static IReadOnlyList<PlinkoItem> LoadBank()
    {
        var assembly = typeof(JsonPlinkoItemSource).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(".Price.plinko-items.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The Plinko item bank (plinko-items.json) is missing.");
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return JsonSerializer.Deserialize<PlinkoItem[]>(reader.ReadToEnd(), Json) ?? [];
    }

    public IReadOnlyList<PlinkoItem> CreateItems(Random rng, int count)
    {
        if (_bank.Count == 0) throw new InvalidOperationException("The Plinko item bank is empty.");
        var deck = _bank.OrderBy(_ => rng.Next()).ToList();
        while (deck.Count < count) deck.AddRange(_bank.OrderBy(_ => rng.Next()));
        return deck.Take(count).ToList();
    }
}

/// <summary>Predictable items for tests: "Snack 1" costs $1.49, "Snack 2" costs $2.49, and so on.</summary>
public sealed class FixedPlinkoItemSource : IPlinkoItemSource
{
    public IReadOnlyList<PlinkoItem> CreateItems(Random rng, int count) =>
        Enumerable.Range(1, count).Select(i => new PlinkoItem($"Snack {i}", "🛒", i * 100 + 49)).ToList();
}

/// <summary>The pegboard: its size, what the bottom slots pay, and how a chip falls through it.</summary>
public static class PlinkoBoard
{
    public const int Slots = 9;
    public const int Rows = 13;            // seven rows of eight pegs and six rows of seven, alternating, starting and ending with eight
    public const int ItemCount = 4;
    public const int FreeChips = 1;

    /// <summary>How long the big screen takes to show one chip falling. The chip counts as landed after this.</summary>
    public const int FallMs = 6500;

    /// <summary>What each bottom slot pays, left to right. The top slots line up with these, so slot 4 drops straight onto the $10,000.</summary>
    public static readonly IReadOnlyList<int> Payouts = [100, 500, 1000, 0, 10000, 0, 1000, 500, 100];

    // Where things sit, in "gap" units: slot i is at x = i (0 to 8). Odd rows have eight pegs at 0.5, 1.5 ... 7.5.
    // Even rows have seven pegs at 1, 2 ... 7. A chip dropped at an integer slot passes row 1 through a gap, then hits a peg on every row
    // and bounces half a unit left or right, which is exactly what lines it up with the next row's pegs.

    public static bool HasPeg(int row, double x)
    {
        if (row % 2 == 1) return x >= 0.5 && x <= 7.5 && x - Math.Floor(x) == 0.5;
        return x >= 1 && x <= 7 && x == Math.Floor(x);
    }

    /// <summary>
    /// How a chip dropped from <paramref name="slot"/> falls. Returns <see cref="Rows"/> + 1 numbers: where it starts, then where it
    /// is after each row. The last one is a whole number: the bottom slot it lands in.
    /// </summary>
    public static IReadOnlyList<double> Fall(int slot, Random rng)
    {
        var path = new double[Rows + 1];
        var x = (double)slot;
        path[0] = x;
        for (var row = 1; row <= Rows; row++)
        {
            if (HasPeg(row, x)) x += rng.Next(2) == 0 ? -0.5 : 0.5;
            path[row] = x;
        }
        return path;
    }

    public static int SlotOf(IReadOnlyList<double> path) => (int)path[^1];

    /// <summary>"$1,000" (the payouts are whole dollars).</summary>
    public static string Money(int dollars) => PriceGame.Money(dollars);

    /// <summary>"$3.49".</summary>
    public static string Cents(int cents) =>
        "$" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A price tag that looks like a shelf price but is wrong: 15% to 45% off the real price, one way or the other.</summary>
    public static int WrongPrice(int actual, Random rng)
    {
        for (var tries = 0; tries < 100; tries++)
        {
            var wiggle = 0.15 + rng.NextDouble() * 0.30;
            var raw = actual * (rng.Next(2) == 0 ? 1 + wiggle : 1 - wiggle);
            var shown = (int)(Math.Round(raw / 10.0) * 10) - 1;
            if (shown >= 29 && shown != actual) return shown;
        }
        return actual + 100;
    }
}

public enum PlinkoStage
{
    Guessing,   // higher or lower, item by item
    Dropping,   // the contestant has a chip to drop and is choosing where
    Falling,    // a chip is on its way down
    Done
}

/// <summary>One hi-lo item. ActualCents is SECRET until Answered is true.</summary>
public sealed record PlinkoItemInfo(string Name, string Icon, int ShownCents, int ActualCents, bool Answered, bool GuessedHigher, bool Correct);

/// <summary>One chip. Path and Payout are known from the moment it drops, but the screens only show the payout once it has Landed.</summary>
public sealed record PlinkoDropInfo(int Number, int Slot, IReadOnlyList<double> Path, int Payout, bool Landed);

public sealed record PlinkoInfo(
    PlinkoStage Stage,
    IReadOnlyList<PlinkoItemInfo> Items,
    int CurrentItem,                        // the item being guessed (the last one, once all are answered)
    int ChipsEarned,                        // the free chip plus one per right answer, so far
    int ChipsLeft,                          // chips not yet dropped
    IReadOnlyList<PlinkoDropInfo> Drops,
    int Aim,                                // the slot the contestant is lining up, shown live on the big screen
    int Total,                              // money from chips that have landed
    bool Skipped)
{
    public PlinkoDropInfo? Falling => Drops.FirstOrDefault(d => !d.Landed);
}

/// <summary>
/// One contestant's game of Plinko. It does no locking of its own: <see cref="PriceGame"/> calls it while holding its lock,
/// and passes in a way to make the announcer talk.
/// </summary>
internal sealed class PlinkoRound
{
    private sealed class Drop(int number, int slot, IReadOnlyList<double> path, int payout)
    {
        public int Number { get; } = number;
        public int Slot { get; } = slot;
        public IReadOnlyList<double> Path { get; } = path;
        public int Payout { get; } = payout;
        public bool Landed;
    }

    private readonly PlinkoItem[] _items;
    private readonly int[] _shown;
    private readonly bool[] _guessedHigher;
    private readonly Random _rng;
    private readonly Action<string, bool> _say;
    private readonly List<Drop> _drops = [];
    private int _answered;
    private int _aim = PlinkoBoard.Slots / 2;
    private bool _skipped;

    public PlinkoRound(IReadOnlyList<PlinkoItem> items, Random rng, Action<string, bool> say)
    {
        _items = items.Take(PlinkoBoard.ItemCount).ToArray();
        _rng = rng;
        _say = say;
        _shown = _items.Select(i => PlinkoBoard.WrongPrice(i.Cents, rng)).ToArray();
        _guessedHigher = new bool[_items.Length];
    }

    public PlinkoStage Stage =>
        _skipped ? PlinkoStage.Done
        : _answered < _items.Length ? PlinkoStage.Guessing
        : _drops.Any(d => !d.Landed) ? PlinkoStage.Falling
        : _drops.Count < ChipsEarned ? PlinkoStage.Dropping
        : PlinkoStage.Done;

    public bool Done => Stage == PlinkoStage.Done;

    private bool IsCorrect(int i) => _guessedHigher[i] ? _items[i].Cents > _shown[i] : _items[i].Cents < _shown[i];

    private int ChipsEarned => PlinkoBoard.FreeChips + Enumerable.Range(0, _answered).Count(IsCorrect);

    private int Total => _drops.Where(d => d.Landed).Sum(d => d.Payout);

    private static string Chips(int n) => n == 1 ? "chip" : "chips";

    /// <summary>The opening words, said after the announcer has called the contestant up.</summary>
    public void Begin(string intro)
    {
        _say($"{intro} Welcome to Plinko! For each of these four items, tell me whether the actual price is higher or lower than the price shown. "
            + $"Every right answer wins a chip, and you get one chip free. First up: {_items[0].Name}, marked at {PlinkoBoard.Cents(_shown[0])}. Higher or lower?", true);
    }

    public Result Guess(bool higher)
    {
        if (Stage != PlinkoStage.Guessing) return Result.Fail("There's no price to guess right now.");
        var i = _answered;
        _guessedHigher[i] = higher;
        _answered++;
        var correct = IsCorrect(i);

        var line = $"The actual price is {PlinkoBoard.Cents(_items[i].Cents)}! " + (correct ? "You win a chip!" : "No chip on that one.");
        line += _answered < _items.Length
            ? $" Next up: {_items[_answered].Name}, marked at {PlinkoBoard.Cents(_shown[_answered])}. Higher or lower?"
            : $" You have {ChipsEarned} {Chips(ChipsEarned)} to play. Choose where to drop your first one.";
        _say(line, correct);
        return Result.Success;
    }

    /// <summary>The contestant slides their chip along the top. The big screen shows it live.</summary>
    public Result Aim(int slot)
    {
        if (Stage != PlinkoStage.Dropping) return Result.Fail("There's no chip to aim.");
        if (slot < 0 || slot >= PlinkoBoard.Slots) return Result.Fail("Pick one of the nine slots.");
        if (slot == _aim) return Result.Fail("Already there.");
        _aim = slot;
        return Result.Success;
    }

    public Result DropChip(int slot)
    {
        if (Stage == PlinkoStage.Falling) return Result.Fail("Wait for the chip to land first.");
        if (Stage != PlinkoStage.Dropping) return Result.Fail("There's no chip to drop.");
        if (slot < 0 || slot >= PlinkoBoard.Slots) return Result.Fail("Pick one of the nine slots.");

        var path = PlinkoBoard.Fall(slot, _rng);
        _aim = slot;
        _drops.Add(new Drop(_drops.Count + 1, slot, path, PlinkoBoard.Payouts[PlinkoBoard.SlotOf(path)]));
        _say("And away it goes!", false);
        return Result.Success;
    }

    /// <summary>The big screen reports that the chip with this number has finished falling (or the emcee skips the wait).</summary>
    public Result Land(int number)
    {
        var drop = _drops.FirstOrDefault(d => !d.Landed);
        if (drop is null || drop.Number != number) return Result.Fail("That chip has already landed.");
        drop.Landed = true;

        var line = drop.Payout switch
        {
            0 => "Nothing there.",
            10000 => "Ten thousand dollars! The jackpot!",
            1000 => "One thousand dollars!",
            _ => $"{PriceGame.Money(drop.Payout)}."
        };
        var left = ChipsEarned - _drops.Count;
        line += left > 0
            ? $" You have {left} {Chips(left)} left. Choose where to drop the next one."
            : $" That's {PriceGame.Money(Total)} in Plinko!";
        _say(line, drop.Payout >= 1000 || (left == 0 && Total >= 1000));
        return Result.Success;
    }

    /// <summary>The emcee ends the game early. Chips already dropped still count.</summary>
    public Result Skip()
    {
        if (Done) return Result.Fail("The pricing game is already over.");
        foreach (var d in _drops) d.Landed = true;
        _skipped = true;
        _say("Let's move on!", false);
        return Result.Success;
    }

    public PlinkoInfo Info() => new(
        Stage,
        _items.Select((item, i) => new PlinkoItemInfo(item.Name, item.Icon, _shown[i], item.Cents,
            Answered: i < _answered, GuessedHigher: _guessedHigher[i], Correct: i < _answered && IsCorrect(i))).ToList(),
        CurrentItem: Math.Min(_answered, _items.Length - 1),
        ChipsEarned: ChipsEarned,
        ChipsLeft: _skipped ? 0 : ChipsEarned - _drops.Count,
        Drops: _drops.Select(d => new PlinkoDropInfo(d.Number, d.Slot, d.Path, d.Payout, d.Landed)).ToList(),
        Aim: _aim,
        Total: Total,
        Skipped: _skipped);
}
