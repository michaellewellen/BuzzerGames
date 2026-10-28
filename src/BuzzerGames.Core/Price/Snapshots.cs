namespace BuzzerGames.Core.Price;

// Frozen pictures of the game for the screens to draw. As in the other games, a snapshot carries everything, including the
// item's real price; each view decides what it may show. The TV and the audience must not see the price until the bids are in.

public enum PricePhase
{
    Lobby,      // the audience is signing in and the emcee is waiting to start the show
    Callout,    // contestants are being called down to Contestants' Row, one at a time
    Reveal,     // the announcer introduces the item up for bid
    Bidding,    // the four contestants bid in turn
    BidResult,  // the price is revealed and someone wins (or everyone went over)
    OnStage,    // the winner is up on stage for a pricing game (the games themselves come later)
    ShowOver    // parting gifts
}

public enum Reaction { Cheer, Boo, Higher, Lower }

/// <summary>How a person looks. Every number is an index into a list of looks the drawing code knows.</summary>
public sealed record Avatar(int Skin, int Hair, int HairColor, int Shirt, bool Glasses)
{
    public const int Skins = 6, Hairs = 6, HairColors = 6, Shirts = 8;

    public static Avatar Random(Random rng) =>
        new(rng.Next(Skins), rng.Next(Hairs), rng.Next(HairColors), rng.Next(Shirts), rng.Next(4) == 0);

    /// <summary>Forces every number into range, so a made-up request can't break the drawing.</summary>
    public Avatar Clean() => new(Wrap(Skin, Skins), Wrap(Hair, Hairs), Wrap(HairColor, HairColors), Wrap(Shirt, Shirts), Glasses);

    private static int Wrap(int v, int n) => ((v % n) + n) % n;
}

public sealed record AudienceInfo(
    Guid Id,
    string Name,
    Avatar Avatar,
    bool InRow,           // sitting in Contestants' Row right now
    bool WentOnStage,     // already played a pricing game this show
    bool Appeared);       // was called down at some point this show

public sealed record RowSlotInfo(int Position, Guid? AudienceId);

public sealed record BidInfo(int Position, Guid AudienceId, int Amount);

public sealed record PriceComment(int Id, string Name, string Text);

public sealed record PartingGiftInfo(string Name, string Gift);

public sealed record PriceItemInfo(string Name, string Description, string Icon, string? Image);

public sealed record PriceSnapshot(
    string Code,
    PricePhase Phase,
    string? EmceeName,
    IReadOnlyList<AudienceInfo> Audience,
    IReadOnlyList<RowSlotInfo> Row,
    Guid? LastCalledId,
    PriceItemInfo? Item,
    int ItemNumber,
    int Price,                            // SECRET until the bids are in
    IReadOnlyList<BidInfo> Bids,
    int BidTurnPosition,                  // the row position that bids next, or -1
    int BidCeiling,                       // after everyone went over: bids must be lower than this (0 = no limit)
    Guid? WinnerId,
    bool ExactPrice,                      // the winner nailed it
    Guid? OnStageId,
    int StageRound,                       // pricing games completed
    int StageRounds,                      // how many make a show
    IReadOnlyList<int> Reactions,         // counts, in the order of the Reaction enum
    IReadOnlyList<PriceComment> Comments,
    bool CommentsOn,
    int AnnounceSeq,                      // goes up each time the announcer has something to say
    string AnnounceText,
    bool AnnounceFanfare,
    IReadOnlyList<PartingGiftInfo> PartingGifts,
    string? Message,
    bool CanStart)
{
    public AudienceInfo? Find(Guid? id) => id is null ? null : Audience.FirstOrDefault(a => a.Id == id);

    public AudienceInfo? InSlot(int position) => Find(Row.FirstOrDefault(r => r.Position == position)?.AudienceId);

    public bool PriceRevealed => Phase is PricePhase.BidResult or PricePhase.OnStage or PricePhase.ShowOver;
}
