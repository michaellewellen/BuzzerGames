namespace BuzzerGames.Core.Jeopardy;

/// <summary>
/// Decides whether the "Holiday category" box starts out ticked. The server runs on UTC, but the party
/// happens in Utah, so the month is worked out in Mountain time (December 31st at 9pm should still count).
/// </summary>
public static class HolidaySeason
{
    public static bool IsActive(DateTimeOffset now) => LocalMonth(now) == 12;

    private static int LocalMonth(DateTimeOffset now)
    {
        try
        {
            var mountain = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
            return TimeZoneInfo.ConvertTime(now, mountain).Month;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // A server without time zone data: assume standard Mountain time (UTC-7), which is close enough for a checkbox default.
            return now.ToOffset(TimeSpan.FromHours(-7)).Month;
        }
    }
}
