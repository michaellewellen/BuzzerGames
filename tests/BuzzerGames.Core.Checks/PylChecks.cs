using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.PressYourLuck;

/// <summary>A clock the checks can wind forward, so "the player pressed 3 ticks in" is exact.</summary>
sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 12, 5, 19, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(double ms) => _now = _now.AddMilliseconds(ms);
    public void SetUnixMs(long ms) => _now = DateTimeOffset.FromUnixTimeMilliseconds(ms);
}

/// <summary>A ready-made Press Your Luck table: one emcee (Host) and three players (Ann, Bob, Cy).</summary>
sealed class PylTable
{
    public PressYourLuckGame Game { get; }
    public FakeClock Clock { get; } = new();
    public Guid Emcee { get; }
    public Guid[] Tokens { get; }
    public Guid[] Ids { get; }

    public PylTable(int seed = 1, bool start = true)
    {
        Game = new PressYourLuckGame("TEST", new FixedPylQuestionSource(), new Random(seed), time: Clock);
        Emcee = Game.JoinEmcee("Host").Token;
        Tokens = new[] { "Ann", "Bob", "Cy" }.Select(n => Game.JoinPlayer(n).Token).ToArray();
        Ids = Tokens.Select(t => Game.GetSeat(t).PlayerId!.Value).ToArray();
        if (start) Check.Ok(Game.Start());
    }

    public PylSnapshot S => Game.Snapshot();
    public PylPlayerInfo P(int i) => S.Players[i];
    public int IndexOf(Guid id) => Array.IndexOf(Ids, id);

    /// <summary>Ann buzzes and is right; Bob picks right, Cy picks wrong. Then Continue.</summary>
    public void PlayQuestion(bool guesserRight = true)
    {
        Check.Ok(Game.Buzz(Tokens[0]));
        Check.Ok(Game.JudgeGuess(Emcee, guesserRight));
        var s = S;
        var right = s.CorrectIndex;
        Check.Ok(Game.Pick(Tokens[1], right));
        Check.Ok(Game.Pick(Tokens[2], (right + 1) % 3));
        Check.Equal(PylPhase.Reveal, S.Phase, "phase after both picks");
        Check.Ok(Game.ContinueFromQuestion(Emcee));
    }

    public void PlayQuestionRound()
    {
        for (var i = 0; i < PressYourLuckGame.QuestionsPerRound; i++) PlayQuestion();
    }

    /// <summary>Starts a spin for whoever is acting and returns the snapshot of it.</summary>
    public PylSnapshot StartSpin()
    {
        Check.Ok(Game.Spin(Tokens[IndexOf(S.ActingId!.Value)]));
        return S;
    }

    /// <summary>Finds a tick (0..1999) at which the light shows an outcome that passes the test.</summary>
    public static int FindTick(PylSnapshot s, Func<OutcomeInfo, bool> wanted)
    {
        for (var tick = 0; tick < s.Spin!.Sequence.Count; tick++)
        {
            var square = s.Spin.Sequence[tick];
            if (wanted(s.Board[square][PressYourLuckGame.FaceAt(square, tick)])) return tick;
        }
        throw new Exception("No such outcome appears on this spin.");
    }

    /// <summary>Winds the clock to the middle of the given tick and presses for the acting player.</summary>
    public Result PressAtTick(PylSnapshot spinning, int tick, Guid? token = null)
    {
        var s = spinning.Spin!;
        Clock.SetUnixMs(s.StartUnixMs + (long)tick * s.TickMs + PressYourLuckGame.PressLagMs + s.TickMs / 2);
        return Game.Press(token ?? Tokens[IndexOf(spinning.ActingId!.Value)]);
    }

    /// <summary>Spins and presses at a tick showing the wanted outcome.</summary>
    public SpinResultInfo SpinFor(Func<OutcomeInfo, bool> wanted)
    {
        var s = StartSpin();
        Check.Ok(PressAtTick(s, FindTick(s, wanted)));
        return S.LastSpin!;
    }

    public static bool IsKind(OutcomeInfo o, string kind) => o.Kind == kind;
    public static bool IsPlainCash(OutcomeInfo o) => o.Kind == "cash" && o.Sub is null;
    public static bool IsCashSpin(OutcomeInfo o) => o.Kind == "cash" && o.Sub == "+ SPIN";
}

static class PylChecks
{
    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine();
        Console.WriteLine("Press Your Luck: the table and the questions");

        run("pyl: needs 1 emcee and 3 players, then starts on a question", () =>
        {
            var t = new PylTable(start: false);
            Check.True(t.S.CanStart, "start is offered once the table is full");
            Check.Fails(new PressYourLuckGame("X", new FixedPylQuestionSource()).Start(), "an empty lobby");
            Check.Ok(t.Game.Start());
            Check.Equal(PylPhase.Question, t.S.Phase, "first phase");
            Check.Equal(1, t.S.Round, "round");
            Check.Fails(t.Game.JoinPlayer("Dee"), "joining after the start");
            Check.True(t.S.Question is { Number: 1, Category: "CATEGORY 1" }, "question 1 is up");
        });

        run("pyl: first buzz wins, spectators and the emcee can't buzz", () =>
        {
            var t = new PylTable();
            Check.Fails(t.Game.Buzz(t.Emcee), "emcee buzzing");
            Check.Fails(t.Game.Buzz(Guid.NewGuid()), "a stranger buzzing");
            Check.Ok(t.Game.Buzz(t.Tokens[1]));
            Check.Fails(t.Game.Buzz(t.Tokens[0]), "second buzz");
            Check.Equal(PylPhase.Answering, t.S.Phase, "phase");
            Check.True(t.S.Players[1].IsGuesser, "Bob is the guesser");
        });

        run("pyl: a right guess earns 3 spins; the OTHER two pick from the answer plus two decoys", () =>
        {
            var t = new PylTable();
            t.Game.Buzz(t.Tokens[0]);
            Check.Fails(t.Game.JudgeGuess(t.Tokens[0], true), "a player judging");
            Check.Ok(t.Game.JudgeGuess(t.Emcee, true));
            var s = t.S;
            Check.Equal(3, t.P(0).Spins, "the guesser's spins");
            Check.Equal(PylPhase.Picking, s.Phase, "phase");
            Check.Equal(3, s.Options.Count, "three choices");
            Check.True(s.Options.Contains("Answer1") && s.Options.Contains("Decoy1A") && s.Options.Contains("Decoy1B"), "answer + 2 decoys");
            Check.Equal("Answer1", s.Options[s.CorrectIndex], "the correct index points at the answer");
            Check.True(!s.Players[0].IsPicker && s.Players[1].IsPicker && s.Players[2].IsPicker, "only Bob and Cy pick");
            Check.Fails(t.Game.Pick(t.Tokens[0], 0), "the guesser picking");
        });

        run("pyl: a wrong guess is one of the choices; picks earn 1 spin each, wrong picks cost nothing", () =>
        {
            var t = new PylTable();
            t.Game.Buzz(t.Tokens[2]);
            Check.Ok(t.Game.JudgeGuess(t.Emcee, false, "  Banana "));
            var s = t.S;
            Check.Equal(0, t.P(2).Spins, "no spins for a wrong guess");
            Check.True(s.Options.Contains("Answer1") && s.Options.Contains("Banana"), "answer and the wrong guess");
            Check.Equal(3, s.Options.Count, "three choices");
            Check.True(s.Options.Count(o => o.StartsWith("Decoy")) == 1, "exactly one decoy");

            Check.Ok(t.Game.Pick(t.Tokens[0], s.CorrectIndex));
            Check.Fails(t.Game.Pick(t.Tokens[0], s.CorrectIndex), "picking twice");
            Check.Equal(PylPhase.Picking, t.S.Phase, "still waiting on Bob");
            Check.Ok(t.Game.Pick(t.Tokens[1], (s.CorrectIndex + 1) % 3));
            Check.Equal(PylPhase.Reveal, t.S.Phase, "reveals when everyone has picked");
            Check.Equal(1, t.P(0).Spins, "the right pick earns 1 spin");
            Check.Equal(0, t.P(1).Spins, "the wrong pick earns nothing, and loses nothing");
        });

        run("pyl: a 'wrong guess' that repeats the answer or is blank falls back to a second decoy", () =>
        {
            foreach (var typed in new[] { "", "   ", "answer1", null })
            {
                var t = new PylTable();
                t.Game.Buzz(t.Tokens[0]);
                Check.Ok(t.Game.JudgeGuess(t.Emcee, false, typed));
                var s = t.S;
                Check.Equal(3, s.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count(), $"three different choices for '{typed}'");
                Check.True(s.Options.Count(o => o.StartsWith("Decoy")) == 2, "two decoys");
            }
        });

        run("pyl: if nobody buzzes, all three pick and each right pick earns a spin", () =>
        {
            var t = new PylTable();
            Check.Fails(t.Game.NoBuzz(t.Tokens[0]), "a player calling it");
            Check.Ok(t.Game.NoBuzz(t.Emcee));
            var s = t.S;
            Check.True(s.Players.All(p => p.IsPicker), "everyone picks");
            Check.Ok(t.Game.Pick(t.Tokens[0], s.CorrectIndex));
            Check.Ok(t.Game.Pick(t.Tokens[1], s.CorrectIndex));
            Check.Ok(t.Game.Pick(t.Tokens[2], (s.CorrectIndex + 2) % 3));
            Check.Equal(PylPhase.Reveal, t.S.Phase, "revealed");
            Check.True(t.S.Message!.Contains("Nobody buzzed in"), "the reveal says so");
            Check.True(t.P(0).Spins == 1 && t.P(1).Spins == 1 && t.P(2).Spins == 0, "spins follow the picks");
        });

        run("pyl: the emcee can reveal early if a phone dies", () =>
        {
            var t = new PylTable();
            t.Game.Buzz(t.Tokens[0]);
            t.Game.JudgeGuess(t.Emcee, true);
            t.Game.Pick(t.Tokens[1], t.S.CorrectIndex);
            Check.Fails(t.Game.RevealNow(t.Tokens[1]), "a player revealing");
            Check.Ok(t.Game.RevealNow(t.Emcee));
            Check.Equal(PylPhase.Reveal, t.S.Phase, "revealed");
            Check.Equal(1, t.P(1).Spins, "Bob still earned his spin");
        });

        run("pyl: three questions per round, then the Big Board with a dealt board", () =>
        {
            var t = new PylTable();
            t.PlayQuestion();
            Check.Equal(PylPhase.Question, t.S.Phase, "next question");
            Check.Equal(2, t.S.Question!.Number, "question 2");
            t.PlayQuestion(); t.PlayQuestion();
            var s = t.S;
            Check.Equal(PylPhase.SpinReady, s.Phase, "the Big Board follows the third question");
            Check.Equal(18, s.Board.Count, "18 squares");
            Check.True(s.Board.All(f => f.Count == 3), "3 faces each");
            Check.True(s.Question is null, "no question while spinning");
        });

        Console.WriteLine();
        Console.WriteLine("Press Your Luck: the Big Board");

        run("pyl: every dealt board has 54 outcomes, 9 Whammies on 9 different squares, and no corner Pick a Corner", () =>
        {
            foreach (var round in new[] { 1, 2 })
            {
                var config = BoardConfig.Load(round);
                for (var seed = 0; seed < 300; seed++)
                {
                    var board = BoardFactory.Deal(config, new Random(seed));
                    var all = board.SelectMany(f => f).ToList();
                    Check.Equal(54, all.Count, "outcomes");
                    Check.Equal(9, all.Count(o => o.Kind == OutcomeKind.Whammy), "Whammies");
                    Check.True(board.All(f => f.Count(o => o.Kind == OutcomeKind.Whammy) <= 1), "no square is a Whammy twice");
                    var perFace = Enumerable.Range(0, 3).Select(f => board.Count(sq => sq[f].Kind == OutcomeKind.Whammy)).Order().ToArray();
                    Check.True(perFace.SequenceEqual([1, 3, 5]), $"Whammies split 1/3/5 across faces, got {string.Join("/", perFace)}");
                    Check.Equal(4, all.Count(o => o.Kind == OutcomeKind.Corner), "Pick a Corner squares");
                    Check.True(BoardFactory.Corners.All(c => board[c].All(o => o.Kind != OutcomeKind.Corner)), "no Pick a Corner on a corner");
                    Check.Equal(14, all.Count(o => o.Kind == OutcomeKind.Cash && o.ExtraSpin), "cash + spin squares (10 + 4 big)");
                    Check.Equal(5, all.Count(o => o.Kind == OutcomeKind.Prize), "prizes");
                }
            }
        });

        run("pyl: round 2's board pays more than round 1's", () =>
        {
            decimal Avg(int round) => (decimal)Enumerable.Range(0, 200)
                .SelectMany(s => BoardFactory.Deal(BoardConfig.Load(round), new Random(s)).SelectMany(f => f))
                .Where(o => o.Kind == OutcomeKind.Cash).Average(o => o.Value);
            Check.True(Avg(2) > Avg(1), "round 2 cash should average higher");
        });

        run("pyl: the fewest spins goes first; spins are only playable on your own turn", () =>
        {
            var t = new PylTable();
            // Ann guesses right every time (9 spins by the end); Bob picks right (3 spins); Cy picks wrong (0 spins).
            t.PlayQuestionRound();
            var s = t.S;
            Check.Equal(9, t.P(0).Spins, "Ann's spins");
            Check.Equal(3, t.P(1).Spins, "Bob's spins");
            Check.Equal(t.Ids[1], s.ActingId!.Value, "Cy has 0 spins, so Bob (the next fewest) is up");
            Check.Fails(t.Game.Spin(t.Tokens[0]), "Ann spinning on Bob's turn");
            Check.Ok(t.Game.Spin(t.Tokens[1]));
        });

        run("pyl: pressing stops the light on exactly the square the server's clock says", () =>
        {
            var t = new PylTable();
            t.PlayQuestionRound();
            var s = t.StartSpin();
            Check.Equal(PylPhase.Spinning, s.Phase, "phase");
            Check.Equal(PressYourLuckGame.SequenceLength, s.Spin!.Sequence.Count, "the light has a long script");
            Check.True(Enumerable.Range(1, 1999).All(i => s.Spin.Sequence[i] != s.Spin.Sequence[i - 1]), "the light always moves");

            foreach (var tick in new[] { 0, 1, 7, 33, 150 })
            {
                var table = new PylTable();
                table.PlayQuestionRound();
                var spinning = table.StartSpin();
                Check.Ok(table.PressAtTick(spinning, tick));
                var result = table.S.LastSpin!;
                Check.Equal(tick, result.Tick, "tick");
                Check.Equal(spinning.Spin!.Sequence[tick], result.Square, $"the lit square at tick {tick}");
            }
        });

        run("pyl: only the acting player or the emcee can press; nobody can press when the light isn't running", () =>
        {
            var t = new PylTable();
            t.PlayQuestionRound();
            Check.Fails(t.Game.Press(t.Tokens[1]), "pressing before a spin");
            var s = t.StartSpin();
            Check.Fails(t.PressAtTick(s, 5, t.Tokens[2]), "the wrong player pressing");
            Check.Fails(t.PressAtTick(s, 5, Guid.NewGuid()), "a stranger pressing");
            Check.Ok(t.PressAtTick(s, 5, t.Emcee));
        });

        run("pyl: cash adds to the score; '+ SPIN' also adds a spin; each spin costs a spin", () =>
        {
            var t = new PylTable();
            t.PlayQuestionRound();
            var actor = t.IndexOf(t.S.ActingId!.Value);
            var before = t.P(actor);

            var plain = t.SpinFor(PylTable.IsPlainCash);
            var after = t.P(actor);
            var amount = int.Parse(plain.Text.TrimStart('$').Replace(",", ""));
            Check.Equal(before.Score + amount, after.Score, "score");
            Check.Equal(before.Spins - 1, after.Spins, "a plain cash spin uses up a spin");

            var withSpin = t.SpinFor(PylTable.IsCashSpin);
            var amount2 = int.Parse(withSpin.Text.TrimStart('$').Replace(",", ""));
            var after2 = t.P(actor);
            Check.Equal(after.Score + amount2, after2.Score, "score");
            Check.Equal(after.Spins - 1 + 1, after2.Spins, "a '+ SPIN' square gives the spin back");
        });

        run("pyl: a Whammy wipes the score; four Whammies knock you out and forfeit your spins", () =>
        {
            var t = new PylTable();
            t.PlayQuestionRound();

            // Let Bob use up his spins on cash, so it becomes Ann's turn (she has 9).
            while (t.S.ActingId != t.Ids[0]) t.SpinFor(PylTable.IsPlainCash);
            var ann = 0;
            t.SpinFor(PylTable.IsPlainCash);
            Check.True(t.P(ann).Score > 0, "some money to lose");

            t.SpinFor(o => o.Kind == "whammy");
            Check.Equal(0, t.P(ann).Score, "score wiped");
            Check.Equal(1, t.P(ann).Whammies, "one Whammy");
            Check.True(t.P(ann).Spins > 0 && !t.P(ann).IsOut, "still in, spins kept");

            t.SpinFor(o => o.Kind == "whammy");
            t.SpinFor(o => o.Kind == "whammy");
            Check.Equal(3, t.P(ann).Whammies, "three Whammies");
            Check.True(!t.P(ann).IsOut, "three is not out");

            var last = t.SpinFor(o => o.Kind == "whammy");
            Check.True(last.Message.Contains("is out"), "the announcement says she is out");
            Check.True(t.P(ann).IsOut, "out after four");
            Check.Equal(0, t.P(ann).Spins, "spins forfeited");
            Check.True(t.S.ActingId != t.Ids[ann] && t.S.Phase != PylPhase.SpinReady || t.S.ActingId != t.Ids[ann], "play moves on");
        });

        run("pyl: Pick a Corner lets you choose one of four corners and take what it shows", () =>
        {
            var t = new PylTable();
            t.PlayQuestionRound();
            var actor = t.S.ActingId!.Value;
            var spinning = t.StartSpin();
            var tick = PylTable.FindTick(spinning, o => o.Kind == "corner");
            Check.Ok(t.PressAtTick(spinning, tick));
            Check.Equal(PylPhase.PickCorner, t.S.Phase, "phase");
            Check.True(t.S.LastSpin!.Pending, "the result is pending a choice");
            Check.Fails(t.Game.Spin(t.Tokens[t.IndexOf(actor)]), "spinning mid-choice");
            Check.Fails(t.Game.PickCorner(t.Tokens[t.IndexOf(actor)], 3), "square 3 isn't a corner");
            Check.Fails(t.Game.PickCorner(t.Tokens[(t.IndexOf(actor) + 1) % 3], 5), "somebody else choosing");

            var shown = spinning.Board[5][PressYourLuckGame.FaceAt(5, tick)];
            Check.Ok(t.Game.PickCorner(t.Tokens[t.IndexOf(actor)], 5));
            var done = t.S.LastSpin!;
            Check.True(!done.Pending && done.Square == 5, "resolved on the chosen corner");
            Check.Equal(shown.Text, done.Text, "you get what that corner was showing when the light stopped");
        });

        run("pyl: passing: not before a first spin, only to the top-scoring opponent, and passed spins can't be re-passed", () =>
        {
            var t = new PylTable();
            t.PlayQuestionRound();
            // Bob (3 spins) is up. Give Ann a higher score than Cy by letting Ann... Ann hasn't played yet, so all are $0:
            // Ann and Cy are tied for the lead, which means Bob must say which one.
            var bob = t.Ids[1];
            Check.Fails(t.Game.Pass(t.Tokens[1]), "passing before spinning");
            t.SpinFor(PylTable.IsPlainCash);
            var s = t.S;
            Check.True(s.CanPass, "pass is offered after a spin");
            Check.Equal(2, s.PassTargets.Count, "Ann and Cy are tied, so either could get them");
            Check.Fails(t.Game.Pass(t.Tokens[1]), "not saying who");
            Check.Fails(t.Game.Pass(t.Tokens[1], bob), "passing to yourself");
            Check.Ok(t.Game.Pass(t.Tokens[1], t.Ids[2]));
            var after = t.S;
            Check.Equal(t.Ids[2], after.ActingId!.Value, "Cy is now up");
            Check.True(after.ActingIsPassed && after.ActingSpinsLeft == 2, "Cy plays Bob's 2 leftover spins");
            Check.Equal(0, t.P(1).Spins, "Bob has none left");
            Check.True(!after.CanPass, "passed spins can't be passed again");
            Check.Fails(t.Game.Pass(t.Tokens[2], t.Ids[0]), "re-passing");
        });

        run("pyl: passing goes to the single highest scorer without choosing", () =>
        {
            var t = new PylTable();
            t.PlayQuestionRound();
            // Bob spins and scores; then Bob isn't the leader among the others. Make Ann the clear leader instead:
            // Bob passes (to a tied pair) to Cy, Cy plays, then Ann's turn comes with 9 spins.
            t.SpinFor(PylTable.IsPlainCash);
            Check.Ok(t.Game.Pass(t.Tokens[1], t.Ids[0]));      // Ann gets Bob's 2 spins
            while (t.S.ActingIsPassed) t.SpinFor(PylTable.IsPlainCash);
            // Now Cy (0 spins) is skipped; Ann's own 9 spins are next.
            var s = t.S;
            Check.Equal(t.Ids[0], s.ActingId!.Value, "Ann's own turn");
            Check.True(!s.ActingIsPassed, "playing her own spins");
            t.SpinFor(PylTable.IsPlainCash);
            // Ann has cash from both; Bob has cash; Cy has none. Ann's opponents: Bob (has money) beats Cy ($0).
            Check.Equal(1, t.S.PassTargets.Count, "one clear leader among her opponents");
            Check.Equal(t.Ids[1], t.S.PassTargets[0], "Bob");
            Check.Ok(t.Game.Pass(t.Tokens[0]));
            Check.Equal(t.Ids[1], t.S.ActingId!.Value, "Bob plays Ann's spins");
        });

        run("pyl: when the last spin is played the round ends; the emcee continues to round 2", () =>
        {
            var t = new PylTable();
            t.PlayQuestionRound();
            var guard = 0;
            while (t.S.Phase is PylPhase.SpinReady && guard++ < 500)
                t.SpinFor(o => o.Kind == "cash" && o.Sub is null);
            Check.Equal(PylPhase.RoundOver, t.S.Phase, "the round ends when nobody has spins");
            Check.True(t.S.Players.All(p => p.Spins == 0), "no spins left");
            Check.Fails(t.Game.ContinueFromRound(t.Tokens[0]), "a player continuing");
            Check.Ok(t.Game.ContinueFromRound(t.Emcee));
            Check.Equal(PylPhase.Question, t.S.Phase, "back to questions");
            Check.Equal(2, t.S.Round, "round 2");
            Check.Equal(1, t.S.Question!.Number, "its first question");
            Check.Equal("CATEGORY 4", t.S.Question.Category, "the 4th question of the game");
            // Scores carry over.
            Check.True(t.S.Players.Any(p => p.Score > 0), "scores carry into round 2");
        });

        run("pyl: a whole game ends with the highest score winning (ties share it)", () =>
        {
            var t = new PylTable();
            for (var round = 0; round < 2; round++)
            {
                t.PlayQuestionRound();
                var guard = 0;
                while (t.S.Phase is PylPhase.SpinReady && guard++ < 500) t.SpinFor(PylTable.IsPlainCash);
                Check.Equal(PylPhase.RoundOver, t.S.Phase, "round over");
                Check.Ok(t.Game.ContinueFromRound(t.Emcee));
            }
            var s = t.S;
            Check.Equal(PylPhase.GameOver, s.Phase, "game over after round 2");
            var best = s.Players.Max(p => p.Score);
            Check.True(s.Winners.Count >= 1 && s.Winners.All(id => s.FindPlayer(id)!.Score == best), "winners hold the top score");
        });

        run("pyl: everyone Whammied to zero means nobody wins", () =>
        {
            var t = new PylTable();
            t.PlayQuestionRound();
            var guard = 0;
            while (t.S.Phase is PylPhase.SpinReady && guard++ < 500) t.SpinFor(o => o.Kind == "whammy");
            Check.Equal(PylPhase.RoundOver, t.S.Phase, "round over");
            Check.Ok(t.Game.ContinueFromRound(t.Emcee));
            t.PlayQuestionRound();
            guard = 0;
            while (t.S.Phase is PylPhase.SpinReady && guard++ < 500) t.SpinFor(o => o.Kind == "whammy");
            if (t.S.Phase == PylPhase.RoundOver) Check.Ok(t.Game.ContinueFromRound(t.Emcee));
            Check.Equal(PylPhase.GameOver, t.S.Phase, "game over");
            Check.Equal(0, t.S.Winners.Count, "no winner when everybody has nothing");
        });

        run("pyl: 300 whole games played by a random bot never break a rule", () =>
        {
            for (var seed = 0; seed < 300; seed++)
            {
                var t = new PylTable(seed);
                var rng = new Random(seed);
                var steps = 0;
                while (t.S.Phase != PylPhase.GameOver)
                {
                    if (++steps > 4000) throw new Exception($"game {seed} did not finish");
                    var s = t.S;
                    switch (s.Phase)
                    {
                        case PylPhase.Question:
                            if (rng.Next(5) == 0) Check.Ok(t.Game.NoBuzz(t.Emcee));
                            else Check.Ok(t.Game.Buzz(t.Tokens[rng.Next(3)]));
                            break;
                        case PylPhase.Answering:
                            Check.Ok(t.Game.JudgeGuess(t.Emcee, rng.Next(2) == 0, rng.Next(2) == 0 ? "Guess" : null));
                            break;
                        case PylPhase.Picking:
                            foreach (var p in s.Players.Where(p => p.IsPicker && p.PickedIndex is null))
                                Check.Ok(t.Game.Pick(t.Tokens[t.IndexOf(p.Id)], rng.Next(3)));
                            break;
                        case PylPhase.Reveal: Check.Ok(t.Game.ContinueFromQuestion(t.Emcee)); break;
                        case PylPhase.SpinReady:
                            if (s.CanPass && rng.Next(3) == 0)
                                Check.Ok(t.Game.Pass(t.Tokens[t.IndexOf(s.ActingId!.Value)], s.PassTargets[rng.Next(s.PassTargets.Count)]));
                            else
                                Check.Ok(t.Game.Spin(t.Tokens[t.IndexOf(s.ActingId!.Value)]));
                            break;
                        case PylPhase.Spinning:
                            Check.Ok(t.PressAtTick(s, rng.Next(0, 400)));
                            break;
                        case PylPhase.PickCorner:
                            Check.Ok(t.Game.PickCorner(t.Tokens[t.IndexOf(s.ActingId!.Value)], BoardFactory.Corners[rng.Next(4)]));
                            break;
                        case PylPhase.RoundOver: Check.Ok(t.Game.ContinueFromRound(t.Emcee)); break;
                    }
                    var after = t.S;
                    Check.True(after.Players.All(p => p.Spins >= 0 && p.Score >= 0 && p.Whammies <= 4), "no negative spins or scores");
                    Check.True(after.Players.All(p => !p.IsOut || (p.Whammies == 4 && p.Spins == 0)), "out means 4 Whammies and no spins");
                }
            }
        });

        run("pyl: real questions from the bank: 6 different categories, 3 choices that are all different", () =>
        {
            var source = new JsonPylQuestionSource();
            for (var seed = 0; seed < 300; seed++)
            {
                var qs = source.CreateQuestions(new Random(seed), 2, 3, new GameOptions(seed % 3 == 0));
                Check.Equal(6, qs.Count, "six questions");
                Check.Equal(6, qs.Select(q => q.Category).Distinct().Count(), "different categories");
                Check.True(qs.All(q => q.Decoys.Count == 2 && q.Decoys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2), "two different decoys");
                Check.True(qs.All(q => !q.Decoys.Contains(q.Answer, StringComparer.OrdinalIgnoreCase)), "decoys differ from the answer");
                Check.True(qs.All(q => q.Answer.Length < 3 || !System.Text.RegularExpressions.Regex.IsMatch(q.Text, @"\b" + System.Text.RegularExpressions.Regex.Escape(q.Answer) + @"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)), "the clue doesn't contain the answer");
            }
        });

        run("pyl: the holiday option adds exactly one holiday question, and only when asked", () =>
        {
            var source = new JsonPylQuestionSource();
            var holidayTitles = QuestionBank.LoadCategories().Where(c => c.Tag == "holiday").Select(c => c.Title.ToUpperInvariant()).ToHashSet();
            for (var seed = 0; seed < 200; seed++)
            {
                var on = source.CreateQuestions(new Random(seed), 2, 3, new GameOptions(true));
                var off = source.CreateQuestions(new Random(seed), 2, 3, new GameOptions(false));
                Check.Equal(1, on.Count(q => holidayTitles.Contains(q.Category)), "one holiday question when asked");
                Check.Equal(0, off.Count(q => holidayTitles.Contains(q.Category)), "none otherwise");
            }
        });
    }
}
