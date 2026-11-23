namespace BuzzerGames.Core.Pyramid;

public enum PyramidPhase
{
    Lobby,        // the TV shows a QR code; whoever scans it and taps the button is the emcee
    Reveal,       // the six topics are face down; the emcee turns them over one at a time, reading each aloud
    Choosing,     // all six are showing; the team up says which one, and the emcee taps it
    Ready,        // a topic is picked (nothing but the pyramid on the TV); the pair line up, and the emcee reads the description and starts the clock
    Playing,      // thirty seconds, seven words: the emcee marks each one
    RoundOver,    // the clock ran out or the seven are done; the TV shows how it went
    LetterPick,   // tiebreaker: a coin flip decides who chooses between two letters
    GameWon,      // somebody won the game
    BonusReady,   // the bonus pyramid is face down and the prize is on the board
    BonusPlay,    // sixty seconds, six topics: valid, invalid or pass
    BonusOver,    // the bonus is done and the winnings are on the board
    BetweenGames, // game one is over: swap celebrities
    MatchOver     // both games and both bonus rounds are done
}

public enum PyramidTileState { Hidden, Revealed, Used }

public enum WordResult { Pending, Correct, Wrong }

public enum WordMark { Correct, Wrong, Pass }

public enum PlayKind { Topic, Letter }

/// <summary>Waiting: not shown yet. Passed: shown, skipped, and can be gone back to. Valid: guessed, and the square shows its money. Invalid: lost for good.</summary>
public enum BonusTileState { Waiting, Passed, Valid, Invalid }

public enum BonusMark { Valid, Invalid, Pass }

/// <summary>A square on the main pyramid. The title is blank until the square has been turned over, so a screen can't show what it isn't supposed to know.</summary>
public sealed record PyramidTileInfo(int Index, PyramidTileState State, string Title);

public sealed record PyramidTeamInfo(int Index, string Name, int Score, int GamesWon);

public sealed record PyramidWordInfo(string Text, WordResult Result);

/// <summary>
/// The round in play (or about to be, or just finished). The words are all here for the emcee's phone; the TV only ever shows
/// <see cref="CurrentWord"/> while the clock runs, and the whole list after it stops. The description is the emcee's script: the TV shows it only
/// for a mystery topic, and only once <see cref="DescriptionShown"/> says so.
/// </summary>
public sealed record PyramidPlayInfo(
    PlayKind Kind,
    string Label,
    string Description,
    bool Mystery,
    bool DescriptionShown,
    int Team,
    double Seconds,
    double? SecondsLeft,
    string ClockKey,
    bool TimeIsUp,
    string? CurrentWord,
    int Correct,
    int Wrong,
    int Remaining,
    IReadOnlyList<PyramidWordInfo> Words);

public sealed record PyramidTiebreakInfo(IReadOnlyList<char> Choices, int Chooser, IReadOnlyList<char> TeamLetters, IReadOnlyList<int> Scores, int Rounds);

/// <summary>A bonus square. The subject is blank while it is still waiting its turn.</summary>
public sealed record PyramidBonusTileInfo(int Index, BonusTileState State, string Subject, int Value);

public sealed record PyramidBonusInfo(
    int Team,
    int Prize,
    IReadOnlyList<PyramidBonusTileInfo> Tiles,
    int Current,
    string CurrentSubject,
    double Seconds,
    double? SecondsLeft,
    string ClockKey,
    bool TimeUp,
    int Winnings,
    bool AllSix);

/// <summary>What one team took home from one game's bonus round.</summary>
public sealed record PyramidWinning(int Game, int Team, int Prize, int Amount, bool AllSix);

public sealed record PyramidSnapshot(
    string Code,
    PyramidPhase Phase,
    bool HasEmcee,
    int GameNumber,
    int Round,                              // 1 to 3 in regulation; 0 during the tiebreaker
    int Turn,                               // 0 for the team that goes first this round, 1 for the other
    IReadOnlyList<PyramidTeamInfo> Teams,
    int Up,                                 // the team picking or playing now
    IReadOnlyList<PyramidTileInfo> Tiles,
    int Revealed,
    PyramidPlayInfo? Play,
    PyramidTiebreakInfo? Tiebreak,
    PyramidBonusInfo? Bonus,
    int? GameWinner,                        // set from GameWon through the bonus round
    IReadOnlyList<PyramidWinning> Winnings,
    string? Message,
    bool CanUndo);
