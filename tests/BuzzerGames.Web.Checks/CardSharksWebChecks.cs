using BuzzerGames.Core.CardSharks;
using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Web;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Card Sharks screens: the TV must never show the survey answer early or a card that is still face down.</summary>
static class CardSharksWebChecks
{
    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine("Card Sharks screens");

        run("sharks lobby: emcee seat, team names, QR on the TV", () =>
        {
            var game = new CardSharksGame("TEST", new FixedCardSharksContent());
            Check.True(Render.Has(Render.CsLobby(game, new Seat(Role.Spectator)), "Join as Emcee"), "spectator can join");
            var tv = Render.CsLobby(game, new Seat(Role.Spectator), boardMode: true);
            Check.True(!Render.Has(tv, "Run the game"), "the TV never takes a seat");
            Check.True(Render.Has(tv, "Scan to join"), "the TV shows the QR");
            var token = game.JoinEmcee("Host").Token;
            Check.True(Render.Has(Render.CsLobby(game, new Seat(Role.Emcee), token), "Start Game"), "emcee can start");
        });

        run("sharks wiring: the real service setup builds the game", () =>
        {
            var services = new ServiceCollection().AddCardSharks().BuildServiceProvider();
            var game = services.GetRequiredService<CardSharksRoomManager>().Create();
            Check.True(game.Snapshot().Phase == CardPhase.Lobby, "a new room is in the lobby");
            var content = services.GetRequiredService<ICardSharksContent>().CreateQuestions(new Random(1), 4);
            Check.Equal(4, content.Count, "the bank deals questions");
        });

        run("sharks TV never shows the survey answer before the call, nor face-down cards", () =>
        {
            var t = new SharksTable("2H 3H 4H 5H 6H 7H 8H", "2S 3S 4S 5S 6S 7S 8S");
            var s = t.S;
            var tv = Render.CsTv(s);
            Check.True(Render.Has(tv, s.QuestionText), "TV shows the question");
            Check.True(!tv.Contains("The real answer"), "no answer yet");
            Check.True(Render.Has(Render.CsEmcee(t), "The real answer"), "the emcee sees it");
            Check.Ok(t.Game.TakeGuess(t.Emcee, 50));
            Check.True(!Render.CsTv(t.S).Contains("The real answer"), "still hidden while the call is pending");
            Check.Ok(t.Game.Call(t.Emcee, t.S.Answer >= 50));
            Check.True(Render.CsTv(t.S).Contains("The real answer"), "shown once called");
            // Nothing turned over yet: no face-up cards at all.
            Check.Equal(0, System.Text.RegularExpressions.Regex.Matches(tv, "cs-card face").Count, "all cards face down");
        });

        run("sharks screens draw in every phase through a fuzzed game", () =>
        {
            for (var seed = 1; seed <= 25; seed++)
            {
                var t = new SharksTable(seed: seed);
                var rng = new Random(seed);
                for (var step = 0; step < 400 && t.S.Phase != CardPhase.MatchOver; step++)
                {
                    var s = t.S;
                    Render.CsTv(s);
                    Render.CsEmcee(t);
                    if (!s.AnswerShown && s.Phase != CardPhase.Lobby)
                        Check.True(!Render.CsTv(s).Contains("The real answer"), "TV leaks the answer in " + s.Phase);
                    var e = t.Emcee;
                    switch (s.Phase)
                    {
                        case CardPhase.Guess: t.Game.TakeGuess(e, rng.Next(0, 101)); break;
                        case CardPhase.Call: t.Game.Call(e, rng.Next(2) == 0); break;
                        case CardPhase.Verdict: case CardPhase.TurnOver: case CardPhase.GameOver: t.Game.Continue(e); break;
                        case CardPhase.Offer: if (rng.Next(2) == 0) t.Game.Keep(e); else t.Game.Change(e); break;
                        case CardPhase.Play:
                            if (s.CanFreeze && rng.Next(3) == 0) t.Game.Freeze(e); else t.Game.HigherLower(e, rng.Next(2) == 0);
                            break;
                        case CardPhase.SuddenPick: t.Game.ChooseFinisher(e, rng.Next(2) == 0); break;
                    }
                }
                Render.CsTv(t.S);
            }
        });
        run("sharks Money Cards: the TV shows the pot and only the cards turned over; the emcee gets bet controls", () =>
        {
            var t = new SharksTable(SharksTable.Rising, "7S 9S 3S 10S 5S 5D 8S KS 2S QS");
            t.WinGame(0); t.Game.Continue(t.Emcee);
            t.WinGame(0); t.Game.Continue(t.Emcee);
            Check.True(Render.Has(Render.CsEmcee(t), "Play Money Cards"), "the emcee can start the bonus");
            Check.Ok(t.Game.StartBonus(t.Emcee));
            var tv = Render.CsTv(t.S);
            Check.True(Render.Has(tv, "$200"), "the pot is up");
            Check.True(Render.Has(tv, "BIG BET") && Render.Has(tv, "$400"), "the prize tiles are showing");
            Check.Equal(1, System.Text.RegularExpressions.Regex.Matches(tv, "cs-card face").Count, "only the base card is face up");
            Check.True(Render.Has(Render.CsEmcee(t), "Keep it"), "base card offer");
            Check.Ok(t.Game.BonusKeep(t.Emcee));
            var phone = Render.CsEmcee(t);
            Check.True(Render.Has(phone, "Higher") && Render.Has(phone, "All in"), "bet controls");
            Check.Ok(t.Game.BonusPlay(t.Emcee, 50, true));
            Check.Equal(2, System.Text.RegularExpressions.Regex.Matches(Render.CsTv(t.S), "cs-card face").Count, "two cards face up now");
        });

        run("sharks Money Cards screens draw through every phase of random rounds", () =>
        {
            for (var seed = 1; seed <= 30; seed++)
            {
                var t = new SharksTable(SharksTable.Rising, seed: seed);
                t.WinGame(0); t.Game.Continue(t.Emcee);
                t.WinGame(0); t.Game.Continue(t.Emcee);
                t.Game.StartBonus(t.Emcee);
                var rnd = new Random(seed);
                while (t.S.Phase != CardPhase.BonusOver)
                {
                    Render.CsTv(t.S); Render.CsEmcee(t);
                    var b = t.S.Bonus!;
                    if (t.S.Phase == CardPhase.BonusOffer) t.Game.BonusKeep(t.Emcee);
                    else t.Game.BonusPlay(t.Emcee, b.MinBet, rnd.Next(2) == 0);
                }
                Check.True(Render.Has(Render.CsTv(t.S), "WINNINGS"), "the result screen");
                Render.CsEmcee(t);
            }
        });
    }
}
