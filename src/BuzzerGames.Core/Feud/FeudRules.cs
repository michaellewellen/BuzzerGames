namespace BuzzerGames.Core.Feud;

/// <summary>Every number Family Feud plays by, in one place.</summary>
public static class FeudRules
{
    public const int Teams = 2;
    public const int MinAnswers = 3;
    public const int MaxAnswers = 8;                 // the board has eight squares
    public const int MaxPoints = 99;
    public const int WinScore = 300;                 // first family to this many points goes to Fast Money
    public const int StrikesAllowed = 3;
    public const int FastQuestions = 5;
    public const int FastGoal = 200;
    public const double FastSecondsFirst = 20;       // the first player
    public const double FastSecondsSecond = 25;      // the second, who has had longer to think about it
    public const int BoardsPerGame = 17;             // up to 12 regular rounds plus the 5 for Fast Money
    public const int ShortBoardAnswers = 4;          // a board with this many answers or fewer is a short one, saved for the triple rounds
    public const int ShortBoardsPerGame = 3;         // short boards dealt per game: two triple rounds at most, plus a spare for a skip
    public const int MinBoardsPerGame = 9;           // 4 regular rounds plus Fast Money
    public const int QuestionMaxLength = 160;
    public const int AnswerMaxLength = 40;
    public const int NameMaxLength = 20;

    /// <summary>Two rounds at face value, then double, then triple for every round after that.</summary>
    public static int Multiplier(int round) => round <= 2 ? 1 : round == 3 ? 2 : 3;

    public static string MultiplierLabel(int round) => Multiplier(round) switch { 2 => "DOUBLE POINTS", 3 => "TRIPLE POINTS", _ => "" };
}
