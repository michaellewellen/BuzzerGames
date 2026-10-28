using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Price;

/// <summary>
/// A ready-made Price Is Right show: an emcee (Host) and an audience of eight: Ann, Bob, Cy, Dee, Eve, Fay, Gus, Hal.
/// Items are "Item 1" ($100), "Item 2" ($200)... so the real price of the Nth item is N * 100.
/// </summary>
sealed class PriceTable
{
    public PriceGame Game { get; }
    public Guid Emcee { get; }
    public Guid[] Tokens { get; }
    public Guid[] Ids { get; }
    public static readonly string[] Names = ["Ann", "Bob", "Cy", "Dee", "Eve", "Fay", "Gus", "Hal"];

    public DateTime Now = new(2026, 12, 5, 19, 0, 0, DateTimeKind.Utc);
    public void Advance(double seconds) => Now = Now.AddSeconds(seconds);

    public PriceTable(int seed = 1, int audience = 8, bool start = true, int stageRounds = 6)
    {
        Game = new PriceGame("TEST", new FixedPriceItemSource(), new FixedPartingGiftSource(), new Random(seed), () => Now, stageRounds);
        Emcee = Game.JoinEmcee("Host").Token;
        Tokens = Names.Take(audience).Select(n => Game.JoinAudience(n, null, new Avatar(0, 0, 0, 0, false)).Token).ToArray();
        Ids = Tokens.Select(t => Game.GetSeat(t).PlayerId!.Value).ToArray();
        if (start) Check.Ok(Game.Start(Emcee));
    }

    public PriceSnapshot S => Game.Snapshot();
    public string NameOf(Guid? id) => S.Find(id)?.Name ?? "?";
    public Guid TokenOf(Guid id) => Tokens[Array.IndexOf(Ids, id)];

    /// <summary>Calls people down until the row has four.</summary>
    public void FillRow()
    {
        while (S.Row.Any(r => r.AudienceId is null)) Check.Ok(Game.CallDown(Emcee));
    }

    /// <summary>Row full, item out, bidding open.</summary>
    public void OpenBidding()
    {
        FillRow();
        Check.Ok(Game.ShowItem(Emcee));
        Check.Ok(Game.OpenBidding(Emcee));
    }

    /// <summary>A round is bid and won, and the winner is on stage. Returns the winner's token.</summary>
    public Guid GoOnStage()
    {
        PlayRound();
        Check.Ok(Game.Continue(Emcee));
        return TokenOf(S.OnStageId!.Value);
    }

    /// <summary>The pricing game on stage is over (skipped), and the emcee moves the show along.</summary>
    public void FinishStage()
    {
        Check.Ok(Game.SkipPricingGame(Emcee));
        Check.Ok(Game.Continue(Emcee));
    }

    /// <summary>The emcee enters each bid in turn.</summary>
    public void Bids(params int[] amounts)
    {
        foreach (var a in amounts) Check.Ok(Game.BidFor(Emcee, a));
    }

    /// <summary>Bidding is open and the current item costs <paramref name="price"/>: the person up bids safely and the rest bid wide.</summary>
    public void PlayRound()
    {
        OpenBidding();
        var item = S.Price;
        Bids(item - 10, item - 20, item - 30, item - 40);   // first bidder is closest
    }
}

static class PriceChecks
{
    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine();
        Console.WriteLine("Price Is Right: the audience");

        run("price: anyone can join the audience, any time; names are unique; one emcee", () =>
        {
            var game = new PriceGame("TEST", new FixedPriceItemSource(), new FixedPartingGiftSource(), new Random(1));
            Check.Fails(game.Start(Guid.NewGuid()), "a stranger starting the show");
            var emcee = game.JoinEmcee("Host");
            Check.Ok(emcee);
            Check.Fails(game.JoinEmcee("Another"), "a second emcee");
            Check.Fails(game.JoinAudience("", null, new Avatar(0, 0, 0, 0, false)), "an empty name");
            Check.Fails(game.JoinAudience("   ", null, new Avatar(0, 0, 0, 0, false)), "a blank name");
            Check.Ok(game.JoinAudience("Josie Williams", "Joe-see Williams", new Avatar(1, 2, 3, 4, true)));
            Check.Fails(game.JoinAudience("josie williams", null, new Avatar(0, 0, 0, 0, false)), "the same name in other letters");
            Check.Fails(game.JoinAudience("host", null, new Avatar(0, 0, 0, 0, false)), "the emcee's name");
            Check.Fails(game.Start(emcee.Token), "starting with one person in the audience");
            for (var i = 0; i < 3; i++) Check.Ok(game.JoinAudience("P" + i, null, new Avatar(0, 0, 0, 0, false)));
            Check.True(game.Snapshot().CanStart, "start appears at four");
            Check.Ok(game.Start(emcee.Token));
            Check.Ok(game.JoinAudience("Latecomer", null, new Avatar(0, 0, 0, 0, false)));
            Check.Equal(5, game.Snapshot().Audience.Count, "someone who arrives mid-show joins the audience");
            Check.Equal(Role.Emcee, game.GetSeat(emcee.Token).Role, "the emcee's seat");
            Check.Equal(Role.Spectator, game.GetSeat(Guid.NewGuid()).Role, "a stranger's seat");
        });

        run("price: avatars are cleaned up, names are tidied, and the studio has a limit", () =>
        {
            var game = new PriceGame("TEST", new FixedPriceItemSource(), new FixedPartingGiftSource(), new Random(1));
            var token = game.JoinAudience("  Zed\t  the\nGreat  ", null, new Avatar(-1, 99, 1000, -7, true)).Token;
            var me = game.Snapshot().Find(game.GetSeat(token).PlayerId)!;
            Check.Equal("Zed the Great", me.Name, "whitespace collapsed");
            Check.True(me.Avatar.Skin is >= 0 and < Avatar.Skins && me.Avatar.Hair is >= 0 and < Avatar.Hairs
                && me.Avatar.HairColor is >= 0 and < Avatar.HairColors && me.Avatar.Shirt is >= 0 and < Avatar.Shirts, "avatar numbers are in range");
            Check.Ok(game.JoinAudience(new string('x', 60), null, new Avatar(0, 0, 0, 0, false)));
            Check.True(game.Snapshot().Audience.Max(a => a.Name.Length) <= PriceGame.NameMaxLength, "names are cut to length");
            for (var i = 0; i < PriceGame.MaxAudience; i++) game.JoinAudience("Fan" + i, null, new Avatar(0, 0, 0, 0, false));
            Check.Fails(game.JoinAudience("OneMore", null, new Avatar(0, 0, 0, 0, false)), "more than the studio holds");
        });

        Console.WriteLine();
        Console.WriteLine("Price Is Right: Contestants' Row");

        run("price: come on down fills Contestants' Row with four different people, one at a time", () =>
        {
            var t = new PriceTable();
            Check.Equal(PricePhase.Callout, t.S.Phase, "phase");
            Check.Fails(t.Game.CallDown(t.Tokens[0]), "an audience member calling people down");
            Check.Fails(t.Game.ShowItem(t.Emcee), "an item before the row is full");
            var seq = t.S.AnnounceSeq;
            var called = new List<Guid>();
            for (var i = 0; i < 4; i++)
            {
                Check.Ok(t.Game.CallDown(t.Emcee));
                var s = t.S;
                Check.True(s.AnnounceSeq > seq, "the announcer speaks each time");
                seq = s.AnnounceSeq;
                Check.True(s.AnnounceFanfare, "with a fanfare");
                called.Add(s.LastCalledId!.Value);
                Check.True(s.AnnounceText.EndsWith("come on down!"), $"the call is \"{s.AnnounceText}\"");
                Check.True(s.AnnounceText.StartsWith(t.NameOf(s.LastCalledId)), "and names the person");
                Check.Equal(i + 1, s.Row.Count(r => r.AudienceId is not null), "the row fills one seat at a time");
            }
            Check.Equal(4, called.Distinct().Count(), "four different people");
            Check.Fails(t.Game.CallDown(t.Emcee), "a fifth contestant");
            Check.True(t.S.Audience.Count(a => a.InRow) == 4, "four people are flagged as in the row");
        });

        run("price: the announcer says the phonetic spelling when someone gave one", () =>
        {
            var game = new PriceGame("TEST", new FixedPriceItemSource(), new FixedPartingGiftSource(), new Random(1));
            var emcee = game.JoinEmcee("Host").Token;
            game.JoinAudience("Siobhan", "Shiv-awn", new Avatar(0, 0, 0, 0, false));
            for (var i = 0; i < 3; i++) game.JoinAudience("P" + i, null, new Avatar(0, 0, 0, 0, false));
            Check.Ok(game.Start(emcee));
            var said = new List<string>();
            for (var i = 0; i < 4; i++) { Check.Ok(game.CallDown(emcee)); said.Add(game.Snapshot().AnnounceText); }
            Check.True(said.Any(s => s.StartsWith("Shiv-awn,")), "the phonetic name is spoken");
            Check.True(!said.Any(s => s.StartsWith("Siobhan,")), "and the spelled name is not");
            Check.Equal("Siobhan", game.Snapshot().Audience.First(a => a.Name == "Siobhan").Name, "while the screen still shows the real name");
        });

        run("price: send back the person just called and someone else comes down", () =>
        {
            var t = new PriceTable();
            Check.Fails(t.Game.Reroll(t.Emcee), "rerolling before anyone was called");
            Check.Ok(t.Game.CallDown(t.Emcee));
            var first = t.S.LastCalledId!.Value;
            Check.Ok(t.Game.Reroll(t.Emcee));
            var s = t.S;
            Check.True(s.LastCalledId != first, "a different person is called");
            Check.Equal(1, s.Row.Count(r => r.AudienceId is not null), "the row still has one person");
            Check.True(!s.Find(first)!.InRow && !s.Find(first)!.Appeared, "the first person goes back to the audience, never having appeared");
            Check.Fails(t.Game.Reroll(t.Tokens[0]), "an audience member rerolling");
        });

        run("price: with only four people in the audience, a reroll puts the person back", () =>
        {
            var t = new PriceTable(audience: 4);
            for (var i = 0; i < 3; i++) Check.Ok(t.Game.CallDown(t.Emcee));
            Check.Ok(t.Game.CallDown(t.Emcee));
            var s = t.S;
            Check.Equal(4, s.Row.Count(r => r.AudienceId is not null), "everyone is in the row");
            Check.Fails(t.Game.Reroll(t.Emcee), "nobody else to call");
            Check.Equal(4, t.S.Row.Count(r => r.AudienceId is not null), "the row is unchanged");
            Check.Equal(s.LastCalledId, t.S.LastCalledId, "and so is the last person called");
        });

        Console.WriteLine();
        Console.WriteLine("Price Is Right: bidding");

        run("price: the item is described out loud, ending 'could be yours, if the price is right'", () =>
        {
            var t = new PriceTable();
            t.FillRow();
            Check.Ok(t.Game.ShowItem(t.Emcee));
            var s = t.S;
            Check.Equal(PricePhase.Reveal, s.Phase, "phase");
            Check.Equal("Item 1", s.Item!.Name, "the first item");
            Check.True(s.AnnounceText.Contains("Description 1."), "the announcer reads the description");
            Check.True(s.AnnounceText.EndsWith("This Item 1 could be yours, if the price is right!"), $"and the tag line: {s.AnnounceText}");
            Check.Fails(t.Game.Bid(t.TokenOf(s.Row[0].AudienceId!.Value), 50), "bidding before bidding opens");
            Check.Ok(t.Game.SwapItem(t.Emcee));
            Check.Equal("Item 2", t.S.Item!.Name, "swapping brings the next item");
            Check.Ok(t.Game.OpenBidding(t.Emcee));
            Check.Fails(t.Game.SwapItem(t.Emcee), "swapping once bidding has started");
        });

        run("price: the emcee can have the announcer repeat or stop talking", () =>
        {
            var t = new PriceTable();
            Check.Ok(t.Game.CallDown(t.Emcee));
            var s = t.S;
            Check.Fails(t.Game.Repeat(t.Tokens[0]), "an audience member repeating");
            Check.Ok(t.Game.Repeat(t.Emcee));
            Check.Equal(s.AnnounceSeq + 1, t.S.AnnounceSeq, "a new announcement to play");
            Check.Equal(s.AnnounceText, t.S.AnnounceText, "with the same words");
            Check.Ok(t.Game.Hush(t.Emcee));
            Check.Equal("", t.S.AnnounceText, "hushed: the TV stops talking");
            Check.Fails(t.Game.Repeat(t.Emcee), "nothing left to repeat");
        });

        run("price: contestants bid in turn from their own phones and nobody may repeat a bid", () =>
        {
            var t = new PriceTable();
            t.OpenBidding();
            var s = t.S;
            Check.Equal(0, s.BidTurnPosition, "the first round starts with the first seat");
            var people = s.Row.Select(r => r.AudienceId!.Value).ToArray();
            Check.Fails(t.Game.Bid(t.TokenOf(people[1]), 100), "bidding out of turn");
            Check.Fails(t.Game.Bid(t.Emcee, 100), "the emcee bidding as a contestant");
            var outsider = t.Ids.First(id => !people.Contains(id));
            Check.Fails(t.Game.Bid(t.TokenOf(outsider), 100), "an audience member who is not in the row");
            Check.Fails(t.Game.Bid(t.TokenOf(people[0]), 0), "a zero bid");
            Check.Fails(t.Game.Bid(t.TokenOf(people[0]), -5), "a negative bid");
            Check.Fails(t.Game.Bid(t.TokenOf(people[0]), PriceGame.MaxBid + 1), "an absurd bid");
            Check.Ok(t.Game.Bid(t.TokenOf(people[0]), 50));
            Check.Equal(1, t.S.BidTurnPosition, "then the next seat");
            Check.Fails(t.Game.Bid(t.TokenOf(people[0]), 60), "bidding twice in a row");
            Check.Fails(t.Game.Bid(t.TokenOf(people[1]), 50), "repeating someone else's bid");
            Check.Ok(t.Game.Bid(t.TokenOf(people[1]), 51));
            Check.Ok(t.Game.Bid(t.TokenOf(people[2]), 52));
            Check.Equal(3, t.S.BidTurnPosition, "the last seat");
            Check.Ok(t.Game.Bid(t.TokenOf(people[3]), 53));
            Check.Equal(PricePhase.BidResult, t.S.Phase, "four bids end the bidding");
            Check.Equal(-1, t.S.BidTurnPosition, "nobody is up");
        });

        run("price: closest without going over wins; the price is announced", () =>
        {
            var t = new PriceTable();
            t.OpenBidding();                                     // Item 1 costs $100
            t.Bids(60, 99, 101, 80);
            var s = t.S;
            var winner = s.Find(s.WinnerId)!;
            Check.Equal(s.Find(s.Row[1].AudienceId)!.Name, winner.Name, "the $99 bid wins; $101 went over");
            Check.True(!s.ExactPrice, "not exact");
            Check.True(s.AnnounceText.Contains("$100") && s.AnnounceText.Contains(winner.Name), $"the announcer says the price and the winner: {s.AnnounceText}");
            Check.True(s.AnnounceText.EndsWith("Come on up!"), "and invites the winner up");
            Check.Equal(4, s.Bids.Count, "all four bids are on the board");
        });

        run("price: an exact bid is called out", () =>
        {
            var t = new PriceTable();
            t.OpenBidding();
            t.Bids(50, 100, 60, 70);
            Check.True(t.S.ExactPrice, "right on the money");
            Check.True(t.S.AnnounceText.Contains("exactly"), "the announcer says so");
        });

        run("price: if everybody goes over, the price is under the lowest bid and everyone bids again", () =>
        {
            var t = new PriceTable();
            t.OpenBidding();                                     // $100
            t.Bids(300, 250, 400, 200);
            var s = t.S;
            Check.Equal(PricePhase.Bidding, s.Phase, "still bidding");
            Check.Equal(200, s.BidCeiling, "the price is below the lowest bid");
            Check.Equal(0, s.Bids.Count, "the bids are cleared");
            Check.Equal(0, s.BidTurnPosition, "the same order again");
            Check.True(s.Message!.Contains("less than $200"), "the message says so");
            Check.Fails(t.Game.BidFor(t.Emcee, 200), "bidding the ceiling");
            Check.Fails(t.Game.BidFor(t.Emcee, 500), "bidding above the ceiling");
            Check.Ok(t.Game.BidFor(t.Emcee, 199));
            Check.Ok(t.Game.BidFor(t.Emcee, 150));
            Check.Ok(t.Game.BidFor(t.Emcee, 120));
            Check.Ok(t.Game.BidFor(t.Emcee, 90));
            Check.Equal(PricePhase.BidResult, t.S.Phase, "now someone wins");
            Check.Equal(t.S.Row[3].AudienceId, t.S.WinnerId, "the $90 bid is closest without going over");
        });

        run("price: the first bidder rotates from one round to the next", () =>
        {
            var t = new PriceTable();
            var starts = new List<int>();
            for (var round = 0; round < 3; round++)
            {
                if (t.S.Phase == PricePhase.Callout && round > 0) { /* row refilled below */ }
                t.OpenBidding();
                starts.Add(t.S.BidTurnPosition);
                var price = t.S.Price;
                t.Bids(price - 1, price - 2, price - 3, price - 4);
                Check.Ok(t.Game.Continue(t.Emcee));              // winner goes on stage
                t.FinishStage();                                 // and is done
            }
            Check.True(starts.SequenceEqual([0, 1, 2]), $"first bidders were {string.Join(",", starts)}");
        });

        run("price: the emcee can bid for someone whose phone died", () =>
        {
            var t = new PriceTable();
            t.OpenBidding();
            Check.Fails(t.Game.BidFor(t.Tokens[0], 50), "an audience member using the emcee's bid");
            Check.Ok(t.Game.BidFor(t.Emcee, 50));
            Check.Equal(1, t.S.Bids.Count, "the bid counted");
        });

        Console.WriteLine();
        Console.WriteLine("Price Is Right: the show");

        run("price: the winner goes on stage, the other three stay, and one more person is called down", () =>
        {
            var t = new PriceTable();
            t.PlayRound();
            var winner = t.S.WinnerId!.Value;
            var others = t.S.Row.Select(r => r.AudienceId!.Value).Where(id => id != winner).ToList();
            Check.Ok(t.Game.Continue(t.Emcee));
            var s = t.S;
            Check.Equal(PricePhase.OnStage, s.Phase, "on stage");
            Check.Equal(winner, s.OnStageId, "the winner is up there");
            Check.True(s.Row.All(r => r.AudienceId != winner), "no longer in the row");
            Check.True(s.AnnounceText.Contains("up to the stage") && s.AnnounceFanfare, "the announcer calls them up");
            Check.Fails(t.Game.CallDown(t.Emcee), "calling someone down during a pricing game");
            t.FinishStage();
            s = t.S;
            Check.Equal(PricePhase.Callout, s.Phase, "back to calling people down");
            Check.True(s.Find(winner)!.WentOnStage, "the winner has played");
            Check.True(others.All(o => s.Row.Any(r => r.AudienceId == o)), "the other three are still in the row");
            Check.Equal(1, s.Row.Count(r => r.AudienceId is null), "one seat is open");
            Check.Fails(t.Game.ShowItem(t.Emcee), "bringing out an item with an empty seat");
            Check.Ok(t.Game.CallDown(t.Emcee));
            Check.True(t.S.LastCalledId != winner, "the person who just played is not called back");
            Check.Ok(t.Game.ShowItem(t.Emcee));
            Check.Equal("Item 2", t.S.Item!.Name, "and the next item comes out");
        });

        run("price: after the last pricing game, contestants who never made it on stage get parting gifts", () =>
        {
            var t = new PriceTable(stageRounds: 2);
            t.PlayRound();
            Check.Ok(t.Game.Continue(t.Emcee)); t.FinishStage();
            Check.Ok(t.Game.CallDown(t.Emcee));
            t.Game.ShowItem(t.Emcee); t.Game.OpenBidding(t.Emcee);
            var price = t.S.Price;
            t.Bids(price - 1, price - 2, price - 3, price - 4);
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(PricePhase.OnStage, t.S.Phase, "the second winner is on stage");
            t.FinishStage();
            var s = t.S;
            Check.Equal(PricePhase.ShowOver, s.Phase, "the show is over after two pricing games");
            var expected = s.Audience.Where(a => a.Appeared && !a.WentOnStage).Select(a => a.Name).OrderBy(n => n).ToList();
            Check.True(expected.Count >= 1, "somebody came down and never played");
            Check.True(s.PartingGifts.Select(g => g.Name).OrderBy(n => n).SequenceEqual(expected), "gifts go to exactly those people");
            Check.True(s.PartingGifts.All(g => g.Gift.StartsWith("gift number")), "from the gift list");
            Check.True(s.AnnounceText.Contains("parting gifts") && s.AnnounceText.Contains("you will receive"), "the announcer reads them out");
            Check.True(s.PartingGifts.All(g => s.AnnounceText.Contains(g.Name) && s.AnnounceText.Contains(g.Gift)), "each name and gift is in the announcement");
            Check.Fails(t.Game.CallDown(t.Emcee), "carrying on after the show");
        });

        run("price: after the show the same audience can do it again", () =>
        {
            var t = new PriceTable(stageRounds: 1);
            t.PlayRound();
            Check.Fails(t.Game.Restart(t.Emcee), "restarting mid-show");
            Check.Ok(t.Game.Continue(t.Emcee)); t.FinishStage();
            Check.Equal(PricePhase.ShowOver, t.S.Phase, "over");
            Check.Fails(t.Game.Restart(t.Tokens[0]), "an audience member restarting");
            Check.Ok(t.Game.Restart(t.Emcee));
            var s = t.S;
            Check.Equal(PricePhase.Callout, s.Phase, "a fresh show");
            Check.Equal(8, s.Audience.Count, "with the same audience");
            Check.True(s.Audience.All(a => !a.WentOnStage && !a.Appeared && !a.InRow), "everyone is eligible again");
            Check.Equal(0, s.StageRound, "no pricing games played");
            Check.True(s.Row.All(r => r.AudienceId is null), "an empty row");
        });

        run("price: the emcee can send someone out of the audience, except in the middle of their round", () =>
        {
            var t = new PriceTable();
            t.OpenBidding();
            var inRow = t.S.Row[0].AudienceId!.Value;
            var bystander = t.Ids.First(id => t.S.Row.All(r => r.AudienceId != id));
            Check.Fails(t.Game.RemoveAudience(t.Tokens[0], bystander), "an audience member removing someone");
            Check.Fails(t.Game.RemoveAudience(t.Emcee, inRow), "removing a contestant who is bidding");
            Check.Ok(t.Game.RemoveAudience(t.Emcee, bystander));
            Check.Equal(7, t.S.Audience.Count, "one fewer");
            Check.Equal(Role.Spectator, t.Game.GetSeat(t.TokenOf(bystander)).Role, "they are no longer seated");
        });

        Console.WriteLine();
        Console.WriteLine("Price Is Right: the audience speaks");

        run("price: reactions are counted, slowed down, and cleared when a new bid comes in", () =>
        {
            var t = new PriceTable();
            t.OpenBidding();
            Check.Fails(t.Game.React(t.Emcee, Reaction.Cheer), "the emcee reacting");
            Check.Ok(t.Game.React(t.Tokens[0], Reaction.Higher));
            Check.Fails(t.Game.React(t.Tokens[0], Reaction.Higher), "tapping again immediately");
            t.Advance(1);
            Check.Ok(t.Game.React(t.Tokens[0], Reaction.Higher));
            Check.Ok(t.Game.React(t.Tokens[1], Reaction.Lower));
            Check.Ok(t.Game.React(t.Tokens[2], Reaction.Boo));
            Check.Ok(t.Game.React(t.Tokens[3], Reaction.Cheer));
            var r = t.S.Reactions;
            Check.Equal(1, r[(int)Reaction.Lower], "one lower");
            Check.Equal(2, r[(int)Reaction.Higher], "two higher");
            Check.Equal(2, r[(int)Reaction.Boo] + r[(int)Reaction.Cheer], "a boo and a cheer");
            Check.Ok(t.Game.BidFor(t.Emcee, 50));
            Check.True(t.S.Reactions.All(x => x == 0), "reactions start over for the next bidder");
        });

        run("price: comments are short, slowed down, tidied, and the emcee can delete them or turn them off", () =>
        {
            var t = new PriceTable();
            Check.Fails(t.Game.Comment(t.Emcee, "hello"), "the emcee commenting");
            Check.Fails(t.Game.Comment(t.Tokens[0], "   "), "an empty comment");
            Check.Ok(t.Game.Comment(t.Tokens[0], "  Go   Josie\nGO!  "));
            Check.Equal("Go Josie GO!", t.S.Comments[0].Text, "tidied");
            Check.Equal("Ann", t.S.Comments[0].Name, "and credited");
            Check.Fails(t.Game.Comment(t.Tokens[0], "again"), "two comments in a row");
            t.Advance(4);
            Check.Ok(t.Game.Comment(t.Tokens[0], new string('x', 300)));
            Check.Equal(PriceGame.CommentMaxLength, t.S.Comments[1].Text.Length, "long comments are cut");
            var id = t.S.Comments[0].Id;
            Check.Fails(t.Game.DeleteComment(t.Tokens[0], id), "an audience member deleting");
            Check.Ok(t.Game.DeleteComment(t.Emcee, id));
            Check.Equal(1, t.S.Comments.Count, "deleted");
            Check.Fails(t.Game.DeleteComment(t.Emcee, id), "deleting twice");
            for (var i = 0; i < 20; i++) { t.Advance(4); Check.Ok(t.Game.Comment(t.Tokens[1], "n" + i)); }
            Check.Equal(PriceGame.MaxComments, t.S.Comments.Count, "only the latest few are kept");
            Check.Ok(t.Game.SetComments(t.Emcee, false));
            Check.True(!t.S.CommentsOn && t.S.Comments.Count == 0, "turned off and cleared");
            t.Advance(4);
            Check.Fails(t.Game.Comment(t.Tokens[1], "hello?"), "commenting while off");
            Check.Ok(t.Game.SetComments(t.Emcee, true));
            t.Advance(4);
            Check.Ok(t.Game.Comment(t.Tokens[1], "back on"));
        });

        run("price: whole shows with random play always finish, and the row never has the same person twice", () =>
        {
            for (var seed = 1; seed <= 60; seed++)
            {
                var rnd = new Random(seed);
                var t = new PriceTable(seed, audience: rnd.Next(4, 9), stageRounds: rnd.Next(1, 5));
                for (var step = 0; step < 600 && t.S.Phase != PricePhase.ShowOver; step++)
                {
                    var s = t.S;
                    switch (s.Phase)
                    {
                        case PricePhase.Callout:
                            if (s.Row.All(r => r.AudienceId is not null)) Check.Ok(t.Game.ShowItem(t.Emcee));
                            else if (rnd.Next(5) == 0 && s.LastCalledId is not null && s.Row.Any(r => r.AudienceId == s.LastCalledId)) t.Game.Reroll(t.Emcee);
                            else Check.Ok(t.Game.CallDown(t.Emcee));
                            break;
                        case PricePhase.Reveal:
                            if (rnd.Next(4) == 0) Check.Ok(t.Game.SwapItem(t.Emcee)); else Check.Ok(t.Game.OpenBidding(t.Emcee));
                            break;
                        case PricePhase.Bidding:
                            var max = s.BidCeiling > 0 ? s.BidCeiling - 1 : 3000;
                            t.Game.BidFor(t.Emcee, rnd.Next(1, max + 1));     // a repeat is simply refused
                            break;
                        default:
                            if (s.Phase == PricePhase.OnStage) Check.Ok(t.Game.SkipPricingGame(t.Emcee)); Check.Ok(t.Game.Continue(t.Emcee));
                            break;
                    }
                    var after = t.S;
                    var inRow = after.Row.Where(r => r.AudienceId is not null).Select(r => r.AudienceId).ToList();
                    Check.Equal(inRow.Count, inRow.Distinct().Count(), "nobody sits in two seats");
                    Check.True(after.OnStageId is null || !inRow.Contains(after.OnStageId), "nobody is in the row and on stage");
                }
                Check.Equal(PricePhase.ShowOver, t.S.Phase, $"seed {seed}: the show finished");
            }
        });

        run("price: the real item bank and gift list load and make sense", () =>
        {
            var items = JsonPriceItemSource.LoadBank();
            Check.True(items.Count >= 50, $"at least 50 items, found {items.Count}");
            Check.Equal(items.Count, items.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "no repeated item names");
            Check.True(items.All(i => i.Price >= 10 && i.Description.Length > 30 && i.Icon.Length > 0), "every item has a price, a description and an icon");
            Check.True(items.All(i => i.Description.EndsWith('.') || i.Description.EndsWith('!')), "descriptions end like sentences");
            var dealt = new JsonPriceItemSource().CreateItems(new Random(1), 24);
            Check.Equal(24, dealt.Select(i => i.Name).Distinct().Count(), "a deal of 24 has no repeats");
            var gifts = TextPartingGiftSource.LoadGifts();
            Check.True(gifts.Count >= 10, "some gifts");
            Check.True(gifts.Any(g => g.Contains("Rice-A-Roni")), "the Rice-A-Roni, of course");
        });
    }
}
