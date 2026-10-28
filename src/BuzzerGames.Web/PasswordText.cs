using BuzzerGames.Core.Password;

namespace BuzzerGames.Web;

/// <summary>Small wording and styling helpers shared by the Password Plus screens.</summary>
public static class PasswordText
{
    /// <summary>What the TV announces the big word is: "A PERSON", "AN IDEA", and so on.</summary>
    public static string KindPhrase(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "person" => "A PERSON",
        "place" => "A PLACE",
        "thing" => "A THING",
        "title" => "A TITLE",
        "idea" => "AN IDEA",
        _ => "A MYSTERY"
    };

    /// <summary>The CSS class that gives a team its color.</summary>
    public static string TeamClass(int team) => team == 0 ? "team-a" : "team-b";

    public static string BonusClass(BonusSlotState state) => state switch
    {
        BonusSlotState.Current => "current",
        BonusSlotState.Passed => "passed",
        BonusSlotState.Correct => "correct",
        BonusSlotState.Zapped => "zapped",
        _ => "waiting"
    };

    public static string BonusMark(BonusSlotState state) => state switch
    {
        BonusSlotState.Correct => "✓",
        BonusSlotState.Zapped => "✗",
        BonusSlotState.Passed => "pass",
        _ => "–"
    };

    public static string Money(int amount) => "$" + amount.ToString("N0");
}
