using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Match;
using BuzzerGames.Web;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Match Game screens: draws the TV, the emcee's phone and every celebrity's phone, and checks what each may show.</summary>
static class MatchWebChecks
{
    // Every celebrity types a distinctive answer like "zq3z", so a leak is easy to spot in the HTML.
    private static string Secret(int number) => $"zq{number}z";

    /// <summary>Nobody may see a typed answer before that celebrity reveals it, and the question stays off the TV until it is asked.</summary>
    private static void AssertSecrets(MatchTable t)
    {
        var s = t.S;
        if (s.Phase is MatchPhase.Lobby) return;
        var tv = Render.MgTv(s);
        var emcee = Render.MgEmcee(t);
        if (!s.QuestionAsked && s.Stage is MatchStage.Regular or MatchStage.Tiebreak && s.Phase is MatchPhase.Ready)
            Check.True(!Render.Has(tv, s.QuestionText), "the TV must not show a question that has not been asked");

        foreach (var c in s.Celebrities)
        {
            var secret = Secret(c.Number);
            if (c.Reveal == RevealState.Hidden)
            {
                Check.True(!Render.Has(tv, secret), $"TV leaks {c.Name}'s hidden answer in {s.Phase}");
                Check.True(!Render.Has(emcee, secret), $"the emcee sees {c.Name}'s hidden answer in {s.Phase}");
                for (var i = 0; i < t.Tokens.Length; i++)
                    if (i + 1 != c.Number)
                        Check.True(!Render.Has(Render.MgPlayer(t, i), secret), $"phone {i + 1} sees {c.Name}'s hidden answer in {s.Phase}");
            }
        }

        // The Super Match board: popular answers stay secret on the TV and on the phones until the emcee posts them.
        if (s.Stage == MatchStage.Super)
        {
            foreach (var row in s.SuperRows)
            {
                var visible = row.Revealed;
                Check.True(Render.Has(tv, row.Answer) == visible || Render.Has(tv, row.Answer) && s.Celebrities.Any(c => c.Answer.Contains(row.Answer)),
                    $"the TV {(visible ? "should" : "must not")} show popular answer {row.Rank} in {s.Phase}");
                if (!visible)
                    for (var i = 0; i < t.Tokens.Length; i++)
                        Check.True(!Render.Has(Render.MgPlayer(t, i), row.Answer), $"a celebrity's phone shows popular answer {row.Rank}");
            }
        }
    }

    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine();
        Console.WriteLine("Match Game screens");

        run("mg lobby: join as Emcee or Celebrity, the emcee names the teams and starts, the TV only shows the QR code", () =>
        {
            var game = new MatchGame("TEST", new FixedMatchContent(), new Random(1));
            var html = Render.MgLobby(game, new Seat(Role.Spectator));
            Check.True(Render.Has(html, "Join as Emcee") && Render.Has(html, "Join as a Celebrity"), "both join buttons");
            Check.True(Render.Has(html, "id=\"qr-TEST\""), "a place for the QR code");
            Check.True(!Render.Has(html, "Start Game"), "no Start button for a stranger");

            var emcee = game.JoinEmcee("Host");
            html = Render.MgLobby(game, new Seat(Role.Spectator));
            Check.True(Render.ButtonDisabled(html, "Join as Emcee"), "the emcee seat is taken");
            Check.True(!Render.ButtonDisabled(html, "Join as a Celebrity"), "celebrity seats are never full");

            html = Render.MgLobby(game, game.GetSeat(emcee.Token), emcee.Token);
            Check.True(Render.Has(html, "Waiting for at least two celebrities") && !Render.Has(html, "Start Game"), "no Start with no celebrities");
            game.JoinCelebrity("Ann");
            game.JoinCelebrity("Bob");
            html = Render.MgLobby(game, game.GetSeat(emcee.Token), emcee.Token);
            Check.True(Render.Has(html, "Start Game") && Render.Has(html, "Save team names"), "the emcee can start and name the teams");
            Check.True(Render.Has(html, "Remove Ann") && Render.Has(html, "Remove Bob"), "and remove celebrities");
            Check.True(Render.Has(html, "Celebrity 1") && Render.Has(html, "Ann"), "the roster lists them");

            var cel = game.GetSeat(game.JoinCelebrity("Cy").Token);
            html = Render.MgLobby(game, cel);
            Check.True(Render.Has(html, "You're in!") && !Render.Has(html, "Start Game") && !Render.Has(html, "Remove "), "a celebrity just waits");

            html = Render.MgLobby(game, new Seat(Role.Spectator), boardMode: true);
            Check.True(Render.Has(html, "Scan the QR code") && Render.Has(html, "board-mode"), "the TV tells people to scan");
            Check.True(!Render.Has(html, "Join as Emcee") && !Render.Has(html, "Start Game"), "and never offers a seat or Start");
        });

        run("mg room page: no stray '_error', TV and phone addresses differ, unknown code is not found, late joiners are offered a seat", () =>
        {
            var rooms = new MatchRoomManager(new FixedMatchContent());
            var game = rooms.Create();
            foreach (var board in new[] { false, true })
            {
                var html = Render.MgRoom(rooms, game.Code, board);
                Check.True(!Render.Has(html, "_error") && !Render.Has(html, "class=\"error\""), $"no stray error text (board={board})");
            }
            Check.True(!Render.Has(Render.MgRoom(rooms, game.Code, board: false), "board-mode"), "phones don't get the TV lobby");

            var emcee = game.JoinEmcee("Host").Token;
            game.JoinCelebrity("Ann"); game.JoinCelebrity("Bob");
            Check.Ok(game.Start(emcee));
            var tv = Render.MgRoom(rooms, game.Code, board: true);
            Check.True(Render.Has(tv, "mg-tv") && Render.Count(tv, "class=\"mg-desk ") == 2, "the TV shows the studio with two desks");
            Check.True(!Render.Has(tv, "Join as"), "no join buttons on the TV");
            var late = Render.MgLate();
            Check.True(Render.Has(late, "Join as a celebrity") && Render.Has(late, "will be in from the next question") || Render.ShowsAnyCase(late, "you'll be in from the next question"), "a latecomer is offered a seat on the panel");
            Check.True(Render.Has(Render.MgRoom(rooms, "ZZZZ", board: true), "Game not found"), "unknown code");
        });

        run("mg tv and phones: a question from asking to the results", () =>
        {
            var t = new MatchTable(celebrities: 4);
            var tv = Render.MgTv(t.S);
            Check.True(Render.Has(tv, "Team A is up!"), "the TV announces the team");
            Check.True(Render.Has(Render.MgEmcee(t), t.S.QuestionText) && Render.Has(Render.MgEmcee(t), "Ask it"), "the emcee reads the question and asks it");
            Check.True(Render.Has(Render.MgPlayer(t, 0), "Get ready"), "celebrities wait");

            Check.Ok(t.Game.AskQuestion(t.Emcee));
            Check.True(Render.Has(Render.MgTv(t.S), t.S.QuestionText), "the question is on the TV once asked");
            Check.True(Render.Has(Render.MgPlayer(t, 0), "Lock it in") && Render.Has(Render.MgPlayer(t, 0), "mg-answer-input"), "a celebrity gets a box to type in");
            Check.True(Render.ShowsAnyCase(Render.MgTv(t.S), "typing…"), "the TV shows who is typing");
            Check.Ok(t.Game.Answer(t.Tokens[0], Secret(1)));
            Check.Ok(t.Game.Answer(t.Tokens[1], Secret(2)));
            Check.Ok(t.Game.Answer(t.Tokens[2], Secret(3)));
            Check.True(Render.Has(Render.MgPlayer(t, 0), "Change my answer") && Render.Has(Render.MgPlayer(t, 0), Secret(1)), "a celebrity sees their own locked answer");
            Check.True(Render.Has(Render.MgTv(t.S), "locked in") && !Render.Has(Render.MgTv(t.S), Secret(1)), "the TV says locked in but not what");
            Check.True(!Render.Has(Render.MgPlayer(t, 1), Secret(1)), "and other celebrities don't see it");
            Check.Ok(t.Game.CloseAnswers(t.Emcee));

            Check.True(Render.Has(Render.MgPlayer(t, 3), "You're sitting out"), "the celebrity who didn't answer sits out");
            Check.True(Render.Has(Render.MgTv(t.S), "sitting out"), "and the TV shows their desk dimmed");
            Check.True(Render.Has(Render.MgEmcee(t), "Start the reveal"), "the emcee can start the reveal");
            Check.Ok(t.Game.SetGuess(t.Emcee, "a goose"));
            Check.True(Render.Has(Render.MgTv(t.S), "a goose") && Render.Has(Render.MgTv(t.S), "Team A"), "the team's guess shows on the TV");
            Check.Ok(t.Game.StartReveal(t.Emcee));

            Check.True(Render.Has(Render.MgPlayer(t, 0), "mg-reveal-btn") && Render.Has(Render.MgPlayer(t, 0), "REVEAL"), "celebrity 1 has the reveal button");
            Check.True(!Render.Has(Render.MgPlayer(t, 1), "mg-reveal-btn"), "celebrity 2 does not yet");
            Check.True(Render.Has(Render.MgEmcee(t), "Reveal it for them"), "the emcee can reveal for a slow celebrity");

            Check.Ok(t.Game.RevealAnswer(t.Tokens[0]));
            Check.True(Render.Has(Render.MgTv(t.S), Secret(1)), "once revealed the answer is on the TV");
            Check.True(Render.ShowsAnyCase(Render.MgEmcee(t), "★ Match") && Render.Has(Render.MgEmcee(t), "No match"), "the emcee gets match and no match");
            Check.Ok(t.Game.Judge(t.Emcee, true));
            Check.True(Render.ShowsAnyCase(Render.MgTv(t.S), "★ MATCH ★"), "the TV stamps MATCH");
            Check.Ok(t.Game.RevealFor(t.Emcee)); Check.Ok(t.Game.Judge(t.Emcee, false));
            Check.True(Render.Has(Render.MgTv(t.S), "NO MATCH"), "and NO MATCH");
            Check.Ok(t.Game.RevealAnswer(t.Tokens[2])); Check.Ok(t.Game.Judge(t.Emcee, false));

            Check.Equal(MatchPhase.QuestionDone, t.S.Phase, "done");
            Check.True(Render.ShowsAnyCase(Render.MgPlayer(t, 0), "★ MATCH! ★"), "celebrity 1's phone celebrates");
            Check.True(Render.Has(Render.MgPlayer(t, 1), "No match"), "celebrity 2's says no match");
            Check.True(Render.Has(Render.MgEmcee(t), "change") && Render.Has(Render.MgEmcee(t), "Next"), "the emcee can change a ruling or move on");
            Check.True(Render.Has(Render.MgTv(t.S), "1") && Render.Has(Render.MgTv(t.S), "match"), "the score strip");
        });

        run("mg super match: the advisors only talk, the contestant chooses, the board posts slowly and the stake stays hidden until it is up", () =>
        {
            var t = new MatchTable(celebrities: 4);
            for (var q = 0; q < 6; q++) t.PlayAndContinue(n => t.S.Team == 0 && n == 1);
            Check.True(Render.Has(Render.MgTv(t.S), "goes to the Super Match"), "the TV announces it");
            var emcee = Render.MgEmcee(t);
            Check.True(Render.Has(emcee, "Go") && Render.Has(emcee, "1 · Cel1"), "the emcee picks celebrities");
            Check.Ok(t.Game.PickSuperCelebrities(t.Emcee, [t.Ids[0], t.Ids[2], t.Ids[3]]));

            emcee = Render.MgEmcee(t);
            Check.True(Render.Has(emcee, "only you can see these") && Render.Has(emcee, "Alpha") && Render.Has(emcee, "Bravo") && Render.Has(emcee, "Charlie"), "the emcee sees the popular answers");
            var tv = Render.MgTv(t.S);
            Check.True(!Render.Has(tv, "Alpha") && !Render.Has(tv, "Bravo") && !Render.Has(tv, "Charlie"), "the TV doesn't");
            Check.True(Render.Has(tv, "$500") && Render.Has(tv, "$250") && Render.Has(tv, "$100"), "but shows what each rank pays");
            for (var i = 0; i < 4; i++) Check.True(!Render.Has(Render.MgPlayer(t, i), "Alpha") && !Render.Has(Render.MgPlayer(t, i), "Charlie"), "no celebrity sees them");
            Check.True(Render.Has(Render.MgPlayer(t, 1), "You're sitting out"), "the celebrity who wasn't called on sits out");

            Check.Equal(MatchPhase.SuperChoice, t.S.Phase, "the contestant chooses: part one is all spoken");
            Check.True(Render.Has(Render.MgPlayer(t, 0), "You&#x27;re an advisor") || Render.ShowsAnyCase(Render.MgPlayer(t, 0), "You're an advisor"), "an advisor's phone says to speak, not type");
            Check.True(!Render.Has(Render.MgPlayer(t, 0), "mg-answer-input"), "and has no box to type in");
            Check.True(Render.Has(Render.MgTv(t.S), "advising"), "the TV shows the three advisors");
            emcee = Render.MgEmcee(t);
            Check.True(Render.Has(emcee, "Not on the board") && Render.Has(emcee, "#3") && Render.Has(emcee, "What the contestant said"), "the emcee has the rank buttons");
            var tv2 = Render.MgTv(t.S);
            Check.True(!Render.Has(tv2, "Bravo") && !Render.Has(tv2, "Alpha"), "the TV shows no popular answers");
            // The contestant says "charlie" (the $100 answer).
            Check.Ok(t.Game.ChooseSuperAnswer(t.Emcee, "charlie", 3));
            tv = Render.MgTv(t.S);
            Check.True(Render.Has(tv, "Contestant") && Render.Has(tv, "charlie"), "the TV shows what the contestant said");
            Check.True(Render.Has(tv, "The popular answers") && !Render.Has(tv, "$5,000") && !Render.Has(tv, "$1,000"), "the board is next and the stake is still hidden");
            Check.True(!Render.Has(tv, "Alpha") && !Render.Has(tv, "Charlie"), "no answer posted yet");
            Check.True(Render.Has(Render.MgEmcee(t), "Reveal the $100 answer"), "the emcee reveals $100 first");
            for (var i = 0; i < 4; i++) Check.True(!Render.Has(Render.MgPlayer(t, i), "$1,000"), "no phone spells out the stake");
            Check.Ok(t.Game.RevealSuperRow(t.Emcee));
            tv = Render.MgTv(t.S);
            Check.True(Render.Has(tv, "Charlie") && !Render.Has(tv, "Bravo") && !Render.Has(tv, "Alpha"), "just the $100 answer is up");
            Check.True(Render.Has(tv, "mg-super-row posted  lit") || Render.Has(tv, "lit"), "and it lights up because it was matched");
            Check.True(Render.Has(Render.MgEmcee(t), "Reveal the $250 answer"), "then $250");
            Check.Ok(t.Game.RevealSuperRow(t.Emcee));
            tv = Render.MgTv(t.S);
            Check.True(Render.Has(tv, "Bravo") && Render.Count(tv, "mg-super-row posted") == 2 && Render.Count(tv, " lit\"") == 1, "an unmatched row is shown but not lit");
            Check.True(!Render.Has(Render.MgEmcee(t), "On to the final match"), "not done until the $500 row is up");
            Check.Ok(t.Game.RevealSuperRow(t.Emcee));
            tv = Render.MgTv(t.S);
            Check.True(Render.Has(tv, "Alpha") && Render.ShowsAnyCase(tv, "$100 × 10 = $1,000"), "the whole board and the stake");
            Check.True(Render.Has(Render.MgEmcee(t), "On to the final match"), "and the emcee can go on");
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.True(Render.Has(Render.MgTv(t.S), "Playing for $1,000"), "the final match");
            Check.Ok(t.Game.PickFinalCelebrity(t.Emcee, t.Ids[1]));
            Check.True(Render.Has(Render.MgPlayer(t, 1), "Lock it in"), "the chosen celebrity types");
            Check.True(Render.Has(Render.MgPlayer(t, 0), "You're sitting out"), "everyone else waits");
            t.AnswerOnly();
            Check.True(Render.Has(Render.MgEmcee(t), "contestant says their answer"), "the emcee waits for the contestant");
            Check.Ok(t.Game.StartReveal(t.Emcee)); Check.Ok(t.Game.RevealAnswer(t.Tokens[1])); Check.Ok(t.Game.Judge(t.Emcee, true));
            Check.True(Render.Has(Render.MgTv(t.S), "$1,000") && Render.Has(Render.MgTv(t.S), "GAME OVER"), "the winning screen");
            Check.True(Render.Has(Render.MgEmcee(t), "Play again with the same panel"), "and play again");
        });

        run("mg: a whole game with random play, drawing every screen after every action; secrets never leak", () =>
        {
            for (var seed = 1; seed <= 6; seed++)
            {
                var rnd = new Random(seed);
                var t = new MatchTable(celebrities: rnd.Next(2, 7), seed: seed);
                var draws = 0;
                t.Game.Changed += () => { AssertSecrets(t); for (var i = 0; i < t.Tokens.Length; i++) Render.MgPlayer(t, i); draws++; };
                var steps = 0;
                while (t.S.Phase != MatchPhase.GameOver)
                {
                    Check.True(++steps < 500, "the game finishes");
                    var s = t.S;
                    switch (s.Phase)
                    {
                        case MatchPhase.Ready: Check.Ok(t.Game.AskQuestion(t.Emcee)); break;
                        case MatchPhase.Answering:
                            foreach (var n in t.Playing) Check.Ok(t.Game.Answer(t.TokenOfNumber(n), Secret(n)));
                            Check.Ok(t.Game.CloseAnswers(t.Emcee));
                            break;
                        case MatchPhase.Guessing: Check.Ok(t.Game.StartReveal(t.Emcee)); break;
                        case MatchPhase.Reveal:
                            Check.Ok(t.Game.RevealAnswer(t.TokenOfNumber(s.Revealing!.Number)));
                            if (s.Stage != MatchStage.Super) Check.Ok(t.Game.Judge(t.Emcee, rnd.Next(3) == 0));
                            break;
                        case MatchPhase.QuestionDone: Check.Ok(t.Game.Continue(t.Emcee)); break;
                        case MatchPhase.SuperChoice: Check.Ok(t.Game.ChooseSuperAnswer(t.Emcee, "x", rnd.Next(4))); break;
                        case MatchPhase.SuperResult: t.PostBoard(); Check.Ok(t.Game.Continue(t.Emcee)); break;
                        case MatchPhase.SuperPick:
                            Check.Ok(t.Game.PickSuperCelebrities(t.Emcee, s.Celebrities.OrderBy(_ => rnd.Next()).Take(Math.Min(3, s.Celebrities.Count)).Select(c => c.Id)));
                            break;
                        case MatchPhase.FinalPick: Check.Ok(t.Game.PickFinalCelebrity(t.Emcee, s.Celebrities[rnd.Next(s.Celebrities.Count)].Id)); break;
                    }
                }
                Check.True(draws > 25, $"seed {seed}: expected many redraws, got {draws}");
                Check.True(Render.Has(Render.MgTv(t.S), "GAME OVER"), "the game over screen");
            }
        });

        run("mg wiring: the app's own service setup hands Match Game the real question bank", () =>
        {
            var services = new ServiceCollection().AddMatchGame().BuildServiceProvider();
            var rooms = (MatchRoomManager)services.GetService(typeof(MatchRoomManager))!;
            var game = rooms.Create();
            var emcee = game.JoinEmcee("Host").Token;
            game.JoinCelebrity("Ann"); game.JoinCelebrity("Bob");
            Check.Ok(game.Start(emcee));
            Check.True(game.Snapshot().QuestionText.Contains("___") && !game.Snapshot().QuestionText.Contains("{name}"), $"a real question with a name in it: {game.Snapshot().QuestionText}");
            var tile = BuzzerGames.Web.Games.GameCatalog.All.Single(g => g.Name == "Match Game");
            Check.True(tile.IsPlayable && tile.Url == "/match-game", "the Match Game tile points at /match-game");
        });
    }
}
