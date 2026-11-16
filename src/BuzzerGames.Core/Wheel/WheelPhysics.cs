namespace BuzzerGames.Core.Wheel;

/// <summary>
/// Everything the screens need to animate one spin. The wheel turns clockwise, easing out, from <see cref="StartAngle"/> to
/// <see cref="EndAngle"/> over <see cref="DurationSeconds"/>, then nudges a few degrees to <see cref="SettledAngle"/> so the clicker rests
/// in the middle of the slice it landed on. Angles are cumulative degrees (they keep growing; they are not wrapped to 0-360).
/// </summary>
public sealed record SpinPlan(
    int Power,
    double ClickerAngle,         // where on the screen the clicker sits, degrees clockwise from the top
    double StartAngle,
    double Travel,
    double EndAngle,
    double SettledAngle,
    double DurationSeconds,
    WheelSlice Landed,
    IReadOnlyList<double> PegClickSeconds);   // when each peg passes the clicker, counted from the start of the spin

/// <summary>
/// The spin, as arithmetic. The slider decides how far the wheel travels (nothing else), and the landing is worked out up front, so the
/// screens just play it back. That is what makes the sweet spot exact: the wheel comes back to where it started when it travels one full turn.
/// </summary>
public static class WheelPhysics
{
    /// <summary>How far, in degrees, a spin at this power turns the wheel.</summary>
    public static double TravelFor(int power)
    {
        var p = Math.Clamp(power, 0, WheelRules.PowerMax) / (double)WheelRules.PowerMax;
        var turns = WheelRules.MinSpinTurns + p * (WheelRules.MaxSpinTurns - WheelRules.MinSpinTurns);
        return turns * 360.0;
    }

    /// <summary>The slider setting that turns the wheel exactly one full turn, so it lands where it started.</summary>
    public static int SweetSpotPower
    {
        get
        {
            var p = (1.0 - WheelRules.MinSpinTurns) / (WheelRules.MaxSpinTurns - WheelRules.MinSpinTurns);
            return (int)Math.Round(p * WheelRules.PowerMax);
        }
    }

    /// <summary>A longer spin takes longer: 2.5 seconds at the lightest, 5 at the hardest.</summary>
    public static double DurationFor(double travel)
    {
        var min = WheelRules.MinSpinTurns * 360.0;
        var max = WheelRules.MaxSpinTurns * 360.0;
        var t = Math.Clamp((travel - min) / (max - min), 0, 1);
        return 2.5 + 2.5 * t;
    }

    /// <summary>How far through the turn the wheel is, 0 to 1, after a fraction <paramref name="u"/> (0 to 1) of the time. Fast at first, then slowing.</summary>
    public static double ProgressAt(double u)
    {
        var c = Math.Clamp(u, 0, 1);
        return 1 - Math.Pow(1 - c, 3);
    }

    /// <summary>
    /// Works out a spin. <paramref name="startAngle"/> is the wheel's cumulative angle now, <paramref name="clickerAngle"/> is where the
    /// player's clicker sits on the screen, and <paramref name="jitterDegrees"/> is the random nudge already chosen by the caller.
    /// </summary>
    public static SpinPlan Plan(WheelLayout layout, double startAngle, double clickerAngle, int power, double jitterDegrees)
    {
        var travel = TravelFor(power) + jitterDegrees;
        var end = startAngle + travel;

        // A point on the wheel at angle w shows up on the screen at w + (how far the wheel has turned), so the clicker sees w = clicker - turned.
        var under = WheelLayout.Normalize(clickerAngle - end);
        var landed = layout.SliceAt(under);
        var settled = end + (under - landed.Mid);
        var duration = DurationFor(travel);

        var clicks = new List<double>();
        foreach (var peg in layout.Pegs)
        {
            // This peg is at the clicker whenever the wheel's turn equals (clicker - peg) plus any whole number of full turns.
            var first = clickerAngle - peg;
            var k = Math.Ceiling((startAngle - first) / 360.0);
            for (var turned = first + 360.0 * k; turned <= end + 1e-9; turned += 360.0)
            {
                if (turned <= startAngle + 1e-9) continue;
                var share = Math.Clamp((turned - startAngle) / travel, 0, 1);
                var u = 1 - Math.Cbrt(1 - share);
                clicks.Add(u * duration);
            }
        }
        clicks.Sort();

        return new SpinPlan(Math.Clamp(power, 0, WheelRules.PowerMax), clickerAngle, startAngle, travel, end, settled, duration, landed, clicks);
    }
}
