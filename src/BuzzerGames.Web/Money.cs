using System.Globalization;

namespace BuzzerGames.Web;

/// <summary>Formats scores the way the show does: $1,200, or -$400 when you're in the red.</summary>
public static class Money
{
    public static string Format(int amount) =>
        (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("N0", CultureInfo.InvariantCulture);
}
