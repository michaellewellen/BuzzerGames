namespace BuzzerGames.Core.Wheel;

/// <summary>
/// Every number the Wheel of Fortune rules depend on, in one place. When the real rules change (or Mike's house rules do),
/// this is the file to edit; nothing else hard-codes a price, a count or an angle.
/// </summary>
public static class WheelRules
{
    public const int Players = 3;
    public const int NameMaxLength = 20;

    /// <summary>Regular puzzles in a game. Quick rounds and the bonus round are added around these in later steps.</summary>
    public const int RegularPuzzles = 6;

    public const int VowelCost = 250;

    /// <summary>Landing on the Million Dollar slice pays this much, like any other cash wedge. The wedge itself is the prize.</summary>
    public const int MillionSliceValue = 1500;

    public const int Wedges = 24;
    public const double WedgeDegrees = 360.0 / Wedges;

    /// <summary>
    /// The 21 cash wedges, in no particular order (the layout shuffles them). Most are under $1,500; there is one $3,500 and one $5,000.
    /// With the two Bankrupt wedges (one of them the Bankrupt / Million / Bankrupt trio) and Lose a Turn, that makes 24.
    /// </summary>
    public static readonly IReadOnlyList<int> CashValues =
    [
        500, 550, 600, 600, 650, 700, 700, 750, 800, 800, 850,
        900, 900, 1000, 1000, 1250, 1500, 2000, 2500, 3500, 5000
    ];

    // ---- The spin ----

    /// <summary>The slider goes from 0 to this.</summary>
    public const int PowerMax = 1000;

    /// <summary>A spin turns the wheel somewhere from half a turn (power 0) to one and three-quarter turns (full power).</summary>
    public const double MinSpinTurns = 0.5;
    public const double MaxSpinTurns = 1.75;

    /// <summary>
    /// The server nudges every spin by up to this many degrees either way, so the sweet spot is very likely but never a sure thing,
    /// and nobody can find it once and repeat it all night. Set to 0 for a perfectly repeatable wheel.
    /// </summary>
    public const double SpinJitterDegrees = 1.5;

    // ---- The players ----

    public static readonly IReadOnlyList<string> PlayerColors = ["Red", "Yellow", "Blue"];

    /// <summary>
    /// Where each player's clicker sits around the wheel, in degrees clockwise from the top. They are 30 degrees (two wedges) apart,
    /// so when one clicker rests in the middle of a wedge, all three do.
    /// </summary>
    public static readonly IReadOnlyList<double> ClickerAngles = [150, 180, 210];
}
