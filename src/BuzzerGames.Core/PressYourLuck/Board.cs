using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuzzerGames.Core.PressYourLuck;

public enum OutcomeKind { Cash, Prize, Corner, Whammy }

/// <summary>
/// What one face of one square shows. Sub is the small second line ("+ SPIN", or a prize's dollar value).
/// </summary>
public sealed record Outcome(OutcomeKind Kind, string Text, string? Sub = null, int Value = 0, bool ExtraSpin = false)
{
    public static Outcome Whammy { get; } = new(OutcomeKind.Whammy, "WHAMMY");

    public static Outcome Cash(int amount, bool extraSpin = false) =>
        new(OutcomeKind.Cash, "$" + amount.ToString("N0", CultureInfo.InvariantCulture), extraSpin ? "+ SPIN" : null, amount, extraSpin);

    public static Outcome Prize(string name, int value) =>
        new(OutcomeKind.Prize, name.ToUpperInvariant(), "$" + value.ToString("N0", CultureInfo.InvariantCulture), value);

    public static Outcome Corner { get; } = new(OutcomeKind.Corner, "PICK A CORNER");
}

public sealed record PrizeRecord(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] int Value);

public sealed record BoardCounts(
    [property: JsonPropertyName("cash")] int Cash,
    [property: JsonPropertyName("cashSpin")] int CashSpin,
    [property: JsonPropertyName("big")] int Big,
    [property: JsonPropertyName("prizes")] int Prizes,
    [property: JsonPropertyName("corner")] int Corner);

/// <summary>A Big Board recipe as written in Data/PressYourLuck/board-N.json.</summary>
public sealed record BoardConfig(
    [property: JsonPropertyName("whammiesPerFace")] int[] WhammiesPerFace,
    [property: JsonPropertyName("counts")] BoardCounts Counts,
    [property: JsonPropertyName("cash")] int[] Cash,
    [property: JsonPropertyName("cashSpin")] int[] CashSpin,
    [property: JsonPropertyName("big")] int[] Big,
    [property: JsonPropertyName("prizes")] PrizeRecord[] Prizes)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Loads Data/PressYourLuck/board-{round}.json from inside the assembly.</summary>
    public static BoardConfig Load(int round)
    {
        var assembly = typeof(BoardConfig).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".PressYourLuck.board-{round}.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Missing the board file for round {round}.");
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return JsonSerializer.Deserialize<BoardConfig>(reader.ReadToEnd(), Json)!;
    }
}

/// <summary>
/// Deals the Big Board: 18 squares, each cycling through 3 faces (54 outcomes). The Whammies sit on
/// 9 different squares, so no square is ever a Whammy on two faces, and they are spread over the faces
/// unevenly (say 1, 3 and 5), which is what makes the board feel different from moment to moment.
/// </summary>
public static class BoardFactory
{
    public const int Squares = 18;
    public const int Faces = 3;

    /// <summary>The four corner squares, in the order the ring runs (clockwise from top-left).</summary>
    public static readonly IReadOnlyList<int> Corners = [0, 5, 9, 14];

    public static Outcome[][] Deal(BoardConfig config, Random rng)
    {
        var whammies = config.WhammiesPerFace.OrderBy(_ => rng.Next()).ToArray();
        var whammyTotal = whammies.Sum();
        var others = config.Counts.Cash + config.Counts.CashSpin + config.Counts.Big + config.Counts.Prizes + config.Counts.Corner;
        if (whammies.Length != Faces || whammyTotal > Squares || whammyTotal + others != Squares * Faces)
            throw new InvalidOperationException($"A board needs exactly {Squares * Faces} outcomes (it has {whammyTotal + others}).");

        for (var attempt = 0; ; attempt++)
        {
            var board = Build(config, whammies, rng);
            // Avoid one square showing the same text on two faces; give up on that nicety after a while.
            if (attempt >= 60 || board.All(faces => faces.Select(o => o.Text).Distinct().Count() == Faces)) return board;
        }
    }

    private static Outcome[][] Build(BoardConfig config, int[] whammies, Random rng)
    {
        var board = Enumerable.Range(0, Squares).Select(_ => new Outcome?[Faces]).ToArray();

        // Whammies: 9 different squares, split across the faces.
        var squareOrder = Enumerable.Range(0, Squares).OrderBy(_ => rng.Next()).ToList();
        var next = 0;
        for (var face = 0; face < Faces; face++)
            for (var i = 0; i < whammies[face]; i++)
                board[squareOrder[next++]][face] = Outcome.Whammy;

        var open = Enumerable.Range(0, Squares).SelectMany(s => Enumerable.Range(0, Faces).Select(f => (s, f)))
            .Where(c => board[c.s][c.f] is null).OrderBy(_ => rng.Next()).ToList();

        // Pick a Corner never sits on a corner (the show avoided that too, or you could pick yourself).
        for (var i = 0; i < config.Counts.Corner; i++)
        {
            var spot = open.First(c => !Corners.Contains(c.s));
            open.Remove(spot);
            board[spot.s][spot.f] = Outcome.Corner;
        }

        var pool = new List<Outcome>();
        for (var i = 0; i < config.Counts.Cash; i++) pool.Add(Outcome.Cash(Pick(config.Cash, rng)));
        for (var i = 0; i < config.Counts.CashSpin; i++) pool.Add(Outcome.Cash(Pick(config.CashSpin, rng), extraSpin: true));
        for (var i = 0; i < config.Counts.Big; i++) pool.Add(Outcome.Cash(Pick(config.Big, rng), extraSpin: true));

        var prizes = config.Prizes.OrderBy(_ => rng.Next()).ToList();
        for (var i = 0; i < config.Counts.Prizes; i++)
        {
            var prize = prizes[i % prizes.Count];
            pool.Add(Outcome.Prize(prize.Name, prize.Value));
        }

        pool = pool.OrderBy(_ => rng.Next()).ToList();
        for (var i = 0; i < open.Count; i++) board[open[i].s][open[i].f] = pool[i];

        return board.Select(faces => faces.Select(o => o!).ToArray()).ToArray();
    }

    private static int Pick(int[] list, Random rng) => list[rng.Next(list.Length)];
}
