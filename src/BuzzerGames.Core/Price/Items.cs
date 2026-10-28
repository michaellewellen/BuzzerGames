using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuzzerGames.Core.Price;

/// <summary>
/// One item up for bid. The voice reads Description and then "This {Say} could be yours if the price is right!".
/// Icon is a big emoji shown until a real photo exists; Image is an optional file name in wwwroot/images/price/.
/// </summary>
public sealed record PriceItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("price")] int Price,
    [property: JsonPropertyName("icon")] string Icon = "🎁",
    [property: JsonPropertyName("image")] string? Image = null,
    [property: JsonPropertyName("say")] string? Say = null)
{
    /// <summary>The words after "This" in the closing line: "stereo system" in "This stereo system could be yours...".</summary>
    public string SaidName => string.IsNullOrWhiteSpace(Say) ? Name : Say!;
}

/// <summary>Where the items for bidding come from.</summary>
public interface IPriceItemSource
{
    /// <summary>Returns <paramref name="count"/> different items in the order they will be shown.</summary>
    IReadOnlyList<PriceItem> CreateItems(Random rng, int count);
}

/// <summary>Deals items from Data/Price/items.json (inside the assembly).</summary>
public sealed class JsonPriceItemSource : IPriceItemSource
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyList<PriceItem> _bank;

    public JsonPriceItemSource() : this(LoadBank()) { }

    public JsonPriceItemSource(IReadOnlyList<PriceItem> bank) => _bank = bank;

    public static IReadOnlyList<PriceItem> LoadBank()
    {
        var assembly = typeof(JsonPriceItemSource).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(".Price.items.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The Price Is Right item bank (items.json) is missing.");
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return JsonSerializer.Deserialize<PriceItem[]>(reader.ReadToEnd(), Json) ?? [];
    }

    public IReadOnlyList<PriceItem> CreateItems(Random rng, int count)
    {
        if (_bank.Count == 0) throw new InvalidOperationException("The item bank is empty.");
        var deck = _bank.OrderBy(_ => rng.Next()).ToList();
        while (deck.Count < count) deck.AddRange(_bank.OrderBy(_ => rng.Next()));
        return deck.Take(count).ToList();
    }
}

/// <summary>Predictable items for tests: "Item 1" costs $100, "Item 2" costs $200, and so on.</summary>
public sealed class FixedPriceItemSource : IPriceItemSource
{
    public IReadOnlyList<PriceItem> CreateItems(Random rng, int count) =>
        Enumerable.Range(1, count)
            .Select(i => new PriceItem($"Item {i}", $"Description {i}.", i * 100, "🎁"))
            .ToList();
}

/// <summary>The consolation prizes the announcer reads to contestants who never made it on stage.</summary>
public interface IPartingGiftSource
{
    IReadOnlyList<string> PickGifts(Random rng, int count);
}

/// <summary>Reads Data/Price/parting-gifts.txt: one gift per line, # for comments.</summary>
public sealed class TextPartingGiftSource : IPartingGiftSource
{
    private readonly IReadOnlyList<string> _gifts;

    public TextPartingGiftSource() : this(LoadGifts()) { }

    public TextPartingGiftSource(IReadOnlyList<string> gifts) => _gifts = gifts;

    public static IReadOnlyList<string> LoadGifts()
    {
        var assembly = typeof(TextPartingGiftSource).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(".Price.parting-gifts.txt", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The parting gifts (parting-gifts.txt) are missing.");
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return Parse(reader.ReadToEnd());
    }

    public static IReadOnlyList<string> Parse(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

    public IReadOnlyList<string> PickGifts(Random rng, int count)
    {
        if (_gifts.Count == 0) return Enumerable.Repeat("a lovely parting gift", count).ToList();
        var deck = _gifts.OrderBy(_ => rng.Next()).ToList();
        while (deck.Count < count) deck.AddRange(_gifts.OrderBy(_ => rng.Next()));
        return deck.Take(count).ToList();
    }
}

/// <summary>Predictable gifts for tests.</summary>
public sealed class FixedPartingGiftSource : IPartingGiftSource
{
    public IReadOnlyList<string> PickGifts(Random rng, int count) =>
        Enumerable.Range(1, count).Select(i => $"gift number {i}").ToList();
}
