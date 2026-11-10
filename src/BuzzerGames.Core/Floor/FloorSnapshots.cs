namespace BuzzerGames.Core.Floor;

public enum FloorPhase
{
    Lobby,     // players join and pick a topic; the emcee builds the floor
    Choose,    // the challenger picks a neighbor to challenge
    Repick,    // the challenged player has run out of prompts and must pick a new topic
    Ready,     // both duelists are named; the emcee starts the duel
    Duel,      // clocks are running
    TimeUp,    // someone ran out (the screen is red); the emcee taps Continue
    Takeover,  // the winner is taking the loser's territory
    GameOver   // one player owns the whole floor
}

/// <summary>Why a duel ended.</summary>
public enum FloorEndReason { Clock, OutOfPrompts }

/// <summary>One player. <see cref="Topics"/> starts with their own topic; anything after it was won from someone else.</summary>
public sealed record FloorPlayerInfo(
    Guid Id,
    string Name,
    IReadOnlyList<string> Topics,
    IReadOnlyList<string> DeadTopics,
    int Squares,
    bool Alive)
{
    public string? HomeTopic => Topics.Count > 0 ? Topics[0] : null;
}

/// <summary>One square of the floor.</summary>
public sealed record FloorSquare(int Index, int Row, int Col, Guid OwnerId);

/// <summary>A topic a player could pick.</summary>
public sealed record FloorTopicChoice(string Name, int Count, bool Dead, string? TakenBy)
{
    public bool Available => !Dead && TakenBy is null;
}

/// <summary>The duel in progress. The answer is here for the emcee's phone; the big screen must only show it while <see cref="RevealAnswer"/> is true.</summary>
public sealed record FloorDuelInfo(
    Guid ChallengerId,
    Guid DefenderId,
    Guid ActiveId,
    int ChallengerMs,
    int DefenderMs,
    int ChallengerCorrect,
    int DefenderCorrect,
    bool Running,
    bool Paused,
    string Topic,
    string? PromptText,
    string? ImageKey,
    string Answer,
    IReadOnlyList<string> Alternates,
    bool RevealAnswer,
    int RevealMs,
    IReadOnlyList<string> Upcoming,
    bool CanUndo,
    int PromptsLeft);

/// <summary>What the winner is taking, so the big screen can animate it.</summary>
public sealed record FloorTakeover(Guid WinnerId, Guid LoserId, IReadOnlyList<int> Cells, IReadOnlyList<string> TopicsGained);

public sealed record FloorSnapshot(
    string Code,
    FloorPhase Phase,
    int Version,
    bool EmceeJoined,
    string? EmceeName,
    IReadOnlyList<FloorPlayerInfo> Players,
    int Rows,
    int Cols,
    IReadOnlyList<FloorSquare> Squares,
    Guid? ChallengerId,
    Guid? DefenderId,
    IReadOnlyList<Guid> Candidates,
    bool FinalDuel,
    Guid? RepickPlayerId,
    IReadOnlyList<FloorTopicChoice> Topics,
    int DeadTopicCount,
    bool Inherit,
    int DuelSeconds,
    FloorDuelInfo? Duel,
    FloorEndReason? EndReason,
    Guid? LoserId,
    FloorTakeover? Takeover,
    Guid? WinnerId,
    int DuelsPlayed,
    int MinPlayers,
    int MaxPlayers)
{
    public int Alive => Players.Count(p => p.Alive);

    public FloorPlayerInfo? Player(Guid? id) => id is null ? null : Players.FirstOrDefault(p => p.Id == id);
}
