using BuzzerGames.Core.Price;

static class PlinkoChecks
{
    /// <summary>Answers the current hi-lo item right or wrong, using the secret real price.</summary>
    public static void Answer(PriceTable t, Guid token, bool right)
    {
        var p = t.S.Plinko!;
        var item = p.Items[p.CurrentItem];
        var higher = item.ActualCents > item.ShownCents;
        Check.Ok(t.Game.PlinkoGuess(token, right ? higher : !higher));
    }

    /// <summary>Drops every chip the contestant has and lets each land. Returns the landed total.</summary>
    public static int DropAll(PriceTable t, Guid token, Random rnd)
    {
        while (t.S.Plinko!.Stage == PlinkoStage.Dropping)
        {
            Check.Ok(t.Game.PlinkoDrop(token, rnd.Next(PlinkoBoard.Slots)));
            Check.Equal(PlinkoStage.Falling, t.S.Plinko!.Stage, "a chip is falling");
            Check.Ok(t.Game.PlinkoLand(t.S.Plinko!.Drops.Count));
        }
        return t.S.Plinko!.Total;
    }

    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine();
        Console.WriteLine("Price Is Right: Plinko");

        run("plinko: the board has 13 rows (seven of eight pegs, six of seven) and nine slots paying 100, 500, 1000, 0, 10000, 0, 1000, 500, 100", () =>
        {
            Check.True(PlinkoBoard.Payouts.SequenceEqual([100, 500, 1000, 0, 10000, 0, 1000, 500, 100]), "payouts");
            var counts = Enumerable.Range(1, PlinkoBoard.Rows)
                .Select(row => Enumerable.Range(0, 17).Count(i => PlinkoBoard.HasPeg(row, i / 2.0))).ToList();
            Check.Equal(13, counts.Count, "rows");
            Check.True(counts.Where((_, i) => i % 2 == 0).All(c => c == 8), "odd rows have eight pegs");
            Check.True(counts.Where((_, i) => i % 2 == 1).All(c => c == 7), "even rows have seven pegs");
            Check.Equal(7, counts.Count(c => c == 8), "seven rows of eight");
            Check.Equal(6, counts.Count(c => c == 7), "six rows of seven");
        });

        run("plinko: a chip only moves half a step at a time, only when it hits a peg, and always lands in a real slot", () =>
        {
            var rnd = new Random(3);
            for (var slot = 0; slot < PlinkoBoard.Slots; slot++)
            {
                for (var n = 0; n < 300; n++)
                {
                    var path = PlinkoBoard.Fall(slot, rnd);
                    Check.Equal(PlinkoBoard.Rows + 1, path.Count, "path length");
                    Check.Equal((double)slot, path[0], "starts where it was dropped");
                    for (var row = 1; row <= PlinkoBoard.Rows; row++)
                    {
                        var step = path[row] - path[row - 1];
                        Check.True(PlinkoBoard.HasPeg(row, path[row - 1]) ? Math.Abs(step) == 0.5 : step == 0, $"row {row}: step {step} from {path[row - 1]}");
                        Check.True(path[row] >= 0 && path[row] <= 8, "stays on the board");
                    }
                    Check.True(path[^1] == Math.Floor(path[^1]), "lands in a slot, not on a peg");
                }
            }
            Check.Equal(0, PlinkoBoard.SlotOf(PlinkoBoard.Fall(0, rnd)), "the left wall slides to the left slot");
            Check.Equal(8, PlinkoBoard.SlotOf(PlinkoBoard.Fall(8, rnd)), "the right wall slides to the right slot");
        });

        run("plinko: dropping in the middle is the best plan, and the odds look right", () =>
        {
            var rnd = new Random(11);
            double Mean(int slot, int n) => Enumerable.Range(0, n).Average(_ => PlinkoBoard.Payouts[PlinkoBoard.SlotOf(PlinkoBoard.Fall(slot, rnd))]);
            var middle = Mean(4, 20000);
            Check.True(middle is > 2200 and < 3000, $"a chip dropped in the middle averages about $2,600, not {middle:N0}");
            Check.True(Mean(0, 200) == 100, "the edge always pays $100");
            Check.True(Mean(4, 20000) > Mean(1, 20000), "the middle beats the sides");

            var jackpots = Enumerable.Range(0, 20000).Count(_ => PlinkoBoard.SlotOf(PlinkoBoard.Fall(4, rnd)) == 4) / 20000.0;
            Check.True(jackpots is > 0.20 and < 0.25, $"about 22% of middle drops hit the $10,000 slot, not {jackpots:P1}");
        });

        run("plinko: the price tag shown is always wrong, shelf-like, and wrong in both directions", () =>
        {
            var rnd = new Random(5);
            var higher = 0; var lower = 0;
            for (var n = 0; n < 2000; n++)
            {
                var actual = rnd.Next(99, 1400) / 10 * 10 + 9;
                var shown = PlinkoBoard.WrongPrice(actual, rnd);
                Check.True(shown != actual && shown >= 29, $"shown {shown} for {actual}");
                var ratio = (double)shown / actual;
                Check.True(ratio > 0.4 && ratio < 1.6 && Math.Abs(1 - ratio) >= 0.10, $"{shown} is a believable but wrong tag for {actual}");
                if (shown > actual) higher++; else lower++;
            }
            Check.True(higher > 600 && lower > 600, "the real price is sometimes higher and sometimes lower");
            Check.Equal("$3.49", PlinkoBoard.Cents(349), "cents");
            Check.Equal("$10.99", PlinkoBoard.Cents(1099), "cents");
            Check.Equal("$0.05", PlinkoBoard.Cents(5), "cents");
        });

        run("plinko: the grocery bank loads, has enough small items, and deals four different ones", () =>
        {
            var bank = JsonPlinkoItemSource.LoadBank();
            Check.True(bank.Count >= 40, $"{bank.Count} items");
            Check.True(bank.All(i => i.Name.Length > 0 && i.Icon.Length > 0 && i.Cents is >= 50 and <= 2000), "every item has a name, an icon and a small price");
            Check.Equal(bank.Count, bank.Select(i => i.Name).Distinct().Count(), "no duplicate names");
            var source = new JsonPlinkoItemSource();
            var rnd = new Random(2);
            for (var n = 0; n < 50; n++) Check.Equal(4, source.CreateItems(rnd, 4).Select(i => i.Name).Distinct().Count(), "four different items");
        });

        run("plinko: the winner goes on stage to a game with four items, a spoken welcome, and nothing leaked", () =>
        {
            var t = new PriceTable();
            var token = t.GoOnStage();
            var s = t.S;
            Check.Equal(PricePhase.OnStage, s.Phase, "on stage");
            var p = s.Plinko!;
            Check.Equal(PlinkoStage.Guessing, p.Stage, "starts with hi-lo");
            Check.Equal(4, p.Items.Count, "four items");
            Check.True(p.Items.All(i => !i.Answered && i.ShownCents != i.ActualCents), "none answered, tags are wrong");
            Check.Equal(1, p.ChipsEarned, "one free chip");
            Check.True(s.AnnounceText.Contains("up to the stage") && s.AnnounceText.Contains("Plinko") && s.AnnounceText.Contains(p.Items[0].Name)
                && s.AnnounceText.Contains(PlinkoBoard.Cents(p.Items[0].ShownCents)) && s.AnnounceText.Contains("Higher or lower"), $"the announcer says: {s.AnnounceText}");
            Check.True(!s.AnnounceText.Contains(PlinkoBoard.Cents(p.Items[0].ActualCents)), "the announcer doesn't give the price away");
            Check.True(token != Guid.Empty, "token");
        });

        run("plinko: only the contestant on stage (or the emcee) can answer, and each right answer is a chip", () =>
        {
            var t = new PriceTable();
            var star = t.GoOnStage();
            var other = t.Tokens.First(tk => tk != star);
            Check.Fails(t.Game.PlinkoGuess(other, true), "someone else answering");
            Check.Fails(t.Game.PlinkoGuess(Guid.NewGuid(), true), "a stranger answering");
            Check.Fails(t.Game.PlinkoDrop(star, 4), "dropping before the guesses are done");
            Check.Fails(t.Game.PlinkoAim(star, 3), "aiming before the guesses are done");
            Check.Fails(t.Game.PlinkoGuessFor(star, true), "the contestant using the emcee's button");

            var first = t.S.Plinko!.Items[0];
            var right = first.ActualCents > first.ShownCents;
            Check.Ok(t.Game.PlinkoGuess(star, right));
            var s = t.S;
            Check.True(s.Plinko!.Items[0].Answered && s.Plinko.Items[0].Correct, "the first answer is right");
            Check.Equal(2, s.Plinko.ChipsEarned, "a chip for it");
            Check.True(s.AnnounceText.Contains(PlinkoBoard.Cents(first.ActualCents)) && s.AnnounceText.Contains("You win a chip") && s.AnnounceFanfare, $"the announcer reveals it: {s.AnnounceText}");
            Check.True(s.AnnounceText.Contains(s.Plinko.Items[1].Name), "and introduces the next item");

            var second = s.Plinko.Items[1];
            Check.Ok(t.Game.PlinkoGuessFor(t.Emcee, second.ActualCents < second.ShownCents));   // the emcee answers wrong on purpose
            s = t.S;
            Check.True(s.Plinko!.Items[1].Answered && !s.Plinko.Items[1].Correct, "the second is wrong");
            Check.Equal(2, s.Plinko.ChipsEarned, "no chip for a wrong answer");
            Check.True(s.AnnounceText.Contains("No chip") && !s.AnnounceFanfare, "the announcer is sympathetic");
        });

        run("plinko: right answers are worth a chip each, up to five; all wrong leaves the free chip", () =>
        {
            foreach (var rightCount in new[] { 0, 1, 2, 3, 4 })
            {
                var t = new PriceTable();
                var star = t.GoOnStage();
                for (var i = 0; i < 4; i++) Answer(t, star, right: i < rightCount);
                var p = t.S.Plinko!;
                Check.Equal(PlinkoStage.Dropping, p.Stage, "time to drop");
                Check.Equal(1 + rightCount, p.ChipsEarned, $"{rightCount} right");
                Check.Equal(1 + rightCount, p.ChipsLeft, "all chips still to drop");
                Check.True(t.S.AnnounceText.Contains($"{1 + rightCount} chip"), $"announcer counts the chips: {t.S.AnnounceText}");
                Check.Fails(t.Game.PlinkoGuess(star, true), "a fifth answer");
            }
        });

        run("plinko: aiming is live, dropping starts a fall, one chip at a time, and landing pays out", () =>
        {
            var t = new PriceTable(seed: 4);
            var star = t.GoOnStage();
            for (var i = 0; i < 4; i++) Answer(t, star, right: i < 2);     // three chips
            Check.Equal(4, t.S.Plinko!.Aim, "the chip starts above the middle");
            Check.Ok(t.Game.PlinkoAim(star, 2));
            Check.Equal(2, t.S.Plinko!.Aim, "aim moved");
            Check.Fails(t.Game.PlinkoAim(star, 2), "aiming at the same place again does nothing");
            Check.Fails(t.Game.PlinkoAim(star, 9), "slot 9");
            Check.Fails(t.Game.PlinkoAim(star, -1), "slot -1");
            Check.Fails(t.Game.PlinkoAim(t.Tokens.First(tk => tk != star), 1), "someone else aiming");
            Check.Fails(t.Game.PlinkoDrop(star, 12), "dropping from slot 12");
            Check.Fails(t.Game.PlinkoLand(1), "landing a chip that was never dropped");

            Check.Ok(t.Game.PlinkoDrop(star, 4));
            var p = t.S.Plinko!;
            Check.Equal(PlinkoStage.Falling, p.Stage, "falling");
            Check.Equal(1, p.Drops.Count, "one drop");
            Check.True(!p.Drops[0].Landed && p.Drops[0].Slot == 4 && p.Drops[0].Path.Count == 14, "dropped from the middle");
            Check.Equal(2, p.ChipsLeft, "two chips left");
            Check.Equal(0, p.Total, "nothing counts until it lands");
            Check.Fails(t.Game.PlinkoDrop(star, 4), "dropping while a chip is still falling");
            Check.Fails(t.Game.PlinkoAim(star, 5), "aiming while a chip is still falling");
            Check.Fails(t.Game.PlinkoLand(2), "landing the wrong chip");
            Check.Fails(t.Game.Continue(t.Emcee), "moving on while a chip falls");

            var payout = p.Drops[0].Payout;
            Check.Equal(PlinkoBoard.Payouts[(int)p.Drops[0].Path[^1]], payout, "the payout is what the landing slot pays");
            Check.Ok(t.Game.PlinkoLand(1));
            Check.Fails(t.Game.PlinkoLand(1), "landing the same chip twice");
            p = t.S.Plinko!;
            Check.Equal(PlinkoStage.Dropping, p.Stage, "ready for the next chip");
            Check.Equal(payout, p.Total, "the payout counts");
            Check.True(t.S.AnnounceText.Contains("2 chips left"), $"announcer: {t.S.AnnounceText}");

            DropAll(t, star, new Random(1));
            p = t.S.Plinko!;
            Check.Equal(PlinkoStage.Done, p.Stage, "all chips dropped");
            Check.Equal(3, p.Drops.Count, "three chips dropped");
            Check.Equal(p.Drops.Sum(d => d.Payout), p.Total, "the total is every payout");
            Check.True(t.S.AnnounceText.Contains(PriceGame.Money(p.Total)) && t.S.AnnounceText.Contains("in Plinko"), $"the announcer gives the total: {t.S.AnnounceText}");
            Check.Fails(t.Game.PlinkoDrop(star, 4), "a chip you don't have");
        });

        run("plinko: the emcee can drop for a contestant whose phone died, and the jackpot gets a fanfare", () =>
        {
            for (var seed = 1; seed < 200; seed++)
            {
                var t = new PriceTable(seed: seed);
                var star = t.GoOnStage();
                for (var i = 0; i < 4; i++) Check.Ok(t.Game.PlinkoGuessFor(t.Emcee, true));
                Check.Ok(t.Game.PlinkoDropFor(t.Emcee, 4));
                Check.Fails(t.Game.PlinkoDropFor(star, 4), "the contestant using the emcee's button");
                var drop = t.S.Plinko!.Drops[0];
                Check.Ok(t.Game.PlinkoLand(1));
                if (drop.Payout == 10000)
                {
                    Check.True(t.S.AnnounceFanfare && t.S.AnnounceText.Contains("jackpot"), "the jackpot gets a fanfare");
                    return;
                }
                if (drop.Payout == 0) Check.True(t.S.AnnounceText.StartsWith("Nothing there."), "zero is read plainly");
            }
            throw new Exception("in 200 shows nobody hit the jackpot");
        });

        run("plinko: Continue waits for the game, then the contestant has played and the show moves on", () =>
        {
            var t = new PriceTable();
            var star = t.GoOnStage();
            var winner = t.S.OnStageId!.Value;
            Check.Fails(t.Game.Continue(t.Emcee), "continuing before the game is played");
            for (var i = 0; i < 4; i++) Answer(t, star, right: false);
            Check.Fails(t.Game.Continue(t.Emcee), "continuing with a chip still to drop");
            DropAll(t, star, new Random(2));
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(PricePhase.Callout, t.S.Phase, "back to calling people down");
            Check.True(t.S.Plinko is null, "no game in progress");
            Check.True(t.S.Find(winner)!.WentOnStage, "the winner has played");
            Check.Fails(t.Game.PlinkoGuess(star, true), "answering after the game is over");
            Check.Fails(t.Game.PlinkoLand(1), "landing after the game is over");
        });

        run("plinko: the emcee can skip a stuck game, chips already dropped still count, and nobody else can skip", () =>
        {
            var t = new PriceTable(seed: 9);
            var star = t.GoOnStage();
            Check.Fails(t.Game.SkipPricingGame(star), "the contestant skipping");
            for (var i = 0; i < 4; i++) Answer(t, star, right: true);
            Check.Ok(t.Game.PlinkoDrop(star, 4));
            Check.Ok(t.Game.PlinkoLand(1));
            var first = t.S.Plinko!.Drops[0].Payout;
            Check.Ok(t.Game.PlinkoDrop(star, 4));                     // second chip is still in the air
            var second = t.S.Plinko!.Drops[1].Payout;
            Check.Ok(t.Game.SkipPricingGame(t.Emcee));
            var p = t.S.Plinko!;
            Check.Equal(PlinkoStage.Done, p.Stage, "done");
            Check.Equal(first + second, p.Total, "both chips count");
            Check.Equal(0, p.ChipsLeft, "no chips left");
            Check.Fails(t.Game.SkipPricingGame(t.Emcee), "skipping twice");
            Check.Ok(t.Game.Continue(t.Emcee));
        });

        run("plinko: a new contestant gets a fresh game each time, and Restart clears it", () =>
        {
            var t = new PriceTable(stageRounds: 2);
            var first = t.GoOnStage();
            var firstNames = t.S.Plinko!.Items.Select(i => i.Name).ToList();
            t.FinishStage();
            Check.True(t.S.Plinko is null, "nothing between games");
            Check.Ok(t.Game.CallDown(t.Emcee));
            t.Game.ShowItem(t.Emcee); t.Game.OpenBidding(t.Emcee);
            var price = t.S.Price;
            t.Bids(price - 1, price - 2, price - 3, price - 4);
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(PlinkoStage.Guessing, t.S.Plinko!.Stage, "a fresh game");
            Check.Equal(0, t.S.Plinko.Drops.Count, "no chips dropped yet");
            Check.True(t.S.Plinko.Items.All(i => !i.Answered), "no answers carried over");
            Check.True(first != Guid.Empty && firstNames.Count == 4, "first game had four items");
        });

        run("plinko: whole shows with random play always finish, with consistent chips and totals", () =>
        {
            for (var seed = 1; seed <= 40; seed++)
            {
                var rnd = new Random(seed);
                var t = new PriceTable(seed: seed, stageRounds: 3);
                var steps = 0;
                while (t.S.Phase != PricePhase.ShowOver)
                {
                    Check.True(++steps < 3000, "the show finishes");
                    var s = t.S;
                    switch (s.Phase)
                    {
                        case PricePhase.Callout:
                            if (s.Row.Any(r => r.AudienceId is null)) Check.Ok(t.Game.CallDown(t.Emcee)); else Check.Ok(t.Game.ShowItem(t.Emcee));
                            break;
                        case PricePhase.Reveal: Check.Ok(t.Game.OpenBidding(t.Emcee)); break;
                        case PricePhase.Bidding:
                            t.Game.BidFor(t.Emcee, rnd.Next(1, (s.BidCeiling > 0 ? s.BidCeiling - 1 : 1500) + 1));
                            break;
                        case PricePhase.BidResult: Check.Ok(t.Game.Continue(t.Emcee)); break;
                        case PricePhase.OnStage:
                            var star = t.TokenOf(s.OnStageId!.Value);
                            var p = s.Plinko!;
                            switch (p.Stage)
                            {
                                case PlinkoStage.Guessing: Check.Ok(t.Game.PlinkoGuess(star, rnd.Next(2) == 0)); break;
                                case PlinkoStage.Dropping:
                                    if (rnd.Next(3) == 0) t.Game.PlinkoAim(star, rnd.Next(9));
                                    if (rnd.Next(12) == 0) { Check.Ok(t.Game.SkipPricingGame(t.Emcee)); break; }
                                    Check.Ok(rnd.Next(2) == 0 ? t.Game.PlinkoDrop(star, rnd.Next(9)) : t.Game.PlinkoDropFor(t.Emcee, rnd.Next(9)));
                                    break;
                                case PlinkoStage.Falling: Check.Ok(t.Game.PlinkoLand(p.Falling!.Number)); break;
                                default: Check.Ok(t.Game.Continue(t.Emcee)); break;
                            }
                            var q = t.S.Plinko;
                            if (q is not null)
                            {
                                Check.True(q.Drops.Count <= q.ChipsEarned, "never more drops than chips");
                                Check.Equal(q.Drops.Where(d => d.Landed).Sum(d => d.Payout), q.Total, "total is landed payouts");
                                Check.True(q.Drops.Count(d => !d.Landed) <= 1, "at most one chip in the air");
                                Check.Equal(q.Items.Count(i => i.Answered && i.Correct) + 1, q.ChipsEarned, "chips = free + right answers");
                            }
                            break;
                        default: throw new Exception("unexpected phase " + s.Phase);
                    }
                }
            }
        });
    }
}
