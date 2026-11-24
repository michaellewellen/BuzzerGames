using BuzzerGames.Core.Feud;
using BuzzerGames.Core.Jeopardy;

/// <summary>
/// A Family Feud game with a fixed bank: question n is "Question n" and its answers are "Qn answer 1" to "Qn answer 6", worth 30, 25, 20, 12, 8
/// and 5 (a hundred in all). The first round plays Question 1; Fast Money gets Questions 13 to 17.
/// </summary>
sealed class FeudTable
{
    public FeudGame Game { get; }
    public Guid Emcee { get; }
    public FixedFeudContent Content { get; }

    public FeudTable(int boards = 20, bool start = true, double firstSeconds = 20, double secondSeconds = 25, int seed = 1, IFeudContent? bank = null)
    {
        Content = new FixedFeudContent(boards);
        Game = new FeudGame("TEST", bank ?? Content, new Random(seed), firstSeconds, secondSeconds);
        Emcee = Game.JoinEmcee().Token;
        if (start) Check.Ok(Game.Start(Emcee));
    }

    public FeudSnapshot S => Game.Snapshot();

    public void Right(int index) => Check.Ok(Game.Right(Emcee, index));
    public void Strike() => Check.Ok(Game.Strike(Emcee));

    /// <summary>The question is read and <paramref name="team"/> wins the buzz and names the number one answer, which wins the face-off.</summary>
    public void WinFaceOff(int team)
    {
        Check.Ok(Game.ReadQuestion(Emcee));
        Check.Ok(Game.Buzz(team));
        Right(0);
        Check.Equal(FeudPhase.ControlChoice, S.Phase, "the top answer wins the face-off");
    }

    /// <summary>One whole round: <paramref name="team"/> wins the face-off, plays, and clears the board. The round is left on RoundOver.</summary>
    public void ClearBoard(int team)
    {
        WinFaceOff(team);
        Check.Ok(Game.Choose(Emcee, play: true));
        for (var i = 1; i < S.Answers.Count; i++) Right(i);
        Check.Equal(FeudPhase.RoundOver, S.Phase, "the board is clear");
        Check.Equal(team, S.RoundWinner, "the family that cleared it wins the round");
    }

    public void Continue() => Check.Ok(Game.Continue(Emcee));

    /// <summary>One round won with a partial bank: the face-off winner takes the top answer, plays, and strikes out; the stealer misses.</summary>
    public void SmallWin(int team)
    {
        WinFaceOff(team);
        Check.Ok(Game.Choose(Emcee, play: true));
        Right(1);
        Strike(); Strike(); Strike();
        Strike();
        Check.Equal(team, S.RoundWinner, "a failed steal leaves the bank where it was");
    }

    /// <summary>Play rounds until Fast Money, with <paramref name="team"/> winning every one.</summary>
    public void WinTheGame(int team)
    {
        for (var guard = 0; guard < 12 && S.Phase != FeudPhase.FastIntro; guard++)
        {
            ClearBoard(team);
            Continue();
        }
        Check.Equal(FeudPhase.FastIntro, S.Phase, "the game is won");
    }

    /// <summary>Fast Money from the intro to the first player ready.</summary>
    public void BeginFast() => Check.Ok(Game.BeginFastMoney(Emcee));

    /// <summary>One player's turn: the clock starts and five answers are tapped by position on each question's board.</summary>
    public void FastTurn(params int[] picks)
    {
        Check.Ok(Game.StartFastClock(Emcee));
        foreach (var pick in picks) Check.Ok(pick < 0 ? Game.FastPass(Emcee) : Game.FastPick(Emcee, pick));
    }
}

static class FeudChecks
{
    private static readonly Guid Stranger = Guid.NewGuid();

    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine("Family Feud: the lobby and the face-off");

        run("feud: the first device is the emcee, nobody else can start, and names are the emcee's to set", () =>
        {
            var game = new FeudGame("TEST", new FixedFeudContent(), new Random(1));
            Check.True(!game.Snapshot().HasEmcee, "no emcee yet");
            Check.Fails(game.Start(Stranger), "starting with no emcee");
            var join = game.JoinEmcee();
            Check.Ok(join);
            Check.Fails(game.JoinEmcee(), "a second emcee");
            Check.Fails(game.Start(Stranger), "a stranger starting it");
            Check.Fails(game.SetTeamName(Stranger, 0, "Hackers"), "a stranger naming a family");
            Check.Ok(game.SetTeamName(join.Token, 0, "  The   Smiths "));
            Check.Ok(game.SetTeamName(join.Token, 1, ""));
            Check.Equal("The Smiths", game.Snapshot().Teams[0].Name, "whitespace tidied");
            Check.Equal("Family 2", game.Snapshot().Teams[1].Name, "a blank name falls back");
            Check.Equal(Role.Emcee, game.GetSeat(join.Token).Role, "the emcee's seat");
            Check.Ok(game.Start(join.Token));
            Check.Fails(game.Start(join.Token), "starting twice");
            Check.Fails(game.SetTeamName(join.Token, 0, "Late"), "renaming mid-game");
        });

        run("feud: a game can't start without enough questions", () =>
        {
            var t = new FeudTable(boards: 5, start: false);
            Check.Fails(t.Game.Start(t.Emcee), "five questions isn't enough for four rounds and Fast Money");
        });

        run("feud: the emcee can see the question before reading it (the TV hides it until then), and no answer is revealed until someone says it", () =>
        {
            var t = new FeudTable();
            Check.Equal(FeudPhase.RoundIntro, t.S.Phase, "starts with the round intro");
            Check.Equal("Question 1", t.S.Question, "the emcee can preview it");
            Check.Equal(1, t.S.Round, "round 1");
            Check.Equal(1, t.S.Multiplier, "face value");
            Check.Equal(6, t.S.Answers.Count, "six answers on the board");
            Check.True(t.S.Answers.All(a => !a.Revealed), "nothing is revealed");
            Check.Fails(t.Game.Buzz(0), "buzzing before the question");
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Equal("Question 1", t.S.Question, "the question is up");
            Check.Equal(FeudPhase.FaceOffBuzz, t.S.Phase, "waiting for the buzz");
            Check.Fails(t.Game.ReadQuestion(t.Emcee), "reading it twice");
        });

        run("feud: the first mouse click wins the buzz; later ones are ignored", () =>
        {
            var t = new FeudTable();
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Fails(t.Game.Buzz(5), "a family that doesn't exist");
            Check.Ok(t.Game.Buzz(1));
            Check.Fails(t.Game.Buzz(0), "the second click, a moment too late");
            Check.Equal(1, t.S.Turn, "family 2 is answering");
            Check.Equal(FeudPhase.FaceOffAnswer, t.S.Phase, "the emcee judges the answer");
        });

        run("feud: the emcee can assign the buzz if the mouse misbehaves", () =>
        {
            var t = new FeudTable();
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Fails(t.Game.AssignBuzz(Stranger, 0), "a stranger assigning it");
            Check.Ok(t.Game.AssignBuzz(t.Emcee, 0));
            Check.Equal(0, t.S.Turn, "family 1 is answering");
        });

        run("feud: the number one answer wins the face-off outright; the other player never answers", () =>
        {
            var t = new FeudTable();
            t.WinFaceOff(1);
            Check.Equal(1, t.S.FaceOffWinner, "family 2 won it");
            Check.True(t.S.Answers[0].Revealed, "the answer is up");
            Check.Fails(t.Game.Right(t.Emcee, 1), "marking an answer while choosing play or pass");
        });

        run("feud: a lower answer gives the other player a chance, and the better answer wins", () =>
        {
            var t = new FeudTable();
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Ok(t.Game.Buzz(0));
            t.Right(2);                                              // family 1 says number three
            Check.Equal(FeudPhase.FaceOffAnswer, t.S.Phase, "still deciding");
            Check.Equal(1, t.S.Turn, "family 2 answers");
            t.Right(1);                                              // family 2 says number two: better
            Check.Equal(FeudPhase.ControlChoice, t.S.Phase, "decided");
            Check.Equal(1, t.S.FaceOffWinner, "the higher answer wins");
        });

        run("feud: the first player keeps it if the other's answer is lower or wrong", () =>
        {
            var low = new FeudTable();
            Check.Ok(low.Game.ReadQuestion(low.Emcee));
            Check.Ok(low.Game.Buzz(0));
            low.Right(1);
            low.Right(4);
            Check.Equal(0, low.S.FaceOffWinner, "number two beats number five");

            var wrong = new FeudTable();
            Check.Ok(wrong.Game.ReadQuestion(wrong.Emcee));
            Check.Ok(wrong.Game.Buzz(0));
            wrong.Right(3);
            wrong.Strike();
            Check.Equal(0, wrong.S.FaceOffWinner, "a strike loses to any answer");
            Check.Equal(0, wrong.S.Strikes, "face-off strikes are not the round's strikes");
        });

        run("feud: a miss gives the other player the chance, and any answer then wins it", () =>
        {
            var t = new FeudTable();
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Ok(t.Game.Buzz(0));
            t.Strike();
            Check.Equal(1, t.S.Turn, "family 2 gets a go");
            t.Right(5);
            Check.Equal(1, t.S.FaceOffWinner, "even the last answer beats a strike");
        });

        run("feud: if both miss, the first player tries again with the question still open", () =>
        {
            var t = new FeudTable();
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Ok(t.Game.Buzz(1));
            t.Strike();
            t.Strike();
            Check.Equal(FeudPhase.FaceOffAnswer, t.S.Phase, "nobody has won it");
            Check.Equal(1, t.S.Turn, "back to the family that buzzed first");
            t.Right(3);
            Check.Equal(0, t.S.Turn, "then the other player answers again");
            t.Right(0);
            Check.Equal(0, t.S.FaceOffWinner, "and the top answer wins it");
        });

        run("feud: an answer already on the board can't be given again", () =>
        {
            var t = new FeudTable();
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Ok(t.Game.Buzz(0));
            t.Right(2);
            Check.Fails(t.Game.Right(t.Emcee, 2), "the same answer twice");
            Check.Fails(t.Game.Right(t.Emcee, 99), "an answer that isn't on the board");
        });

        run("feud: only the emcee can mark answers and strikes", () =>
        {
            var t = new FeudTable();
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Ok(t.Game.Buzz(0));
            Check.Fails(t.Game.Right(Stranger, 0), "a stranger marking an answer");
            Check.Fails(t.Game.Strike(Stranger), "a stranger striking");
        });

        Console.WriteLine("Family Feud: playing the board");

        run("feud: the winner can play or pass, and a pass hands the board to the other family", () =>
        {
            var play = new FeudTable();
            play.WinFaceOff(0);
            Check.Ok(play.Game.Choose(play.Emcee, play: true));
            Check.Equal(0, play.S.Control, "family 1 plays");
            Check.Equal(FeudPhase.Play, play.S.Phase, "playing");

            var pass = new FeudTable();
            pass.WinFaceOff(0);
            Check.Fails(pass.Game.Choose(Stranger, true), "a stranger choosing");
            Check.Ok(pass.Game.Choose(pass.Emcee, play: false));
            Check.Equal(1, pass.S.Control, "family 2 plays");
        });

        run("feud: three strikes hand the other family a steal; a failed steal leaves the bank with the family in control", () =>
        {
            var t = new FeudTable();
            t.WinFaceOff(0);
            Check.Ok(t.Game.Choose(t.Emcee, true));
            t.Right(1);
            t.Strike(); t.Strike();
            Check.Equal(2, t.S.Strikes, "two strikes");
            Check.Equal(FeudPhase.Play, t.S.Phase, "still playing");
            t.Strike();
            Check.Equal(FeudPhase.Steal, t.S.Phase, "three strikes: a steal");
            Check.Equal(1, t.S.Stealer, "the other family steals");
            Check.Equal(55, t.S.Bank, "the bank they are stealing: 30 + 25");
            t.Strike();
            Check.Equal(FeudPhase.RoundOver, t.S.Phase, "the steal missed");
            Check.Equal(0, t.S.RoundWinner, "family 1 keeps the bank");
            Check.Equal(0, t.S.Teams[0].Score, "but the points aren't in the score until the emcee continues");
        });

        run("feud: a good steal takes the whole bank, including the answer that stole it", () =>
        {
            var t = new FeudTable();
            t.WinFaceOff(0);
            Check.Ok(t.Game.Choose(t.Emcee, true));
            t.Strike(); t.Strike(); t.Strike();
            Check.Equal(30, t.S.Bank, "just the face-off answer so far");
            t.Right(2);
            Check.Equal(FeudPhase.RoundOver, t.S.Phase, "the steal worked");
            Check.Equal(1, t.S.RoundWinner, "family 2 wins the round");
            Check.Equal(50, t.S.Bank, "30 + 20");
            t.Continue();
            Check.Equal(50, t.S.Teams[1].Score, "the stolen points land with the stealer");
            Check.Equal(0, t.S.Teams[0].Score, "and not with the family that struck out");
        });

        run("feud: clearing the board wins the bank for the family in control, even after a pass", () =>
        {
            var t = new FeudTable();
            t.WinFaceOff(0);
            Check.Ok(t.Game.Choose(t.Emcee, play: false));          // family 1 passes to family 2
            for (var i = 1; i < 6; i++) t.Right(i);
            Check.Equal(FeudPhase.RoundOver, t.S.Phase, "board cleared");
            Check.Equal(1, t.S.RoundWinner, "family 2 was playing");
            Check.Equal(100, t.S.Bank, "a hundred in all");
        });

        run("feud: strikes only count in play, and an answer can't be marked after the round ends", () =>
        {
            var t = new FeudTable();
            t.ClearBoard(0);
            Check.Fails(t.Game.Strike(t.Emcee), "striking after the round");
            Check.Fails(t.Game.Right(t.Emcee, 0), "marking after the round");
            Check.Fails(t.Game.Choose(t.Emcee, true), "choosing after the round");
        });

        run("feud: points move into the score only when the emcee continues, and rounds then advance", () =>
        {
            var t = new FeudTable();
            t.ClearBoard(0);
            Check.Equal(0, t.S.Teams[0].Score, "not yet");
            Check.Equal(100, t.S.Bank, "the bank is on screen for the celebration");
            Check.Fails(t.Game.Continue(Stranger), "a stranger continuing");
            t.Continue();
            Check.Equal(100, t.S.Teams[0].Score, "banked");
            Check.Equal(2, t.S.Round, "round 2");
            Check.Equal(FeudPhase.RoundIntro, t.S.Phase, "a fresh round");
            Check.True(t.S.Answers.All(a => !a.Revealed), "a fresh board");
            Check.Fails(t.Game.Continue(t.Emcee), "continuing twice");
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Equal("Question 2", t.S.Question, "the next question");
        });

        run("feud: rounds one and two are face value, round three doubles and round four triples", () =>
        {
            var t = new FeudTable();
            t.SmallWin(0); Check.Equal(55, t.S.Bank, "round 1 at face value");
            t.Continue();
            t.SmallWin(1); Check.Equal(55, t.S.Bank, "round 2 at face value");
            t.Continue();
            Check.Equal(2, t.S.Multiplier, "round 3 is double");
            t.SmallWin(0); Check.Equal(110, t.S.Bank, "round 3 doubled");
            t.Continue();
            Check.Equal(3, t.S.Multiplier, "round 4 is triple");
            t.SmallWin(1); Check.Equal(165, t.S.Bank, "round 4 tripled");
            t.Continue();
            Check.Equal(3, t.S.Multiplier, "and it stays triple");
            Check.Equal(165, t.S.Teams[0].Score, "family 1: 55 + 110");
            Check.Equal(220, t.S.Teams[1].Score, "family 2: 55 + 165");
        });

        run("feud: the bank on screen is already doubled or tripled", () =>
        {
            var t = new FeudTable();
            t.SmallWin(0); t.Continue();
            t.SmallWin(0); t.Continue();
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Ok(t.Game.Buzz(0));
            t.Right(0);
            Check.Equal(60, t.S.Bank, "30 doubled");
        });

        run("feud: a family at 300 or more goes to Fast Money; neither at 300 keeps playing", () =>
        {
            var t = new FeudTable();
            t.ClearBoard(0); t.Continue();                            // 100
            t.ClearBoard(0); t.Continue();                            // 200
            Check.Equal(FeudPhase.RoundIntro, t.S.Phase, "200 is not enough");
            t.ClearBoard(0); t.Continue();                            // 400: round three is double
            Check.Equal(FeudPhase.FastIntro, t.S.Phase, "400 is");
            Check.Equal(0, t.S.Fast!.Team, "family 1 plays Fast Money");
            Check.Equal(400, t.S.Teams[0].Score, "the score stands");
        });

        run("feud: skipping a question deals the next one, but only before anyone has scored", () =>
        {
            var t = new FeudTable();
            Check.Ok(t.Game.SkipQuestion(t.Emcee));
            Check.Equal(1, t.S.Round, "still round 1");
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Equal("Question 2", t.S.Question, "the skipped one is gone");
            Check.Ok(t.Game.Buzz(0));
            Check.Ok(t.Game.SkipQuestion(t.Emcee));                   // still allowed: nothing is on the board
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Ok(t.Game.Buzz(0));
            t.Right(1);
            Check.Fails(t.Game.SkipQuestion(t.Emcee), "skipping once an answer is up");
            Check.Fails(t.Game.SkipQuestion(Stranger), "a stranger skipping");
        });

        run("feud: undo takes back the last emcee action, even a whole round's score", () =>
        {
            var t = new FeudTable();
            Check.True(!t.S.CanUndo, "nothing to undo at the start of the first round");
            Check.Ok(t.Game.ReadQuestion(t.Emcee));
            Check.Ok(t.Game.Buzz(0));
            t.Right(0);
            Check.Ok(t.Game.Undo(t.Emcee));
            Check.Equal(FeudPhase.FaceOffAnswer, t.S.Phase, "back to the answer");
            Check.True(!t.S.Answers[0].Revealed, "and the answer is hidden again");
            Check.Fails(t.Game.Undo(Stranger), "a stranger undoing");

            var r = new FeudTable();
            r.ClearBoard(1);
            r.Continue();
            Check.Equal(100, r.S.Teams[1].Score, "scored");
            Check.Ok(r.Game.Undo(r.Emcee));
            Check.Equal(0, r.S.Teams[1].Score, "taken back");
            Check.Equal(FeudPhase.RoundOver, r.S.Phase, "to the end of the round");
        });

        Console.WriteLine("Family Feud: Fast Money");

        run("feud: Fast Money names two players, then gives the first player five questions on the clock", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(1);
            Check.Equal(1, t.S.Fast!.Team, "family 2 won");
            Check.Fails(t.Game.SetFastPlayer(Stranger, 0, "X"), "a stranger naming a player");
            Check.Ok(t.Game.SetFastPlayer(t.Emcee, 0, "Grandma"));
            Check.Ok(t.Game.SetFastPlayer(t.Emcee, 1, "  "));
            Check.Equal("Grandma", t.S.Fast!.Players[0], "named");
            Check.Equal("Player 2", t.S.Fast!.Players[1], "default");
            Check.Fails(t.Game.StartFastClock(t.Emcee), "starting the clock before the player is ready");
            t.BeginFast();
            Check.Equal(FeudPhase.FastReady, t.S.Phase, "ready");
            Check.Equal(0, t.S.Fast!.Player, "player one first");
            Check.Fails(t.Game.FastPick(t.Emcee, 0), "answering before the clock starts");
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.Equal(FeudPhase.FastPlay, t.S.Phase, "the clock is on");
            Check.Equal(0, t.S.Fast!.Question, "question one");
            Check.Equal("Question 13", t.S.Fast!.QuestionText, "the first Fast Money question");
            Check.Equal(6, t.S.Fast!.Board.Count, "the emcee has the survey answers to tap");
            Check.True(t.S.Fast!.SecondsLeft is > 19 and <= 20, "twenty seconds for the first player");
        });

        run("feud: each answer moves on to the next question, and five answers end the turn", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.Ok(t.Game.FastPick(t.Emcee, 0));
            Check.Equal(1, t.S.Fast!.Question, "on to question two");
            Check.Ok(t.Game.FastType(t.Emcee, "totally wrong"));
            Check.Ok(t.Game.FastPass(t.Emcee));
            Check.Ok(t.Game.FastPick(t.Emcee, 2));
            Check.Equal(4, t.S.Fast!.Question, "question five");
            Check.Fails(t.Game.FastPick(t.Emcee, 99), "an answer that isn't on the board");
            Check.Ok(t.Game.FastPick(t.Emcee, 5));
            Check.Equal(FeudPhase.FastReady, t.S.Phase, "player one is done; player two is up");
            Check.Equal(1, t.S.Fast!.Player, "player two");
            var one = t.S.Fast!.Entries[0];
            Check.Equal("Q13 answer 1", one[0].Text, "tapped answers keep the survey's wording");
            Check.Equal(30, one[0].Points, "and its points");
            Check.Equal("totally wrong", one[1].Text, "typed text is kept as typed");
            Check.Equal(0, one[1].Points, "and scores nothing");
            Check.Equal("(no answer)", one[2].Text, "a pass");
            Check.Equal(20, one[3].Points, "the third survey answer");
            Check.Equal(5, one[4].Points, "the sixth survey answer");
        });

        run("feud: a typed answer that matches a survey answer scores it, ignoring case, spaces, plurals and 'the'", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.Ok(t.Game.FastType(t.Emcee, "  q13   ANSWER 2 "));
            Check.Equal(25, t.S.Fast!.Entries[0][0].Points, "matched");
            Check.Fails(t.Game.FastType(t.Emcee, "   "), "an empty answer");
            Check.Fails(t.Game.FastType(Stranger, "x"), "a stranger typing");
        });

        run("feud: the matcher is forgiving about plurals and a leading article", () =>
        {
            Check.Equal(FeudContentRules.Normalize("Dogs"), FeudContentRules.Normalize("the dog"), "plural and article");
            Check.Equal(FeudContentRules.Normalize("Mom!"), FeudContentRules.Normalize("mom"), "punctuation");
            Check.True(FeudContentRules.Normalize("Glass") != FeudContentRules.Normalize("Glas"), "a double s stays");
        });

        run("feud: the second player can't repeat the first player's answer to the same question", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            t.FastTurn(0, 1, 2, 3, 4);
            Check.Equal(1, t.S.Fast!.Player, "second player");
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.Fails(t.Game.FastPick(t.Emcee, 0), "the same survey answer");
            Check.Equal(0, t.S.Fast!.Question, "still on question one");
            Check.Fails(t.Game.FastType(t.Emcee, "q13 answer 1"), "the same answer typed");
            Check.Ok(t.Game.FastPick(t.Emcee, 1));
            Check.Ok(t.Game.FastPick(t.Emcee, 0));
        });

        run("feud: the emcee can type what was said and count it as a survey answer, so 'Sofa' scores as 'Couch'", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.Ok(t.Game.FastPick(t.Emcee, 2, "Sofa"));                       // the third answer is worth 20 in the test bank
            var entry = t.S.Fast!.Entries[0][0];
            Check.Equal("Sofa", entry.Text, "the board shows what they said");
            Check.Equal(20, entry.Points, "and scores the survey answer's points");
            Check.Ok(t.Game.FastPick(t.Emcee, 1));                               // tapping with nothing typed uses the survey wording
            Check.True(t.S.Fast!.Entries[0][1].Text.EndsWith("answer 2"), "no typed word: the survey's wording");
            Check.Ok(t.Game.FastType(t.Emcee, "Banana"));
            Check.Equal(0, t.S.Fast!.Entries[0][2].Points, "typed and not on the list: a miss");
            Check.Ok(t.Game.FastType(t.Emcee, "q13 answer 4"));
            Check.Equal(12, t.S.Fast!.Entries[0][3].Points, "typed exactly as a survey answer: that answer's points");
        });

        run("feud: the second player can't land on the same survey answer even in other words, and the buzzer records nothing", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.Fails(t.Game.FastBuzzer(Stranger), "a stranger hitting the buzzer");
            Check.Ok(t.Game.FastPick(t.Emcee, 2, "Couch"));
            for (var q = 1; q < FeudRules.FastQuestions; q++) Check.Ok(t.Game.FastPick(t.Emcee, 0));
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.Fails(t.Game.FastPick(t.Emcee, 2, "Sofa"), "same survey answer in other words");
            Check.Equal(0, t.S.Fast!.Question, "still on question one");
            var before = t.S.Fast!.Buzzes;
            Check.Ok(t.Game.FastBuzzer(t.Emcee));
            Check.Equal(before + 1, t.S.Fast!.Buzzes, "the buzzer is counted so the TV can sound it");
            Check.Equal(0, t.S.Fast!.Question, "the buzzer records nothing and the question stays");
            Check.Ok(t.Game.FastPick(t.Emcee, 1, "Loveseat"));
            Check.Equal(1, t.S.Fast!.Question, "a different answer moves on");
        });

        run("feud: a pass by the first player doesn't block the second player", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            t.FastTurn(-1, 1, 2, 3, 4);
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.Ok(t.Game.FastPick(t.Emcee, 0));
        });

        run("feud: the clock runs out, the emcee ends the turn, and anything unanswered scores nothing", () =>
        {
            var t = new FeudTable(secondSeconds: 0.2, firstSeconds: 0.2);
            t.WinTheGame(0);
            t.BeginFast();
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.Ok(t.Game.FastPick(t.Emcee, 0));
            Check.Ok(t.Game.FastPick(t.Emcee, 1));
            Thread.Sleep(250);
            Check.Ok(t.Game.FastTimeUp());
            Check.True(t.S.Fast!.TimeUp, "time is up");
            Check.Fails(t.Game.FastTimeUp(), "buzzing twice");
            Check.Ok(t.Game.EndFastTurn(t.Emcee));
            Check.Equal(FeudPhase.FastReady, t.S.Phase, "on to player two");
            var one = t.S.Fast!.Entries[0];
            Check.Equal(30, one[0].Points, "answered");
            Check.Equal(25, one[1].Points, "answered");
            Check.Equal("(no answer)", one[2].Text, "not reached");
            Check.Equal(0, one[4].Points, "not reached");
            Check.Fails(t.Game.EndFastTurn(t.Emcee), "ending a turn nobody is playing");
        });

        run("feud: the time-up call is ignored while there is time left", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.Fails(t.Game.FastTimeUp(), "twenty seconds haven't passed");
        });

        run("feud: the second player has 25 seconds and answers the same five questions", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            t.FastTurn(0, 0, 0, 0, 0);
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.True(t.S.Fast!.SecondsLeft is > 24 and <= 25, "twenty-five seconds");
            Check.Equal("Question 13", t.S.Fast!.QuestionText, "the same first question");
        });

        run("feud: the reveal is one tap at a time (answer, then its points), and the total builds as the points show", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            t.FastTurn(0, 1, 2, 3, 4);              // 30 + 25 + 20 + 12 + 8 = 95
            t.FastTurn(1, 0, 3, 2, 5);              // 25 + 30 + 12 + 20 + 5 = 92
            Check.Equal(FeudPhase.FastReveal, t.S.Phase, "both players are done");
            Check.Equal(0, t.S.Fast!.TotalShown, "nothing is showing");
            Check.True(t.S.Fast!.Entries.All(p => p.All(e => !e.ShowText && !e.ShowPoints)), "no text or points yet");

            Check.Ok(t.Game.RevealNext(t.Emcee));
            Check.True(t.S.Fast!.Entries[0][0].ShowText && !t.S.Fast!.Entries[0][0].ShowPoints, "first the answer");
            Check.Equal(0, t.S.Fast!.TotalShown, "no points yet");
            Check.Ok(t.Game.RevealNext(t.Emcee));
            Check.True(t.S.Fast!.Entries[0][0].ShowPoints, "then its points");
            Check.Equal(30, t.S.Fast!.TotalShown, "the total starts");
            Check.True(!t.S.Fast!.Entries[0][1].ShowText, "the next answer waits");

            for (var i = 0; i < 8; i++) Check.Ok(t.Game.RevealNext(t.Emcee));
            Check.Equal(95, t.S.Fast!.TotalShown, "player one's five answers");
            Check.True(t.S.Fast!.Entries[1].All(e => !e.ShowText), "player two hasn't started");
            Check.Equal(FeudPhase.FastReveal, t.S.Phase, "not over");

            for (var i = 0; i < 9; i++) Check.Ok(t.Game.RevealNext(t.Emcee));
            Check.Equal(FeudPhase.FastReveal, t.S.Phase, "one tap to go");
            Check.Ok(t.Game.RevealNext(t.Emcee));
            Check.Equal(FeudPhase.FastOver, t.S.Phase, "done");
            Check.Equal(187, t.S.Fast!.Total, "95 + 92");
            Check.Equal(187, t.S.Fast!.TotalShown, "all shown");
            Check.True(!t.S.Fast!.Won, "187 is under 200");
            Check.Fails(t.Game.RevealNext(t.Emcee), "nothing left to reveal");
            Check.Fails(t.Game.RevealNext(Stranger), "a stranger revealing");
        });

        run("feud: 200 points or more wins Fast Money", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            t.FastTurn(0, 0, 0, 0, 0);              // 150
            t.FastTurn(1, 1, 1, 1, 1);              // 125
            for (var i = 0; i < 20; i++) Check.Ok(t.Game.RevealNext(t.Emcee));
            Check.Equal(FeudPhase.FastOver, t.S.Phase, "done");
            Check.Equal(275, t.S.Fast!.Total, "total");
            Check.True(t.S.Fast!.Won, "a win");
        });

        run("feud: over 200 wins and 167 doesn't", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            t.FastTurn(0, 0, 0, 0, 3);              // 30*4 + 12 = 132
            t.FastTurn(2, 2, 2, 2, 4);              // 20*4 + 8 = 88
            for (var i = 0; i < 20; i++) Check.Ok(t.Game.RevealNext(t.Emcee));
            Check.Equal(220, t.S.Fast!.Total, "132 + 88");
            Check.True(t.S.Fast!.Won, "over 200 is still a win");
            var edge = new FeudTable();
            edge.WinTheGame(0);
            edge.BeginFast();
            edge.FastTurn(0, 1, 2, 3, -1);          // 30 + 25 + 20 + 12 = 87
            edge.FastTurn(1, 0, 4, 5, 3);           // 25 + 30 + 8 + 5 + 12 = 80 -> 167
            for (var i = 0; i < 20; i++) Check.Ok(edge.Game.RevealNext(edge.Emcee));
            Check.True(!edge.S.Fast!.Won, "167 is a loss");
        });

        run("feud: undo works inside Fast Money, so a mis-tap can be taken back", () =>
        {
            var t = new FeudTable();
            t.WinTheGame(0);
            t.BeginFast();
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.Ok(t.Game.FastPick(t.Emcee, 3));
            Check.Ok(t.Game.Undo(t.Emcee));
            Check.Equal(0, t.S.Fast!.Question, "back on question one");
            Check.True(!t.S.Fast!.Entries[0][0].Entered, "the answer is gone");
            Check.Ok(t.Game.FastPick(t.Emcee, 0));
            Check.Equal(30, t.S.Fast!.Entries[0][0].Points, "the right one");
        });

        run("feud: the game never reuses a question, and every question it dealt is marked used", () =>
        {
            var t = new FeudTable();
            Check.Equal(17, t.Content.Used.Count, "17 boards dealt");
            Check.Equal(17, t.Content.Used.Distinct().Count(), "all different");
            t.WinTheGame(0);
            t.BeginFast();
            Check.Ok(t.Game.StartFastClock(t.Emcee));
            Check.True(t.S.Fast!.QuestionText != "Question 1", "Fast Money isn't a regular round's question");
        });

        run("feud: a game with too few boards for twelve rounds still plays, and ends in Fast Money when the questions run out", () =>
        {
            var t = new FeudTable(boards: 9);
            // Four regular boards: each round is a tiny win, so nobody reaches 300.
            var rounds = 0;
            while (t.S.Phase != FeudPhase.FastIntro && rounds < 10)
            {
                t.SmallWin(rounds % 2);
                t.Continue();
                rounds++;
            }
            Check.Equal(FeudPhase.FastIntro, t.S.Phase, "Fast Money when the questions are gone");
            Check.Equal(4, rounds, "after the four regular boards");
        });

        run("feud: a fuzzed game of legal and illegal actions never breaks the rules", () =>
        {
            for (var seed = 0; seed < 40; seed++)
            {
                var rng = new Random(seed);
                var t = new FeudTable(seed: seed, firstSeconds: 0.01, secondSeconds: 0.01);
                for (var step = 0; step < 400; step++)
                {
                    var token = rng.Next(10) == 0 ? Stranger : t.Emcee;
                    switch (rng.Next(16))
                    {
                        case 0: t.Game.ReadQuestion(token); break;
                        case 1: t.Game.Buzz(rng.Next(3)); break;
                        case 2: t.Game.AssignBuzz(token, rng.Next(2)); break;
                        case 3: case 4: t.Game.Right(token, rng.Next(-1, 9)); break;
                        case 5: case 6: t.Game.Strike(token); break;
                        case 7: t.Game.Choose(token, rng.Next(2) == 0); break;
                        case 8: t.Game.Continue(token); break;
                        case 9: t.Game.SkipQuestion(token); break;
                        case 10: t.Game.BeginFastMoney(token); t.Game.StartFastClock(token); break;
                        case 11: t.Game.FastPick(token, rng.Next(-1, 9)); break;
                        case 12: t.Game.FastType(token, rng.Next(3) == 0 ? "" : "q13 answer " + rng.Next(1, 8)); break;
                        case 13: t.Game.FastPass(token); t.Game.EndFastTurn(token); break;
                        case 14: t.Game.RevealNext(token); t.Game.FastTimeUp(); break;
                        default: t.Game.Undo(token); break;
                    }
                    var s = t.S;
                    Check.True(s.Teams.All(x => x.Score >= 0), "scores never go negative");
                    Check.True(s.Strikes is >= 0 and <= FeudRules.StrikesAllowed, "strikes stay between 0 and 3");
                    Check.True(s.Answers.Count <= FeudRules.MaxAnswers, "never more than eight answers");
                    if (s.Fast is { } f) Check.True(f.TotalShown <= f.Total, "the total shown never exceeds the real total");
                }
            }
        });

        Console.WriteLine("Family Feud: the question bank");

        run("feud bank: import reads 'question | answer: points' lines, sorts answers best first, and reports what it couldn't read", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "feud-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var bank = new FolderFeudContent(folder, seedStarters: false);
                var report = bank.Import("""
                    # a comment
                    Name a fruit | Banana: 20 | Apple: 40 | Cherry (15) | Plum=5
                    Name a color | Red: 50 | Blue: 30
                    Name a thing | Pen: 0 | Cup: 30 | Hat: 20
                    Name a pet | Dog: 50 | Cat: 30 | Fish: 10 | Hamster: 5 | Bird: 5
                    Name a pet | Dog: 50 | Cat: 30 | Fish: 10
                    no pipes here
                    Name a tool | Saw 20 | Axe: 10 | Drill: 5
                    """);
                Check.Equal(2, report.Added, "the fruit and the pet");
                Check.Equal(1, report.Duplicates, "the repeated pet question");
                Check.Equal(4, report.Problems.Count, "the two-answer board, the zero-point answer, the line with no pipes, and the answer with no points");
                var fruit = bank.Boards().First(b => b.Question == "Name a fruit");
                Check.Equal("Apple", fruit.Answers[0].Text, "best first");
                Check.Equal("Plum", fruit.Answers[^1].Text, "worst last");
                Check.Equal(15, fruit.Answers[2].Points, "the bracket style works");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        });

        run("feud bank: answers are limited to eight, three at least, and can't repeat", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "feud-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var bank = new FolderFeudContent(folder, seedStarters: false);
                Check.True(!bank.Add("Q?", [("a", 5), ("b", 4)]).Ok, "two answers");
                Check.True(!bank.Add("Q?", Enumerable.Range(1, 9).Select(i => ((string?)$"a{i}", i)).ToList()).Ok, "nine answers");
                Check.True(!bank.Add("Q?", [("a", 5), ("A", 4), ("b", 3)]).Ok, "the same answer twice");
                Check.True(!bank.Add("", [("a", 5), ("b", 4), ("c", 3)]).Ok, "no question");
                Check.True(!bank.Add("Q?", [("a", 100), ("b", 4), ("c", 3)]).Ok, "a hundred points");
                var ok = bank.Add("Name a drink", [("Water", 50), ("Juice", 30), ("Milk", 20)]);
                Check.True(ok.Ok, "a good one");
                Check.True(bank.Add("name a drink", [("Water", 50), ("Juice", 30), ("Milk", 20)]).Duplicate, "the same question in other case");
                var warned = bank.Add("Name a wine", [("Red", 50), ("White", 30), ("Rose", 20)]);
                Check.True(warned.Ok && warned.Warnings!.Count > 0, "alcohol gets a warning");
                var over = bank.Add("Name a snack", [("Chips", 60), ("Nuts", 50), ("Fruit", 20)]);
                Check.True(over.Ok && over.Warnings!.Count > 0, "over 100 points gets a warning");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        });

        run("feud bank: the starter set loads, plays, survives a restart, and each board is a proper survey", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "feud-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var bank = new FolderFeudContent(folder);
                var all = bank.Boards();
                Check.True(all.Count >= 40, $"a decent starter set (found {all.Count})");
                foreach (var b in all)
                {
                    Check.True(b.Answers.Count is >= FeudRules.MinAnswers and <= FeudRules.MaxAnswers, $"answer count: {b.Question}");
                    Check.True(b.Answers.Sum(a => a.Points) <= 100, $"under 100 points: {b.Question}");
                    Check.True(b.Answers.Zip(b.Answers.Skip(1), (x, y) => x.Points >= y.Points).All(x => x), $"best first: {b.Question}");
                }
                Check.True(bank.TryDeal(new Random(1), FeudRules.BoardsPerGame, out var dealt, out _), "a game can be dealt");
                Check.Equal(FeudRules.BoardsPerGame, dealt.Count, "a full game's worth");
                Check.Equal(dealt.Count, dealt.Select(d => d.Question).Distinct().Count(), "no repeated question");

                bank.MarkUsed(dealt.Select(d => d.Id));
                var again = new FolderFeudContent(folder);
                Check.Equal(all.Count, again.Boards().Count, "the bank survived a restart");
                Check.Equal(all.Count - dealt.Count, again.Counts().Fresh, "and so did which ones are used");
                Check.True(again.TryDeal(new Random(2), FeudRules.BoardsPerGame, out var next, out _), "dealing again");
                Check.True(next.Count(n => !dealt.Any(d => d.Id == n.Id)) >= Math.Min(FeudRules.BoardsPerGame, again.Counts().Fresh), "fresh questions come first");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        });

        run("feud bank: an older starter set is swapped for the new one, and questions added by hand stay", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "feud-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var retired = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BuzzerGames.Core", "Data", "Feud", "feud-retired.txt")).First();
                Directory.CreateDirectory(Path.Combine(folder, "feud"));
                File.WriteAllText(Path.Combine(folder, "feud", "boards.json"),
                    "{\"boards\":[{\"id\":\"old1\",\"question\":" + System.Text.Json.JsonSerializer.Serialize(retired) + ",\"answers\":[{\"text\":\"A\",\"points\":50},{\"text\":\"B\",\"points\":30},{\"text\":\"C\",\"points\":10}]}," +
                    "{\"id\":\"mine\",\"question\":\"Name my own thing\",\"answers\":[{\"text\":\"A\",\"points\":50},{\"text\":\"B\",\"points\":30},{\"text\":\"C\",\"points\":10}]}],\"used\":[\"old1\",\"mine\"]}");
                var bank = new FolderFeudContent(folder);
                var all = bank.Boards();
                Check.True(all.Any(b => b.Id == "mine"), "the hand-added question stays");
                Check.True(!all.Any(b => b.Id == "old1"), "the retired starter question goes");
                Check.True(bank.IsUsed("mine") && !bank.IsUsed("old1"), "used marks follow");
                Check.True(all.Count > 1000, "the new set is in");
                var again = new FolderFeudContent(folder);
                Check.Equal(all.Count, again.Boards().Count, "a restart doesn't add the set twice");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        });

        run("feud rounds: the longer boards go to the early rounds and a four-answer board is the triple round", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "feud-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var bank = new FolderFeudContent(folder, seedStarters: false);
                for (var i = 0; i < 30; i++)
                    Check.True(bank.Add($"Long {i}", [($"a{i}", 30), ($"b{i}", 25), ($"c{i}", 20), ($"d{i}", 10), ($"e{i}", 8), ($"f{i}", 5)]).Ok, "added");
                for (var i = 0; i < 12; i++)
                    Check.True(bank.Add($"Short {i}", [($"a{i}", 40), ($"b{i}", 30), ($"c{i}", 20), ($"d{i}", 10)]).Ok, "added");

                for (var seed = 1; seed <= 6; seed++)
                {
                    var t = new FeudTable(start: true, seed: seed, bank: bank);
                    Check.True(t.S.Answers.Count > FeudRules.ShortBoardAnswers, $"round 1 is a long board (seed {seed})");
                    t.SmallWin(0); t.Continue();
                    Check.True(t.S.Answers.Count > FeudRules.ShortBoardAnswers, "round 2 is a long board");
                    t.SmallWin(1); t.Continue();
                    Check.True(t.S.Answers.Count > FeudRules.ShortBoardAnswers, "round 3 (double) is a long board");
                    t.SmallWin(0); t.Continue();
                    Check.Equal(4, t.S.Round, "round 4");
                    Check.Equal(3, t.S.Multiplier, "round 4 is the triple");
                    Check.Equal(FeudRules.ShortBoardAnswers, t.S.Answers.Count, "the triple round is a four-answer board");
                    Check.Ok(t.Game.SkipQuestion(t.Emcee));
                    Check.Equal(FeudRules.ShortBoardAnswers, t.S.Answers.Count, "a skipped triple round is replaced by another four-answer board");
                }

                Check.True(bank.TryDeal(new Random(3), FeudRules.BoardsPerGame, FeudRules.ShortBoardsPerGame, out var dealt, out _), "dealt");
                Check.Equal(FeudRules.ShortBoardsPerGame, dealt.Count(b => b.Answers.Count <= FeudRules.ShortBoardAnswers), "the short boards in a deal");
                Check.Equal(FeudRules.BoardsPerGame, dealt.Count, "a full deal");

                // A bank with no short boards at all still plays.
                var longOnly = new FolderFeudContent(Path.Combine(folder, "b"), seedStarters: false);
                for (var i = 0; i < 20; i++)
                    Check.True(longOnly.Add($"Long {i}", [($"a{i}", 30), ($"b{i}", 25), ($"c{i}", 20), ($"d{i}", 10), ($"e{i}", 8)]).Ok, "added");
                var g = new FeudTable(start: true, bank: longOnly);
                g.SmallWin(0); g.Continue(); g.SmallWin(1); g.Continue(); g.SmallWin(0); g.Continue();
                Check.Equal(4, g.S.Round, "still gets a fourth round with no short boards");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        });

        run("feud bank: delete removes a question and clear-used makes everything fresh again", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "feud-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var bank = new FolderFeudContent(folder);
                var first = bank.Boards()[0];
                Check.True(bank.Delete(first.Id).Ok, "deleted");
                Check.True(!bank.Delete(first.Id).Ok, "deleting twice");
                Check.True(!bank.Boards().Any(b => b.Id == first.Id), "gone");
                bank.MarkUsed(bank.Boards().Select(b => b.Id));
                Check.Equal(0, bank.Counts().Fresh, "all used");
                bank.ClearUsed();
                Check.Equal(bank.Counts().Total, bank.Counts().Fresh, "all fresh");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        });
    }
}
