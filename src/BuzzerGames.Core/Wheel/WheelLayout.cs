namespace BuzzerGames.Core.Wheel;

public enum SliceKind { Cash, Bankrupt, LoseATurn, Million }

/// <summary>
/// A piece of the wheel's rim. Angles are in degrees clockwise from the top of the wheel, measured on the wheel itself (not on the screen),
/// so they don't change when the wheel turns.
/// </summary>
public readonly record struct WheelSlice(int Wedge, double Start, double End, SliceKind Kind, int Value)
{
    public double Mid => (Start + End) / 2;
    public double Width => End - Start;

    public string Label => Kind switch
    {
        SliceKind.Cash => $"${Value:N0}",
        SliceKind.Bankrupt => "BANKRUPT",
        SliceKind.LoseATurn => "LOSE A TURN",
        SliceKind.Million => "$1,000,000",
        _ => ""
    };
}

public enum WedgeType { Cash, Bankrupt, LoseATurn, MillionTrio }

/// <summary>One 15-degree wedge of the wheel. <see cref="Value"/> only matters for cash.</summary>
public readonly record struct WedgeSpec(WedgeType Type, int Value = 0)
{
    public static WedgeSpec Cash(int value) => new(WedgeType.Cash, value);
    public static WedgeSpec Bankrupt => new(WedgeType.Bankrupt);
    public static WedgeSpec LoseATurn => new(WedgeType.LoseATurn);

    /// <summary>One wedge cut into three 5-degree slices: Bankrupt, $1,000,000, Bankrupt.</summary>
    public static WedgeSpec MillionTrio => new(WedgeType.MillionTrio);
}

/// <summary>
/// The wheel's rim: 24 wedges, each 15 degrees wide, one of them split into three 5-degree slices. Pegs (the things that click) sit at
/// every 5 degrees. A layout never changes once built, so it is safe to share.
/// </summary>
public sealed class WheelLayout
{
    private readonly WheelSlice[] _slices;

    private WheelLayout(WheelSlice[] slices)
    {
        _slices = slices;
        Pegs = PegAngles;
    }

    public IReadOnlyList<WheelSlice> Slices => _slices;

    /// <summary>The angle of every peg, in order: one at every 5 degrees, so 72. They don't depend on the slices (a picked-up Million wedge leaves them all in place).</summary>
    public static IReadOnlyList<double> PegAngles { get; } =
        Enumerable.Range(0, (int)Math.Round(360 / WheelRules.PegDegrees)).Select(i => i * WheelRules.PegDegrees).ToList();

    public IReadOnlyList<double> Pegs { get; }

    /// <summary>The slice at a given angle on the wheel. Any angle works; it wraps around.</summary>
    public WheelSlice SliceAt(double wheelAngle)
    {
        var a = Normalize(wheelAngle);
        foreach (var slice in _slices)
            if (a >= slice.Start && a < slice.End) return slice;
        return _slices[^1];
    }

    /// <summary>
    /// The wheel after someone picks up the Million Dollar Wedge: the whole Bankrupt / Million / Bankrupt wedge becomes one plain 15-degree
    /// Bankrupt. The pegs stay where they are. Returns this same layout when the wheel has no Million slice.
    /// </summary>
    public WheelLayout WithMillionCollected()
    {
        if (!_slices.Any(s => s.Kind == SliceKind.Million)) return this;
        var million = _slices.First(s => s.Kind == SliceKind.Million);
        var rebuilt = new List<WheelSlice>();
        foreach (var slice in _slices)
        {
            if (slice.Wedge != million.Wedge) rebuilt.Add(slice);
            else if (slice.Kind == SliceKind.Million) rebuilt.Add(new WheelSlice(slice.Wedge, slice.Start - WheelRules.WedgeDegrees / 3, slice.End + WheelRules.WedgeDegrees / 3, SliceKind.Bankrupt, 0));
        }
        return new WheelLayout(rebuilt.ToArray());
    }

    public static double Normalize(double degrees)
    {
        var a = degrees % 360.0;
        return a < 0 ? a + 360.0 : a;
    }

    /// <summary>Builds a wheel from exactly <see cref="WheelRules.Wedges"/> wedges, in order, starting at the top and going clockwise.</summary>
    public static WheelLayout FromWedges(IReadOnlyList<WedgeSpec> wedges)
    {
        if (wedges.Count != WheelRules.Wedges) throw new ArgumentException($"A wheel has {WheelRules.Wedges} wedges, not {wedges.Count}.");
        var slices = new List<WheelSlice>();
        for (var i = 0; i < wedges.Count; i++)
        {
            var start = i * WheelRules.WedgeDegrees;
            var end = start + WheelRules.WedgeDegrees;
            var w = wedges[i];
            switch (w.Type)
            {
                case WedgeType.Cash:
                    slices.Add(new WheelSlice(i, start, end, SliceKind.Cash, w.Value));
                    break;
                case WedgeType.Bankrupt:
                    slices.Add(new WheelSlice(i, start, end, SliceKind.Bankrupt, 0));
                    break;
                case WedgeType.LoseATurn:
                    slices.Add(new WheelSlice(i, start, end, SliceKind.LoseATurn, 0));
                    break;
                case WedgeType.MillionTrio:
                    var third = WheelRules.WedgeDegrees / 3;
                    slices.Add(new WheelSlice(i, start, start + third, SliceKind.Bankrupt, 0));
                    slices.Add(new WheelSlice(i, start + third, start + 2 * third, SliceKind.Million, WheelRules.MillionSliceValue));
                    slices.Add(new WheelSlice(i, start + 2 * third, end, SliceKind.Bankrupt, 0));
                    break;
            }
        }
        return new WheelLayout(slices.ToArray());
    }

    /// <summary>
    /// A shuffled wheel. The non-cash wedges are placed the way the real wheel has them: the lone Bankrupt sits exactly opposite the
    /// Bankrupt / Million / Bankrupt wedge (180 degrees apart), and Lose a Turn sits a quarter turn (90 degrees) from both. Where that
    /// whole arrangement sits on the wheel is random. The 21 cash wedges are then shuffled into the other places until no two equal
    /// values touch and the two biggest values ($3,500 and $5,000) aren't neighbors.
    /// </summary>
    public static WheelLayout Create(Random rng)
    {
        const int half = WheelRules.Wedges / 2;
        const int quarter = WheelRules.Wedges / 4;

        var trio = rng.Next(WheelRules.Wedges);
        var bankrupt = (trio + half) % WheelRules.Wedges;
        var lose = (trio + (rng.Next(2) == 0 ? quarter : WheelRules.Wedges - quarter)) % WheelRules.Wedges;

        var cash = WheelRules.CashValues.ToList();
        var wedges = new WedgeSpec[WheelRules.Wedges];

        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            for (var i = cash.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (cash[i], cash[j]) = (cash[j], cash[i]);
            }
            var next = 0;
            for (var i = 0; i < wedges.Length; i++)
            {
                wedges[i] = i == trio ? WedgeSpec.MillionTrio
                    : i == bankrupt ? WedgeSpec.Bankrupt
                    : i == lose ? WedgeSpec.LoseATurn
                    : WedgeSpec.Cash(cash[next++]);
            }
            if (Acceptable(wedges)) return FromWedges(wedges);
        }
        throw new InvalidOperationException("Could not arrange the wheel.");
    }

    private static bool Acceptable(IReadOnlyList<WedgeSpec> w)
    {
        for (var i = 0; i < w.Count; i++)
        {
            var a = w[i];
            var b = w[(i + 1) % w.Count];
            if (a.Type != WedgeType.Cash && b.Type != WedgeType.Cash) return false;
            if (a.Type == WedgeType.Cash && b.Type == WedgeType.Cash)
            {
                if (a.Value == b.Value) return false;
                if (a.Value >= 3500 && b.Value >= 3500) return false;
            }
        }
        return true;
    }
}
