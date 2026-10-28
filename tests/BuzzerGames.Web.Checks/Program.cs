using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Password;
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

Console.WriteLine("Password Plus screens");

// Whatever the screens show, the secrets must stay secret: the TV never shows an unrevealed word or the big word, a guesser's phone
// never shows the word being played, and no player's phone ever shows the big word. Drawn after every action of a whole game below.
bool ShowsWord(string html, string word) =>
    System.Text.RegularExpressions.Regex.IsMatch(html, $"(?<![A-Za-z]){System.Text.RegularExpressions.Regex.Escape(word)}(?![A-Za-z])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

// The bonus round has its own secrets: the TV shows only the word being played (nothing before or after the clock),
// the guesser has their back turned so no phone may show a bonus word, and only the emcee's phone shows the current one.
void AssertBonusSecrets(PasswordTable t)
{
    var s = t.S;
    var tv = Render.PwTv(t);
    for (var i = 0; i < s.BonusSlots.Count; i++)
    {
        var word = s.BonusSlots[i].Word;
        var allowedOnTv = s.Phase == PasswordPhase.BonusResult || (s.Phase == PasswordPhase.BonusPlay && i == s.BonusCurrent);
        Check.True(ShowsWord(tv, word) == allowedOnTv, $"the TV {(allowedOnTv ? "should" : "must not")} show \"{word}\" in {s.Phase}");
        for (var p = 0; p < 4; p++)
            Check.True(!ShowsWord(Render.PwPlayer(t, p), word), $"player {p}'s phone shows the bonus word \"{word}\"");
        var emcee = Render.PwEmcee(t);
        Check.True(ShowsWord(emcee, word) == (s.Phase == PasswordPhase.BonusPlay && i == s.BonusCurrent), $"the emcee should see only the current word (\"{word}\" in {s.Phase})");
    }
}

void AssertPasswordSecrets(PasswordTable t)
{
    var s = t.S;
    if (s.Phase is PasswordPhase.BonusReady or PasswordPhase.BonusPlay or PasswordPhase.BonusResult) { AssertBonusSecrets(t); return; }
    if (s.Phase is PasswordPhase.Lobby or PasswordPhase.GameOver) return;

    var tv = Render.PwTv(t);
    foreach (var w in s.Words)
        if (w.State is not (WordState.Solved or WordState.Posted))
            Check.True(!Render.ShowsAnyCase(tv, w.Text), $"the TV shows an unrevealed word ({w.Text}) in phase {s.Phase}");
    if (!s.BigWordRevealed)
        Check.True(!Render.ShowsAnyCase(tv, s.BigWord), $"the TV shows the big word early in phase {s.Phase}");
    Check.True(!Render.Has(tv, "pw-secret") && !Render.Has(tv, "emcee-clue"), $"the TV drew a secret panel in phase {s.Phase}");
    foreach (var extra in new[] { "tv-top", "pw-status", "pw-clue", "Room" })
        Check.True(!Render.Has(tv, extra), $"the TV board is only the board, but it shows \"{extra}\" in phase {s.Phase}");
    Check.True(Render.Has(tv, "pw-scores") && Render.Has(tv, "Team A") && Render.Has(tv, "Team B"), $"the TV board shows both team scores in phase {s.Phase}");

    var current = s.Words[s.WordNumber - 1];
    for (var i = 0; i < 4; i++)
    {
        var phone = Render.PwPlayer(t, i);
        var me = s.FindPlayer(t.Ids[i])!;
        Check.True(!Render.ShowsAnyCase(phone, "Mystery"), $"player {i}'s phone shows the big word in phase {s.Phase}");

        var mayKnowWord = current.State is WordState.Solved or WordState.Posted
            || (me.IsGiver && s.Phase == PasswordPhase.Clue);
        foreach (var w in s.Words)
        {
            var revealed = w.State is WordState.Solved or WordState.Posted;
            if (revealed || (w.Number == s.WordNumber && mayKnowWord)) continue;
            Check.True(!Render.ShowsAnyCase(phone, w.Text), $"player {i} sees \"{w.Text}\" in phase {s.Phase}");
        }
    }
}

Run("pw lobby: Emcee, Team A and Team B buttons; Start only once the table is full; full teams switch off", () =>
{
    var game = new PasswordGame("TEST", new FixedPasswordPuzzleSource(), new Random(1));
    var html = Render.PwLobby(game, new Seat(Role.Spectator));
    Check.True(Render.Has(html, "Join as Emcee") && Render.Has(html, "Join Team A") && Render.Has(html, "Join Team B"), "all three join buttons are shown");
    Check.True(!Render.Has(html, "Start Game"), "Start must be hidden while the table is empty");
    Check.True(Render.Has(html, "id=\"qr-TEST\""), "a place for the QR code");

    var emceeToken = game.JoinEmcee("Host").Token;
    html = Render.PwLobby(game, new Seat(Role.Spectator));
    Check.True(Render.ButtonDisabled(html, "Join as Emcee"), "the emcee button turns off once the seat is taken");
    Check.True(!Render.ButtonDisabled(html, "Join Team A") && !Render.ButtonDisabled(html, "Join Team B"), "both team buttons are still on");
    Check.True(Render.Has(html, "Host"), "the emcee's name is on the roster");

    game.JoinPlayer("Ann", 0);
    game.JoinPlayer("Bob", 0);
    html = Render.PwLobby(game, new Seat(Role.Spectator));
    Check.True(Render.ButtonDisabled(html, "Join Team A"), "Team A switches off when it has two players");
    Check.True(!Render.ButtonDisabled(html, "Join Team B"), "Team B is still open");
    Check.True(Render.Has(html, "Ann") && Render.Has(html, "Bob"), "Team A's players are on the roster");

    game.JoinPlayer("Cy", 1);
    Check.True(!Render.Has(Render.PwLobby(game, new Seat(Role.Spectator)), "Start Game"), "still no Start with three players");
    game.JoinPlayer("Dee", 1);
    html = Render.PwLobby(game, new Seat(Role.Spectator));
    Check.True(Render.Has(html, "Start Game"), "Start appears once the table is full");
    Check.True(Render.ButtonDisabled(html, "Join Team B"), "Team B switches off too");

    html = Render.PwLobby(game, game.GetSeat(emceeToken));
    Check.True(Render.Has(html, "joined as") && Render.Has(html, "Host"), "a device that already joined is told so");
});

Run("pw room page: no stray '_error', the TV address shows the QR code and no join buttons, an unknown code is not found", () =>
{
    var rooms = new PasswordRoomManager(new FixedPasswordPuzzleSource());
    var game = rooms.Create();
    foreach (var board in new[] { false, true })
    {
        var html = Render.PwRoom(rooms, game.Code, board);
        Check.True(!Render.Has(html, "_error"), $"no stray _error text (board={board})");
        Check.True(!Render.Has(html, "class=\"error\""), $"no error paragraph when nothing went wrong (board={board})");
    }

    var tv = Render.PwRoom(rooms, game.Code, board: true);
    Check.True(Render.Has(tv, "Scan the QR code") && Render.Has(tv, "board-mode"), "the TV lobby tells people to scan");
    Check.True(!Render.Has(tv, "Join as Emcee") && !Render.Has(tv, "Join Team A"), "the TV never offers to take a seat");

    var phone = Render.PwRoom(rooms, game.Code, board: false);
    Check.True(!Render.Has(phone, "board-mode") && !Render.Has(phone, "Scan the QR code"), "phones don't get the big-screen lobby");

    game.JoinEmcee("Host");
    game.JoinPlayer("Ann", 0); game.JoinPlayer("Bob", 0); game.JoinPlayer("Cy", 1); game.JoinPlayer("Dee", 1);
    Check.True(Render.Has(Render.PwRoom(rooms, game.Code, board: true), "Start Game"), "the TV shows Start once the table is full");
    game.Start();
    tv = Render.PwRoom(rooms, game.Code, board: true);
    Check.Equal(5, Render.Count(tv, "class=\"pw-word "), "the board shows five word slots once the game starts");
    Check.True(!Render.Has(tv, "Join as Emcee"), "no join buttons on a running game");

    Check.True(Render.Has(Render.PwRoom(rooms, "ZZZZ", board: true), "Game not found"), "unknown code says game not found");
});

Run("pw tv: only the board; never the password or the big word until the game reveals them", () =>
{
    var t = new PasswordTable();
    var html = Render.PwTv(t);
    string Board(string h) => h[h.IndexOf("pw-stage", StringComparison.Ordinal)..h.IndexOf("scores pw-scores", StringComparison.Ordinal)];
    var board = Board(html);
    Check.True(Render.Has(html, "pw-sign") && Render.Has(html, "Password") && Render.Has(html, "PUZZLE"), "the Password PUZZLE sign is up");
    Check.True(!Render.Has(html, "class=\"pw-bigword revealed\""), "the big word is not revealed");
    Check.Equal(5, Render.Count(html, "class=\"pw-word "), "five word slots");
    Check.Equal(0, Render.Count(html, "<span class=\"pw-word-text\">A") + Render.Count(html, "<span class=\"pw-word-text\">B"), "and no words in them yet");
    foreach (var extra in new[] { "tv-top", "pw-status", "pw-clue", "play or pass", "coin flip", "A THING" })
        Check.True(!Render.Has(html, extra), $"the TV is only the board, but it shows \"{extra}\"");
    foreach (var w in FixedPasswordPuzzleSource.Words)
        Check.True(!Render.ShowsAnyCase(html, w), $"{w} must not be on the TV at the start");
    Check.True(!Render.ShowsAnyCase(html, "Mystery1"), "the big word must not be on the TV");
    Check.True(Render.Has(html, "pw-scores") && Render.Has(html, "Team A") && Render.Has(html, "Team B"), "but both team scores are on it");

    t.Open(0);
    Check.Equal(board, Board(Render.PwTv(t)), "choosing play or pass changes nothing on the board");
    Check.Ok(t.Game.JudgeGuess(t.Emcee, false));
    Check.Ok(t.Game.RejectClue(t.Emcee));
    Check.Equal(board, Board(Render.PwTv(t)), "wrong guesses and invalid clues change nothing on the board: the game is spoken");

    Check.Ok(t.Game.JudgeGuess(t.Emcee, true));          // Team A gets it on the third clue
    html = Render.PwTv(t);
    Check.True(Render.Has(html, "pw-word solved") && Render.ShowsAnyCase(html, "Anchor"), "a guessed word goes up on the board");
    Check.Equal(1, Render.Count(html, "pw-word solved"), "just the one word");
    Check.True(!Render.ShowsAnyCase(html, "Mystery1"), "but the big word is still secret");
    Check.True(!Render.ShowsAnyCase(html, "Banjo"), "and later words are still hidden");

    Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, false));
    Check.Ok(t.Game.Continue(t.Emcee));
    html = Render.PwTv(t);
    Check.True(Render.ShowsAnyCase(html, "Anchor") && !Render.ShowsAnyCase(html, "Banjo"), "the solved word stays up; the next one is hidden");

    t.SolveWord(t.S.ControlTeam, bigWordRight: true);
    html = Render.PwTv(t);
    Check.True(Render.ShowsAnyCase(html, "Mystery1"), "the big word shows once the puzzle is solved");
    Check.True(Render.Has(html, "class=\"pw-bigword revealed\""), "the big word takes the place of the sign");
    Check.True(!Render.Has(html, "PUZZLE"), "so the Password PUZZLE sign is gone");
    foreach (var w in FixedPasswordPuzzleSource.Words)
        Check.True(Render.ShowsAnyCase(html, w), $"{w} is shown when the puzzle ends");
});

Run("pw tv: a word nobody guessed is posted on the board, and the big word stays secret", () =>
{
    var t = new PasswordTable();
    t.Open(0);
    t.MissAllClues();
    var html = Render.PwTv(t);
    Check.True(Render.Has(html, "pw-word posted") && Render.ShowsAnyCase(html, "Anchor"), "the missed word is up, marked as posted");
    Check.True(!Render.ShowsAnyCase(html, "Mystery1"), "the big word is still secret");
    Check.True(!Render.Has(html, "goes up on the board") && !Render.Has(html, "pw-status"), "and the TV says nothing else");
});

Console.WriteLine("The Password Plus phones");

Run("pw players: nobody sees the word at the control choice; only the team with control gets Play and Pass", () =>
{
    var t = new PasswordTable();
    var control = t.S.ControlTeam;
    for (var i = 0; i < 4; i++)
    {
        var html = Render.PwPlayer(t, i);
        Check.True(!Render.ShowsAnyCase(html, "Anchor") && !Render.Has(html, "pw-secret"), $"player {i} must not see the word yet");
        var onControl = i / 2 == control;
        Check.True(Render.Has(html, "PLAY") == onControl, $"Play button for player {i}");
        Check.True(Render.Has(html, "PASS") == onControl, $"Pass button for player {i}");
    }
});

Run("pw players: both clue-givers see the word, the guessers never do, and nobody has anything to type", () =>
{
    var t = new PasswordTable();
    t.Open(0);                                           // Team A clues: Ann (0) gives, Bob (1) guesses; Cy (2) gives, Dee (3) guesses for Team B
    foreach (var giver in new[] { 0, 2 })
    {
        var html = Render.PwPlayer(t, giver);
        Check.True(Render.Has(html, "pw-secret-word") && Render.ShowsAnyCase(html, "Anchor"), $"giver {giver} sees the word");
    }
    foreach (var guesser in new[] { 1, 3 })
    {
        var html = Render.PwPlayer(t, guesser);
        Check.True(!Render.ShowsAnyCase(html, "Anchor") && !Render.Has(html, "pw-secret"), $"guesser {guesser} must not see the word");
    }
    for (var i = 0; i < 4; i++)
    {
        var html = Render.PwPlayer(t, i);
        Check.True(!Render.Has(html, "<input") && !Render.Has(html, "Send clue"), $"player {i} has no clue box: it is spoken");
        Check.True(!Render.Has(html, "pw-clue"), $"player {i} sees no clue list");
    }
    Check.True(Render.Has(Render.PwPlayer(t, 0), "Your clue!") && Render.Has(Render.PwPlayer(t, 0), "ONE word out loud"), "the giver on the clock is told to speak");
    Check.True(Render.Has(Render.PwPlayer(t, 2), "Keep the word to yourself"), "the other team's giver is reminded to stay quiet");
    Check.True(Render.Has(Render.PwPlayer(t, 1), "Your guess!"), "Bob (Team A's guesser) is up");
    Check.True(!Render.Has(Render.PwPlayer(t, 3), "Your guess!"), "Dee isn't");
});

Run("pw players: the turn follows the emcee's rulings, and the roles hold for the puzzle", () =>
{
    var t = new PasswordTable();
    t.Open(0);
    Check.Ok(t.Game.JudgeGuess(t.Emcee, false));         // Team A misses: Team B is up
    Check.True(Render.Has(Render.PwPlayer(t, 3), "Your guess!") && !Render.Has(Render.PwPlayer(t, 1), "Your guess!"), "now Dee guesses and Bob doesn't");
    Check.True(Render.Has(Render.PwPlayer(t, 2), "Your clue!") && !Render.Has(Render.PwPlayer(t, 0), "Your clue!"), "and Cy gives the clue");
    Check.True(Render.ShowsAnyCase(Render.PwPlayer(t, 0), "Anchor") && Render.ShowsAnyCase(Render.PwPlayer(t, 2), "Anchor"), "both givers still see the word");
    Check.True(!Render.ShowsAnyCase(Render.PwPlayer(t, 1), "Anchor") && !Render.ShowsAnyCase(Render.PwPlayer(t, 3), "Anchor"), "the guessers still don't");
});

Run("pw players: only the player whose turn it is gets the shot at the big word, and no phone shows the big word", () =>
{
    var t = new PasswordTable();
    t.PlayToWordFive(0);
    t.Open(0);
    if (t.S.TurnTeam != 0) t.ClueAndJudge(false);        // Team B clues first and misses
    t.ClueAndJudge(true);                                // word 5: Bob, then Ann, then Dee, then Cy
    var order = new[] { 1, 0, 3, 2 };
    for (var attempt = 0; attempt < 4; attempt++)
    {
        for (var i = 0; i < 4; i++)
        {
            var html = Render.PwPlayer(t, i);
            Check.True(Render.Has(html, "Your shot at the big word!") == (i == order[attempt]), $"attempt {attempt + 1}, player {i}");
            Check.True(!Render.ShowsAnyCase(html, "Mystery"), "no phone shows the big word");
        }
        var tv = Render.PwTv(t);
        Check.True(!Render.ShowsAnyCase(tv, "Mystery1"), "the TV doesn't show the big word either");
        if (attempt < 3) Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, false));
    }
});

Console.WriteLine("The Password Plus emcee");

Run("pw emcee: sees the password, the big word and the other answers, with the right buttons for each moment", () =>
{
    var t = new PasswordTable();
    var html = Render.PwEmcee(t);
    Check.True(Render.Has(html, "emcee-clue-answer") && Render.ShowsAnyCase(html, "Mystery1"), "the big word is shown to the emcee");
    Check.True(Render.ShowsAnyCase(html, "also accept: Alt1"), "so are the other answers to accept");
    Check.True(Render.ShowsAnyCase(html, "Anchor") && Render.ShowsAnyCase(html, "Engine"), "and all five words");
    Check.True(Render.Has(html, "Play") && Render.Has(html, "Pass"), "the emcee can choose play or pass for a team");

    Check.True(Render.Has(Render.PwEmcee(t), "Swap this puzzle") && !Render.Has(Render.PwEmcee(t), "Yes, swap it"), "a swap button, not yet armed");
    for (var i = 0; i < 4; i++) Check.True(!Render.Has(Render.PwPlayer(t, i), "Swap this puzzle"), $"player {i} has no swap button");
    Check.True(!Render.Has(Render.PwTv(t), "Swap this puzzle"), "the TV has no swap button");

    t.Open(0);
    html = Render.PwEmcee(t);
    Check.True(!Render.Has(html, "<input") && !Render.Has(html, "Send clue"), "nothing to type: it is spoken");
    Check.True(Render.Has(html, "bonus-right") && Render.Has(html, "bonus-wrong") && Render.Has(html, "bonus-pass"), "the green check, the red X and the yellow button");
    Check.True(Render.Has(html, "✓") && Render.Has(html, "✗") && Render.Has(html, "Bad clue"), "right, wrong and invalid clue");
    Check.True(Render.ShowsAnyCase(html, "Ann") && Render.ShowsAnyCase(html, "Bob"), "says who is giving and who is guessing");
    Check.True(Render.Has(html, "clue 1 of 4"), "and which clue it is");

    Check.Ok(t.Game.JudgeGuess(t.Emcee, true));
    html = Render.PwEmcee(t);
    Check.True(Render.Has(html, "100 for Team A"), "shows what the big word is worth");
    Check.True(Render.Has(html, "Correct") && Render.Has(html, "Wrong"), "buttons to rule on the big word");

    Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, false));
    Check.True(Render.Has(Render.PwEmcee(t), "Next word"), "a Next word button after a missed big word");
    Check.True(Render.Has(Render.PwEmcee(t), "keeps control"), "and it says the team keeps control");

    Check.Ok(t.Game.Continue(t.Emcee));
    t.SolveWord(t.S.ControlTeam, bigWordRight: true);
    Check.True(Render.Has(Render.PwEmcee(t), "Next puzzle"), "a Next puzzle button after a solved puzzle");
});

Run("pw emcee: a win leads to the bonus round, and the end of the game offers the play-again buttons, only to the emcee", () =>
{
    var t = new PasswordTable();
    var guard = 0;
    while (t.S.Phase != PasswordPhase.BonusReady && guard++ < 20)
    {
        t.SolveWord(0, bigWordRight: true);
        if (t.S.Teams[0].Score >= PasswordGame.WinScore) Check.True(Render.Has(Render.PwEmcee(t), "On to the bonus round"), "the last result says on to the bonus round");
        Check.Ok(t.Game.Continue(t.Emcee));
    }
    Check.True(Render.Has(Render.PwEmcee(t), "bonus round") && Render.Has(Render.PwEmcee(t), "Start the 60-second clock"), "the bonus round is announced");
    t.PlayBonusOut();

    var emcee = Render.PwEmcee(t);
    Check.True(Render.Has(emcee, "Play again, same players") && Render.Has(emcee, "Play again, switch partners") && Render.Has(emcee, "Play again, new players"), "all three play-again buttons");
    Check.True(Render.Has(emcee, "bonus round"), "the bonus round is in the ending");
    for (var i = 0; i < 4; i++) Check.True(!Render.Has(Render.PwPlayer(t, i), "Play again"), $"player {i} doesn't get them");
    Check.True(!Render.Has(Render.PwTv(t), "Play again,"), "the TV doesn't get them");

    Check.True(Render.Has(Render.PwPlayer(t, 0), "Your team won") && Render.Has(Render.PwPlayer(t, 1), "Your team won"), "the winning team is congratulated");
    Check.True(Render.Has(Render.PwPlayer(t, 2), "Game over") && !Render.Has(Render.PwPlayer(t, 3), "Your team won"), "the other team is not");
    Check.True(Render.Has(Render.PwTv(t), "GAME OVER") && Render.Has(Render.PwTv(t), "bonus round"), "the TV shows the winner");
});


Console.WriteLine("Every Password Plus screen, every phase");

Run("pw: a whole game, drawing the TV, the emcee and all four phones after every single action, and the secrets never leak", () =>
{
    var t = new PasswordTable(seed: 3);
    var draws = 0;
    t.Game.Changed += () =>
    {
        AssertPasswordSecrets(t);
        Render.PwEmcee(t);
        draws++;
    };

    var n = 0;
    var bonusMoves = 0;
    var guard = 0;
    var phases = new HashSet<PasswordPhase>();
    while (t.S.Phase != PasswordPhase.GameOver && guard++ < 3000)
    {
        var s = t.S;
        phases.Add(s.Phase);
        n++;
        switch (s.Phase)
        {
            case PasswordPhase.Control:
                Check.Ok(t.Game.ChooseControl(t.TokenOf(s.Players.First(p => p.Team == s.ControlTeam).Id), n % 3 != 0));
                break;
            case PasswordPhase.Clue:
                if (n % 7 == 0) Check.Ok(t.Game.RejectClue(t.Emcee));
                else Check.Ok(t.Game.JudgeGuess(t.Emcee, n % 3 == 0));
                break;
            case PasswordPhase.PuzzleGuess:
                Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, n % 4 == 0));
                break;
            case PasswordPhase.BonusReady:
                if (n % 2 == 0) Check.Ok(t.Game.BonusSwapRoles(t.Emcee));
                Check.Ok(t.Game.StartBonus(t.Emcee));
                bonusMoves = 0;
                break;
            case PasswordPhase.BonusPlay:
                bonusMoves++;
                if (bonusMoves > 14) { t.Advance(61); Check.Ok(t.Game.Tick()); }
                else if (bonusMoves % 5 == 0) Check.Ok(t.Game.BonusPass(t.Emcee));
                else if (bonusMoves % 7 == 0) Check.Ok(t.Game.BonusZap(t.Emcee));
                else Check.Ok(t.Game.BonusCorrect(t.Emcee));
                break;
            default:
                Check.Ok(t.Game.Continue(t.Emcee));
                break;
        }
    }

    Check.Equal(PasswordPhase.GameOver, t.S.Phase, "the game finished");
    Check.True(draws > 60, $"expected dozens of redraws, got {draws}");
    foreach (var phase in new[] { PasswordPhase.Control, PasswordPhase.Clue, PasswordPhase.PuzzleGuess, PasswordPhase.WordResult, PasswordPhase.PuzzleResult, PasswordPhase.BonusReady, PasswordPhase.BonusPlay, PasswordPhase.BonusResult })
        Check.True(phases.Contains(phase), $"the game should have passed through {phase}");

    // And once more with the real puzzle bank, so every real word draws without trouble.
    bonusMoves = 0;
    var real = new PasswordTable(seed: 9, source: new JsonPasswordPuzzleSource(), bonus: new TextBonusWordSource());
    real.Game.Changed += () => { AssertPasswordSecrets(real); Render.PwEmcee(real); for (var i = 0; i < 4; i++) Render.PwPlayer(real, i); };
    guard = 0;
    while (real.S.Phase != PasswordPhase.GameOver && guard++ < 3000)
    {
        var s = real.S;
        n++;
        switch (s.Phase)
        {
            case PasswordPhase.Control: Check.Ok(real.Game.ChooseControl(real.Emcee, true)); break;
            case PasswordPhase.Clue: Check.Ok(real.Game.JudgeGuess(real.Emcee, n % 3 == 0)); break;
            case PasswordPhase.PuzzleGuess: Check.Ok(real.Game.JudgePuzzleGuess(real.Emcee, n % 4 == 0)); break;
            case PasswordPhase.BonusReady:
                if (n % 2 == 0) Check.Ok(real.Game.BonusSwapRoles(real.Emcee));
                Check.Ok(real.Game.StartBonus(real.Emcee));
                bonusMoves = 0;
                break;
            case PasswordPhase.BonusPlay:
                bonusMoves++;
                if (bonusMoves > 14) { real.Advance(61); Check.Ok(real.Game.Tick()); }
                else if (bonusMoves % 5 == 0) Check.Ok(real.Game.BonusPass(real.Emcee));
                else if (bonusMoves % 7 == 0) Check.Ok(real.Game.BonusZap(real.Emcee));
                else Check.Ok(real.Game.BonusCorrect(real.Emcee));
                break;
            default: Check.Ok(real.Game.Continue(real.Emcee)); break;
        }
    }
    Check.Equal(PasswordPhase.GameOver, real.S.Phase, "the real-bank game finished");
});

Console.WriteLine("The Password Plus bonus round");

Run("pw bonus: ready screens, the TV shows the letters and who does what but no words, phones have no words", () =>
{
    var t = new PasswordTable();
    t.PlayToBonus(0);
    var tv = Render.PwTv(t);
    Check.True(Render.Has(tv, "BONUS ROUND") && Render.Has(tv, "$10,000 FOR ALL TEN"), "the TV announces it");
    Check.Equal(10, Render.Count(tv, "class=\"bonus-letter "), "ten letters");
    foreach (var letter in "ABCDEFGHIJ") Check.True(Render.Has(tv, $">{letter}</span>"), $"the letter {letter} is on the TV");
    Check.True(Render.Has(tv, "class=\"bonus-clock ") && Render.Has(tv, ">60<"), "a clock at 60");
    Check.True(!Render.Has(tv, "data-clock-end"), "the clock isn't counting yet");
    Check.True(Render.Has(tv, "Ann") && Render.Has(tv, "face the screen") && Render.Has(tv, "Bob") && Render.Has(tv, "turn your back"), "says who faces the TV and who turns around");
    Check.True(!Render.Has(tv, "class=\"bonus-word\""), "no word yet");

    var emcee = Render.PwEmcee(t);
    Check.True(Render.Has(emcee, "Start the 60-second clock") && Render.Has(emcee, "Swap who gives the clues"), "the emcee can swap and start");
    Check.True(!Render.Has(emcee, "bonus-btn"), "no ruling buttons before the clock starts");

    Check.True(Render.Has(Render.PwPlayer(t, 0), "Face the big screen!") && Render.Has(Render.PwPlayer(t, 1), "Back to the screen!"), "Ann faces, Bob turns around");
    Check.True(Render.Has(Render.PwPlayer(t, 2), "Team A is playing the bonus round") && Render.Has(Render.PwPlayer(t, 3), "Team A is playing the bonus round"), "Team B watches");

    Check.Ok(t.Game.BonusSwapRoles(t.Emcee));
    Check.True(Render.Has(Render.PwPlayer(t, 1), "Face the big screen!") && Render.Has(Render.PwPlayer(t, 0), "Back to the screen!"), "after the swap Bob faces and Ann turns around");
});

Run("pw bonus: while playing, the TV shows the one current word and the clock; the emcee has the three buttons", () =>
{
    var t = new PasswordTable();
    t.PlayToBonus(0);
    Check.Ok(t.Game.StartBonus(t.Emcee));

    var tv = Render.PwTv(t);
    Check.True(Render.Has(tv, "class=\"bonus-word\">Aest<"), "the A word is on the TV");
    Check.True(!Render.Has(tv, "Best"), "and no other word");
    Check.True(Render.Has(tv, "data-clock-end=") && Render.Has(tv, "data-clock-ms=\"60000\""), "the clock carries its end time and the time left");
    Check.True(Render.Has(tv, "bonus-letter current"), "the current letter is lit");

    var emcee = Render.PwEmcee(t);
    Check.True(Render.Has(emcee, "Aest"), "the emcee sees the word");
    Check.True(Render.Has(emcee, "bonus-right") && Render.Has(emcee, "bonus-pass") && Render.Has(emcee, "bonus-wrong"), "the check, the yellow PASS and the red X");
    Check.True(Render.Has(emcee, "✓") && Render.Has(emcee, "✗") && Render.Has(emcee, "PASS"), "marked as a check, an X and PASS");
    for (var i = 0; i < 4; i++) Check.True(!Render.Has(Render.PwPlayer(t, i), "Aest"), $"player {i} doesn't see the word");

    Check.Ok(t.Game.BonusCorrect(t.Emcee));
    Check.Ok(t.Game.BonusPass(t.Emcee));
    Check.Ok(t.Game.BonusZap(t.Emcee));
    tv = Render.PwTv(t);
    Check.True(Render.Has(tv, "class=\"bonus-word\">Dest<") && !Render.Has(tv, "Aest") && !Render.Has(tv, "Best"), "now the D word");
    Check.True(Render.Has(tv, "bonus-letter correct") && Render.Has(tv, "bonus-letter passed") && Render.Has(tv, "bonus-letter zapped"), "A won, B passed, C zapped");
});

Run("pw bonus: the results show every word, and the ending shows the winnings", () =>
{
    var t = new PasswordTable();
    t.PlayToBonus(0);
    Check.Ok(t.Game.StartBonus(t.Emcee));
    for (var i = 0; i < 3; i++) Check.Ok(t.Game.BonusCorrect(t.Emcee));
    Check.Ok(t.Game.BonusZap(t.Emcee));
    t.Advance(61);
    Check.Ok(t.Game.Tick());

    var tv = Render.PwTv(t);
    Check.Equal(10, Render.Count(tv, "bonus-review-word"), "every word is listed");
    Check.True(Render.Has(tv, "Aest") && Render.Has(tv, "Jest"), "the words are revealed now");
    Check.True(Render.Has(tv, "TIME!") && Render.Has(tv, "$300"), "time is up and they won $300");
    Check.True(Render.Has(Render.PwEmcee(t), "Finish the game"), "the emcee can finish");
    Check.True(Render.Has(Render.PwPlayer(t, 0), "$300"), "the players see the result");

    Check.Ok(t.Game.Continue(t.Emcee));
    Check.True(Render.Has(Render.PwTv(t), "GAME OVER") && Render.Has(Render.PwTv(t), "$300 in the bonus round"), "the game over screen has the winnings");

    var perfect = new PasswordTable();
    perfect.PlayToBonus(1);
    Check.Ok(perfect.Game.StartBonus(perfect.Emcee));
    for (var i = 0; i < 10; i++) Check.Ok(perfect.Game.BonusCorrect(perfect.Emcee));
    Check.True(Render.Has(Render.PwTv(perfect), "ALL TEN!") && Render.Has(Render.PwTv(perfect), "$10,000"), "the jackpot");
});

Run("pw home page: Password Plus is listed as playable, and Super Password is gone from the picker", () =>
{
    var tile = BuzzerGames.Web.Games.GameCatalog.All.Single(g => g.Name == "Password Plus");
    Check.True(tile.IsPlayable && tile.Url == "/password-plus", "the Password Plus tile points at /password-plus");
    Check.True(BuzzerGames.Web.Games.GameCatalog.All.All(g => g.Name != "Super Password"), "no separate Super Password tile");
});

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
