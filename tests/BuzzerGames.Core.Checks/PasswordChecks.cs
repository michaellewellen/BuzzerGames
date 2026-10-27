using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Password;

/// <summary>
/// A ready-made Password Plus table: one emcee (Host), Team A = Ann (player 1) and Bob (player 2),
/// Team B = Cy (player 1) and Dee (player 2). Tokens and Ids are in that order: Ann, Bob, Cy, Dee.
/// </summary>
sealed class PasswordTable
{
    // Clues that are never part of the fixed puzzle's passwords (Anchor, Banjo, Cactus, Dolphin, Engine).
    private static readonly string[] Hints = ["red", "blue", "green", "pink", "gray", "gold", "teal", "navy", "plum", "lime"];

    private int _hintCounter;

    public PasswordGame Game { get; }
    public Guid Emcee { get; }
    public Guid[] Tokens { get; }
    public Guid[] Ids { get; }

    /// <summary>The game's clock. It only moves when a check calls <see cref="Advance"/>.</summary>
    public DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public void Advance(double seconds) => Now = Now.AddSeconds(seconds);

    public PasswordTable(int seed = 1, bool start = true, IPasswordPuzzleSource? source = null, IBonusWordSource? bonus = null)
    {
        Game = new PasswordGame("TEST", source ?? new FixedPasswordPuzzleSource(), new Random(seed),
            bonus ?? new FixedBonusWordSource('A'), () => Now);
        Emcee = Game.JoinEmcee("Host").Token;
        Tokens =
        [
            Game.JoinPlayer("Ann", 0).Token,
            Game.JoinPlayer("Bob", 0).Token,
            Game.JoinPlayer("Cy", 1).Token,
            Game.JoinPlayer("Dee", 1).Token
        ];
        Ids = Tokens.Select(t => Game.GetSeat(t).PlayerId!.Value).ToArray();
        if (start) Check.Ok(Game.Start());
    }

    public PasswordSnapshot S => Game.Snapshot();
    public Guid TokenOf(Guid id) => Tokens[Array.IndexOf(Ids, id)];
    public int IndexOf(Guid id) => Array.IndexOf(Ids, id);
    public Guid GiverToken(int team) => TokenOf(S.Giver(team)!.Id);
    public string NextHint() => Hints[_hintCounter++ % Hints.Length];

    /// <summary>From the Control phase: the team with control chooses so that <paramref name="team"/> gives the first clue.</summary>
    public void Open(int team)
    {
        var control = S.ControlTeam;
        var chooser = TokenOf(S.Players.First(p => p.Team == control).Id);
        Check.Ok(Game.ChooseControl(chooser, play: team == control));
        Check.Equal(PasswordPhase.Clue, S.Phase, "phase after choosing");
        Check.Equal(team, S.TurnTeam, "the team giving the first clue");
    }

    /// <summary>The clue-giver of the team on the clock sends a (valid) clue.</summary>
    public void Clue() => Check.Ok(Game.SubmitClue(GiverToken(S.TurnTeam), NextHint()));

    /// <summary>A clue, then the emcee's ruling on the guess.</summary>
    public void ClueAndJudge(bool right)
    {
        Clue();
        Check.Ok(Game.JudgeGuess(Emcee, right));
    }

    /// <summary>Plays all eight clues of a word, every guess wrong.</summary>
    public void MissAllClues()
    {
        for (var i = 0; i < PasswordGame.Teams * PasswordGame.CluesPerSide; i++) ClueAndJudge(false);
    }

    /// <summary>From the Control phase: <paramref name="team"/> clues first, their guesser gets the word, and the big-word guess is ruled.</summary>
    public void SolveWord(int team, bool bigWordRight)
    {
        Open(team);
        ClueAndJudge(true);
        Check.Equal(PasswordPhase.PuzzleGuess, S.Phase, "phase after a right word");
        Check.Ok(Game.JudgePuzzleGuess(Emcee, bigWordRight));
    }

    /// <summary>Starts the clock, lets all 60 seconds run out, and moves on to the end of the game.</summary>
    public void PlayBonusOut()
    {
        Check.Equal(PasswordPhase.BonusReady, S.Phase, "ready for the bonus round");
        Check.Ok(Game.StartBonus(Emcee));
        Advance(PasswordGame.BonusSeconds + 1);
        Check.Ok(Game.Tick());
        Check.Equal(PasswordPhase.BonusResult, S.Phase, "the clock ran out");
        Check.Ok(Game.Continue(Emcee));
        Check.Equal(PasswordPhase.GameOver, S.Phase, "game over");
    }

    /// <summary>Solves puzzle after puzzle for <paramref name="team"/> until the game reaches the bonus round.</summary>
    public void PlayToBonus(int team)
    {
        var guard = 0;
        while (S.Phase != PasswordPhase.BonusReady && guard++ < 20)
        {
            SolveWord(team, bigWordRight: true);
            Check.Ok(Game.Continue(Emcee));
        }
        Check.Equal(PasswordPhase.BonusReady, S.Phase, "reached the bonus round");
    }

    /// <summary>Plays on, solving everything for <paramref name="team"/> and playing the bonus out, until the game is over.</summary>
    public void PlayToGameOver(int team)
    {
        var guard = 0;
        while (S.Phase != PasswordPhase.GameOver && guard++ < 40)
        {
            if (S.Phase == PasswordPhase.BonusReady) { PlayBonusOut(); break; }
            SolveWord(team, bigWordRight: true);
            Check.Ok(Game.Continue(Emcee));
        }
        Check.Equal(PasswordPhase.GameOver, S.Phase, "the game ended");
    }

    /// <summary>From the Control phase: <paramref name="team"/> solves the whole puzzle on its first word, then the emcee continues.</summary>
    public void SolvePuzzle(int team)
    {
        SolveWord(team, bigWordRight: true);
        Check.Equal(PasswordPhase.PuzzleResult, S.Phase, "phase after solving the puzzle");
        Check.Ok(Game.Continue(Emcee));
    }

    /// <summary>
    /// Plays the first four words of the puzzle, each won by <paramref name="team"/> (who keeps control) with a wrong
    /// big-word guess, ending in the Control phase of word 5.
    /// </summary>
    public void PlayToWordFive(int team)
    {
        Open(team);
        ClueAndJudge(true);
        Check.Ok(Game.JudgePuzzleGuess(Emcee, false));
        Check.Ok(Game.Continue(Emcee));
        for (var i = 1; i < PasswordGame.WordsPerPuzzle - 1; i++)
        {
            Open(team);
            ClueAndJudge(true);
            Check.Equal(1, S.AttemptsLeft, $"only the guesser gets a shot on word {i + 1}");
            Check.Ok(Game.JudgePuzzleGuess(Emcee, false));
            Check.Ok(Game.Continue(Emcee));
        }
        Check.Equal(PasswordGame.WordsPerPuzzle, S.WordNumber, "now on the last word");
        Check.Equal(PasswordPhase.Control, S.Phase, "waiting for the play-or-pass choice");
    }
}

/// <summary>A source that only has a single puzzle, to check what happens when the puzzles run out.</summary>
sealed class OnePuzzleSource : IPasswordPuzzleSource
{
    public IReadOnlyList<PasswordPuzzle> CreatePuzzles(Random rng, int count) =>
        new FixedPasswordPuzzleSource().CreatePuzzles(rng, 1);
}

static class PasswordChecks
{
    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine();
        Console.WriteLine("Password Plus: the table");

        run("password: needs 1 emcee and two players on each team, then starts", () =>
        {
            var game = new PasswordGame("TEST", new FixedPasswordPuzzleSource(), new Random(1));
            Check.Fails(game.Start(), "starting an empty lobby");
            Check.Ok(game.JoinEmcee("Host"));
            Check.Fails(game.JoinEmcee("Second Host"), "a second emcee");
            Check.Ok(game.JoinPlayer("Ann", 0));
            Check.Ok(game.JoinPlayer("Bob", 0));
            Check.Fails(game.JoinPlayer("Cy", 0), "a third player on Team A");
            Check.Fails(game.JoinPlayer("   ", 1), "a blank name");
            Check.Fails(game.JoinPlayer("ann", 1), "a duplicate name, ignoring case");
            Check.Fails(game.JoinPlayer("Cy", 2), "a team that doesn't exist");
            Check.Fails(game.JoinPlayer("Cy", -1), "a negative team");
            Check.True(!game.Snapshot().CanStart, "start must be hidden while the table is not full");
            Check.Ok(game.JoinPlayer("Cy", 1));
            Check.Fails(game.Start(), "starting with three players");
            Check.Ok(game.JoinPlayer("Dee", 1));
            Check.Fails(game.JoinPlayer("Eve", 1), "a third player on Team B");
            Check.True(game.Snapshot().CanStart, "start appears once the table is full");
            Check.Ok(game.Start());
            Check.Fails(game.Start(), "starting twice");
            Check.Fails(game.JoinEmcee("Late"), "joining a started game as emcee");
            Check.Fails(game.JoinPlayer("Late", 0), "joining a started game as a player");
        });

        run("password: players land on their team in joining order, and names are trimmed", () =>
        {
            var game = new PasswordGame("TEST", new FixedPasswordPuzzleSource(), new Random(1));
            game.JoinEmcee("  Host  ");
            game.JoinPlayer(new string('x', 50), 1);   // first on Team B
            game.JoinPlayer("Bob", 0);                 // first on Team A
            game.JoinPlayer("Cy", 1);                  // second on Team B
            var s = game.Snapshot();
            Check.Equal("Host", s.EmceeName, "the emcee's name is trimmed");
            Check.Equal(3, s.Players.Count, "players so far");
            Check.Equal("Bob", s.Players[0].Name, "Team A comes first on the roster");
            Check.Equal(0, s.Players[0].Slot, "Bob is player 1 of Team A");
            Check.Equal(PasswordGame.NameMaxLength, s.Players[1].Name.Length, "a long name is cut to the limit");
            Check.Equal(0, s.Players[1].Slot, "the long name is player 1 of Team B");
            Check.Equal(1, s.Players[2].Slot, "Cy is player 2 of Team B");
            Check.Equal(1, s.Teams[0].PlayerIds.Count, "Team A has one player so far");
            Check.Equal(2, s.Teams[1].PlayerIds.Count, "Team B has two");
        });

        run("password: a device's secret token maps back to its seat", () =>
        {
            var t = new PasswordTable();
            Check.Equal(Role.Emcee, t.Game.GetSeat(t.Emcee).Role, "emcee token");
            Check.Equal(Role.Player, t.Game.GetSeat(t.Tokens[2]).Role, "player token");
            Check.Equal(t.Ids[2], t.Game.GetSeat(t.Tokens[2]).PlayerId!.Value, "player id");
            Check.Equal(Role.Spectator, t.Game.GetSeat(Guid.NewGuid()).Role, "unknown token");
        });

        Console.WriteLine();
        Console.WriteLine("Password Plus: control, clues and guesses");

        run("password: the game opens on a coin flip for control, and both teams can win it", () =>
        {
            var winners = new HashSet<int>();
            for (var seed = 1; seed <= 30; seed++)
            {
                var s = new PasswordTable(seed).S;
                Check.Equal(PasswordPhase.Control, s.Phase, "phase at the start");
                Check.True(s.ControlTeam is 0 or 1, "someone has control");
                Check.True(s.Message is not null && s.Message.Contains("coin flip"), "the coin flip is announced");
                Check.Equal(1, s.PuzzleNumber, "puzzle number");
                Check.Equal(1, s.WordNumber, "word number");
                Check.Equal(100, s.PuzzleValue, "the first puzzle is worth 100");
                winners.Add(s.ControlTeam);
            }
            Check.Equal(2, winners.Count, "both teams should win the flip sometimes");
        });

        run("password: only the team with control (or the emcee) chooses play or pass", () =>
        {
            var t = new PasswordTable();
            var control = t.S.ControlTeam;
            var other = 1 - control;
            Check.Fails(t.Game.ChooseControl(Guid.NewGuid(), true), "a stranger choosing");
            Check.Fails(t.Game.ChooseControl(t.Tokens[other * 2], true), "the other team choosing");
            Check.Fails(t.Game.ChooseControl(t.Tokens[other * 2 + 1], false), "the other team's second player choosing");
            Check.Fails(t.Game.SubmitClue(t.Tokens[control * 2], "red"), "a clue before the choice is made");
            Check.Ok(t.Game.ChooseControl(t.Tokens[control * 2 + 1], play: true));
            Check.Equal(control, t.S.TurnTeam, "play: the team with control clues first");
            Check.Fails(t.Game.ChooseControl(t.Tokens[control * 2], false), "choosing twice");
        });

        run("password: pass gives the first clue to the other team, and the emcee can choose for a dead phone", () =>
        {
            var t = new PasswordTable();
            var control = t.S.ControlTeam;
            Check.Ok(t.Game.ChooseControl(t.Emcee, play: false));
            Check.Equal(1 - control, t.S.TurnTeam, "pass: the other team clues first");
            Check.Equal(PasswordPhase.Clue, t.S.Phase, "phase");
        });

        run("password: roles hold for the whole puzzle and only swap when the next big word begins", () =>
        {
            var t = new PasswordTable();
            void ExpectGiver(int slot, string when)
            {
                foreach (var team in new[] { 0, 1 })
                {
                    Check.True(t.S.Players.Single(p => p.Team == team && p.Slot == slot).IsGiver, $"team {team} player {slot + 1} gives {when}");
                    Check.True(t.S.Players.Single(p => p.Team == team && p.Slot == 1 - slot).IsGuesser, $"team {team} player {2 - slot} guesses {when}");
                }
            }

            ExpectGiver(0, "on word 1 of puzzle 1");
            t.Open(0);
            t.MissAllClues();
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(2, t.S.WordNumber, "word 2");
            ExpectGiver(0, "on word 2 of puzzle 1 (nobody swaps mid-puzzle)");

            t.SolveWord(0, bigWordRight: true);                 // the big word is found on word 2
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(2, t.S.PuzzleNumber, "puzzle 2");
            Check.Equal(1, t.S.WordNumber, "its first word");
            ExpectGiver(1, "in puzzle 2");

            t.Open(0);
            t.MissAllClues();
            Check.Ok(t.Game.Continue(t.Emcee));
            ExpectGiver(1, "on word 2 of puzzle 2");

            t.SolveWord(0, bigWordRight: true);
            Check.Ok(t.Game.Continue(t.Emcee));
            ExpectGiver(0, "in puzzle 3");
        });

        run("password: only the clue-giver on the clock can send a clue", () =>
        {
            var t = new PasswordTable();
            t.Open(0);                                          // Team A clues; Ann (player 1) gives
            Check.Fails(t.Game.SubmitClue(t.Tokens[1], "red"), "Bob, the guesser, sending a clue");
            Check.Fails(t.Game.SubmitClue(t.Tokens[2], "red"), "Cy, the other team's clue-giver, sending a clue");
            Check.Fails(t.Game.SubmitClue(Guid.NewGuid(), "red"), "a stranger sending a clue");
            Check.Ok(t.Game.SubmitClue(t.Tokens[0], "red"));
            Check.Equal(PasswordPhase.Guess, t.S.Phase, "phase after a clue");

            var u = new PasswordTable();
            u.Open(1);
            Check.Ok(u.Game.SubmitClue(u.Emcee, "red"));        // the emcee can send it for a dead phone
        });

        run("password: the emcee judges clues; throwing one out costs only that clue and passes the turn", () =>
        {
            var t = new PasswordTable();
            t.Open(0);                                          // the password is "Anchor"
            foreach (var bad in new[] { "", "   ", new string('a', 40) })
                Check.Fails(t.Game.SubmitClue(t.Tokens[0], bad), $"the clue \"{bad.Trim()}\"");
            Check.Equal(PasswordGame.CluesPerSide, t.S.Teams[0].CluesLeft, "no clue used up by an empty try");

            // Ann breaks the rules (two words). The emcee throws it out: that clue is spent, Bob never guesses, and Team B is up.
            Check.Ok(t.Game.SubmitClue(t.Tokens[0], "two words"));
            Check.Equal(PasswordPhase.Guess, t.S.Phase, "the emcee gets to rule on it");
            Check.Fails(t.Game.RejectClue(t.Tokens[1]), "a player throwing out a clue");
            Check.Ok(t.Game.RejectClue(t.Emcee));
            Check.Equal(ClueOutcome.Rejected, t.S.Clues[^1].Outcome, "the clue is marked thrown out");
            Check.Equal(PasswordPhase.Clue, t.S.Phase, "back to a clue");
            Check.Equal(1, t.S.TurnTeam, "Team B is next");
            Check.Equal(3, t.S.Teams[0].CluesLeft, "Team A used one of its four");
            Check.Equal(WordState.Current, t.S.Words[0].State, "the word is still in play");

            t.Clue();                                           // Cy gives, Dee guesses, wrong
            Check.Ok(t.Game.JudgeGuess(t.Emcee, false));
            Check.Equal(0, t.S.TurnTeam, "and then Team A's clue-giver goes again");
            Check.Ok(t.Game.SubmitClue(t.Tokens[0], "ship"));
            Check.True(t.S.Guesser(0)!.IsGuesser && t.S.Guesser(0)!.OnClock, "Bob can guess this time");
        });

        run("password: after a miss the other team clues, and each side gets four clues", () =>
        {
            var t = new PasswordTable();
            t.Open(1);
            var order = new List<int>();
            for (var i = 0; i < 8; i++)
            {
                order.Add(t.S.TurnTeam);
                Check.Equal(PasswordPhase.Clue, t.S.Phase, $"waiting for clue {i + 1}");
                t.Clue();
                Check.Equal(PasswordPhase.Guess, t.S.Phase, $"waiting for guess {i + 1}");
                Check.Equal(t.S.TurnTeam, t.S.Clues[^1].Team, "the guess belongs to the team that just clued");
                Check.Ok(t.Game.JudgeGuess(t.Emcee, false));
                if (i == 1)
                    Check.True(t.S.Teams[0].CluesLeft == 3 && t.S.Teams[1].CluesLeft == 3, "after two clues in all, each side has used one");
            }
            Check.True(order.SequenceEqual([1, 0, 1, 0, 1, 0, 1, 0]), "the teams alternate, starting with the team that played");
            Check.Equal(8, t.S.Clues.Count, "eight clues were given");
            Check.Equal(0, t.S.Teams[0].CluesLeft, "Team A is out of clues");
            Check.Equal(0, t.S.Teams[1].CluesLeft, "Team B is out of clues");
        });

        run("password: only the guesser of the team that just clued is up, and only the emcee rules", () =>
        {
            var t = new PasswordTable();
            t.Open(0);
            t.Clue();
            var s = t.S;
            Check.True(s.Players.Single(p => p.Id == t.Ids[1]).OnClock && s.Players.Single(p => p.Id == t.Ids[1]).IsGuesser, "Bob (Team A's guesser) is on the clock");
            Check.True(!s.Players.Single(p => p.Id == t.Ids[3]).OnClock, "Dee (Team B's guesser) is not");
            Check.Fails(t.Game.JudgeGuess(t.Tokens[1], true), "the guesser ruling on themselves");
            Check.Fails(t.Game.JudgeGuess(Guid.NewGuid(), true), "a stranger ruling");
            Check.Fails(t.Game.RejectClue(t.Tokens[0]), "a player throwing out a clue");
            Check.Fails(t.Game.JudgePuzzleGuess(t.Emcee, true), "ruling on the big word before the word is guessed");
        });

        run("password: the emcee can throw out a clue; it still uses a clue and nobody guesses", () =>
        {
            var t = new PasswordTable();
            t.Open(0);
            t.Clue();
            Check.Ok(t.Game.RejectClue(t.Emcee));
            var s = t.S;
            Check.Equal(ClueOutcome.Rejected, s.Clues[0].Outcome, "the clue is marked thrown out");
            Check.Equal(3, s.Teams[0].CluesLeft, "it counted as one of Team A's four");
            Check.Equal(PasswordPhase.Clue, s.Phase, "the other team is up to give a clue");
            Check.Equal(1, s.TurnTeam, "Team B's turn");
            Check.Fails(t.Game.RejectClue(t.Emcee), "throwing out a clue when there isn't one");
        });

        run("password: after eight misses the word is posted, nobody may guess the big word, and control stays put", () =>
        {
            var t = new PasswordTable();
            var control = t.S.ControlTeam;
            t.Open(1 - control);
            t.MissAllClues();
            var s = t.S;
            Check.Equal(PasswordPhase.WordResult, s.Phase, "phase after eight misses");
            Check.Equal(WordState.Posted, s.Words[0].State, "the word goes up on the board");
            Check.True(s.Message is not null && s.Message.Contains("Anchor"), "the word is announced");
            Check.Equal(0, s.Teams[0].Score + s.Teams[1].Score, "no points");
            Check.Fails(t.Game.JudgePuzzleGuess(t.Emcee, true), "a big-word guess after nobody got the word");
            Check.Fails(t.Game.JudgeGuess(t.Emcee, true), "another guess");
            Check.Fails(t.Game.ChooseControl(t.Emcee, true), "a control choice before moving on");
            Check.Fails(t.Game.Continue(t.Tokens[0]), "a player continuing");
            Check.Ok(t.Game.Continue(t.Emcee));
            s = t.S;
            Check.Equal(PasswordPhase.Control, s.Phase, "back to a control choice");
            Check.Equal(2, s.WordNumber, "word 2");
            Check.Equal(control, s.ControlTeam, "nobody got it, so control didn't change");
            Check.Equal(WordState.Current, s.Words[1].State, "the second word is now current");
            Check.Equal(WordState.Waiting, s.Words[2].State, "later words wait");
        });

        Console.WriteLine();
        Console.WriteLine("Password Plus: the big word");

        run("password: a right guess lets only that guesser try the big word, and a miss carries on to the next word", () =>
        {
            var t = new PasswordTable();
            t.Open(0);
            t.ClueAndJudge(true);
            var s = t.S;
            Check.Equal(PasswordPhase.PuzzleGuess, s.Phase, "phase");
            Check.Equal(WordState.Solved, s.Words[0].State, "the word is up on the board");
            Check.Equal(t.Ids[1], s.AttemptingId!.Value, "Bob, Team A's guesser, takes the shot");
            Check.Equal(1, s.AttemptsLeft, "only one attempt on a normal word");
            Check.Equal(0, s.ControlTeam, "Team A got the last right answer, so they have control");
            Check.True(s.Players.Single(p => p.Id == t.Ids[1]).IsAttempting, "Bob is flagged as attempting");
            Check.Fails(t.Game.JudgePuzzleGuess(t.Tokens[1], true), "the guesser ruling on themselves");
            Check.Fails(t.Game.JudgeGuess(t.Emcee, true), "judging a clue guess during the big-word guess");
            Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, false));
            s = t.S;
            Check.Equal(PasswordPhase.WordResult, s.Phase, "the puzzle carries on");
            Check.True(!s.BigWordRevealed, "the big word is still a secret");
            Check.Equal(0, s.Teams[0].Score, "no points yet");
            Check.Ok(t.Game.Continue(t.Emcee));
            s = t.S;
            Check.Equal(PasswordPhase.Control, s.Phase, "the next word starts with a control choice");
            Check.Equal(0, s.ControlTeam, "the team that got the last word has control");
            Check.Equal(2, s.WordNumber, "word 2 of the same puzzle");
            Check.Equal(1, s.PuzzleNumber, "still puzzle 1");
        });

        run("password: solving the big word scores for that team, shows everything, and gives them control", () =>
        {
            var t = new PasswordTable();
            t.Open(1);
            t.ClueAndJudge(true);
            Check.Equal(t.Ids[3], t.S.AttemptingId!.Value, "Dee, Team B's guesser, takes the shot");
            Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, true));
            var s = t.S;
            Check.Equal(PasswordPhase.PuzzleResult, s.Phase, "phase");
            Check.Equal(100, s.Teams[1].Score, "Team B scores the puzzle's value");
            Check.Equal(0, s.Teams[0].Score, "Team A scores nothing");
            Check.True(s.BigWordRevealed, "the big word is revealed");
            Check.True(s.Words.All(w => w.State != WordState.Waiting && w.State != WordState.Current), "every word is up on the board");
            Check.Equal(WordState.Solved, s.Words[0].State, "the word that was guessed");
            Check.Equal(WordState.Posted, s.Words[4].State, "words nobody played are shown anyway");
            Check.Equal(1, s.ControlTeam, "the team that solved it has control");
            Check.Fails(t.Game.JudgePuzzleGuess(t.Emcee, true), "scoring the same puzzle twice");

            Check.Ok(t.Game.Continue(t.Emcee));
            s = t.S;
            Check.Equal(2, s.PuzzleNumber, "the next puzzle");
            Check.Equal(1, s.WordNumber, "starts at word 1");
            Check.True(!s.BigWordRevealed, "the new big word is hidden");
            Check.True(s.Words.Count(w => w.State == WordState.Waiting) == 4 && s.Words[0].State == WordState.Current, "only the first word is in play");
            Check.Equal(1, s.ControlTeam, "control carries over");
        });

        run("password: puzzles are worth 100, 100, then 200 each", () =>
        {
            var expected = new[] { 100, 100, 200, 200, 200, 200, 200 };
            for (var i = 0; i < expected.Length; i++)
                Check.Equal(expected[i], PasswordGame.PuzzleValue(i), $"value of puzzle {i + 1}");

            // And the game really pays them: Team B takes puzzles 1 and 2 (100 + 100), Team A takes puzzle 3 (200).
            var t = new PasswordTable();
            t.SolvePuzzle(1);
            Check.Equal(100, t.S.Teams[1].Score, "after puzzle 1");
            Check.Equal(100, t.S.PuzzleValue, "puzzle 2 is worth 100");
            t.SolvePuzzle(1);
            Check.Equal(200, t.S.Teams[1].Score, "after puzzle 2");
            Check.Equal(200, t.S.PuzzleValue, "puzzle 3 is worth 200");
            t.SolvePuzzle(0);
            Check.Equal(200, t.S.Teams[0].Score, "Team A scores 200 for puzzle 3");
            Check.Equal(200, t.S.PuzzleValue, "puzzle 4 is worth 200");
        });

        run("password: on the fifth word the guesser, their partner, then the other team's guesser and clue-giver each get a try", () =>
        {
            var t = new PasswordTable();
            t.PlayToWordFive(0);
            t.Open(0);
            t.ClueAndJudge(true);                                // word 5: player 1 gives, so Bob guesses
            var s = t.S;
            Check.Equal(PasswordPhase.PuzzleGuess, s.Phase, "phase");
            Check.Equal(4, s.AttemptsLeft, "four attempts in all");
            var order = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                order.Add(t.IndexOf(t.S.AttemptingId!.Value));
                Check.Equal(4 - i, t.S.AttemptsLeft, $"attempts left before try {i + 1}");
                Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, false));
                Check.Equal(PasswordPhase.PuzzleGuess, t.S.Phase, $"still guessing after try {i + 1}");
            }
            order.Add(t.IndexOf(t.S.AttemptingId!.Value));
            Check.True(order.SequenceEqual([1, 0, 3, 2]), $"order was Bob, Ann, Dee, Cy but got {string.Join(",", order)}");
            Check.True(t.S.Teams[1].OnClock && !t.S.Teams[0].OnClock, "the other team is on the clock for the last try");
            Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, true));    // Cy gets it
            s = t.S;
            Check.Equal(PasswordPhase.PuzzleResult, s.Phase, "puzzle over");
            Check.Equal(100, s.Teams[1].Score, "the other team scores, because their player got it");
            Check.Equal(0, s.Teams[0].Score, "the team that got the word does not");
            Check.Equal(1, s.ControlTeam, "and they get control");
        });

        run("password: if nobody gets the big word on the fifth word, the puzzle is thrown out", () =>
        {
            var t = new PasswordTable();
            t.PlayToWordFive(1);
            t.Open(1);
            t.ClueAndJudge(true);
            for (var i = 0; i < 4; i++) Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, false));
            var s = t.S;
            Check.Equal(PasswordPhase.PuzzleResult, s.Phase, "phase");
            Check.Equal(0, s.Teams[0].Score + s.Teams[1].Score, "no points");
            Check.True(s.BigWordRevealed, "the big word is shown anyway");
            Check.True(s.Message is not null && s.Message.Contains("No points"), "the result says so");
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(2, t.S.PuzzleNumber, "on to the next puzzle");
        });

        run("password: if the fifth word goes unguessed, the last team to guess a word gets the first shot at the big word", () =>
        {
            // Team A won words 1-3, Team B won word 4, and nobody gets word 5: Team B was the last to guess, so Dee (their guesser) starts.
            var t = new PasswordTable();
            for (var i = 0; i < 3; i++) { t.SolveWord(0, bigWordRight: false); Check.Ok(t.Game.Continue(t.Emcee)); }
            t.SolveWord(1, bigWordRight: false);
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(PasswordGame.WordsPerPuzzle, t.S.WordNumber, "word 5");
            t.Open(0);
            t.MissAllClues();
            var s = t.S;
            Check.Equal(PasswordPhase.PuzzleGuess, s.Phase, "a shot at the big word");
            Check.Equal(WordState.Posted, s.Words[4].State, "the fifth word is posted");
            Check.True(!s.BigWordRevealed, "the big word is still secret");
            Check.Equal(t.Ids[3], s.AttemptingId, "Dee, Team B's guesser, goes first");
            Check.Equal(4, s.AttemptsLeft, "four tries");
            var order = new List<int>();
            for (var i = 0; i < 4; i++)
            {
                order.Add(t.IndexOf(t.S.AttemptingId!.Value));
                Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, false));
            }
            Check.True(order.SequenceEqual([3, 2, 1, 0]), $"Dee, Cy, then Team A's guesser Bob and giver Ann (got {string.Join(",", order)})");
            Check.Equal(PasswordPhase.PuzzleResult, t.S.Phase, "everybody missed");
            Check.True(t.S.BigWordRevealed, "so the big word is shown");
            Check.Equal(0, t.S.Teams[0].Score + t.S.Teams[1].Score, "no points");
        });

        run("password: a right big word after an unsolved fifth word scores for whoever got it", () =>
        {
            var t = new PasswordTable();
            t.PlayToWordFive(0);
            t.Open(0);
            t.MissAllClues();
            Check.Equal(t.Ids[1], t.S.AttemptingId, "Bob, Team A's guesser, goes first");
            Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, false));
            Check.Equal(t.Ids[0], t.S.AttemptingId, "then Ann, Team A's clue-giver");
            Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, false));
            Check.Equal(t.Ids[3], t.S.AttemptingId, "then Team B's guesser, Dee");
            Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, true));
            Check.Equal(100, t.S.Teams[1].Score, "Team B scores the puzzle");
            Check.Equal(PasswordPhase.PuzzleResult, t.S.Phase, "puzzle over");
        });

        run("password: a puzzle where nobody guesses a single word is thrown out", () =>
        {
            var t = new PasswordTable();
            for (var i = 0; i < PasswordGame.WordsPerPuzzle; i++)
            {
                t.Open(0);
                t.MissAllClues();
                if (i < PasswordGame.WordsPerPuzzle - 1)
                {
                    Check.Equal(PasswordPhase.WordResult, t.S.Phase, $"word {i + 1} goes up on the board, and the puzzle carries on");
                    Check.Equal(WordState.Posted, t.S.Words[i].State, $"word {i + 1} is posted");
                    Check.Ok(t.Game.Continue(t.Emcee));
                }
            }
            Check.Equal(PasswordPhase.PuzzleResult, t.S.Phase, "puzzle over");
            Check.True(t.S.BigWordRevealed, "the big word is shown");
            Check.Equal(0, t.S.Teams[0].Score + t.S.Teams[1].Score, "no points");
            Check.Fails(t.Game.JudgePuzzleGuess(t.Emcee, true), "a big-word guess");
        });

        Console.WriteLine();
        Console.WriteLine("Password Plus: winning and playing again");

        run("password: the first team to reach 500 wins and goes to the bonus round", () =>
        {
            var t = new PasswordTable();
            t.SolvePuzzle(0);   // A: 100
            t.SolvePuzzle(1);   // B: 100
            t.SolvePuzzle(0);   // A: 300 (puzzle 3 is worth 200)
            t.SolvePuzzle(1);   // B: 300
            Check.Equal(PasswordPhase.Control, t.S.Phase, "nobody has 500 yet, so the game goes on");
            Check.Equal(300, t.S.Teams[0].Score, "Team A");
            Check.Equal(300, t.S.Teams[1].Score, "Team B");
            t.SolveWord(0, bigWordRight: true);   // puzzle 5 is worth 200: A reaches exactly 500
            Check.Equal(PasswordPhase.PuzzleResult, t.S.Phase, "the result screen comes first");
            Check.Equal(500, t.S.Teams[0].Score, "Team A");
            Check.Ok(t.Game.Continue(t.Emcee));
            var s = t.S;
            Check.Equal(PasswordPhase.BonusReady, s.Phase, "Team A goes on to the bonus round");
            Check.True(s.Winners.SequenceEqual([0]), "Team A won");
            Check.True(s.Message is not null && s.Message.Contains("bonus round") && s.Message.Contains("Team A"), "the bonus round is announced");
            Check.Fails(t.Game.ChooseControl(t.Emcee, true), "playing the puzzle during the bonus round");
            Check.Equal("", s.CurrentWord, "no puzzle word is left lying around");
            t.PlayBonusOut();
            s = t.S;
            Check.True(s.Winners.SequenceEqual([0]), "Team A still won");
            Check.True(s.Message is not null && s.Message.Contains("Team A"), "and the ending names them");
            Check.Fails(t.Game.Continue(t.Emcee), "continuing a finished game");
            Check.Fails(t.Game.ChooseControl(t.Emcee, true), "playing on after the game");
            Check.Equal("", s.CurrentWord, "no secret word is left lying around");
        });

        run("password: only the emcee can start another game, and only once this one is over", () =>
        {
            var t = new PasswordTable();
            Check.Fails(t.Game.Restart(t.Emcee, true), "restarting mid-game");
            t.PlayToGameOver(0);
            Check.Fails(t.Game.Restart(t.Tokens[0], true), "a player restarting");
            Check.Fails(t.Game.Restart(Guid.NewGuid(), false), "a stranger restarting");

            Check.Ok(t.Game.Restart(t.Emcee, samePlayers: true));
            var s = t.S;
            Check.Equal(PasswordPhase.Control, s.Phase, "a fresh game starts at once");
            Check.Equal(0, s.Teams[0].Score + s.Teams[1].Score, "scores are reset");
            Check.Equal(1, s.PuzzleNumber, "back to puzzle 1");
            Check.Equal(4, s.Players.Count, "everyone keeps their seat");
            Check.Equal(Role.Player, t.Game.GetSeat(t.Tokens[3]).Role, "and their phone is still signed in");
            Check.True(s.Winners.Count == 0, "no winner yet");

            t.PlayToGameOver(1);
            Check.Ok(t.Game.Restart(t.Emcee, samePlayers: false));
            s = t.S;
            Check.Equal(PasswordPhase.Lobby, s.Phase, "back to the lobby");
            Check.Equal(0, s.Players.Count, "the seats are empty");
            Check.Equal("Host", s.EmceeName, "the emcee stays");
            Check.Equal(Role.Spectator, t.Game.GetSeat(t.Tokens[0]).Role, "the old players are spectators now");
            Check.Ok(t.Game.JoinPlayer("New", 0));
        });

        run("password: the game never ends by running out of puzzles, and there are never ties", () =>
        {
            // A source with a single puzzle runs dry after every puzzle; the game just deals more and plays on.
            var t = new PasswordTable(source: new OnePuzzleSource());
            t.SolveWord(1, bigWordRight: true);                 // B: 100
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(PasswordPhase.Control, t.S.Phase, "more puzzles were dealt");
            Check.Equal(2, t.S.PuzzleNumber, "puzzle 2");

            // Nobody ever scores, puzzle after puzzle: still no ending and no tie.
            var dry = new PasswordTable(source: new OnePuzzleSource());
            for (var puzzle = 1; puzzle <= 15; puzzle++)
            {
                for (var word = 0; word < PasswordGame.WordsPerPuzzle; word++)
                {
                    dry.Open(dry.S.ControlTeam);
                    dry.MissAllClues();
                    Check.Ok(dry.Game.Continue(dry.Emcee));
                }
                Check.Equal(PasswordPhase.Control, dry.S.Phase, $"puzzle {puzzle} is over and the game goes on");
            }

            // Whoever first reaches 500 is alone in front, on any path to it.
            for (var seed = 1; seed <= 40; seed++)
            {
                var rng = new Random(seed);
                var g = new PasswordTable(seed: seed);
                var guard = 0;
                while (g.S.Phase != PasswordPhase.BonusReady && guard++ < 60)
                {
                    g.SolveWord(rng.Next(2), bigWordRight: true);
                    Check.Ok(g.Game.Continue(g.Emcee));
                }
                Check.Equal(1, g.S.Winners.Count, $"seed {seed}: exactly one winner");
                var score = g.S.Teams[g.S.Winners[0]].Score;
                var other = g.S.Teams[1 - g.S.Winners[0]].Score;
                Check.True(score >= PasswordGame.WinScore && other < PasswordGame.WinScore && score > other, $"seed {seed}: {score} to {other}");
            }
        });

        run("password: the emcee can swap a dud puzzle for the next one, mid-word, without costing anyone anything", () =>
        {
            var t = new PasswordTable();
            t.SolveWord(1, bigWordRight: false);                // Team B gets word 1 and misses the big word
            Check.Ok(t.Game.Continue(t.Emcee));
            t.Open(1);
            t.Clue();                                           // a clue is up on word 2
            var before = t.S;
            Check.Equal("Mystery1", before.BigWord, "the first puzzle");
            Check.Equal(1, before.ControlTeam, "Team B has control");

            Check.Fails(t.Game.SwapPuzzle(t.Tokens[0]), "a player swapping");
            Check.Ok(t.Game.SwapPuzzle(t.Emcee));
            var s = t.S;
            Check.Equal("Mystery2", s.BigWord, "the next puzzle takes its place");
            Check.Equal(1, s.PuzzleNumber, "still the first puzzle of the game");
            Check.Equal(1, s.WordNumber, "back at word 1");
            Check.Equal(PasswordPhase.Control, s.Phase, "the control choice again");
            Check.Equal(1, s.ControlTeam, "control is unchanged");
            Check.Equal(0, s.Clues.Count, "no clues carried over");
            Check.True(s.Words.Count(w => w.State == WordState.Waiting) == 4 && s.Words[0].State == WordState.Current, "a fresh set of words");
            Check.Equal(0, s.Teams[0].Score + s.Teams[1].Score, "scores untouched");
            Check.True(!s.BigWordRevealed, "the new big word is secret");
            Check.True(s.Message is not null && s.Message.Contains("New puzzle"), "says what happened");

            // It plays on from there, and the worth of the puzzle is unchanged.
            t.Open(1);
            t.ClueAndJudge(true);
            Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, true));
            Check.Equal(PasswordGame.PuzzleValue(0), t.S.Teams[1].Score, "Team B scores puzzle 1's value for the swapped-in puzzle");

            // Once a puzzle is over there is nothing to swap, and neither in the lobby or after the game.
            Check.Fails(t.Game.SwapPuzzle(t.Emcee), "swapping a finished puzzle");
            Check.Fails(new PasswordTable(start: false).Game.SwapPuzzle(Guid.NewGuid()), "swapping in the lobby");
        });

        run("password: switching partners keeps every phone signed in, with the second players trading teams", () =>
        {
            var t = new PasswordTable();
            t.PlayToGameOver(0);
            Check.Fails(t.Game.RestartSwitched(t.Tokens[0]), "a player switching partners");
            Check.Ok(t.Game.RestartSwitched(t.Emcee));
            var s = t.S;
            Check.Equal(PasswordPhase.Control, s.Phase, "a fresh game starts at once");
            Check.Equal(0, s.Teams[0].Score + s.Teams[1].Score, "scores are reset");
            Check.Equal(0, s.FindPlayer(t.Ids[0])!.Team, "Ann stays on Team A");
            Check.Equal(0, s.FindPlayer(t.Ids[3])!.Team, "Dee moves to Team A");
            Check.Equal(1, s.FindPlayer(t.Ids[2])!.Team, "Cy stays on Team B");
            Check.Equal(1, s.FindPlayer(t.Ids[1])!.Team, "Bob moves to Team B");
            Check.True(s.Players.Count(p => p.Team == 0) == 2 && s.Players.Count(p => p.Team == 1) == 2, "two on each team");
            Check.True(s.Players.Where(p => p.Slot == 0).All(p => p.IsGiver) && s.Players.Where(p => p.Slot == 1).All(p => p.IsGuesser), "player 1 gives in puzzle 1 again");
            for (var i = 0; i < 4; i++) Check.Equal(Role.Player, t.Game.GetSeat(t.Tokens[i]).Role, $"phone {i} is still a player");
            Check.Fails(t.Game.RestartSwitched(t.Emcee), "switching mid-game");

            // And it plays: the new Team A (Ann + Dee) clues, Ann gives and Dee guesses.
            t.Open(0);
            Check.Ok(t.Game.SubmitClue(t.Tokens[0], "red"));
            Check.True(t.S.Guesser(0)!.Id == t.Ids[3], "Dee is Team A's guesser now");
        });

        run("password: the snapshot carries the secrets, so every view must decide what to hide", () =>
        {
            var game = new PasswordGame("TEST", new FixedPasswordPuzzleSource(), new Random(1));
            Check.Equal("", game.Snapshot().CurrentWord, "no secret word in the lobby");
            Check.Equal(0, game.Snapshot().Words.Count, "no words in the lobby");
            var t = new PasswordTable();
            var s = t.S;
            Check.Equal("Anchor", s.CurrentWord, "the current password");
            Check.Equal("Mystery1", s.BigWord, "the big word");
            Check.Equal("Alt1", s.BigWordAlso, "the other answers to accept");
            Check.Equal("Thing", s.PuzzleKind, "what kind of answer it is");
            Check.Equal(WordsPerPuzzle, s.Words.Count, "five words");
            Check.True(s.Words.Select(w => w.Text).SequenceEqual(FixedPasswordPuzzleSource.Words), "in the order they are played");
        });

        Console.WriteLine();
        Console.WriteLine("Password Plus: the bonus round");

        run("bonus: the winners get ten words, A to J here, with Ann facing the TV and Bob guessing, until the emcee swaps them", () =>
        {
            var t = new PasswordTable();
            t.PlayToBonus(0);
            var s = t.S;
            Check.Equal(0, s.BonusTeam, "Team A plays");
            Check.Equal(10, s.BonusSlots.Count, "ten words");
            Check.True(s.BonusSlots.Select(x => x.Letter).SequenceEqual("ABCDEFGHIJ"), "A to J in order");
            Check.True(s.BonusSlots.All(x => x.Word[0] == x.Letter), "each word starts with its letter");
            Check.True(s.BonusSlots.All(x => x.State == BonusSlotState.Waiting), "nothing played yet");
            Check.Equal(-1, s.BonusCurrent, "no word is showing before the clock starts");
            Check.Equal(null, s.BonusEndsAtUtc, "and no clock yet");
            Check.Equal(t.Ids[0], s.BonusGiverId, "Ann gives the clues");
            Check.Equal(t.Ids[1], s.BonusGuesserId, "Bob guesses");

            Check.Fails(t.Game.BonusSwapRoles(t.Tokens[0]), "a player swapping");
            Check.Fails(t.Game.StartBonus(t.Tokens[0]), "a player starting the clock");
            Check.Fails(t.Game.BonusCorrect(t.Emcee), "ruling before the clock starts");
            Check.Ok(t.Game.BonusSwapRoles(t.Emcee));
            Check.Equal(t.Ids[1], t.S.BonusGiverId, "Bob gives the clues after the swap");
            Check.Equal(t.Ids[0], t.S.BonusGuesserId, "Ann guesses");
            Check.Ok(t.Game.BonusSwapRoles(t.Emcee));
            Check.Equal(t.Ids[0], t.S.BonusGiverId, "and swaps back");

            Check.Ok(t.Game.StartBonus(t.Emcee));
            s = t.S;
            Check.Equal(PasswordPhase.BonusPlay, s.Phase, "playing");
            Check.Equal(0, s.BonusCurrent, "the A word is up");
            Check.Equal(t.Now.AddSeconds(60), s.BonusEndsAtUtc, "sixty seconds on the clock");
            Check.Fails(t.Game.BonusSwapRoles(t.Emcee), "swapping with the clock running");
            Check.Fails(t.Game.StartBonus(t.Emcee), "starting twice");
        });

        run("bonus: only the emcee rules; correct moves on; pass sends the word to the back of the line and brings it back", () =>
        {
            var t = new PasswordTable();
            t.PlayToBonus(0);
            Check.Ok(t.Game.StartBonus(t.Emcee));
            Check.Fails(t.Game.BonusCorrect(t.Tokens[0]), "the clue-giver ruling");
            Check.Fails(t.Game.BonusPass(t.Tokens[1]), "the guesser passing");
            Check.Fails(t.Game.BonusZap(Guid.NewGuid()), "a stranger zapping");

            Check.Ok(t.Game.BonusPass(t.Emcee));               // pass on A
            var s = t.S;
            Check.Equal(BonusSlotState.Passed, s.BonusSlots[0].State, "A is passed");
            Check.Equal(1, s.BonusCurrent, "B is next");
            Check.Equal(BonusSlotState.Current, s.BonusSlots[1].State, "B is showing");

            for (var i = 1; i < 10; i++)
            {
                Check.Equal(i, t.S.BonusCurrent, $"the {"ABCDEFGHIJ"[i]} word is up");
                Check.Ok(t.Game.BonusCorrect(t.Emcee));
            }
            s = t.S;
            Check.Equal(PasswordPhase.BonusPlay, s.Phase, "still going: A is waiting");
            Check.Equal(0, s.BonusCurrent, "the passed A word comes back after J");
            Check.Equal(BonusSlotState.Current, s.BonusSlots[0].State, "and is showing");
            Check.Equal(9, s.BonusCorrect, "nine right so far");

            Check.Ok(t.Game.BonusCorrect(t.Emcee));
            s = t.S;
            Check.Equal(PasswordPhase.BonusResult, s.Phase, "all ten done, so the round ends before the clock does");
            Check.Equal(10, s.BonusCorrect, "all ten");
            Check.Equal(10_000, s.BonusEarned, "$10,000 for all ten");
            Check.True(s.Message!.Contains("ALL TEN") && s.Message.Contains("10,000"), "announced");
            Check.Fails(t.Game.BonusCorrect(t.Emcee), "ruling after the round");

            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(PasswordPhase.GameOver, t.S.Phase, "game over");
            Check.True(t.S.Message!.Contains("Team A") && t.S.Message.Contains("10,000"), "the winnings are in the ending");
        });

        run("bonus: passed words come back in the order they were passed, and a word can be passed again", () =>
        {
            var t = new PasswordTable();
            t.PlayToBonus(0);
            Check.Ok(t.Game.StartBonus(t.Emcee));
            Check.Ok(t.Game.BonusPass(t.Emcee));               // A passed
            Check.Ok(t.Game.BonusPass(t.Emcee));               // B passed
            for (var i = 2; i < 10; i++) Check.Ok(t.Game.BonusCorrect(t.Emcee));   // C to J
            Check.Equal(0, t.S.BonusCurrent, "A comes back first");
            Check.Ok(t.Game.BonusPass(t.Emcee));               // A again
            Check.Equal(1, t.S.BonusCurrent, "then B");
            Check.Ok(t.Game.BonusCorrect(t.Emcee));
            Check.Equal(0, t.S.BonusCurrent, "and A one more time");
            Check.Ok(t.Game.BonusCorrect(t.Emcee));
            Check.Equal(10_000, t.S.BonusEarned, "and it still adds up");
        });

        run("bonus: a zapped word is lost, $100 a word, and no jackpot without all ten", () =>
        {
            var t = new PasswordTable();
            t.PlayToBonus(1);
            Check.Ok(t.Game.StartBonus(t.Emcee));
            Check.Ok(t.Game.BonusZap(t.Emcee));
            Check.Equal(BonusSlotState.Zapped, t.S.BonusSlots[0].State, "the A word is zapped");
            for (var i = 1; i < 10; i++) Check.Ok(t.Game.BonusCorrect(t.Emcee));
            var s = t.S;
            Check.Equal(PasswordPhase.BonusResult, s.Phase, "nothing is left to play: a zapped word does not come back");
            Check.Equal(9, s.BonusCorrect, "nine right");
            Check.Equal(900, s.BonusEarned, "nine hundred dollars");
            Check.Equal(1, s.BonusTeam, "Team B played");
            Check.Ok(t.Game.Continue(t.Emcee));
            Check.True(t.S.Winners.SequenceEqual([1]) && t.S.Message!.Contains("Team B") && t.S.Message.Contains("900"), "Team B and $900 in the ending");
        });

        run("bonus: the clock is the server's; time up ends the round, and a late tap does not count", () =>
        {
            var t = new PasswordTable();
            t.PlayToBonus(0);
            Check.Fails(t.Game.Tick(), "ticking before the clock starts");
            Check.Ok(t.Game.StartBonus(t.Emcee));
            Check.Ok(t.Game.BonusCorrect(t.Emcee));
            Check.Ok(t.Game.BonusCorrect(t.Emcee));
            t.Advance(59.5);
            Check.Fails(t.Game.Tick(), "half a second left");
            Check.Equal(PasswordPhase.BonusPlay, t.S.Phase, "still playing");
            Check.Ok(t.Game.BonusPass(t.Emcee));                // within the time, still allowed
            t.Advance(1);
            Check.Ok(t.Game.BonusCorrect(t.Emcee));             // too late: the round ends and this is ignored
            var s = t.S;
            Check.Equal(PasswordPhase.BonusResult, s.Phase, "time is up");
            Check.Equal(2, s.BonusCorrect, "only the two before the buzzer count");
            Check.Equal(200, s.BonusEarned, "two hundred dollars");
            Check.True(s.BonusSlots.Count(x => x.State == BonusSlotState.Waiting) + s.BonusSlots.Count(x => x.State == BonusSlotState.Passed) == 8, "the rest were not won");
            Check.Equal(-1, s.BonusCurrent, "no word is showing any more");
        });

        run("bonus: it can start anywhere and wraps from Z back to A", () =>
        {
            var t = new PasswordTable(bonus: new FixedBonusWordSource('X'));
            t.PlayToBonus(0);
            Check.True(t.S.BonusSlots.Select(x => x.Letter).SequenceEqual("XYZABCDEFG"), "X to G");
            Check.True(t.S.BonusSlots.Select(x => x.Word).SequenceEqual(["Xest", "Yest", "Zest", "Aest", "Best", "Cest", "Dest", "Eest", "Fest", "Gest"]), "the words follow");
        });

        run("bonus: nobody is left in the bonus round after a restart, and a tie has no bonus round", () =>
        {
            var t = new PasswordTable();
            t.PlayToGameOver(0);
            Check.Ok(t.Game.Restart(t.Emcee, samePlayers: true));
            var s = t.S;
            Check.Equal(-1, s.BonusTeam, "no bonus team");
            Check.Equal(0, s.BonusSlots.Count, "no bonus words");
            Check.Equal(null, s.BonusEndsAtUtc, "no clock");
            Check.Equal(0, s.BonusEarned, "no winnings");

            // The restart while a clock is running isn't possible (the game isn't over), but a timer from an old round must be harmless.
            Check.Fails(t.Game.Tick(), "a stray tick");
        });

        run("bonus: before the bonus round the snapshot holds no bonus words", () =>
        {
            var t = new PasswordTable();
            var s = t.S;
            Check.Equal(0, s.BonusSlots.Count, "no words dealt yet");
            Check.Equal(-1, s.BonusTeam, "no team yet");
            t.SolveWord(0, bigWordRight: true);
            Check.Equal(0, t.S.BonusSlots.Count, "still none after a puzzle");
        });

        Console.WriteLine();
        Console.WriteLine("Password Plus: the bonus word bank");

        run("bonus bank: every letter has plenty of clean, clue-able single words", () =>
        {
            var words = TextBonusWordSource.LoadBank();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in words)
            {
                Check.True(w.Length is >= 2 and <= 15 && w.All(char.IsLetter), $"\"{w}\" must be one plain word of letters");
                Check.True(char.IsUpper(w[0]), $"\"{w}\" starts with a capital");
                Check.True(seen.Add(w), $"\"{w}\" appears twice");
            }
            var bank = TextBonusWordSource.Group(words);
            for (var c = 'A'; c <= 'Z'; c++)
            {
                var need = c is 'X' ? 8 : c is 'Q' ? 12 : 14;
                Check.True(bank.TryGetValue(c, out var list) && list.Count >= need, $"{c}: needs at least {need} words, has {(bank.TryGetValue(c, out var l2) ? l2.Count : 0)}");
            }
        });

        run("bonus bank: every deal is ten words, one per letter in order, and a night of games doesn't repeat itself", () =>
        {
            var source = new TextBonusWordSource();
            var rng = new Random(5);
            var starts = new HashSet<char>();
            var wrapped = false;
            var perLetter = new Dictionary<char, List<string>>();
            for (var round = 0; round < 60; round++)
            {
                var r = source.CreateRound(rng);
                starts.Add(r.StartLetter);
                if (r.StartLetter > 'Q') wrapped = true;
                Check.Equal(10, r.Words.Count, "ten words");
                for (var i = 0; i < 10; i++)
                {
                    var letter = r.LetterAt(i);
                    Check.Equal(letter, char.ToUpperInvariant(r.Words[i][0]), $"word {i + 1} starts with {letter}");
                    if (!perLetter.TryGetValue(letter, out var list)) perLetter[letter] = list = [];
                    list.Add(r.Words[i]);
                }
                Check.Equal(10, r.Words.Distinct().Count(), "the ten words are different");
            }
            Check.True(starts.Count >= 12, $"the starting letter varies (saw {starts.Count})");
            Check.True(wrapped, "some rounds wrapped around from Z to A");
            // No word shows up twice for a letter until that letter's whole list has been used.
            var bank = source.Bank;
            foreach (var (letter, dealt) in perLetter)
            {
                var size = bank[letter].Count;
                foreach (var chunk in dealt.Chunk(size))
                    Check.Equal(chunk.Length, chunk.Distinct().Count(), $"{letter}: no repeats within a pass through {size} words");
            }
        });

        Console.WriteLine();
        Console.WriteLine("Password Plus: the puzzle bank");

        run("bank: enough puzzles, each with five good single words that don't give the answer away", () =>
        {
            var bank = JsonPasswordPuzzleSource.LoadBank();
            Check.True(bank.Count >= PasswordGame.PuzzlesPerGame, $"need at least {PasswordGame.PuzzlesPerGame} puzzles, have {bank.Count}");
            var kinds = new HashSet<string> { "Person", "Place", "Thing" };
            var answers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in bank)
            {
                Check.True(kinds.Contains(p.Kind), $"{p.Answer}: kind \"{p.Kind}\" must be one of Person, Place, Thing");
                Check.True(!string.IsNullOrWhiteSpace(p.Answer), "an answer");
                Check.True(answers.Add(p.Answer), $"{p.Answer} appears twice");
                Check.Equal(PasswordGame.WordsPerPuzzle, p.Words.Length, $"{p.Answer}: five words");
                Check.Equal(PasswordGame.WordsPerPuzzle, p.Words.Select(Letters).Distinct().Count(), $"{p.Answer}: the five words are different");

                var forbidden = new[] { p.Answer }.Concat(p.AlsoAccept ?? []).Select(Letters).Where(a => a.Length >= 3).ToList();
                foreach (var w in p.Words)
                {
                    Check.True(w.Length >= 2 && w.All(char.IsLetter), $"{p.Answer}: \"{w}\" must be one plain word of letters");
                    var word = Letters(w);
                    foreach (var a in forbidden)
                        Check.True(!a.Contains(word) && !word.Contains(a), $"{p.Answer}: the word \"{w}\" gives the answer away (it overlaps \"{a}\")");
                }
            }
        });

        run("bank: a deal is 12 different puzzles, varies by seed, and rarely puts two of a kind back to back", () =>
        {
            var source = new JsonPasswordPuzzleSource();
            var firsts = new HashSet<string>();
            var pairs = 0;
            var sameKind = 0;
            for (var seed = 1; seed <= 300; seed++)
            {
                var deal = source.CreatePuzzles(new Random(seed), PasswordGame.PuzzlesPerGame);
                Check.Equal(PasswordGame.PuzzlesPerGame, deal.Count, "puzzles dealt");
                Check.Equal(deal.Count, deal.Select(p => p.Answer).Distinct().Count(), $"seed {seed}: no repeats");
                firsts.Add(deal[0].Answer);
                for (var i = 1; i < deal.Count; i++)
                {
                    pairs++;
                    if (deal[i].Kind == deal[i - 1].Kind) sameKind++;
                }
            }
            Check.True(firsts.Count > 10, $"the first puzzle should vary between games, saw only {firsts.Count} different ones");
            Check.True(sameKind * 50 < pairs, $"too many same-kind neighbors: {sameKind} of {pairs}");
        });

        run("bank: real puzzles play through the engine (300 games started, a word clued in each)", () =>
        {
            var source = new JsonPasswordPuzzleSource();
            for (var seed = 1; seed <= 300; seed++)
            {
                var t = new PasswordTable(seed, source: source);
                var s = t.S;
                Check.True(s.PuzzleKind.Length > 0 && s.BigWord.Length > 0, $"seed {seed}: a puzzle was dealt");
                Check.Equal(PasswordGame.WordsPerPuzzle, s.Words.Count, "five words");
                t.Open(s.ControlTeam);
                Check.Ok(t.Game.SubmitClue(t.GiverToken(t.S.TurnTeam), "zebra"));
            }
        });
    }

    private const int WordsPerPuzzle = PasswordGame.WordsPerPuzzle;

    private static string Letters(string text) =>
        new(text.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
}
