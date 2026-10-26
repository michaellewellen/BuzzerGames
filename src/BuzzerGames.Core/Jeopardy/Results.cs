namespace BuzzerGames.Core.Jeopardy;

public enum GamePhase
{
    Lobby,                 // waiting for 1 emcee + 3 players
    Board,                 // the emcee picks the next clue
    ClueOpen,              // clue is showing, anyone who hasn't missed it can buzz
    Answering,             // someone buzzed; the emcee judges their answer
    DailyDoubleWager,      // the player in control is entering a wager
    DailyDoubleAnswering,  // wager is in; the emcee judges that one player
    Reveal,                // the answer is on screen; the emcee taps Continue
    FinalWager,            // Final Jeopardy: players enter wagers
    FinalAnswer,           // Final Jeopardy: players type answers
    FinalJudging,          // Final Jeopardy: the emcee judges each answer
    GameOver
}

public enum Role { Spectator, Emcee, Player }

/// <summary>Who a device is. A spectator is anyone without a seat, such as the big screen.</summary>
public readonly record struct Seat(Role Role, Guid? PlayerId = null);

// Every action on the game returns a result instead of throwing, because "too slow, someone
// else buzzed first" is a normal thing to happen at a party, not an error.

public interface IResult
{
    bool Ok { get; }
}

public readonly record struct Result(bool Ok, string? Error = null) : IResult
{
    public static Result Success => new(true);
    public static Result Fail(string error) => new(false, error);
}

/// <summary>Returned when someone takes a seat. The token is their secret key for that seat.</summary>
public readonly record struct JoinResult(bool Ok, string? Error = null, Guid Token = default) : IResult
{
    public static JoinResult Success(Guid token) => new(true, null, token);
    public static JoinResult Fail(string error) => new(false, error);
}
