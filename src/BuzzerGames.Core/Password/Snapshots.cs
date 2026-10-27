namespace BuzzerGames.Core.Password;

// Frozen, read-only pictures of the game, just like Jeopardy's and Press Your Luck's. The pages draw from these.
// As before, a snapshot carries everything, including the secret word and the big word. Each view decides what it
// may show: the TV never shows either until they have been revealed.

public enum PasswordPhase
{
    Lobby,        // waiting for 1 emcee + 2 teams of 2
    Control,      // the team with control chooses: play (give the first clue) or pass
    Clue,         // the clue-giver on the clock types a one-word clue
    Guess,        // a clue is up; the guesser says a guess out loud and the emcee rules on it
    PuzzleGuess,  // the word was guessed; someone gets a shot at the big word
    WordResult,   // the word is done and the puzzle is still open; the emcee taps Continue
    PuzzleResult, // the puzzle is done (solved or thrown out); the emcee taps Continue
    BonusReady,   // the winning team is set up for the bonus round; the emcee taps Start
    BonusPlay,    // 60 seconds, ten words, one clue-giver facing the TV and one guesser with their back to it
    BonusResult,  // the clock ran out (or all ten were done); the emcee taps Continue
    GameOver
}

public enum WordState
{
    Waiting,  // not played yet
    Current,  // being played now
    Solved,   // guessed, so it is up on the board
    Posted    // nobody got it (or the puzzle ended), so it was put up on the board anyway
}

public enum BonusSlotState
{
    Waiting,  // not seen yet
    Current,  // on the screen now
    Passed,   // passed, and in line to come back
    Correct,
    Zapped    // a clue rule was broken, so the word is lost
}

public enum ClueOutcome { Pending, Miss, Hit, Rejected }

public sealed record PasswordPlayerInfo(
    Guid Id,
    string Name,
    int Team,
    int Slot,             // 0 = player 1, 1 = player 2
    bool IsGiver,         // gives the clues on the current word
    bool IsGuesser,       // guesses on the current word
    bool OnClock,         // it is their team's turn to clue or guess
    bool IsAttempting);   // their turn to take a shot at the big word

public sealed record PasswordTeamInfo(
    int Index,
    string Name,
    int Score,
    bool HasControl,
    bool OnClock,
    int CluesLeft,
    IReadOnlyList<Guid> PlayerIds);

public sealed record PasswordWordInfo(int Number, string Text, WordState State);

public sealed record PasswordClueInfo(int Team, string Text, ClueOutcome Outcome);

public sealed record BonusSlotInfo(char Letter, string Word, BonusSlotState State);

public sealed record PasswordSnapshot(
    string Code,
    PasswordPhase Phase,
    int PuzzleNumber,
    int PuzzleValue,
    int WordNumber,             // 1 to 5, within the puzzle
    int WordsPerPuzzle,
    int WinScore,
    string? EmceeName,
    IReadOnlyList<PasswordPlayerInfo> Players,
    IReadOnlyList<PasswordTeamInfo> Teams,
    int ControlTeam,
    int TurnTeam,               // the team whose clue or guess it is, or -1
    string PuzzleKind,          // "Person", "Place", "Thing", "Title", "Idea"
    IReadOnlyList<PasswordWordInfo> Words,
    string CurrentWord,         // SECRET: only the clue-givers and the emcee may see it
    string BigWord,             // SECRET until BigWordRevealed
    string? BigWordAlso,        // SECRET: other answers the emcee should accept
    bool BigWordRevealed,
    IReadOnlyList<PasswordClueInfo> Clues,
    Guid? AttemptingId,
    int AttemptsLeft,           // including the current one
    string? Message,
    IReadOnlyList<int> Winners,
    bool CanStart,
    int BonusTeam,                          // the team playing the bonus round, or -1
    Guid? BonusGiverId,                     // faces the TV and gives the clues
    Guid? BonusGuesserId,                   // back to the TV
    IReadOnlyList<BonusSlotInfo> BonusSlots, // SECRET: the TV only shows the current word until the round is over
    int BonusCurrent,                       // index into BonusSlots of the word on the screen, or -1
    DateTime? BonusEndsAtUtc,
    int BonusMillisLeft,                    // by the game's own clock, when this picture was taken
    int BonusSeconds,
    int BonusCorrect,
    int BonusEarned)
{
    public PasswordPlayerInfo? FindPlayer(Guid? id) => id is null ? null : Players.FirstOrDefault(p => p.Id == id);
    public PasswordPlayerInfo? Attempting => FindPlayer(AttemptingId);
    public PasswordPlayerInfo? Giver(int team) => Players.FirstOrDefault(p => p.Team == team && p.IsGiver);
    public PasswordPlayerInfo? Guesser(int team) => Players.FirstOrDefault(p => p.Team == team && p.IsGuesser);
    public string TeamName(int team) => team is >= 0 and < PasswordGame.Teams ? Teams[team].Name : "";
}
