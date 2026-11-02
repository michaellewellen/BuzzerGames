using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Match;

/// <summary>
/// A ready-made Match Game: an emcee (Host) and <c>celebrities</c> celebrities named Cel1, Cel2... in seat order.
/// Questions are "Question N about {name}: ___" and every Super Match blank has the popular answers Alpha, Bravo and Charlie.
/// </summary>
sealed class MatchTable
{
    public MatchGame Game { get; }
    public Guid Emcee { get; }
    public Guid[] Tokens { get; }
    public Guid[] Ids { get; }

    public MatchTable(int celebrities = 6, int seed = 1, bool start = true)
    {
        Game = new MatchGame("TEST", new FixedMatchContent(), new Random(seed));
        Emcee = Game.JoinEmcee("Host").Token;
        Tokens = Enumerable.Range(1, celebrities).Select(i => Game.JoinCelebrity("Cel" + i).Token).ToArray();
        Ids = Tokens.Select(t => Game.GetSeat(t).PlayerId!.Value).ToArray();
        if (start) Check.Ok(Game.Start(Emcee));
    }

    public MatchSnapshot S => Game.Snapshot();

    /// <summary>The numbers (1-based seat numbers) of the celebrities playing the current question.</summary>
    public List<int> Playing => S.Celebrities.Where(c => c.Playing).Select(c => c.Number).ToList();

    public Guid TokenOfNumber(int number) => Tokens[number - 1];

    /// <summary>Ask, everybody in play answers, the emcee closes the answers.</summary>
    public void AskAndAnswer(Func<int, string>? answerOf = null)
    {
        Check.Ok(Game.AskQuestion(Emcee));
        foreach (var n in Playing) Check.Ok(Game.Answer(TokenOfNumber(n), answerOf?.Invoke(n) ?? "answer " + n));
        Check.Ok(Game.CloseAnswers(Emcee));
    }

    /// <summary>After the Super Match answers are ranked, the emcee posts the board: $100, $250, then $500.</summary>
    public void PostBoard()
    {
        Check.Equal(0, S.SuperAmount, "the stake is hidden until the board is up");
        Check.Fails(Game.Continue(Emcee), "moving on before the board is up");
        foreach (var rank in new[] { 3, 2, 1 })
        {
            Check.True(S.SuperRows.Count(r => r.Revealed) == 3 - rank, "rows post from the bottom");
            Check.Ok(Game.RevealSuperRow(Emcee));
            Check.True(S.SuperRows[rank - 1].Revealed, $"row {rank} is up");
        }
        Check.Fails(Game.RevealSuperRow(Emcee), "a fourth row");
    }

    /// <summary>For stages that go straight to answering (Super and Final): everybody in play answers and the emcee closes.</summary>
    public void AnswerOnly()
    {
        foreach (var n in Playing) Check.Ok(Game.Answer(TokenOfNumber(n), "answer " + n));
        Check.Ok(Game.CloseAnswers(Emcee));
    }

    /// <summary>The team speaks, the celebrities reveal in order, and the emcee rules: match when <paramref name="isMatch"/> says so.</summary>
    public void RevealAll(Func<int, bool> isMatch)
    {
        Check.Ok(Game.StartReveal(Emcee));
        foreach (var n in Playing)
        {
            Check.Ok(Game.RevealAnswer(TokenOfNumber(n)));
            Check.Ok(Game.Judge(Emcee, isMatch(n)));
        }
    }

    /// <summary>A whole regular question, start to the "question done" screen.</summary>
    public void PlayQuestion(Func<int, bool> isMatch)
    {
        AskAndAnswer();
        RevealAll(isMatch);
        Check.Equal(MatchPhase.QuestionDone, S.Phase, "the question is done");
    }

    /// <summary>Plays the question and moves on to whatever is next.</summary>
    public void PlayAndContinue(Func<int, bool> isMatch)
    {
        PlayQuestion(isMatch);
        Check.Ok(Game.Continue(Emcee));
    }
}

static class MatchChecks
{
    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine();
        Console.WriteLine("Match Game: the panel");

        run("match: one emcee, any number of celebrities (two or more to start), unique names", () =>
        {
            var game = new MatchGame("TEST", new FixedMatchContent(), new Random(1));
            Check.Fails(game.Start(Guid.NewGuid()), "a stranger starting the game");
            var emcee = game.JoinEmcee("Host");
            Check.Ok(emcee);
            Check.Fails(game.JoinEmcee("Another"), "a second emcee");
            Check.Fails(game.JoinCelebrity(""), "an empty name");
            Check.Fails(game.JoinCelebrity("host"), "the emcee's name");
            var first = game.JoinCelebrity("Ann");
            Check.Ok(first);
            Check.Fails(game.JoinCelebrity("ann"), "the same name in other letters");
            Check.Fails(game.Start(emcee.Token), "starting with one celebrity");
            Check.True(!game.Snapshot().CanStart, "no start button yet");
            Check.Ok(game.JoinCelebrity("Bob"));
            Check.True(game.Snapshot().CanStart, "two is enough");
            for (var i = 0; i < 10; i++) Check.Ok(game.JoinCelebrity("Extra" + i));
            Check.Equal(12, game.Snapshot().Celebrities.Count, "ten more is fine");
            Check.Fails(game.Start(first.Token), "a celebrity starting the game");
            Check.Equal(Role.Emcee, game.GetSeat(emcee.Token).Role, "the emcee's seat");
            Check.Equal(Role.Player, game.GetSeat(first.Token).Role, "a celebrity's seat");
            Check.Equal(Role.Spectator, game.GetSeat(Guid.NewGuid()).Role, "a stranger's seat");
            Check.Ok(game.SetTeamNames(emcee.Token, "Reds", "Blues"));
            Check.Fails(game.SetTeamNames(emcee.Token, "Same", "same"), "two teams with one name");
            Check.Fails(game.SetTeamNames(first.Token, "X", "Y"), "a celebrity renaming the teams");
            Check.Ok(game.RemoveCelebrity(emcee.Token, game.Snapshot().Celebrities[2].Id));
            Check.Equal(11, game.Snapshot().Celebrities.Count, "a celebrity removed in the lobby");
            Check.Equal(3, game.Snapshot().Celebrities[2].Number, "and the seat numbers close up");
            Check.Ok(game.Start(emcee.Token));
            Check.Fails(game.SetTeamNames(emcee.Token, "Late", "Late2"), "renaming after the start");
            Check.Fails(game.JoinEmcee("Late"), "an emcee after the start");
            Check.Fails(game.RemoveCelebrity(emcee.Token, game.Snapshot().Celebrities[0].Id), "removing a celebrity after the start");
            Check.Ok(game.JoinCelebrity("Latecomer"));
            Check.Equal(12, game.Snapshot().Celebrities.Count, "a latecomer can still join the panel");
        });

        Console.WriteLine();
        Console.WriteLine("Match Game: a question");

        run("match: Team A is up first with a question that has a family name in it, and the emcee can swap it", () =>
        {
            var t = new MatchTable();
            var s = t.S;
            Check.Equal(MatchPhase.Ready, s.Phase, "ready");
            Check.Equal(0, s.Team, "Team A first");
            Check.Equal("Question 1 of 3", s.Label, "label");
            Check.True(!s.QuestionText.Contains("{name}") && new[] { "Ann", "Bob", "Cy" }.Any(n => s.QuestionText.Contains(n)), $"the name is filled in: {s.QuestionText}");
            Check.Equal(6, t.Playing.Count, "everybody is in play at first");
            Check.True(!s.QuestionAsked, "the room doesn't see the question yet");
            var before = s.QuestionText;
            Check.Ok(t.Game.SwapQuestion(t.Emcee));
            Check.True(t.S.QuestionText != before, "swapped for a different question");
            Check.Fails(t.Game.SwapQuestion(t.Tokens[0]), "a celebrity swapping");
            Check.Fails(t.Game.AskQuestion(t.Tokens[0]), "a celebrity asking");
            Check.Ok(t.Game.AskQuestion(t.Emcee));
            Check.Equal(MatchPhase.Answering, t.S.Phase, "answering");
            Check.True(t.S.QuestionAsked, "now the room can read it");
            Check.Fails(t.Game.SwapQuestion(t.Emcee), "swapping after it is asked");
        });

        run("match: names come out in a shuffled order, each used before any repeats", () =>
        {
            var t = new MatchTable(seed: 7);
            string Name() => new[] { "Ann", "Bob", "Cy" }.Single(n => t.S.QuestionText.Contains(n));
            var seen = new List<string> { Name() };
            for (var i = 0; i < 2; i++) { Check.Ok(t.Game.SwapQuestion(t.Emcee)); seen.Add(Name()); }
            Check.Equal(3, seen.Distinct().Count(), "the first three draws use three different names");
        });

        run("match: celebrities type secret answers; they can retype; the emcee closes; non-answerers sit out", () =>
        {
            var t = new MatchTable();
            Check.Fails(t.Game.Answer(t.Tokens[0], "too early"), "answering before the question is asked");
            Check.Ok(t.Game.AskQuestion(t.Emcee));
            Check.Fails(t.Game.Answer(t.Emcee, "I am the emcee"), "the emcee answering");
            Check.Fails(t.Game.Answer(Guid.NewGuid(), "stranger"), "a stranger answering");
            Check.Fails(t.Game.Answer(t.Tokens[0], "   "), "a blank answer");
            Check.Fails(t.Game.CloseAnswers(t.Emcee), "closing with no answers");
            Check.Ok(t.Game.Answer(t.Tokens[0], "  a   chicken\t "));
            Check.Equal("a chicken", t.S.Celebrities[0].Answer, "whitespace is tidied");
            Check.Ok(t.Game.Answer(t.Tokens[0], "a goose"));
            Check.Equal("a goose", t.S.Celebrities[0].Answer, "retyping replaces it");
            Check.Ok(t.Game.Answer(t.Tokens[2], new string('x', 100)));
            Check.True(t.S.Celebrities[2].Answer.Length <= MatchGame.AnswerMaxLength, "answers are cut to length");
            Check.True(t.S.Celebrities[0].Submitted && !t.S.Celebrities[1].Submitted, "who is locked in");
            Check.Fails(t.Game.CloseAnswers(t.Tokens[0]), "a celebrity closing the answers");
            Check.Ok(t.Game.CloseAnswers(t.Emcee));
            Check.Equal(MatchPhase.Guessing, t.S.Phase, "now the team guesses");
            Check.True(t.Playing.SequenceEqual([1, 3]), "only the two who answered are in this question");
            Check.Fails(t.Game.Answer(t.Tokens[1], "too late"), "answering after the answers are closed");
        });

        run("match: the emcee can record the team's guess, and the celebrities reveal in seat order with the emcee ruling", () =>
        {
            var t = new MatchTable(celebrities: 3);
            t.AskAndAnswer();
            Check.Ok(t.Game.SetGuess(t.Emcee, "  a   duck "));
            Check.Equal("a duck", t.S.Guess, "the guess is tidied");
            Check.Fails(t.Game.SetGuess(t.Tokens[0], "sneaky"), "a celebrity recording the guess");
            Check.Fails(t.Game.RevealAnswer(t.Tokens[0]), "revealing before the team has guessed");
            Check.Fails(t.Game.Judge(t.Emcee, true), "ruling before the reveal");
            Check.Ok(t.Game.StartReveal(t.Emcee));
            Check.Equal(1, t.S.Revealing!.Number, "celebrity 1 is up");
            Check.Fails(t.Game.RevealAnswer(t.Tokens[1]), "celebrity 2 jumping the queue");
            Check.Fails(t.Game.RevealAnswer(t.Emcee), "the emcee as a celebrity");
            Check.Fails(t.Game.Judge(t.Emcee, true), "ruling on a hidden answer");
            Check.Equal(RevealState.Hidden, t.S.Celebrities[0].Reveal, "hidden");
            Check.Ok(t.Game.RevealAnswer(t.Tokens[0]));
            Check.Equal(RevealState.Shown, t.S.Celebrities[0].Reveal, "shown");
            Check.Fails(t.Game.RevealAnswer(t.Tokens[0]), "revealing twice");
            Check.Fails(t.Game.RevealAnswer(t.Tokens[1]), "celebrity 2 before celebrity 1 is ruled on");
            Check.Fails(t.Game.Judge(t.Tokens[0], true), "a celebrity ruling on themselves");
            Check.Ok(t.Game.Judge(t.Emcee, true));
            Check.Equal(1, t.S.Scores[0], "a match scores for Team A");
            Check.Equal(RevealState.Judged, t.S.Celebrities[0].Reveal, "judged");
            Check.Equal(2, t.S.Revealing!.Number, "celebrity 2 is up");
            Check.Ok(t.Game.RevealFor(t.Emcee));                       // the emcee reveals for a slow celebrity
            Check.Ok(t.Game.Judge(t.Emcee, false));
            Check.Ok(t.Game.RevealAnswer(t.Tokens[2]));
            Check.Ok(t.Game.Judge(t.Emcee, true));
            Check.Equal(MatchPhase.QuestionDone, t.S.Phase, "all revealed");
            Check.Equal(2, t.S.Scores[0], "two matches");
            Check.Equal(0, t.S.Scores[1], "Team B has none");
            Check.True(t.S.Message!.Contains("matched 2 of 3"), $"the summary: {t.S.Message}");
        });

        run("match: a celebrity who matched sits out that team's remaining questions, and the other team still has them", () =>
        {
            var t = new MatchTable(celebrities: 6);
            // Team A, question 1: celebrities 1, 2 and 3 match.
            t.PlayAndContinue(n => n <= 3);
            Check.Equal(1, t.S.Team, "Team B is up");
            Check.True(t.Playing.SequenceEqual([1, 2, 3, 4, 5, 6]), "Team B starts with everyone");
            // Team B, question 1: celebrities 2, 4 and 6 match.
            t.PlayAndContinue(n => n % 2 == 0);
            Check.Equal(0, t.S.Team, "Team A is up again");
            Check.Equal("Question 2 of 3", t.S.Label, "A's second question");
            Check.True(t.Playing.SequenceEqual([4, 5, 6]), $"Team A now only has 4, 5, 6 but had {string.Join(",", t.Playing)}");
            Check.True(t.S.Celebrities.Where(c => c.Number <= 3).All(c => c.OutForTeam), "1, 2 and 3 are out for Team A");
            t.AskAndAnswer();
            t.RevealAll(n => false);
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(1, t.S.Team, "Team B is up again");
            Check.True(t.Playing.SequenceEqual([1, 3, 5]), $"Team B now only has 1, 3, 5 but had {string.Join(",", t.Playing)}");
            Check.True(t.S.Celebrities[0].MatchedA && !t.S.Celebrities[0].MatchedB && t.S.Celebrities[1].MatchedA && t.S.Celebrities[1].MatchedB, "who has matched for which team");
        });

        run("match: only the celebrities who answered are revealed; a question with no one left for a team is skipped", () =>
        {
            var t = new MatchTable(celebrities: 2);
            t.PlayAndContinue(n => true);                                   // Team A matches both
            t.PlayAndContinue(n => false);                                  // Team B matches neither
            Check.Equal(1, t.S.Team, "A's second question is skipped, so it is B's turn");
            Check.Equal("Question 2 of 3", t.S.Label, "B's second question");
            Check.True(t.S.Message!.Contains("Team A has matched everyone"), $"the note says why: {t.S.Message}");
            t.PlayAndContinue(n => false);
            Check.Equal(1, t.S.Team, "A's third question is skipped too");
            Check.Equal("Question 3 of 3", t.S.Label, "B's third question");
        });

        run("match: the emcee can change a ruling, and the score and the sit-out list follow", () =>
        {
            var t = new MatchTable(celebrities: 3);
            t.PlayQuestion(n => n == 1);
            Check.Equal(1, t.S.Scores[0], "one match");
            Check.Fails(t.Game.Rejudge(t.Tokens[0], t.Ids[0]), "a celebrity changing a ruling");
            Check.Ok(t.Game.Rejudge(t.Emcee, t.Ids[0]));
            Check.Equal(0, t.S.Scores[0], "that match is taken back");
            Check.True(!t.S.Celebrities[0].MatchedA, "and they are back in play for Team A");
            Check.Ok(t.Game.Rejudge(t.Emcee, t.Ids[1]));
            Check.Equal(1, t.S.Scores[0], "a no-match turned into a match");
            Check.True(t.S.Celebrities[1].MatchedA, "so they now sit out");
            Check.Fails(t.Game.Rejudge(t.Emcee, Guid.NewGuid()), "an unknown celebrity");
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Fails(t.Game.Rejudge(t.Emcee, t.Ids[0]), "changing a ruling after moving on");
        });

        Console.WriteLine();
        Console.WriteLine("Match Game: tiebreakers and the Super Match");

        run("match: after three questions each, the team with more matches plays the Super Match", () =>
        {
            var t = new MatchTable(celebrities: 6);
            for (var q = 0; q < 6; q++)
            {
                var team = t.S.Team;
                Check.Equal(q % 2, team, $"question {q + 1} goes to team {q % 2}");
                t.PlayAndContinue(n => team == 0 && n == 1 + q / 2);        // Team A matches one celebrity per question; B never matches
            }
            var s = t.S;
            Check.Equal(MatchPhase.SuperPick, s.Phase, "on to the Super Match");
            Check.Equal(0, s.SuperTeam, "Team A won");
            Check.Equal(3, s.Scores[0], "three matches");
            Check.Equal(0, s.Scores[1], "none for B");
        });

        run("match: a tie after the six questions is settled by one question each with everyone back in play", () =>
        {
            var t = new MatchTable(celebrities: 4, seed: 3);
            for (var q = 0; q < 6; q++) t.PlayAndContinue(n => q < 2 && n == 1);          // A matches 1, B matches 1: tied 1 to 1... and nothing else
            Check.Equal(1, t.S.Scores[0], "A has one");
            Check.Equal(1, t.S.Scores[1], "B has one");
            var s = t.S;
            Check.Equal(MatchPhase.Ready, s.Phase, "a tiebreaker is waiting");
            Check.Equal("Tiebreaker", s.Label, "label");
            Check.Equal(0, s.Team, "Team A first");
            Check.True(t.Playing.SequenceEqual([1, 2, 3, 4]), "everyone is back in play, even the celebrity who matched");
            t.PlayAndContinue(n => n == 2);                                               // A: 2 total
            Check.Equal(1, t.S.Team, "then Team B");
            Check.Equal("Tiebreaker", t.S.Label, "B's tiebreaker");
            Check.True(t.Playing.Count == 4, "everyone for Team B too");
            t.PlayAndContinue(n => n == 3);                                               // B: 2 total, still tied
            Check.Equal(MatchPhase.Ready, t.S.Phase, "still tied: another round of tiebreakers");
            Check.Equal(0, t.S.Team, "back to Team A");
            t.PlayAndContinue(n => false);
            t.PlayAndContinue(n => n <= 2);                                               // B pulls ahead
            Check.Equal(MatchPhase.SuperPick, t.S.Phase, "a winner at last");
            Check.Equal(1, t.S.SuperTeam, "Team B wins the tiebreaker");
        });

        run("match: the Super Match, part one: three celebrities, ranked against the popular answers, best one sets the amount", () =>
        {
            var t = new MatchTable(celebrities: 6);
            for (var q = 0; q < 6; q++) t.PlayAndContinue(n => t.S.Team == 0 && n == 1);
            Check.Equal(MatchPhase.SuperPick, t.S.Phase, "super pick");
            Check.Fails(t.Game.PickSuperCelebrities(t.Tokens[0], [t.Ids[0], t.Ids[1], t.Ids[2]]), "a celebrity picking");
            Check.Fails(t.Game.PickSuperCelebrities(t.Emcee, [t.Ids[0], t.Ids[1]]), "only two");
            Check.Fails(t.Game.PickSuperCelebrities(t.Emcee, [t.Ids[0], t.Ids[0], t.Ids[1]]), "the same celebrity twice");
            Check.Fails(t.Game.PickSuperCelebrities(t.Emcee, [t.Ids[0], t.Ids[1], Guid.NewGuid()]), "a stranger");
            Check.Ok(t.Game.PickSuperCelebrities(t.Emcee, [t.Ids[4], t.Ids[0], t.Ids[2]]));          // any three, matched or not
            var s = t.S;
            Check.Equal(MatchPhase.Answering, s.Phase, "answering");
            Check.Equal(MatchStage.Super, s.Stage, "super stage");
            Check.True(t.Playing.SequenceEqual([1, 3, 5]), "the three are in seat order");
            Check.True(s.QuestionText.StartsWith("Super"), $"the blank: {s.QuestionText}");
            Check.True(s.SuperRows.All(r => !r.Revealed), "the popular answers are hidden");
            Check.Ok(t.Game.Answer(t.Tokens[0], "the alpha"));
            Check.Ok(t.Game.Answer(t.Tokens[2], "Zebra"));
            Check.Ok(t.Game.Answer(t.Tokens[4], "charlie brown"));
            Check.Fails(t.Game.Answer(t.Tokens[1], "not picked"), "a celebrity who wasn't picked");
            Check.Ok(t.Game.CloseAnswers(t.Emcee));
            Check.Equal(MatchPhase.Reveal, t.S.Phase, "no team guess in part one: straight to the reveal");
            Check.True(t.S.Celebrities.Where(c => c.Playing).Select(c => c.SuggestedRank).SequenceEqual([1, 0, 3]), "the computer suggests rank 1, none, rank 3");
            Check.Fails(t.Game.Judge(t.Emcee, true), "match or no match in part one");
            Check.Fails(t.Game.Rank(t.Emcee, 1), "ranking an answer that is still hidden");
            Check.Ok(t.Game.RevealAnswer(t.Tokens[0]));
            Check.Fails(t.Game.Rank(t.Emcee, 4), "rank 4");
            Check.Fails(t.Game.Rank(t.Tokens[0], 1), "a celebrity ranking themselves");
            Check.Ok(t.Game.Rank(t.Emcee, 1));
            Check.True(t.S.SuperRows.All(r => !r.Revealed), "ranking doesn't post anything: the emcee does that slowly");
            Check.Ok(t.Game.RevealAnswer(t.Tokens[2]));
            Check.Ok(t.Game.Rank(t.Emcee, 0));
            Check.Ok(t.Game.RevealFor(t.Emcee));
            Check.Ok(t.Game.Rank(t.Emcee, 3));
            s = t.S;
            Check.Equal(MatchPhase.SuperResult, s.Phase, "result");
            t.PostBoard();
            s = t.S;
            Check.True(s.SuperRows[0].Lit && !s.SuperRows[1].Lit && s.SuperRows[2].Lit, "rows 1 and 3 were matched, so they light up");
            Check.Equal(500, s.SuperAmount, "the best answer was number one: $500");
            Check.Equal(5000, s.FinalStake, "so the final match is for ten times that");
            Check.True(s.SuperRows.All(r => r.Revealed), "the whole board is posted");
            Check.Equal(0, s.Scores[1], "the match score is untouched");
        });

        run("match: the amounts are $500, $250 and $100 for the best answer, and a consolation $50 for none", () =>
        {
            foreach (var (ranks, expected) in new[] { (new[] { 3, 2, 3 }, 250), (new[] { 3, 3, 3 }, 100), (new[] { 0, 0, 0 }, 50), (new[] { 0, 3, 0 }, 100), (new[] { 2, 1, 0 }, 500) })
            {
                var t = new MatchTable(celebrities: 3);
                for (var q = 0; q < 6; q++) t.PlayAndContinue(n => t.S.Team == 0 && n == 1);
                Check.Ok(t.Game.PickSuperCelebrities(t.Emcee, t.Ids));
                t.AnswerOnly();
                foreach (var (n, rank) in new[] { 1, 2, 3 }.Zip(ranks))
                {
                    Check.Ok(t.Game.RevealAnswer(t.Tokens[n - 1]));
                    Check.Ok(t.Game.Rank(t.Emcee, rank));
                }
                t.PostBoard();
                Check.Equal(expected, t.S.SuperAmount, $"ranks {string.Join(",", ranks)}");
                Check.Equal(expected * 10, t.S.FinalStake, "ten times");
            }
        });

        run("match: the final match: one celebrity, one question, an exact match wins ten times the amount", () =>
        {
            foreach (var win in new[] { true, false })
            {
                var t = new MatchTable(celebrities: 6);
                for (var q = 0; q < 6; q++) t.PlayAndContinue(n => t.S.Team == 0 && n == 1);
                Check.Ok(t.Game.PickSuperCelebrities(t.Emcee, [t.Ids[0], t.Ids[1], t.Ids[2]]));
                t.AnswerOnly();
                Check.Ok(t.Game.RevealAnswer(t.Tokens[0])); Check.Ok(t.Game.Rank(t.Emcee, 2));
                Check.Ok(t.Game.RevealAnswer(t.Tokens[1])); Check.Ok(t.Game.Rank(t.Emcee, 3));
                Check.Ok(t.Game.RevealAnswer(t.Tokens[2])); Check.Ok(t.Game.Rank(t.Emcee, 0));
                t.PostBoard();
                Check.Equal(250, t.S.SuperAmount, "$250 for the second answer");
                Check.Fails(t.Game.PickFinalCelebrity(t.Emcee, t.Ids[3]), "picking before the result is acknowledged");
                Check.Ok(t.Game.Continue(t.Emcee));
                Check.Equal(MatchPhase.FinalPick, t.S.Phase, "final pick");
                Check.Fails(t.Game.PickFinalCelebrity(t.Tokens[0], t.Ids[3]), "a celebrity picking");
                Check.Fails(t.Game.PickFinalCelebrity(t.Emcee, Guid.NewGuid()), "a stranger");
                Check.Ok(t.Game.PickFinalCelebrity(t.Emcee, t.Ids[5]));
                var s = t.S;
                Check.Equal(MatchStage.Final, s.Stage, "final stage");
                Check.True(t.Playing.SequenceEqual([6]), "only the chosen celebrity plays");
                Check.True(!s.QuestionText.Contains("{name}") && s.QuestionText.Length > 0, "a question with a name in it");
                Check.Fails(t.Game.Answer(t.Tokens[0], "not chosen"), "someone else answering");
                Check.Ok(t.Game.Answer(t.Tokens[5], "pineapple"));
                Check.Ok(t.Game.CloseAnswers(t.Emcee));
                Check.Equal(MatchPhase.Guessing, t.S.Phase, "the contestant says theirs out loud");
                Check.Ok(t.Game.SetGuess(t.Emcee, "pineapple"));
                Check.Ok(t.Game.StartReveal(t.Emcee));
                Check.Ok(t.Game.RevealAnswer(t.Tokens[5]));
                Check.Ok(t.Game.Judge(t.Emcee, win));
                s = t.S;
                Check.Equal(MatchPhase.GameOver, s.Phase, "game over");
                Check.Equal(win, s.FinalWon, "the result");
                Check.True(win ? s.Message!.Contains("$2,500") : s.Message!.Contains("$250"), $"the message: {s.Message}");
            }
        });

        run("match: play again with the same panel", () =>
        {
            var t = new MatchTable(celebrities: 3);
            for (var q = 0; q < 6; q++) t.PlayAndContinue(n => t.S.Team == 1 && n == 1);
            Check.Equal(1, t.S.SuperTeam, "Team B won");
            Check.Fails(t.Game.Restart(t.Emcee), "restarting mid-game");
            Check.Ok(t.Game.PickSuperCelebrities(t.Emcee, t.Ids));
            t.AnswerOnly();
            foreach (var tk in t.Tokens) { Check.Ok(t.Game.RevealAnswer(tk)); Check.Ok(t.Game.Rank(t.Emcee, 0)); }
            t.PostBoard();
            Check.Equal(50, t.S.SuperAmount, "the consolation");
            Check.True(t.S.SuperRows.All(r => r.Revealed && !r.Lit), "all three rows are posted and none lit");
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Ok(t.Game.PickFinalCelebrity(t.Emcee, t.Ids[0]));
            t.AnswerOnly();
            Check.Ok(t.Game.StartReveal(t.Emcee)); Check.Ok(t.Game.RevealAnswer(t.Tokens[0])); Check.Ok(t.Game.Judge(t.Emcee, false));
            Check.Equal(MatchPhase.GameOver, t.S.Phase, "over");
            Check.Fails(t.Game.Restart(t.Tokens[0]), "a celebrity restarting");
            Check.Ok(t.Game.Restart(t.Emcee));
            var s = t.S;
            Check.Equal(MatchPhase.Ready, s.Phase, "a fresh game");
            Check.True(s.Scores.All(x => x == 0) && s.Team == 0 && s.Label == "Question 1 of 3", "scores and turn are reset");
            Check.Equal(3, s.PlayingNow.Count(), "the whole panel is in play again");
            Check.True(s.Celebrities.All(c => !c.MatchedA && !c.MatchedB), "nobody is sitting out");
        });

        run("match: the text matcher only suggests: case, punctuation, 'the' and '&' don't matter", () =>
        {
            var q = new SuperQuestion("___ chips", [["Potato"], ["Poker"], ["Fish and", "Fish & chips"]]);
            Check.Equal(1, MatchText.SuggestRank(q, "  POTATO! "), "case and punctuation");
            Check.Equal(3, MatchText.SuggestRank(q, "fish & chips"), "ampersand");
            Check.Equal(3, MatchText.SuggestRank(q, "Fish and"), "and");
            Check.Equal(2, MatchText.SuggestRank(q, "the poker"), "leading 'the'");
            Check.Equal(0, MatchText.SuggestRank(q, "chocolate"), "not on the list");
            Check.Equal(0, MatchText.SuggestRank(q, ""), "nothing typed");
        });

        run("match: the real question bank loads, and every Super Match blank has three answers", () =>
        {
            var content = new JsonMatchContent();
            Check.True(content.Names.Count >= 2, "names");
            var rng = new Random(1);
            var questions = content.CreateQuestions(rng, 40);
            Check.Equal(40, questions.Count, "enough questions even for a long game");
            Check.True(questions.All(q => q.Text.Contains("___")), "every question has a blank");
            var supers = content.CreateSupers(rng, 20);
            Check.True(supers.All(s => s.Text.Contains("___") && s.Answers.Count == 3 && s.Answers.All(a => a.Count > 0)), "every Super blank has three answers");
            var bad = new JsonMatchContent("""{ "names": ["A"], "questions": ["Q ___"], "supers": [ { "text": "Only two ___", "answers": [["x"],["y"]] }, { "text": "Good ___", "answers": [["a"],["b"],["c"]] } ] }""");
            Check.Equal(1, bad.CreateSupers(rng, 5).Select(s => s.Text).Distinct().Count(), "a Super blank without three answers is ignored");
        });

        run("match: whole games with random play and random panels always finish, with consistent scores", () =>
        {
            for (var seed = 1; seed <= 60; seed++)
            {
                var rnd = new Random(seed);
                var t = new MatchTable(celebrities: rnd.Next(2, 9), seed: seed);
                var steps = 0;
                while (t.S.Phase != MatchPhase.GameOver)
                {
                    Check.True(++steps < 400, $"seed {seed}: the game finishes");
                    var s = t.S;
                    switch (s.Phase)
                    {
                        case MatchPhase.Ready:
                            if (rnd.Next(5) == 0) Check.Ok(t.Game.SwapQuestion(t.Emcee));
                            Check.Ok(t.Game.AskQuestion(t.Emcee));
                            break;
                        case MatchPhase.Answering:
                            foreach (var n in t.Playing.Where(_ => rnd.Next(5) != 0)) Check.Ok(t.Game.Answer(t.TokenOfNumber(n), (rnd.Next(2) == 0 ? "Alpha" : "x" + n)));
                            if (!t.Game.CloseAnswers(t.Emcee).Ok) Check.Ok(t.Game.Answer(t.TokenOfNumber(t.Playing[0]), "forced"));
                            break;
                        case MatchPhase.Guessing:
                            if (rnd.Next(2) == 0) Check.Ok(t.Game.SetGuess(t.Emcee, "guess " + steps));
                            Check.Ok(t.Game.StartReveal(t.Emcee));
                            break;
                        case MatchPhase.Reveal:
                            var up = s.Revealing!;
                            if (rnd.Next(2) == 0) Check.Ok(t.Game.RevealAnswer(t.TokenOfNumber(up.Number))); else Check.Ok(t.Game.RevealFor(t.Emcee));
                            Check.Ok(s.Stage == MatchStage.Super ? t.Game.Rank(t.Emcee, rnd.Next(4)) : t.Game.Judge(t.Emcee, rnd.Next(3) == 0));
                            break;
                        case MatchPhase.QuestionDone:
                            if (rnd.Next(4) == 0) t.Game.Rejudge(t.Emcee, s.Celebrities.First(c => c.Playing).Id);
                            Check.Ok(t.Game.Continue(t.Emcee));
                            break;
                        case MatchPhase.SuperPick:
                            var need = Math.Min(3, s.Celebrities.Count);
                            Check.Ok(t.Game.PickSuperCelebrities(t.Emcee, s.Celebrities.OrderBy(_ => rnd.Next()).Take(need).Select(c => c.Id)));
                            break;
                        case MatchPhase.SuperResult: t.PostBoard(); Check.Ok(t.Game.Continue(t.Emcee)); break;
                        case MatchPhase.FinalPick: Check.Ok(t.Game.PickFinalCelebrity(t.Emcee, s.Celebrities[rnd.Next(s.Celebrities.Count)].Id)); break;
                        default: throw new Exception("unexpected phase " + s.Phase);
                    }
                    var now = t.S;
                    var judged = now.Celebrities.Count(c => c.Reveal == RevealState.Judged);
                    Check.True(now.Scores.All(x => x >= 0), "scores are never negative");
                    Check.True(now.Celebrities.Count(c => c.MatchedA) <= now.Celebrities.Count, "matched sets make sense");
                    Check.True(judged <= now.Celebrities.Count, "no more rulings than celebrities");
                }
                var end = t.S;
                Check.True(end.SuperTeam is 0 or 1, "someone played the Super Match");
                Check.True(end.SuperAmount is 500 or 250 or 100 or 50, "the amount is one of the four");
                Check.True(end.FinalWon is not null, "the final match was ruled on");
            }
        });
    }
}
