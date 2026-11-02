namespace BuzzerGames.Core.Match;

// Frozen pictures of the game for the screens to draw. As in the other games, a snapshot carries everything, secrets included
// (the celebrities' typed answers, the Super Match's top three); each view decides what it may show.

public enum MatchPhase
{
    Lobby,          // waiting for an emcee and at least two celebrities
    Ready,          // a team is up: the emcee reads the question and taps Ask
    Answering,      // the celebrities in play type their secret answers
    Guessing,       // the team (or the Super Match contestant) says their answer out loud
    Reveal,         // the celebrities show their answers one at a time and the emcee rules on each
    QuestionDone,   // the results of that question
    SuperPick,      // the winning team's contestant calls on three celebrities
    SuperResult,    // the top three are posted and the stake is known
    FinalPick,      // the contestant picks the one celebrity they will try to match exactly
    GameOver
}

/// <summary>Which kind of question is being played.</summary>
public enum MatchStage { Regular, Tiebreak, Super, Final }

public enum RevealState { Hidden, Shown, Judged }

public sealed record CelebrityInfo(
    Guid Id,
    int Number,                // seat order, 1 to N: the order they reveal in
    string Name,
    bool Playing,              // answering the current question
    bool Submitted,            // has typed something (SECRET: Answer)
    string Answer,             // SECRET until Reveal is Shown or Judged
    RevealState Reveal,
    int Outcome,               // when Judged: 1 = match, 0 = no match; in the Super Match: 0 = not on the list, 1 to 3 = which popular answer
    int SuggestedRank,         // Super Match only: the computer's guess at which popular answer this is (0 = none). The emcee decides.
    bool OutForTeam,           // has already matched for the team that is up, so sits out its remaining questions
    bool MatchedA,
    bool MatchedB);

public sealed record SuperRowInfo(int Rank, int Payout, string Answer, bool Revealed);   // Answer is SECRET until Revealed

public sealed record MatchSnapshot(
    string Code,
    MatchPhase Phase,
    MatchStage Stage,
    string? EmceeName,
    IReadOnlyList<CelebrityInfo> Celebrities,
    IReadOnlyList<string> TeamNames,
    IReadOnlyList<int> Scores,
    int Team,                           // the team that is up (-1 before the game starts)
    string Label,                       // "Question 2 of 3", "Tiebreaker", "Super Match"...
    string QuestionText,                // the question to read. SECRET from the audience until Answering (the emcee sees it in Ready)
    string Guess,                       // what the team (or the contestant) said, if the emcee typed it in
    int RevealIndex,                    // which celebrity is up to reveal (an index into the playing celebrities)
    int SuperTeam,                      // the team that won the first part (-1 until then)
    IReadOnlyList<SuperRowInfo> SuperRows,
    int SuperAmount,                    // what the first part of the Super Match paid
    int FinalStake,                     // what the contestant is playing for in the final match
    Guid? FinalCelebrityId,
    bool? FinalWon,
    string? Message,
    bool CanStart)
{
    public CelebrityInfo? Find(Guid? id) => id is null ? null : Celebrities.FirstOrDefault(c => c.Id == id);

    public IEnumerable<CelebrityInfo> PlayingNow => Celebrities.Where(c => c.Playing);

    /// <summary>The celebrity whose turn it is to reveal, if the reveal is under way.</summary>
    public CelebrityInfo? Revealing => Phase == MatchPhase.Reveal ? PlayingNow.ElementAtOrDefault(RevealIndex) : null;

    public string TeamName(int team) => team >= 0 && team < TeamNames.Count ? TeamNames[team] : "";

    /// <summary>True once the emcee has asked the question, so the room may read it.</summary>
    public bool QuestionAsked => Phase is MatchPhase.Answering or MatchPhase.Guessing or MatchPhase.Reveal or MatchPhase.QuestionDone;
}
