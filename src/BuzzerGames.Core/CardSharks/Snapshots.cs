namespace BuzzerGames.Core.CardSharks;

public enum CardPhase
{
    Lobby,        // waiting for the emcee
    Guess,        // the emcee has read the question; the guessing team says a number and the emcee types it in
    Call,         // the other team calls higher or lower
    Verdict,      // the survey answer is up and one team has control
    Offer,        // the team in control sees its first card and may keep it or swap it
    Play,         // higher or lower on the next card; freeze to bank progress
    TurnOver,     // that team failed or froze
    SuddenPick,   // sudden death: the team in control chooses who finishes their row
    GameOver,     // somebody completed their row
    MatchOver,    // somebody won two games
    BonusOffer,   // Money Cards: the base card of a row is showing; keep it or change it
    BonusBet,     // Money Cards: the contestant names a bet and calls higher or lower
    BonusOver     // Money Cards: the last card has been turned or the money ran out
}

/// <summary>One team's row of cards. Only cards that have been turned over are listed; the rest are face down and nobody can see them.</summary>
public sealed record RowInfo(
    string Name,
    int GamesWon,
    int Length,                        // cards in a full row: 7 normally, 4 in the tiebreaker game
    IReadOnlyList<Card> Cards,         // the cards turned over, in order
    int Frozen,                        // index of the last frozen card, or -1
    bool Failed);                      // the last card turned over sank them (it goes away when the emcee continues)

/// <summary>The Money Cards bonus round. Rows are listed bottom to top; each lists only the cards turned over (the first one is the base card).</summary>
public sealed record BonusInfo(
    int Pot,
    int Row,                               // 0 = the opening row, 1 = the $400 row, 2 = the Big Bet row
    IReadOnlyList<IReadOnlyList<Card>> Rows,
    IReadOnlyList<int> RowLengths,         // cards in each row including the base card
    int MinBet,
    int Winnings);                         // the final prize once the round is over, otherwise -1

public sealed record CardSnapshot(
    string Code,
    CardPhase Phase,
    string? EmceeName,
    IReadOnlyList<RowInfo> Rows,
    int GameNumber,
    int QuestionNumber,
    bool SuddenDeath,
    string Label,
    string QuestionText,
    int Answer,                        // SECRET from the audience until AnswerShown
    bool AnswerShown,
    int? Guess,
    int Guesser,                       // the team that guessed a number (-1 before the game starts)
    bool? CalledHigher,
    int Control,                       // the team that won control of this question (-1 until known)
    int Turn,                          // the team playing its cards right now (-1 when nobody is)
    bool CanFreeze,
    int GameWinner,                    // the team that won the last game (-1 if none yet)
    int MatchWinner,
    string? Message,
    bool CanStart,
    BonusInfo? Bonus = null)
{
    public string TeamName(int team) => team >= 0 && team < Rows.Count ? Rows[team].Name : "";

    /// <summary>The team that did not guess.</summary>
    public int Caller => Guesser < 0 ? -1 : 1 - Guesser;

    public bool QuestionAsked => Phase is not (CardPhase.Lobby or CardPhase.MatchOver or CardPhase.BonusOffer or CardPhase.BonusBet or CardPhase.BonusOver) && QuestionText.Length > 0;
}
