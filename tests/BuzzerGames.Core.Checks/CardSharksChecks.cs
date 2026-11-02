using BuzzerGames.Core.CardSharks;
using BuzzerGames.Core.Jeopardy;

/// <summary>
/// A Card Sharks match with stacked decks. <c>deckA</c>/<c>deckB</c> list the cards that come off the top, in order ("7H 9S 3C"); the rest
/// of the deck follows in a fixed order. Questions are "Question N" with answers 10, 20, 30... across the whole match.
/// </summary>
sealed class SharksTable
{
    public CardSharksGame Game { get; }
    public Guid Emcee { get; }

    public SharksTable(string deckA = "", string deckB = "", int seed = 1, bool start = true)
    {
        Game = new CardSharksGame("TEST", new FixedCardSharksContent(), new Random(seed), team => Stack(team == 0 ? deckA : deckB));
        Emcee = Game.JoinEmcee("Host").Token;
        if (start) Check.Ok(Game.Start(Emcee));
    }

    private static List<Card> Stack(string top)
    {
        var listed = top.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(ParseCard).ToList();
        return [.. listed, .. Card.FullDeck().Where(c => !listed.Contains(c))];
    }

    public static Card ParseCard(string s)
    {
        var suit = s[^1];
        var rank = s[..^1] switch { "J" => 11, "Q" => 12, "K" => 13, "A" => 14, var n => int.Parse(n) };
        return new Card(rank, suit);
    }

    public CardSnapshot S => Game.Snapshot();

    /// <summary>The guessing team's number and the other team's call, arranged so that <paramref name="team"/> ends up in control. Then Continue to the cards.</summary>
    public void WinControl(int team)
    {
        var s = S;
        Check.Equal(CardPhase.Guess, s.Phase, "a question is waiting for a guess");
        var low = s.Answer < 50;
        var guess = low ? 50 : 0;                       // the real answer is lower than 50 or at least 50, so the direction is known
        Check.Ok(Game.TakeGuess(Emcee, guess));
        // The caller is right by calling the true direction: lower when the answer is below the guess.
        var callerWins = team != s.Guesser;
        var trueDirectionIsHigher = !low;
        Check.Ok(Game.Call(Emcee, callerWins ? trueDirectionIsHigher : !trueDirectionIsHigher));
        Check.Equal(team, S.Control, "the right team has control");
        Check.Ok(Game.Continue(Emcee));
    }

    public RowInfo Row(int team) => S.Rows[team];

    /// <summary>Control, keep the first card and call higher until the row is complete. Needs a rising deck for that team.</summary>
    public void WinGame(int team)
    {
        WinControl(team);
        if (S.Phase == CardPhase.Offer) Check.Ok(Game.Keep(Emcee));
        var length = S.Rows[team].Length;
        for (var i = 1; i < length; i++) Check.Ok(Game.HigherLower(Emcee, true));
        Check.Equal(CardPhase.GameOver, S.Phase, "the row is complete");
        Check.Equal(team, S.GameWinner, "the right team won");
    }

    public const string Rising = "2H 3H 4H 5H 6H 7H 8H";
}

static class CardSharksChecks
{
    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine();
        Console.WriteLine("Card Sharks: setting up and the question");

        run("sharks: an emcee, two named teams, and Start", () =>
        {
            var game = new CardSharksGame("TEST", new FixedCardSharksContent(), new Random(1));
            Check.Fails(game.Start(Guid.NewGuid()), "a stranger starting");
            Check.True(!game.Snapshot().CanStart, "no Start without an emcee");
            var emcee = game.JoinEmcee("Host");
            Check.Ok(emcee);
            Check.Fails(game.JoinEmcee("Another"), "a second emcee");
            Check.Equal(Role.Emcee, game.GetSeat(emcee.Token).Role, "the emcee's seat");
            Check.Equal(Role.Spectator, game.GetSeat(Guid.NewGuid()).Role, "a stranger's seat");
            Check.True(game.Snapshot().CanStart, "Start is available");
            Check.Fails(game.SetTeamNames(Guid.NewGuid(), "X", "Y"), "a stranger naming the teams");
            Check.Fails(game.SetTeamNames(emcee.Token, "Same", "same"), "two teams with one name");
            Check.Ok(game.SetTeamNames(emcee.Token, "Reds", "Blues"));
            Check.Ok(game.Start(emcee.Token));
            Check.Equal("Reds", game.Snapshot().TeamName(0), "team names stick");
            Check.Fails(game.Start(emcee.Token), "starting twice");
            Check.Fails(game.SetTeamNames(emcee.Token, "Late", "Late2"), "renaming after the start");
            var s = game.Snapshot();
            Check.Equal(CardPhase.Guess, s.Phase, "the first question is up");
            Check.Equal("Game 1 · Question 1 of 4", s.Label, "label");
            Check.Equal(0, s.Guesser, "Team A guesses first");
            Check.Equal(7, s.Rows[0].Length, "a full row is seven cards");
            Check.True(s.Rows.All(r => r.Cards.Count == 0), "every card is still face down");
        });

        run("sharks: the guessing team gives a number from 0 to 100; the other team calls higher or lower; the survey answer stays hidden until the call", () =>
        {
            var t = new SharksTable();
            Check.True(!t.S.AnswerShown, "the answer is hidden");
            Check.Fails(t.Game.Call(t.Emcee, true), "calling before a guess");
            Check.Fails(t.Game.TakeGuess(Guid.NewGuid(), 50), "a stranger typing the guess");
            Check.Fails(t.Game.TakeGuess(t.Emcee, -1), "below 0");
            Check.Fails(t.Game.TakeGuess(t.Emcee, 101), "above 100");
            Check.Ok(t.Game.TakeGuess(t.Emcee, 50));
            Check.Equal(CardPhase.Call, t.S.Phase, "now the other team calls");
            Check.Equal(50, t.S.Guess, "the guess is on screen");
            Check.True(!t.S.AnswerShown, "still hidden");
            Check.Fails(t.Game.TakeGuess(t.Emcee, 60), "guessing twice");
            Check.Ok(t.Game.Call(t.Emcee, false));                          // answer is 10, lower: Team B is right
            var s = t.S;
            Check.True(s.AnswerShown && s.Answer == 10, "now the answer is shown");
            Check.Equal(1, s.Control, "Team B was right, so Team B has control");
            Check.Equal(CardPhase.Verdict, s.Phase, "verdict");
        });

        run("sharks: a wrong call or an exact guess leaves control with the guesser", () =>
        {
            var t = new SharksTable();
            Check.Ok(t.Game.TakeGuess(t.Emcee, 50));
            Check.Ok(t.Game.Call(t.Emcee, true));                            // answer 10 is lower, so the call is wrong
            Check.Equal(0, t.S.Control, "a wrong call keeps control with the guesser");

            var exact = new SharksTable();
            Check.Ok(exact.Game.TakeGuess(exact.Emcee, 10));
            Check.Ok(exact.Game.Call(exact.Emcee, false));
            Check.Equal(0, exact.S.Control, "an exact guess goes to the guesser, whichever way the other team calls");
            var exact2 = new SharksTable();
            Check.Ok(exact2.Game.TakeGuess(exact2.Emcee, 10));
            Check.Ok(exact2.Game.Call(exact2.Emcee, true));
            Check.Equal(0, exact2.S.Control, "even calling higher");
        });

        run("sharks: the guessing team alternates every question, and the first guesser alternates every game", () =>
        {
            var t = new SharksTable("9H 2C 9H 2C 9H 2C", "9H 2C 9H 2C 9H 2C");
            var guessers = new List<int> { t.S.Guesser };
            for (var q = 0; q < 2; q++)
            {
                t.WinControl(0);
                Check.Ok(t.Game.Keep(t.Emcee)); Check.Ok(t.Game.HigherLower(t.Emcee, true));   // fails: 9 then 2
                Check.Ok(t.Game.Continue(t.Emcee));
                Check.Ok(t.Game.HigherLower(t.Emcee, true));                                   // the other team fails too
                Check.Ok(t.Game.Continue(t.Emcee));
                guessers.Add(t.S.Guesser);
            }
            Check.True(guessers.SequenceEqual([0, 1, 0]), $"A, B, A: {string.Join(",", guessers)}");
        });

        Console.WriteLine();
        Console.WriteLine("Card Sharks: playing the cards");

        run("sharks: the team in control sees its first card and may keep it or swap it once, for the next card off its own deck", () =>
        {
            var t = new SharksTable("7H 9S 3C KD AS", "5D 6D");
            t.WinControl(0);
            var s = t.S;
            Check.Equal(CardPhase.Offer, s.Phase, "keep or change");
            Check.True(s.Rows[0].Cards.SequenceEqual([SharksTable.ParseCard("7H")]), "the first card from Team A's deck");
            Check.True(s.Rows[1].Cards.Count == 0, "Team B's cards are still down");
            Check.Fails(t.Game.HigherLower(t.Emcee, true), "playing before deciding");
            Check.Fails(t.Game.Keep(Guid.NewGuid()), "a stranger deciding");
            Check.Ok(t.Game.Change(t.Emcee));
            Check.True(t.Row(0).Cards.SequenceEqual([SharksTable.ParseCard("9S")]), "swapped for the next card off the deck");
            Check.Equal(CardPhase.Play, t.S.Phase, "now they play");
            Check.Fails(t.Game.Change(t.Emcee), "swapping twice");
            Check.Fails(t.Game.Keep(t.Emcee), "keeping after playing has begun");
            Check.Equal(0, t.S.Turn, "Team A is up");
        });

        run("sharks: a right call moves on; a wrong call or a tie removes the cards, then the other team plays its row without a swap", () =>
        {
            var t = new SharksTable("7H 9S 3C 8D 8S", "5D KC 2H");
            t.WinControl(0);
            Check.Ok(t.Game.Keep(t.Emcee));
            Check.Ok(t.Game.HigherLower(t.Emcee, true));                      // 9S is higher than 7H: right
            Check.Equal(2, t.Row(0).Cards.Count, "one card moved up");
            Check.Equal(CardPhase.Play, t.S.Phase, "still playing");
            Check.Ok(t.Game.HigherLower(t.Emcee, true));                      // 3C is lower: wrong
            Check.Equal(CardPhase.TurnOver, t.S.Phase, "turn over");
            Check.True(t.Row(0).Failed && t.Row(0).Cards.Count == 3, "the losing card is still showing");
            Check.True(t.S.Message!.Contains("loses its cards"), $"message: {t.S.Message}");
            Check.Fails(t.Game.HigherLower(t.Emcee, true), "playing after the turn is over");
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(0, t.Row(0).Cards.Count, "every card was removed");
            Check.Equal(CardPhase.Play, t.S.Phase, "the other team plays at once: no swap offered");
            Check.Equal(1, t.S.Turn, "Team B is up");
            Check.True(t.Row(1).Cards.SequenceEqual([SharksTable.ParseCard("5D")]), "Team B's own deck");
            Check.Fails(t.Game.Change(t.Emcee), "Team B swapping its first card");
            Check.Ok(t.Game.HigherLower(t.Emcee, true));                      // KC: right
            Check.Ok(t.Game.Freeze(t.Emcee));                                  // the other team may freeze too
            Check.Equal(CardPhase.TurnOver, t.S.Phase, "froze");
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(2, t.S.QuestionNumber, "next question");
            Check.Equal(CardPhase.Guess, t.S.Phase, "waiting for a guess");
            Check.Equal(1, t.Row(1).Cards.Count - 1 + 0, "Team B kept its progress");
        });

        run("sharks: a tie is a failure", () =>
        {
            var t = new SharksTable("7H 7S", "5D");
            t.WinControl(0);
            Check.Ok(t.Game.Keep(t.Emcee));
            Check.Ok(t.Game.HigherLower(t.Emcee, true));
            Check.True(t.Row(0).Failed, "7 then 7 loses");
            Check.True(t.S.Message!.Contains("tie"), $"message: {t.S.Message}");
        });

        run("sharks: freeze needs a correct call first, protects the cards, and later failures fall back to the frozen card", () =>
        {
            var t = new SharksTable("7H 9S KD 2C 4D 5H", "5D 2C 5H");
            t.WinControl(0);
            Check.Ok(t.Game.Keep(t.Emcee));
            Check.True(!t.S.CanFreeze, "no freezing straight away");
            Check.Fails(t.Game.Freeze(t.Emcee), "freezing before getting one right");
            Check.Ok(t.Game.HigherLower(t.Emcee, true));                      // 9S
            Check.Ok(t.Game.HigherLower(t.Emcee, true));                      // KD
            Check.True(t.S.CanFreeze, "now they can freeze");
            Check.Fails(t.Game.Freeze(Guid.NewGuid()), "a stranger freezing");
            Check.Ok(t.Game.Freeze(t.Emcee));
            Check.Equal(2, t.Row(0).Frozen, "frozen on the third card");
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(CardPhase.Guess, t.S.Phase, "a freeze ends the turn: the other team doesn't play");
            Check.Equal(0, t.Row(1).Cards.Count, "Team B's cards never came out");
            // Next question: Team A wins control again, picks up from the frozen card (no swap), and misses.
            t.WinControl(0);
            Check.Equal(CardPhase.Play, t.S.Phase, "no swap offer when cards are already out");
            Check.Equal(3, t.Row(0).Cards.Count, "the frozen cards are still there");
            Check.Ok(t.Game.HigherLower(t.Emcee, true));                      // 2C after a king: wrong
            Check.True(t.Row(0).Failed, "missed");
            Check.True(t.S.Message!.Contains("frozen"), $"message: {t.S.Message}");
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(3, t.Row(0).Cards.Count, "falls back to the frozen card, not to nothing");
            Check.True(t.Row(0).Cards[^1].Equals(SharksTable.ParseCard("KD")), "the king is still showing");
        });

        run("sharks: completing the row wins the game; a team that finishes in its second turn wins too", () =>
        {
            var t = new SharksTable(SharksTable.Rising, SharksTable.Rising);
            t.WinGame(0);
            Check.True(t.S.Message!.Contains("wins the game"), $"message: {t.S.Message}");
            Check.Equal(1, t.S.Rows[0].GamesWon, "one game for Team A");
            Check.Fails(t.Game.HigherLower(t.Emcee, true), "playing after the game is over");

            var second = new SharksTable("9H 2C", SharksTable.Rising);
            second.WinControl(0);
            Check.Ok(second.Game.Keep(second.Emcee)); Check.Ok(second.Game.HigherLower(second.Emcee, true));   // fails
            Check.Ok(second.Game.Continue(second.Emcee));
            for (var i = 1; i < 7; i++) Check.Ok(second.Game.HigherLower(second.Emcee, true));
            Check.Equal(1, second.S.GameWinner, "Team B won on its turn after Team A failed");
        });

        Console.WriteLine();
        Console.WriteLine("Card Sharks: sudden death and the match");

        run("sharks: question four is sudden death: the winner of control chooses who finishes; no freezing; finishing wins, failing loses", () =>
        {
            // Both teams miss on the first three questions (each draws "9H 2C" per question), then a rising run for the fourth.
            const string deck = "9H 2C 9H 2C 9H 2C 2D 3D 4D 5D 6D 7D 8D";
            foreach (var controllerPlays in new[] { true, false })
            {
                var t = new SharksTable(deck, deck);
                for (var q = 1; q <= 3; q++)
                {
                    t.WinControl(0);
                    Check.Ok(t.Game.Keep(t.Emcee)); Check.Ok(t.Game.HigherLower(t.Emcee, true)); Check.Ok(t.Game.Continue(t.Emcee));
                    Check.Ok(t.Game.HigherLower(t.Emcee, true)); Check.Ok(t.Game.Continue(t.Emcee));
                }
                var s = t.S;
                Check.True(s.SuddenDeath && s.Label.Contains("Sudden death"), $"sudden death: {s.Label}");
                Check.True(s.Message!.Contains("SUDDEN DEATH"), "the question is announced");
                t.WinControl(0);
                Check.Equal(CardPhase.SuddenPick, t.S.Phase, "the team in control chooses");
                Check.Fails(t.Game.ChooseFinisher(Guid.NewGuid(), true), "a stranger choosing");
                Check.Ok(t.Game.ChooseFinisher(t.Emcee, controllerPlays));
                var finisher = controllerPlays ? 0 : 1;
                if (controllerPlays)
                {
                    Check.Equal(CardPhase.Offer, t.S.Phase, "the controlling team may still swap its untouched first card");
                    Check.Ok(t.Game.Keep(t.Emcee));
                }
                Check.Equal(CardPhase.Play, t.S.Phase, "playing");
                Check.Equal(finisher, t.S.Turn, "the chosen team plays");
                Check.True(!t.S.CanFreeze, "no freezing");
                Check.Ok(t.Game.HigherLower(t.Emcee, true));
                Check.Fails(t.Game.Freeze(t.Emcee), "freezing in sudden death");
                for (var i = 2; i < 7; i++) Check.Ok(t.Game.HigherLower(t.Emcee, true));
                Check.Equal(finisher, t.S.GameWinner, "finishing the row wins");
            }

            var fail = new SharksTable("9H 2C 9H 2C 9H 2C 9H 2C", "9H 2C 9H 2C 9H 2C 9H 2C");
            for (var q = 1; q <= 3; q++)
            {
                fail.WinControl(0);
                Check.Ok(fail.Game.Keep(fail.Emcee)); Check.Ok(fail.Game.HigherLower(fail.Emcee, true)); Check.Ok(fail.Game.Continue(fail.Emcee));
                Check.Ok(fail.Game.HigherLower(fail.Emcee, true)); Check.Ok(fail.Game.Continue(fail.Emcee));
            }
            fail.WinControl(0);
            Check.Ok(fail.Game.ChooseFinisher(fail.Emcee, false));            // make Team B finish: base 9H, then 2C
            Check.Ok(fail.Game.HigherLower(fail.Emcee, true));
            Check.Equal(CardPhase.GameOver, fail.S.Phase, "one miss ends it");
            Check.Equal(0, fail.S.GameWinner, "the team that picked the finisher wins when they miss");
        });

        run("sharks: a team that wins the sudden death question's control but picks itself and misses loses the game", () =>
        {
            var t = new SharksTable("9H 2C 9H 2C 9H 2C 9H 2C", "9H 2C 9H 2C 9H 2C 9H 2C");
            for (var q = 1; q <= 3; q++)
            {
                t.WinControl(0);
                Check.Ok(t.Game.Keep(t.Emcee)); Check.Ok(t.Game.HigherLower(t.Emcee, true)); Check.Ok(t.Game.Continue(t.Emcee));
                Check.Ok(t.Game.HigherLower(t.Emcee, true)); Check.Ok(t.Game.Continue(t.Emcee));
            }
            t.WinControl(1);
            Check.Ok(t.Game.ChooseFinisher(t.Emcee, true));
            Check.Ok(t.Game.Keep(t.Emcee));
            Check.Ok(t.Game.HigherLower(t.Emcee, true));
            Check.Equal(0, t.S.GameWinner, "Team B missed, so Team A wins");
        });

        run("sharks: best of three: two wins takes the match; 1-1 sends it to a four-card tiebreaker game", () =>
        {
            var t = new SharksTable(SharksTable.Rising, SharksTable.Rising);
            Check.Equal(0, t.S.Guesser, "game one starts with Team A guessing");
            t.WinGame(0);
            Check.Ok(t.Game.Continue(t.Emcee));
            var s = t.S;
            Check.Equal(2, s.GameNumber, "game two");
            Check.Equal("Game 2 · Question 1 of 4", s.Label, "label");
            Check.Equal(1, s.Guesser, "game two starts with Team B guessing");
            Check.True(s.Rows.All(r => r.Cards.Count == 0), "fresh rows");
            Check.Equal(1, s.Rows[0].GamesWon, "the game count carries on");
            t.WinGame(1);
            Check.Ok(t.Game.Continue(t.Emcee));
            s = t.S;
            Check.Equal(3, s.GameNumber, "the tiebreaker");
            Check.True(s.Label.StartsWith("Tiebreaker"), $"label: {s.Label}");
            Check.Equal(4, s.Rows[0].Length, "four cards each");
            t.WinGame(0);
            Check.Equal(2, t.S.Rows[0].GamesWon, "two for Team A");
            Check.Ok(t.Game.Continue(t.Emcee));
            s = t.S;
            Check.Equal(CardPhase.MatchOver, s.Phase, "match over");
            Check.Equal(0, s.MatchWinner, "Team A wins");
            Check.True(s.Message!.Contains("2 games to 1") && s.Message.Contains("big money"), $"message: {s.Message}");
            Check.Fails(t.Game.Restart(Guid.NewGuid()), "a stranger restarting");
            Check.Ok(t.Game.Restart(t.Emcee));
            s = t.S;
            Check.True(s.Rows.All(r => r.GamesWon == 0) && s.GameNumber == 1 && s.Phase == CardPhase.Guess, "a fresh match");

            var sweep = new SharksTable(SharksTable.Rising, SharksTable.Rising);
            sweep.WinGame(1); Check.Ok(sweep.Game.Continue(sweep.Emcee));
            sweep.WinGame(1);
            Check.Ok(sweep.Game.Continue(sweep.Emcee));
            Check.Equal(CardPhase.MatchOver, sweep.S.Phase, "2-0 ends the match without a third game");
        });

        run("sharks Money Cards: rows, carried cards, bonuses, the tie and the Big Bet", () =>
        {
            var t = new SharksTable(SharksTable.Rising, "7S 9S 3S 10S 5S 5D 8S KS 2S QS");
            Check.Fails(t.Game.StartBonus(t.Emcee), "no bonus before the match is won");
            t.WinGame(0); Check.Ok(t.Game.Continue(t.Emcee));
            t.WinGame(0); Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(CardPhase.MatchOver, t.S.Phase, "match won");
            Check.Fails(t.Game.StartBonus(Guid.NewGuid()), "a stranger starting the bonus");
            Check.Ok(t.Game.StartBonus(t.Emcee));
            var b = t.S.Bonus!;
            Check.Equal(CardPhase.BonusOffer, t.S.Phase, "base card offered");
            Check.Equal(200, b.Pot, "starts with $200");
            Check.Equal(7, b.Rows[0][0].Rank, "the first card");
            Check.Ok(t.Game.BonusKeep(t.Emcee));
            Check.Fails(t.Game.BonusPlay(t.Emcee, 49, true), "below the $50 minimum");
            Check.Fails(t.Game.BonusPlay(t.Emcee, 201, true), "more than the pot");
            Check.Ok(t.Game.BonusPlay(t.Emcee, 50, true));   // 9: right
            Check.Equal(250, t.S.Bonus!.Pot, "won $50");
            Check.Ok(t.Game.BonusPlay(t.Emcee, 100, false)); // 3: right
            Check.Ok(t.Game.BonusPlay(t.Emcee, 50, true));   // 10: right, row done
            b = t.S.Bonus!;
            Check.Equal(CardPhase.BonusOffer, t.S.Phase, "next row offers its card");
            Check.Equal(1, b.Row, "on the second row");
            Check.Equal(800, b.Pot, "$400 added to $400");
            Check.Equal(10, b.Rows[1][0].Rank, "the last card moved up");
            Check.Ok(t.Game.BonusChange(t.Emcee));            // swapped for the 5
            Check.Equal(5, t.S.Bonus!.Rows[1][0].Rank, "the changed base card");
            Check.Ok(t.Game.BonusPlay(t.Emcee, 100, true));  // 5 again: a tie loses
            Check.Equal(700, t.S.Bonus!.Pot, "a tie loses the bet");
            Check.Ok(t.Game.BonusPlay(t.Emcee, 50, true));   // 8
            Check.Ok(t.Game.BonusPlay(t.Emcee, 50, true));   // K, row done
            b = t.S.Bonus!;
            Check.Equal(2, b.Row, "the Big Bet row");
            Check.Equal(1800, b.Pot, "$1000 added");
            Check.Ok(t.Game.BonusKeep(t.Emcee));
            Check.Equal(900, t.S.Bonus!.MinBet, "at least half");
            Check.Fails(t.Game.BonusPlay(t.Emcee, 899, false), "under half");
            Check.Ok(t.Game.BonusPlay(t.Emcee, 900, false)); // 2: right
            Check.Equal(CardPhase.BonusOver, t.S.Phase, "done");
            Check.Equal(2700, t.S.Bonus!.Winnings, "wins what is in the pot");
            Check.Ok(t.Game.Restart(t.Emcee));
            Check.True(t.S.Bonus is null && t.S.Phase == CardPhase.Guess, "restart after the bonus");
        });

        run("sharks Money Cards: losing everything ends it with nothing", () =>
        {
            var t = new SharksTable(SharksTable.Rising, "7S 9S 3S");
            t.WinGame(0); Check.Ok(t.Game.Continue(t.Emcee));
            t.WinGame(0); Check.Ok(t.Game.Continue(t.Emcee));
            Check.Ok(t.Game.StartBonus(t.Emcee));
            Check.Ok(t.Game.BonusKeep(t.Emcee));
            Check.Ok(t.Game.BonusPlay(t.Emcee, 200, false)); // 9 is higher: wrong
            Check.Equal(CardPhase.BonusOver, t.S.Phase, "out of money");
            Check.Equal(0, t.S.Bonus!.Winnings, "nothing won");
        });

        run("sharks Money Cards: random play always ends with a sane prize", () =>
        {
            for (var seed = 1; seed <= 100; seed++)
            {
                var t = new SharksTable(SharksTable.Rising, seed: seed);
                t.WinGame(0); Check.Ok(t.Game.Continue(t.Emcee));
                t.WinGame(0); Check.Ok(t.Game.Continue(t.Emcee));
                Check.Ok(t.Game.StartBonus(t.Emcee));
                var rnd = new Random(seed);
                for (var steps = 0; t.S.Phase != CardPhase.BonusOver; steps++)
                {
                    Check.True(steps < 30, "the round finishes");
                    var b = t.S.Bonus!;
                    if (t.S.Phase == CardPhase.BonusOffer) Check.Ok(rnd.Next(2) == 0 ? t.Game.BonusKeep(t.Emcee) : t.Game.BonusChange(t.Emcee));
                    else Check.Ok(t.Game.BonusPlay(t.Emcee, rnd.Next(b.MinBet, b.Pot + 1), rnd.Next(2) == 0));
                }
                Check.True(t.S.Bonus!.Winnings >= 0, "never a negative prize");
            }
        });

        run("sharks: the real survey bank loads and every answer is 0 to 100", () =>
        {
            var content = new JsonCardSharksContent();
            var qs = content.CreateQuestions(new Random(1), 60);
            Check.Equal(60, qs.Count, "plenty for a long match");
            Check.True(qs.All(q => q.Text.Length > 10 && q.Answer is >= 0 and <= 100), "every question has text and an answer from 0 to 100");
            Check.True(new JsonCardSharksContent("""{ "questions": [ { "text": "ok", "answer": 50 }, { "text": "bad", "answer": 150 } ] }""").CreateQuestions(new Random(1), 5).All(q => q.Text == "ok"), "a question with an impossible answer is ignored");
        });

        run("sharks: whole matches with random play always finish, and the cards never exceed a row", () =>
        {
            for (var seed = 1; seed <= 150; seed++)
            {
                var rnd = new Random(seed);
                var t = new SharksTable(seed: seed);
                var steps = 0;
                while (t.S.Phase != CardPhase.MatchOver)
                {
                    Check.True(++steps < 4000, $"seed {seed}: the match finishes");
                    var s = t.S;
                    switch (s.Phase)
                    {
                        case CardPhase.Guess: Check.Ok(t.Game.TakeGuess(t.Emcee, rnd.Next(101))); break;
                        case CardPhase.Call: Check.Ok(t.Game.Call(t.Emcee, rnd.Next(2) == 0)); break;
                        case CardPhase.Verdict: case CardPhase.TurnOver: case CardPhase.GameOver: Check.Ok(t.Game.Continue(t.Emcee)); break;
                        case CardPhase.Offer: Check.Ok(rnd.Next(2) == 0 ? t.Game.Keep(t.Emcee) : t.Game.Change(t.Emcee)); break;
                        case CardPhase.SuddenPick: Check.Ok(t.Game.ChooseFinisher(t.Emcee, rnd.Next(2) == 0)); break;
                        case CardPhase.Play:
                            if (s.CanFreeze && rnd.Next(3) == 0) Check.Ok(t.Game.Freeze(t.Emcee));
                            else Check.Ok(t.Game.HigherLower(t.Emcee, rnd.Next(2) == 0));
                            break;
                        default: throw new Exception("unexpected phase " + s.Phase);
                    }
                    var now = t.S;
                    Check.True(now.Rows.All(r => r.Cards.Count <= r.Length), "a row never has more cards than it should");
                    Check.True(now.Rows.All(r => r.GamesWon <= 2), "nobody wins more than two games");
                }
                Check.True(t.S.MatchWinner is 0 or 1, "someone won");
                Check.True(t.S.Rows[t.S.MatchWinner].GamesWon == 2, "with two games");
            }
        });
    }
}
