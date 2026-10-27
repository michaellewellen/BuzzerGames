using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.PressYourLuck;
using BuzzerGames.Web;

// Run with:  dotnet run --project tests/BuzzerGames.Web.Checks
// Plays real games with the real engine, draws each screen to HTML, and checks what each device
// is (and is not) allowed to show. The important rule: only the emcee's phone ever sees an answer.

var passed = 0;
var failed = 0;

void Run(string name, Action body)
{
    try
    {
        body();
        passed++;
        Console.WriteLine($"  ok    {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}\n          {ex.Message}");
    }
}

Console.WriteLine("The lobby");

Run("lobby: three choices, Start only appears once the table is full", () =>
{
    var game = new JeopardyGame("TEST", new DummyGameDataSource(), new Random(1));
    var html = Render.Lobby(game, new Seat(Role.Spectator));
    Check.True(Render.Has(html, "Join as Emcee") && Render.Has(html, "Join as Player"), "both join buttons are shown");
    Check.True(!Render.Has(html, "Start Game"), "Start must be hidden while the table is empty");
    Check.True(Render.Has(html, "id=\"qr-TEST\""), "a place for the QR code");

    var emceeToken = game.JoinEmcee("Host").Token;
    html = Render.Lobby(game, new Seat(Role.Spectator));
    Check.True(Render.ButtonDisabled(html, "Join as Emcee"), "the emcee button turns off once the seat is taken");
    Check.True(!Render.ButtonDisabled(html, "Join as Player"), "the player button is still on");
    Check.True(Render.Has(html, "Host"), "the emcee's name is on the roster");

    game.JoinPlayer("Ann");
    game.JoinPlayer("Bob");
    Check.True(!Render.Has(Render.Lobby(game, new Seat(Role.Spectator)), "Start Game"), "still no Start with two players");
    game.JoinPlayer("Cy");

    html = Render.Lobby(game, new Seat(Role.Spectator));
    Check.True(Render.Has(html, "Start Game"), "Start appears with 1 emcee and 3 players");
    Check.True(Render.ButtonDisabled(html, "Join as Player"), "the player button turns off when full");

    html = Render.Lobby(game, game.GetSeat(emceeToken));
    Check.True(Render.Has(html, "joined as") && Render.Has(html, "Host"), "a device that already joined is told so");
    Check.True(Render.Has(html, "Start Game"), "Start shows on every device, joined or not");
});

Console.WriteLine("The big screen");

Run("TV: board shows 6 categories and 30 values, and played clues go dark", () =>
{
    var table = Table.Started();
    var html = Render.Tv(table);
    Check.Equal(6, Render.Count(html, "board-category"), "category headers");
    Check.Equal(30, Render.Count(html, "board-value"), "value cells");
    Check.Equal(0, Render.Count(html, "board-value used"), "used cells at the start");

    table.OpenNormalClue();
    table.Game.GiveUp(table.Emcee);
    table.Game.Continue(table.Emcee);
    Check.True(Render.Count(Render.Tv(table), "board-value used") >= 1, "a played clue is marked used");
});

Run("TV: the clue shows, but the answer stays hidden until the reveal", () =>
{
    var table = Table.Started();
    table.OpenNormalClue();
    var clue = table.S.Clue!;

    var html = Render.Tv(table);
    Check.True(Render.Shows(html, clue.Text), "the clue text is on the big screen");
    Check.True(Render.Has(html, "Buzzers are open"), "buzzers-open message");
    Check.True(!Render.Has(html, "clue-answer"), "the answer must not be on the big screen while the clue is live");

    table.Game.Buzz(table.Tokens[0]);
    html = Render.Tv(table);
    Check.True(Render.Has(html, "is answering"), "shows who is answering");
    Check.True(!Render.Has(html, "clue-answer"), "the answer must not show while someone is answering");

    table.Game.Judge(table.Emcee, false);
    Check.True(!Render.Has(Render.Tv(table), "clue-answer"), "the answer must not show after one wrong guess either");

    table.Game.Buzz(table.Tokens[1]);
    table.Game.Judge(table.Emcee, true);
    html = Render.Tv(table);
    Check.True(Render.Has(html, "clue-answer") && Render.Shows(html, clue.Answer), "the answer shows at the reveal");
    Check.True(Render.Has(html, "got it!"), "the reveal message is shown");
});

Run("TV: a Daily Double hides its clue until the wager is in", () =>
{
    var table = Table.Started();
    table.OpenDailyDouble();

    var html = Render.Tv(table);
    Check.True(Render.Has(html, "DAILY DOUBLE!"), "announces the Daily Double");
    Check.True(!Render.Has(html, "clue-text"), "no clue text before the wager");

    table.Game.SubmitDailyDoubleWager(table.TokenOf(table.S.ControlPlayer!.Id), 200);
    html = Render.Tv(table);
    Check.True(Render.Has(html, "clue-text"), "clue text appears after the wager");
    Check.True(Render.Has(html, "Daily Double, $200"), "the wager is shown");
    Check.True(!Render.Has(html, "clue-answer"), "still no answer");
});

Run("TV: Final Jeopardy keeps wagers, answers, and the solution secret until the right moment", () =>
{
    var table = Table.Started(seed: 7);
    table.PlayRound(i => i % 3);
    table.PlayRound(i => i % 3);
    var final = table.S;

    var html = Render.Tv(table);
    Check.True(Render.Has(html, "FINAL JEOPARDY") && Render.Shows(html, final.FinalCategory!), "category shows during wagers");
    Check.True(!Render.Has(html, "clue-text"), "clue is hidden while wagering");

    for (var i = 0; i < 3; i++) table.Game.SubmitFinalWager(table.Tokens[i], 1);
    var clueText = table.S.FinalText!;
    html = Render.Tv(table);
    Check.True(Render.Shows(html, clueText), "clue shows once wagers are in");
    Check.True(!Render.Has(html, "clue-answer"), "answer hidden while players write");

    for (var i = 0; i < 3; i++) table.Game.SubmitFinalAnswer(table.Tokens[i], "secret guess " + i);
    html = Render.Tv(table);
    Check.True(Render.Has(html, "clue-answer"), "solution shows while the emcee judges");
    Check.True(!Render.Has(html, "secret guess"), "typed answers stay hidden until judged");

    table.Game.JudgeFinal(table.Emcee, table.Ids[0], true);
    html = Render.Tv(table);
    Check.True(Render.Has(html, "secret guess 0"), "a judged answer is revealed");
    Check.True(!Render.Has(html, "secret guess 1"), "an unjudged answer is still hidden");

    table.Game.JudgeFinal(table.Emcee, table.Ids[1], true);
    table.Game.JudgeFinal(table.Emcee, table.Ids[2], true);
    Check.True(Render.Has(Render.Tv(table), "wins!"), "the winner is announced");
});

Console.WriteLine("The emcee's phone");

Run("emcee: the pick board has 30 buttons and played clues are disabled", () =>
{
    var table = Table.Started();
    var html = Render.Emcee(table);
    Check.Equal(30, Render.Count(html, "class=\"pick-cell\""), "buttons");
    Check.Equal(0, System.Text.RegularExpressions.Regex.Matches(html, @"class=""pick-cell""[^>]*\bdisabled").Count, "disabled at the start");

    table.OpenNormalClue();
    table.Game.GiveUp(table.Emcee);
    table.Game.Continue(table.Emcee);
    html = Render.Emcee(table);
    Check.True(System.Text.RegularExpressions.Regex.Matches(html, @"class=""pick-cell""[^>]*\bdisabled").Count >= 1, "a played clue is disabled");
});

Run("emcee: sees the clue AND the correct answer, with judging buttons that show the stakes", () =>
{
    var table = Table.Started();
    table.OpenNormalClue();
    var clue = table.S.Clue!;

    var html = Render.Emcee(table);
    Check.True(Render.Shows(html, clue.Text), "the clue is shown to the emcee");
    Check.True(Render.Has(html, "emcee-clue-answer") && Render.Shows(html, clue.Answer), "the answer is shown to the emcee");
    Check.True(Render.Has(html, "reveal the answer"), "give-up button is available while the clue is open");

    table.Game.Buzz(table.Tokens[2]);
    html = Render.Emcee(table);
    Check.True(Render.Has(html, "Cy"), "says who buzzed");
    Check.True(Render.Has(html, "Correct") && Render.Has(html, "Wrong"), "yes/no buttons");
    // Shows() also matches the HTML-escaped form: Blazor writes "+" as &#x2B;, which browsers display as "+".
    Check.True(Render.Shows(html, "+" + Money.Format(clue.Value)), "shows what a right answer is worth");

    table.Game.Judge(table.Emcee, true);
    Check.True(Render.Has(Render.Emcee(table), "Continue"), "a Continue button after the ruling");
});

Run("emcee: nothing about a Daily Double clue leaks before the wager", () =>
{
    var table = Table.Started();
    table.OpenDailyDouble();
    var html = Render.Emcee(table);
    Check.True(Render.Has(html, "DAILY DOUBLE!") && Render.Has(html, "to make a wager"), "waiting message");
    Check.True(!Render.Has(html, "emcee-clue-text") && !Render.Has(html, "emcee-clue-answer"), "no clue or answer yet");

    table.Game.SubmitDailyDoubleWager(table.TokenOf(table.S.ControlPlayer!.Id), 100);
    html = Render.Emcee(table);
    Check.True(Render.Has(html, "emcee-clue-answer"), "the emcee sees the answer once the wager is in");
    Check.True(Render.Has(html, "−$100") || Render.Has(html, "-$100"), "stakes reflect the wager");
});

Run("emcee: Final Jeopardy judging lists each finalist's wager and typed answer", () =>
{
    var table = Table.Started(seed: 7);
    table.PlayRound(i => i % 3);
    table.PlayRound(i => i % 3);
    for (var i = 0; i < 3; i++) table.Game.SubmitFinalWager(table.Tokens[i], 5);
    for (var i = 0; i < 3; i++) table.Game.SubmitFinalAnswer(table.Tokens[i], "typed by " + i);

    var html = Render.Emcee(table);
    Check.True(Render.Has(html, "typed by 0") && Render.Has(html, "typed by 1") && Render.Has(html, "typed by 2"), "all typed answers");
    Check.True(Render.Has(html, "emcee-clue-answer"), "the solution is shown for judging");
    Check.Equal(3, Render.Count(html, "class=\"btn btn-yes\""), "one Correct button per finalist");
});

Console.WriteLine("The players' phones");

Run("player: the buzzer is there, but the clue and answer are never on a phone", () =>
{
    var table = Table.Started();
    table.OpenNormalClue();
    var clue = table.S.Clue!;

    for (var i = 0; i < 3; i++)
    {
        var html = Render.Player(table, i);
        Check.True(Render.Has(html, "class=\"buzzer\"") && Render.Has(html, "BUZZ"), $"player {i} has a buzzer");
        Check.True(!Render.Has(html, "clue-text") && !Render.Has(html, "clue-answer") && !Render.Has(html, "emcee-clue"), $"player {i} must not see clue or answer");
        Check.True(!Render.Shows(html, clue.Text), $"player {i} must not see the clue text anywhere");
    }
});

Run("player: someone who missed is locked out while the others can still buzz", () =>
{
    var table = Table.Started();
    table.OpenNormalClue();
    table.Game.Buzz(table.Tokens[0]);
    table.Game.Judge(table.Emcee, false);

    var missed = Render.Player(table, 0);
    Check.True(Render.Has(missed, "MISSED") && !Render.Has(missed, ">BUZZ<"), "the player who missed sees a dead buzzer");
    Check.True(Render.ButtonDisabled(missed, "MISSED"), "the dead buzzer is disabled");
    var other = Render.Player(table, 1);
    Check.True(Render.Has(other, ">BUZZ<") && !Render.Has(other, "MISSED"), "everyone else still has a live buzzer");
});

Run("player: the person in control is told so, the others are told who is picking", () =>
{
    var table = Table.Started();
    var control = Array.IndexOf(table.Ids, table.S.ControlPlayer!.Id);
    for (var i = 0; i < 3; i++)
    {
        var html = Render.Player(table, i);
        if (i == control) Check.True(Render.Has(html, "in control!"), $"player {i} should be told they are in control");
        else Check.True(Render.Has(html, "is picking the next clue"), $"player {i} should see who is picking");
    }
});

Run("player: only the Daily Double player gets a wager box", () =>
{
    var table = Table.Started();
    table.OpenDailyDouble();
    var control = Array.IndexOf(table.Ids, table.S.ControlPlayer!.Id);
    for (var i = 0; i < 3; i++)
    {
        var html = Render.Player(table, i);
        Check.True(Render.Has(html, "Lock in wager") == (i == control), $"wager box for player {i}");
        Check.True(!Render.Has(html, "clue-text"), $"no clue on player {i}'s phone");
    }
});

Run("player: Final Jeopardy forms for finalists, and a polite note for everyone else", () =>
{
    var table = Table.Started();
    table.PlayRound(_ => 0);   // only Ann ends up with money
    table.PlayRound(_ => 0);

    Check.True(Render.Has(Render.Player(table, 0), "Lock in wager"), "the finalist can wager");
    Check.True(Render.Has(Render.Player(table, 1), "qualify") && !Render.Has(Render.Player(table, 1), "Lock in wager"), "a non-finalist is told they didn't qualify");

    table.Game.SubmitFinalWager(table.Tokens[0], 10);
    Check.True(Render.Has(Render.Player(table, 0), "Lock in answer"), "the finalist can now answer");
    table.Game.SubmitFinalAnswer(table.Tokens[0], "x");
    table.Game.JudgeFinal(table.Emcee, table.Ids[0], true);
    Check.True(Render.Has(Render.Player(table, 0), "You won!"), "the winner is congratulated");
    Check.True(!Render.Has(Render.Player(table, 1), "You won!"), "the others aren't");
});

Console.WriteLine("The big-screen address and the _error bug");

Run("room page: no literal '_error' text on any lobby screen", () =>
{
    // The bug: Error="_error" (no @) passed the *text* "_error" to the lobby, which showed it as an error message.
    var rooms = new GameRoomManager(new DummyGameDataSource());
    var game = rooms.Create();
    foreach (var board in new[] { false, true })
    {
        var html = Render.Room(rooms, game.Code, board);
        Check.True(!Render.Has(html, "_error"), $"no stray _error text (board={board})");
        Check.True(!Render.Has(html, "class=\"error\""), $"no error paragraph at all when nothing went wrong (board={board})");
    }
});

Run("room page: /board shows the QR code and no join buttons; phone address shows the join buttons", () =>
{
    var rooms = new GameRoomManager(new DummyGameDataSource());
    var game = rooms.Create();

    var tv = Render.Room(rooms, game.Code, board: true);
    Check.True(Render.Has(tv, "Scan the QR code"), "the TV tells people to scan");
    Check.True(Render.Has(tv, "board-mode"), "the TV lobby uses the big-screen layout");
    Check.True(!Render.Has(tv, "Join as Emcee") && !Render.Has(tv, "Join as Player"), "the TV never offers to take a seat");
    Check.True(!Render.Has(tv, "Start Game"), "no Start button until the table is full");

    // A phone first shows "Connecting…" while it asks the browser for its saved seat (a JS call this
    // test harness doesn't have), so all we can verify here is that it did NOT get the TV treatment.
    var phone = Render.Room(rooms, game.Code, board: false);
    Check.True(!Render.Has(phone, "board-mode") && !Render.Has(phone, "Scan the QR code"), "phones don't get the big-screen lobby");

    game.JoinEmcee("Host");
    game.JoinPlayer("Ann");
    game.JoinPlayer("Bob");
    game.JoinPlayer("Cy");
    Check.True(Render.Has(Render.Room(rooms, game.Code, board: true), "Start Game"), "the TV shows Start once the table is full");
});

Run("room page: after Start, /board becomes the board and an un-joined phone sees it too", () =>
{
    var rooms = new GameRoomManager(new DummyGameDataSource());
    var game = rooms.Create();
    game.JoinEmcee("Host");
    foreach (var n in new[] { "Ann", "Bob", "Cy" }) game.JoinPlayer(n);
    game.Start();

    var tv = Render.Room(rooms, game.Code, board: true);
    Check.Equal(6, Render.Count(tv, "board-category"), "six category headers on the TV");
    Check.True(!Render.Has(tv, "Join as Emcee"), "no join buttons on a running game");
});

Run("room page: unknown code says game not found", () =>
{
    var rooms = new GameRoomManager(new DummyGameDataSource());
    Check.True(Render.Has(Render.Room(rooms, "ZZZZ", board: true), "Game not found"), "friendly message");
});

Run("emcee picker: 30 cells inside the pick-board wrapper", () =>
{
    var html = Render.Emcee(Table.Started());
    Check.True(Render.Has(html, "class=\"pick-board\""), "wrapper present");
    Check.Equal(30, Render.Count(html, "class=\"pick-cell\""), "30 pick buttons");
    Check.Equal(6, Render.Count(html, "class=\"pick-category\""), "6 category columns");
});

Console.WriteLine("Every screen, every phase");

Run("a whole game, drawing the TV, the emcee, and all three players after every single action", () =>
{
    var table = Table.Started(seed: 11);
    var draws = 0;
    table.Game.Changed += () =>
    {
        Render.Tv(table);
        Render.Emcee(table);
        for (var i = 0; i < 3; i++) Render.Player(table, i);
        draws++;
    };

    table.PlayRound(i => i % 3);
    table.PlayRound(i => i % 3);
    for (var i = 0; i < 3; i++) table.Game.SubmitFinalWager(table.Tokens[i], i);
    for (var i = 0; i < 3; i++) table.Game.SubmitFinalAnswer(table.Tokens[i], "answer " + i);
    for (var i = 0; i < 3; i++) table.Game.JudgeFinal(table.Emcee, table.Ids[i], i != 1);

    Check.Equal(GamePhase.GameOver, table.S.Phase, "the game finished");
    Check.True(draws > 200, $"expected hundreds of redraws, got {draws}");
});

Console.WriteLine("Press Your Luck screens");

Run("pyl: only the emcee's phone sees the answer before the reveal", () =>
{
    var t = new PylTable();
    Check.True(Render.Has(Render.PylEmcee(t), "Answer1"), "emcee sees the answer");
    Check.True(!Render.Has(Render.PylTv(t), "Answer1"), "TV hides the answer");
    for (var i = 0; i < 3; i++)
        Check.True(!Render.Has(Render.PylPlayer(t, i), "Answer1"), $"player {i} hides the answer");
});

Run("pyl: phones never show the question text, the TV does", () =>
{
    var t = new PylTable();
    Check.True(Render.Has(Render.PylTv(t), "Clue number 1"), "TV shows the question");
    for (var i = 0; i < 3; i++)
        Check.True(!Render.Has(Render.PylPlayer(t, i), "Clue number 1"), $"player {i} phone hides the question");
});

Run("pyl: TV shows the answer once the reveal arrives", () =>
{
    var t = new PylTable();
    Check.Ok(t.Game.Buzz(t.Tokens[0]));
    Check.Ok(t.Game.JudgeGuess(t.Emcee, true));
    Check.Ok(t.Game.Pick(t.Tokens[1], t.S.CorrectIndex));
    Check.Ok(t.Game.Pick(t.Tokens[2], (t.S.CorrectIndex + 1) % 3));
    Check.Equal(PylPhase.Reveal, t.S.Phase, "reveal phase");
    Check.True(Render.Has(Render.PylTv(t), "Answer1"), "TV shows the answer at the reveal");
});

Run("pyl: a whole game, drawing every screen after every action", () =>
{
    var t = new PylTable(seed: 5);
    var draws = 0;
    t.Game.Changed += () =>
    {
        Render.PylTv(t);
        Render.PylEmcee(t);
        for (var i = 0; i < 3; i++) Render.PylPlayer(t, i);
        draws++;
    };
    t.PlayQuestionRound();
    // Spin and press on a cash square a few times so the TV, emcee and phones are drawn mid-spin and after.
    for (var n = 0; n < 4; n++)
    {
        if (t.S.Phase != PylPhase.SpinReady || t.S.ActingId is null) break;
        t.SpinFor(o => PylTable.IsPlainCash(o));
        if (t.S.Phase == PylPhase.SpinReady && t.S.ActingId is not null) t.Game.Pass(t.Tokens[t.IndexOf(t.S.ActingId.Value)], null);
    }
    Check.True(draws > 20, $"expected plenty of redraws, got {draws}");
});

Run("pyl: Move One Space shows the two choices to the player and the emcee", () =>
{
    var t = new PylTable();
    t.PlayQuestionRound();
    var acting = t.IndexOf(t.S.ActingId!.Value);
    var spinning = t.StartSpin();
    Check.Ok(t.PressAtTick(spinning, PylTable.FindTick(spinning, o => o.Kind == "move")));
    Check.Equal(PylPhase.PickMove, t.S.Phase, "waiting for a choice");
    var player = Render.PylPlayer(t, acting);
    Check.Equal(2, Render.Count(player, "corner-btn"), "two choice buttons on the acting player's phone");
    Check.True(Render.Has(Render.PylEmcee(t), "Move One Space!"), "the emcee can choose too");
    Check.True(!Render.Has(Render.PylPlayer(t, (acting + 1) % 3), "corner-btn"), "other players only watch");
    Render.PylTv(t);
});

Run("pyl: when the game is over only the emcee gets the play-again buttons", () =>
{
    var t = new PylTable(seed: 3);
    for (var round = 0; round < PressYourLuckGame.Rounds; round++)
    {
        t.PlayQuestionRound();
        var guard = 0;
        while (t.S.Phase == PylPhase.SpinReady && guard++ < 500) t.SpinFor(o => o.Kind == "cash" && o.Sub is null);
        Check.Ok(t.Game.ContinueFromRound(t.Emcee));
    }
    var emcee = Render.PylEmcee(t);
    Check.True(Render.Has(emcee, "Play again, same players"), "same players button");
    Check.True(Render.Has(emcee, "Play again, new players"), "new players button");
    Check.True(!Render.Has(Render.PylPlayer(t, 0), "Play again"), "players don't get them");
    Check.True(!Render.Has(Render.PylTv(t), "Play again,"), "the TV doesn't get them");
});

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
