namespace BuzzerGames.Core.Feud;

public enum FeudPhase
{
    Lobby,         // the TV shows a QR code; whoever scans it is the emcee, and can name the two families
    RoundIntro,    // the round number and its worth are up; the emcee taps when ready to read the question
    FaceOffBuzz,   // one player from each family is at the laptop: left click is family 1, right click is family 2
    FaceOffAnswer, // the family that buzzed (or the other) answers; the emcee taps the matching answer or a strike
    ControlChoice, // the face-off winner plays or passes
    Play,          // the family in control keeps naming answers until they clear the board or earn three strikes
    Steal,         // three strikes: the other family gets one guess at stealing the bank
    RoundOver,     // the round has a winner and the whole board is showing; the points move when the emcee continues
    FastIntro,     // a family has 300: the emcee names the two players for Fast Money
    FastReady,     // the player is at the mic; nothing is on the clock until the emcee starts it
    FastPlay,      // 20 (or 25) seconds, five questions: the emcee types what the player says
    FastReveal,    // both players are done; the emcee reveals the answers and the points one at a time
    FastOver       // the total is on the board and so is the verdict
}

/// <summary>One square on the board. The text and points are here for the emcee's phone; the TV only shows them once <see cref="Revealed"/> (or the round is over).</summary>
public sealed record FeudAnswerInfo(int Rank, bool Revealed, string Text, int Points);

public sealed record FeudTeamInfo(int Index, string Name, int Score);

/// <summary>
/// One Fast Money answer. Text and Points are always filled in for the emcee's phone; the TV shows them only when <see cref="ShowText"/> and
/// <see cref="ShowPoints"/> say the reveal has got that far.
/// </summary>
public sealed record FeudFastEntryInfo(bool Entered, string Text, int Points, bool ShowText, bool ShowPoints);

public sealed record FeudFastInfo(
    int Team,
    IReadOnlyList<string> Players,
    int Player,                                  // 0 or 1: whose turn it is (or whose turn comes next)
    int Question,                                // 0 to 4: the question being asked now
    string QuestionText,                         // for the emcee to read out; empty unless a turn is on
    IReadOnlyList<FeudAnswerInfo> Board,         // the current question's survey answers, for the emcee to tap (empty on the TV's side of things)
    IReadOnlyList<IReadOnlyList<FeudFastEntryInfo>> Entries,   // [player][question]
    double Seconds,
    double? SecondsLeft,
    string ClockKey,
    bool TimeUp,
    int RevealStep,
    int TotalShown,
    int Total,
    int Goal,
    bool Won,
    int Buzzes);                                 // how many times the emcee has hit the buzzer in Fast Money, so a screen can tell a new one happened

public sealed record FeudSnapshot(
    string Code,
    FeudPhase Phase,
    bool HasEmcee,
    IReadOnlyList<FeudTeamInfo> Teams,
    int Round,
    int Multiplier,
    string Question,
    IReadOnlyList<FeudAnswerInfo> Answers,
    int Bank,                    // the revealed points so far, already doubled or tripled
    int Strikes,
    int StrikeEvents,            // goes up by one for every strike, in a face-off, in play or on a steal
    int Turn,                    // the family answering now in a face-off, -1 otherwise
    int Control,                 // the family playing the board, -1 until chosen
    int FaceOffWinner,
    int Stealer,
    int RoundWinner,             // set in RoundOver
    FeudFastInfo? Fast,
    string? Message,
    bool CanUndo);
