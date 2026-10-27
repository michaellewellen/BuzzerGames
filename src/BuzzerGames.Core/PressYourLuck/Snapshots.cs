namespace BuzzerGames.Core.PressYourLuck;

// Frozen, read-only pictures of the game, just like Jeopardy's snapshots. The pages draw from these.
// As in Jeopardy, a snapshot carries everything, including answers; each view decides what it may show.

public enum PylPhase
{
    Lobby,        // waiting for 1 emcee + 3 players
    Question,     // an open question is up; buzzers are open
    Answering,    // someone buzzed; the emcee judges what they said
    Picking,      // the three choices are up; the other contestants pick one
    Reveal,       // the answer is on screen, spins have been awarded; the emcee taps Continue
    SpinReady,    // the contestant whose turn it is chooses: spin, or pass their spins
    Spinning,     // the light is running; the contestant presses to stop it
    PickCorner,   // they landed on Pick a Corner and choose one
    RoundOver,    // the Big Board round is finished; the emcee taps Continue
    GameOver
}

public sealed record PylPlayerInfo(
    Guid Id,
    string Name,
    int Score,
    int Spins,          // spins they have saved up
    int PassedSpins,    // spins passed to them that they are playing right now
    int Whammies,
    bool IsOut,         // four Whammies
    bool IsActing,      // it is their turn on the Big Board
    bool IsGuesser,     // the one who buzzed in on this question
    bool IsPicker,      // they get to pick among the three choices
    int? PickedIndex);

public sealed record QuestionInfo(
    int Number,         // within the round: 1, 2, 3
    int PerRound,
    string Category,
    string Text,
    string Answer,
    string? AlsoAccept);

public sealed record OutcomeInfo(string Kind, string Text, string? Sub, int Value = 0);

/// <summary>Everything a screen needs to run the light: it is sent once per spin.</summary>
public sealed record SpinInfo(
    int Id,
    IReadOnlyList<int> Sequence,   // which square is lit at tick 0, 1, 2...
    IReadOnlyList<int> Pages,      // which face the whole board shows at tick 0, 1, 2...
    long StartUnixMs,              // when tick 0 happens (server clock)
    long ServerNowUnixMs,          // the server clock right now, so a screen can correct for its own clock
    int TickMs);

public sealed record SpinResultInfo(
    int Id,
    Guid PlayerId,
    string PlayerName,
    int Tick,          // the tick the light stopped on
    int Face,          // the face the whole board was showing when it stopped
    int Square,        // the lit square
    int LinkedSquare,  // a second square to light with it (Big Bucks lights the square it is paying), or -1
    string Kind,       // cash, prize, corner, whammy
    string Text,
    string? Sub,
    string Message,
    bool Pending);     // true while a Pick a Corner choice is still to be made

public sealed record PylSnapshot(
    string Code,
    PylPhase Phase,
    int Round,
    int Rounds,
    string? EmceeName,
    IReadOnlyList<PylPlayerInfo> Players,
    QuestionInfo? Question,
    IReadOnlyList<string> Options,
    int CorrectIndex,
    string? GuessText,
    string? Message,                       // reveal line, or a "passes 3 spins" note
    int BoardId,                           // changes whenever a new board is dealt
    IReadOnlyList<IReadOnlyList<OutcomeInfo>> Board,
    SpinInfo? Spin,
    SpinResultInfo? LastSpin,
    Guid? ActingId,
    int ActingSpinsLeft,
    bool ActingIsPassed,
    bool CanPass,
    IReadOnlyList<Guid> PassTargets,
    IReadOnlyList<int> Corners,
    IReadOnlyList<Guid> Winners,
    bool CanStart)
{
    public PylPlayerInfo? FindPlayer(Guid? id) => id is null ? null : Players.FirstOrDefault(p => p.Id == id);
    public PylPlayerInfo? Acting => FindPlayer(ActingId);
    public PylPlayerInfo? Guesser => Players.FirstOrDefault(p => p.IsGuesser);
}
