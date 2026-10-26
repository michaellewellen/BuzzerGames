using BuzzerGames.Core.Jeopardy;

/// <summary>
/// A ready-made game for the checks: one emcee (Host) and three players (Ann, Bob, Cy).
/// Plenty of helpers so each check reads like a short story.
/// </summary>
sealed class Table
{
    public JeopardyGame Game { get; }
    public Guid Emcee { get; }
    public Guid[] Tokens { get; }   // secret tokens, one per player
    public Guid[] Ids { get; }      // public ids, same order

    public Table(int seed = 1)
    {
        Game = new JeopardyGame("TEST", new DummyGameDataSource(), new Random(seed));
        Emcee = Game.JoinEmcee("Host").Token;
        Tokens = new[] { "Ann", "Bob", "Cy" }.Select(name => Game.JoinPlayer(name).Token).ToArray();
        Ids = Tokens.Select(t => Game.GetSeat(t).PlayerId!.Value).ToArray();
    }

    public static Table Started(int seed = 1)
    {
        var table = new Table(seed);
        Check.Ok(table.Game.Start());
        return table;
    }

    public GameSnapshot S => Game.Snapshot();

    public int Score(int player) => S.Players[player].Score;

    public Guid TokenOf(Guid playerId) => Tokens[Array.IndexOf(Ids, playerId)];

    /// <summary>Taps the first clue on the board that hasn't been played.</summary>
    public void PickNext()
    {
        var snapshot = S;
        for (var c = 0; c < snapshot.Categories.Count; c++)
            for (var r = 0; r < snapshot.Categories[c].Cells.Count; r++)
                if (!snapshot.Categories[c].Cells[r].IsUsed)
                {
                    Check.Ok(Game.SelectClue(Emcee, c, r));
                    return;
                }
        throw new Exception("There are no unplayed clues left on the board.");
    }

    /// <summary>
    /// Picks clues until a normal one is showing. If a Daily Double comes up along the way, it is
    /// played out (control player wagers $5 and gets it right) so the check can carry on.
    /// </summary>
    public void OpenNormalClue()
    {
        while (true)
        {
            PickNext();
            if (S.Phase == GamePhase.ClueOpen) return;

            Check.Equal(GamePhase.DailyDoubleWager, S.Phase, "phase after picking a Daily Double");
            Check.Ok(Game.SubmitDailyDoubleWager(TokenOf(S.ControlPlayer!.Id), 5));
            Check.Ok(Game.Judge(Emcee, true));
            Check.Ok(Game.Continue(Emcee));
        }
    }

    /// <summary>Picks clues until a Daily Double is showing (gives up on the normal ones, which costs nobody anything).</summary>
    public void OpenDailyDouble()
    {
        while (true)
        {
            PickNext();
            if (S.Phase == GamePhase.DailyDoubleWager) return;
            Check.Ok(Game.GiveUp(Emcee));
            Check.Ok(Game.Continue(Emcee));
        }
    }

    /// <summary>
    /// Plays every clue of the current round. <paramref name="answerer"/> says which player answers
    /// each clue correctly. With giveUp, nobody answers anything. Returns how many Daily Doubles came up.
    /// </summary>
    public int PlayRound(Func<int, int> answerer, bool giveUp = false)
    {
        var round = S.Round;
        var clueNumber = 0;
        var dailyDoubles = 0;

        while (S.Phase == GamePhase.Board && S.Round == round)
        {
            PickNext();
            if (S.Phase == GamePhase.DailyDoubleWager)
            {
                dailyDoubles++;
                Check.Ok(Game.SubmitDailyDoubleWager(TokenOf(S.ControlPlayer!.Id), 5));
                Check.Ok(Game.Judge(Emcee, correct: !giveUp));
            }
            else if (giveUp)
            {
                Check.Ok(Game.GiveUp(Emcee));
            }
            else
            {
                Check.Ok(Game.Buzz(Tokens[answerer(clueNumber)]));
                Check.Ok(Game.Judge(Emcee, correct: true));
            }
            Check.Ok(Game.Continue(Emcee));
            clueNumber++;
        }
        return dailyDoubles;
    }
}
