using Microsoft.Extensions.DependencyInjection;
using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Password;
using BuzzerGames.Core.Price;
using BuzzerGames.Core.PressYourLuck;
using BuzzerGames.Web;
using BuzzerGames.Web.Components.Password;
using BuzzerGames.Web.Components.Price;

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
            || (me.IsGiver && s.Phase is PasswordPhase.Clue or PasswordPhase.Control);
        foreach (var w in s.Words)
        {
            var revealed = w.State is WordState.Solved or WordState.Posted;
            if (revealed || (w.Number == s.WordNumber && mayKnowWord)) continue;
            Check.True(!Render.ShowsAnyCase(phone, w.Text), $"player {i} sees \"{w.Text}\" in phase {s.Phase}, word {s.WordNumber}, giver={me.IsGiver}, puzzle {s.PuzzleNumber}, msg={s.Message}");
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

Run("pw players: both clue-givers see the word at the control choice; only the giver with control gets Play and Pass", () =>
{
    var t = new PasswordTable();
    var control = t.S.ControlTeam;
    for (var i = 0; i < 4; i++)
    {
        var html = Render.PwPlayer(t, i);
        var giver = i % 2 == 0;                          // puzzle 1: Ann and Cy give
        Check.True(Render.ShowsAnyCase(html, "Anchor") == giver, $"player {i}: the word is for the clue-givers only");
        var onControl = i / 2 == control && giver;
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
                Check.Ok(t.Game.ChooseControl(t.TokenOf(s.Players.First(p => p.Team == s.ControlTeam && p.IsGiver).Id), n % 3 != 0));
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

Run("pw live: screens that stay open while the game moves on redraw without trouble, through the bonus round too", () =>
{
    foreach (var seed in new[] { 1, 2, 3, 4 })
    {
        var t = new PasswordTable(seed: seed, source: new JsonPasswordPuzzleSource(), bonus: new TextBonusWordSource());
        var views = new List<LiveView>();
        Dictionary<string, object?> P(int who) => who switch
        {
            -2 => new() { ["Snapshot"] = t.S },
            -1 => new() { ["Game"] = t.Game, ["Snapshot"] = t.S, ["Token"] = t.Emcee },
            _ => new() { ["Game"] = t.Game, ["Snapshot"] = t.S, ["Token"] = t.Tokens[who], ["PlayerId"] = t.Ids[who] }
        };
        views.Add(new LiveView("TV", typeof(PasswordTvView), P(-2)));
        views.Add(new LiveView("emcee", typeof(PasswordEmceeView), P(-1)));
        for (var i = 0; i < 4; i++) views.Add(new LiveView($"player {i}", typeof(PasswordPlayerView), P(i)));
        var who = new[] { -2, -1, 0, 1, 2, 3 };
        var redraws = 0;
        t.Game.Changed += () =>
        {
            for (var v = 0; v < views.Count; v++) views[v].Update(P(who[v]));
            redraws++;
        };

        var n = 0; var bonusMoves = 0; var guard = 0;
        while (t.S.Phase != PasswordPhase.GameOver && guard++ < 3000)
        {
            var s = t.S;
            n++;
            switch (s.Phase)
            {
                case PasswordPhase.Control:
                    Check.Ok(t.Game.ChooseControl(t.TokenOf(s.Players.First(p => p.Team == s.ControlTeam && p.IsGiver).Id), n % 3 != 0));
                    break;
                case PasswordPhase.Clue:
                    if (n % 7 == 0) Check.Ok(t.Game.RejectClue(t.Emcee));
                    else Check.Ok(t.Game.JudgeGuess(t.Emcee, n % 3 == 0));
                    break;
                case PasswordPhase.PuzzleGuess: Check.Ok(t.Game.JudgePuzzleGuess(t.Emcee, n % 4 == 0)); break;
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
                default: Check.Ok(t.Game.Continue(t.Emcee)); break;
            }
        }
        Check.Equal(PasswordPhase.GameOver, t.S.Phase, $"seed {seed}: the game finished");
        Check.True(redraws > 50, "many redraws");
        foreach (var v in views) v.Dispose();
    }
});

Run("pw live fuzz: random play, swaps and restarts keep every open screen drawing", () =>
{
    for (var seed = 100; seed < 140; seed++)
    {
        var rnd = new Random(seed);
        var t = new PasswordTable(seed: seed, source: new JsonPasswordPuzzleSource(), bonus: new TextBonusWordSource());
        var views = new List<LiveView>();
        Dictionary<string, object?> P(int who) => who switch
        {
            -2 => new() { ["Snapshot"] = t.S },
            -1 => new() { ["Game"] = t.Game, ["Snapshot"] = t.S, ["Token"] = t.Emcee },
            _ => new() { ["Game"] = t.Game, ["Snapshot"] = t.S, ["Token"] = t.Tokens[who], ["PlayerId"] = t.Ids[who] }
        };
        var who = new[] { -2, -1, 0, 1, 2, 3 };
        views.Add(new LiveView("TV", typeof(PasswordTvView), P(-2)));
        views.Add(new LiveView("emcee", typeof(PasswordEmceeView), P(-1)));
        for (var i = 0; i < 4; i++) views.Add(new LiveView($"player {i}", typeof(PasswordPlayerView), P(i)));
        t.Game.Changed += () => { for (var v = 0; v < views.Count; v++) views[v].Update(P(who[v])); };

        var bonusMoves = 0;
        for (var step = 0; step < 4000; step++)
        {
            var s = t.S;
            if (s.Phase == PasswordPhase.GameOver)
            {
                var pick = rnd.Next(3);
                if (pick == 0) t.Game.Restart(t.Emcee, true); else if (pick == 1) t.Game.RestartSwitched(t.Emcee); else break;
                continue;
            }
            switch (s.Phase)
            {
                case PasswordPhase.Control:
                    if (rnd.Next(12) == 0) t.Game.SwapPuzzle(t.Emcee);
                    else t.Game.ChooseControl(rnd.Next(2) == 0 ? t.Emcee : t.TokenOf(s.Players.First(p => p.Team == s.ControlTeam && p.IsGiver).Id), rnd.Next(2) == 0);
                    break;
                case PasswordPhase.Clue:
                    if (rnd.Next(15) == 0) t.Game.SwapPuzzle(t.Emcee);
                    else if (rnd.Next(6) == 0) t.Game.RejectClue(t.Emcee);
                    else t.Game.JudgeGuess(t.Emcee, rnd.Next(3) == 0);
                    break;
                case PasswordPhase.PuzzleGuess: t.Game.JudgePuzzleGuess(t.Emcee, rnd.Next(3) == 0); break;
                case PasswordPhase.BonusReady:
                    if (rnd.Next(2) == 0) t.Game.BonusSwapRoles(t.Emcee);
                    t.Game.StartBonus(t.Emcee);
                    bonusMoves = 0;
                    break;
                case PasswordPhase.BonusPlay:
                    bonusMoves++;
                    if (bonusMoves > 14) { t.Advance(61); t.Game.Tick(); }
                    else switch (rnd.Next(3)) { case 0: t.Game.BonusCorrect(t.Emcee); break; case 1: t.Game.BonusPass(t.Emcee); break; default: t.Game.BonusZap(t.Emcee); break; }
                    break;
                default: t.Game.Continue(t.Emcee); break;
            }
        }
        foreach (var v in views) v.Dispose();
    }
});

Run("pw wiring: the app's own service setup hands Password Plus real puzzles and real bonus words", () =>
{
    var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddPasswordPlus().BuildServiceProvider();
    var bonus = services.GetService(typeof(IBonusWordSource)) as IBonusWordSource;
    Check.True(bonus is not null, "a bonus word source is registered");
    for (var i = 0; i < 50; i++)
    {
        var round = bonus!.CreateRound(new Random(i));
        Check.Equal(10, round.Words.Count, "ten words");
        for (var k = 0; k < 10; k++)
            Check.True(round.Words[k].Length > 0 && char.ToUpperInvariant(round.Words[k][0]) == round.LetterAt(k), $"word {k} starts with its letter");
    }
    var rooms = (PasswordRoomManager)services.GetService(typeof(PasswordRoomManager))!;
    var game = rooms.Create();
    game.JoinEmcee("Host");
    for (var i = 0; i < 4; i++) Check.Ok(game.JoinPlayer("P" + i, i / 2));
    Check.Ok(game.Start());
    Check.True(game.Snapshot().Words.Count == 5, "the real puzzle bank deals a puzzle");
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
Console.WriteLine("The Price Is Right screens");

Run("price home page: the tile is playable and points at /price-is-right", () =>
{
    var tile = BuzzerGames.Web.Games.GameCatalog.All.Single(g => g.Name == "The Price Is Right");
    Check.True(tile.IsPlayable && tile.Url == "/price-is-right", "the Price Is Right tile is live");
});

Run("price wiring: the app's own service setup deals real items and gifts", () =>
{
    var services = new ServiceCollection().AddPriceIsRight().BuildServiceProvider();
    var items = (IPriceItemSource)services.GetService(typeof(IPriceItemSource))!;
    var gifts = (IPartingGiftSource)services.GetService(typeof(IPartingGiftSource))!;
    Check.Equal(24, items.CreateItems(new Random(1), 24).Count, "24 items");
    Check.Equal(3, gifts.PickGifts(new Random(1), 3).Count(g => g.Length > 5), "3 real gifts");
    var rooms = (PriceRoomManager)services.GetService(typeof(PriceRoomManager))!;
    var game = rooms.Create();
    var emcee = game.JoinEmcee("Host").Token;
    for (var i = 0; i < 4; i++) game.JoinAudience("P" + i, null, new Avatar(0, 0, 0, 0, false));
    Check.Ok(game.Start(emcee));
    for (var i = 0; i < 4; i++) Check.Ok(game.CallDown(emcee));
    Check.Ok(game.ShowItem(emcee));
    Check.True(game.Snapshot().AnnounceText.Contains("could be yours"), "a real description is read");
});

Run("price avatar: the cartoon uses the chosen colors, glasses and pose", () =>
{
    var plain = Render.PrAvatar(new Avatar(0, 0, 0, 0, false));
    Check.True(Render.Has(plain, "pr-avatar") && Render.Has(plain, PriceAvatar.Skins[0]) && Render.Has(plain, PriceAvatar.Shirts[0]), "skin and shirt colors");
    Check.True(!Render.Has(plain, "stroke-width=\"2\" />") || !Render.Has(plain, "rgba(255,255,255,.18)"), "no glasses");
    var fancy = Render.PrAvatar(new Avatar(5, 3, 4, 7, true), "cheer");
    Check.True(Render.Has(fancy, PriceAvatar.Skins[5]) && Render.Has(fancy, PriceAvatar.HairColors[4]) && Render.Has(fancy, PriceAvatar.Shirts[7]), "the chosen colors");
    Check.True(Render.Has(fancy, "rgba(255,255,255,.18)"), "glasses");
    Check.True(Render.Count(fancy, "<line") > Render.Count(plain, "<line"), "arms go up in the cheer pose");
    for (var hair = 0; hair < Avatar.Hairs; hair++)
        Check.True(Render.Has(Render.PrAvatar(new Avatar(1, hair, 1, 1, false)), "<svg"), $"hair style {hair} draws");
});

Run("price join: anyone can join the audience, with a picker; one person may be the emcee", () =>
{
    var game = new PriceGame("TEST", new FixedPriceItemSource(), new FixedPartingGiftSource(), new Random(1));
    var html = Render.PrJoin(game.Snapshot());
    Check.True(Render.Has(html, "Join the audience") && Render.Has(html, "Take my seat"), "a join button");
    Check.True(Render.Has(html, "Randomize") && Render.Has(html, "Glasses") && Render.Count(html, "pr-swatch") >= PriceAvatar.Skins.Length, "an avatar picker");
    Check.True(Render.Has(html, "tricky"), "a box for how to say the name");
    Check.True(Render.ShowsAnyCase(html, "I'm the emcee"), "while the chair is empty there is a button for it");
    game.JoinEmcee("Host");
    html = Render.PrJoin(game.Snapshot());
    Check.True(!Render.ShowsAnyCase(html, "I'm the emcee"), "and not once someone has taken it");
});

Run("price tv: the lobby is a QR code, the room code and who has arrived", () =>
{
    var t = new PriceTable(start: false);
    var html = Render.PrTv(t);
    Check.True(Render.Has(html, "THE PRICE IS RIGHT") && Render.Has(html, "id=\"qr-TEST\""), "a title and a QR code");
    Check.True(Render.Has(html, "TEST") && Render.Has(html, "<strong>8</strong>"), "the room code and the audience count");
    Check.Equal(8, Render.Count(html, "class=\"pr-crowd-person\""), "everyone is drawn in the crowd");
});

Run("price tv: Contestants' Row has four podiums and shows who was called down", () =>
{
    var t = new PriceTable();
    var html = Render.PrTv(t);
    Check.Equal(4, Render.Count(html, "class=\"pr-podium "), "four podiums");
    Check.Equal(4, Render.Count(html, "pr-podium empty"), "all empty at first");
    Check.Ok(t.Game.CallDown(t.Emcee));
    var s = t.S;
    var name = t.NameOf(s.LastCalledId);
    html = Render.PrTv(t);
    Check.True(Render.Has(html, "pr-comedown") && Render.ShowsAnyCase(html, name + ","), $"the big banner calls {name}");
    Check.True(Render.Has(html, "come on down!"), "come on down!");
    Check.Equal(3, Render.Count(html, "pr-podium empty"), "one podium is filled");
    Check.Equal(7, Render.Count(html, "class=\"pr-crowd-person\""), "and that person has left the crowd");
    t.FillRow();
    html = Render.PrTv(t);
    Check.Equal(0, Render.Count(html, "pr-podium empty"), "a full row");
    Check.Equal(4, Render.Count(html, "pr-avatar") - Render.Count(html, "class=\"pr-crowd-person\""), "four people stand at the podiums");
});

Run("price tv and phones: the real price stays secret until the bids are in", () =>
{
    var t = new PriceTable();
    t.FillRow();
    Check.Ok(t.Game.ShowItem(t.Emcee));
    var price = PriceGame.Money(t.S.Price);                // "$100"
    var item = t.S.Item!;
    var tv = Render.PrTv(t);
    Check.True(Render.ShowsAnyCase(tv, item.Name) && Render.ShowsAnyCase(tv, item.Description), "the TV shows the item and its description");
    Check.True(Render.Has(tv, "pr-item-icon"), "with a big icon while there is no photo");
    foreach (var phase in new[] { "reveal", "bidding" })
    {
        if (phase == "bidding") { Check.Ok(t.Game.OpenBidding(t.Emcee)); Check.Ok(t.Game.BidFor(t.Emcee, 37)); }
        tv = Render.PrTv(t);
        Check.True(!Render.Has(tv, price), $"the TV must not show {price} while {phase}");
        for (var i = 0; i < 8; i++)
            Check.True(!Render.Has(Render.PrAudience(t, i), price), $"audience member {i} must not see {price} while {phase}");
        Check.True(Render.Has(Render.PrEmcee(t), price), $"the emcee does see {price} while {phase}");
    }
    t.Bids(41, 43, 47);
    Check.Equal(PricePhase.BidResult, t.S.Phase, "bids are in");
    Check.True(Render.Has(Render.PrTv(t), price) && Render.Has(Render.PrTv(t), "ACTUAL RETAIL PRICE"), "now the TV announces the price");
    Check.True(Render.Has(Render.PrAudience(t, 0), price), "and phones show it");
});

Run("price tv: bids appear on the podiums, the bidder up is lit, and winners cheer", () =>
{
    var t = new PriceTable();
    t.OpenBidding();
    var html = Render.PrTv(t);
    Check.Equal(1, Render.Count(html, "pr-podium active"), "one podium is lit");
    Check.True(Render.ShowsAnyCase(html, ", what's your bid?"), "the TV asks the bidder");
    Check.Ok(t.Game.BidFor(t.Emcee, 63));
    html = Render.PrTv(t);
    Check.True(Render.Has(html, "$63"), "their bid is on their podium");
    Check.Equal(1, Render.Count(html, "pr-podium active") , "the next podium is lit");
    t.Bids(77, 55, 90);                                      // $90 is over $100? no: Item 1 costs $100, so $90 wins
    html = Render.PrTv(t);
    Check.Equal(1, Render.Count(html, "pr-podium winner"), "the winner's podium is gold");
    Check.True(Render.Has(html, "$90") && Render.Has(html, "$77") && Render.Has(html, "$63") && Render.Has(html, "$55"), "all four bids are showing");
});

Run("price tv: an all-over round tells everyone the price is lower", () =>
{
    var t = new PriceTable();
    t.OpenBidding();
    t.Bids(300, 250, 400, 200);
    var html = Render.PrTv(t);
    Check.True(Render.Has(html, "Everyone went over") && Render.ShowsAnyCase(html, "less than $200"), "the TV says so");
    Check.True(Render.ShowsAnyCase(Render.PrAudience(t, 0), "less than"), "so does a phone");
});

Run("price tv: on stage, and the parting gifts at the end", () =>
{
    var t = new PriceTable(stageRounds: 1);
    t.PlayRound();
    Check.Ok(t.Game.Continue(t.Emcee));
    var star = t.NameOf(t.S.OnStageId);
    var html = Render.PrTv(t);
    Check.True(Render.Has(html, "PLINKO") && Render.ShowsAnyCase(html, star), "the winner is on stage, playing Plinko");
    t.FinishStage();
    html = Render.PrTv(t);
    Check.True(Render.Has(html, "THAT&#x27;S THE SHOW") || Render.Has(html, "THAT'S THE SHOW"), "the show is over");
    foreach (var gift in t.S.PartingGifts)
        Check.True(Render.ShowsAnyCase(html, gift.Name) && Render.ShowsAnyCase(html, gift.Gift), $"{gift.Name}'s parting gift is on the screen");
});

Run("price phones: the contestant whose turn it is gets a bid box; nobody else does", () =>
{
    var t = new PriceTable();
    t.OpenBidding();
    var s = t.S;
    var bidderId = s.Row[s.BidTurnPosition].AudienceId!.Value;
    for (var i = 0; i < 8; i++)
    {
        var html = Render.PrAudience(t, i);
        var mine = t.Ids[i] == bidderId;
        Check.True(Render.Has(html, "pr-bid-input") == mine, $"player {i}: bid box only for the bidder");
        Check.True(Render.Has(html, ">BID<") == mine, $"player {i}: BID button only for the bidder");
    }
    Check.True(Render.ShowsAnyCase(Render.PrAudience(t, Array.IndexOf(t.Ids, bidderId)), "Your bid!"), "the bidder is told");
});

Run("price phones: reactions and comments, which the emcee can switch off", () =>
{
    var t = new PriceTable();
    var lobbyish = new PriceTable(start: false);
    Check.True(!Render.Has(Render.PrAudience(lobbyish, 0), "pr-react-btn"), "no reaction buttons before the show");
    var html = Render.PrAudience(t, 0);
    Check.Equal(4, Render.Count(html, "class=\"pr-react-btn\""), "four reaction buttons");
    Check.True(Render.Has(html, "Higher!") && Render.Has(html, "Lower!") && Render.Has(html, "Cheer") && Render.Has(html, "Boo"), "cheer, boo, higher, lower");
    Check.True(Render.Has(html, "pr-comment-box"), "a comment box");
    Check.Ok(t.Game.SetComments(t.Emcee, false));
    Check.True(!Render.Has(Render.PrAudience(t, 0), "pr-comment-box"), "no comment box once comments are off");
    Check.True(Render.Has(Render.PrAudience(t, 0), "pr-react-btn"), "but the reactions stay");
});

Run("price tv: comments show up on the big screen and vanish when deleted or switched off", () =>
{
    var t = new PriceTable();
    t.OpenBidding();
    Check.Ok(t.Game.Comment(t.Tokens[0], "Higher, Bob, higher!"));
    var html = Render.PrTv(t);
    Check.True(Render.Has(html, "pr-comments") && Render.ShowsAnyCase(html, "Higher, Bob, higher!"), "the comment is on screen");
    Check.True(Render.Has(html, "pr-meters"), "so are the reaction meters");
    Check.Ok(t.Game.DeleteComment(t.Emcee, t.S.Comments[0].Id));
    Check.True(!Render.Has(Render.PrTv(t), "pr-comments"), "gone once deleted");
});

Run("price emcee: the right buttons for each moment, and moderation tools", () =>
{
    var lobby = new PriceTable(start: false);
    Check.True(Render.Has(Render.PrEmcee(lobby), "Start the show") && !Render.ButtonDisabled(Render.PrEmcee(lobby), "Start the show"), "start is ready with eight in the audience");
    var tiny = new PriceTable(start: false, audience: 3);
    Check.True(Render.ButtonDisabled(Render.PrEmcee(tiny), "Start the show"), "start is disabled with three");

    var t = new PriceTable();
    var html = Render.PrEmcee(t);
    Check.True(Render.Has(html, "Call someone down") && !Render.Has(html, "Bring out the item"), "calling people down");
    t.FillRow();
    html = Render.PrEmcee(t);
    Check.True(Render.ButtonDisabled(html, "Call someone down") && Render.Has(html, "Bring out the item"), "the row is full: bring out the item");
    Check.True(Render.Has(html, "call someone else"), "and the last person can be sent back");
    Check.Ok(t.Game.ShowItem(t.Emcee));
    html = Render.PrEmcee(t);
    Check.True(Render.Has(html, "Open bidding") && Render.Has(html, "Pick a different item"), "open bidding or swap the item");
    Check.Ok(t.Game.OpenBidding(t.Emcee));
    html = Render.PrEmcee(t);
    Check.True(Render.Has(html, "Enter bid for") && Render.Has(html, "pr-bid-input"), "the emcee can bid for someone");
    t.Bids(10, 20, 30, 40);
    Check.True(Render.Has(Render.PrEmcee(t), "up on stage"), "send the winner up on stage");
    Check.Ok(t.Game.Continue(t.Emcee));
    Check.True(Render.Has(Render.PrEmcee(t), "Skip this pricing game"), "then the emcee can skip the pricing game");
    t.FinishStage();
    var tools = Render.PrEmcee(t);
    Check.True(Render.Has(tools, "Say it again") && Render.Has(tools, "Stop talking") && Render.Has(tools, "turn them off"), "announcer and comment controls");
    Check.True(Render.Has(tools, "The audience (8)"), "the audience list");
    foreach (var i in new[] { 0, 1 }) Check.True(!Render.Has(Render.PrAudience(t, i), "Say it again"), "audience phones have no emcee tools");
});

Run("price room: the big screen has the announcer button; phones do not; a stranger sees the join page", () =>
{
    var rooms = new PriceRoomManager(new FixedPriceItemSource(), new FixedPartingGiftSource());
    var game = rooms.Create();
    var board = Render.PrRoom(rooms, game.Code, board: true);
    Check.True(Render.Has(board, "pr-sound-btn") && Render.ShowsAnyCase(board, "Turn on the announcer"), "the TV can switch the voice on");
    Check.True(Render.Has(board, "id=\"qr-" + game.Code + "\""), "and it shows the QR code in the lobby");
    var phone = Render.PrRoom(rooms, game.Code, board: false);
    Check.True(!Render.Has(phone, "pr-sound-btn"), "phones have no announcer button");
    Check.True(Render.Has(phone, "Connecting") || Render.Has(phone, "Join the audience"), "a phone connects, then offers the join page");
    Check.True(Render.Has(Render.PrRoom(rooms, "ZZZZ", false), "Show not found"), "an unknown code is handled");
});

Run("plinko tv: the pegboard, the payouts, and price tags without real prices until each is answered", () =>
{
    var t = new PriceTable();
    t.GoOnStage();
    var p = t.S.Plinko!;
    var html = Render.PrTv(t);
    Check.Equal(98, System.Text.RegularExpressions.Regex.Matches(html, "class=\"pr-peg\"").Count, "seven rows of 8 pegs and six rows of 7");
    foreach (var pay in new[] { "$100", "$500", "$1,000", "$0", "$10,000" }) Check.True(Render.Has(html, ">" + pay + "<"), $"the board shows {pay}");
    Check.Equal(9, System.Text.RegularExpressions.Regex.Matches(html, "class=\"pr-slot-pay\"").Count, "nine paying slots");
    foreach (var item in p.Items) Check.True(Render.ShowsAnyCase(html, item.Name) && Render.Has(html, PlinkoBoard.Cents(item.ShownCents)), $"{item.Name} and its tag");
    Check.True(!Render.Has(html, "pr-plinko-actual") && !Render.Has(html, "ACTUAL"), "no real price before any answer");
    Check.True(Render.Has(html, "Higher or lower?"), "the current item asks the question");
    Check.True(!Render.Has(html, "pr-chip aiming") && !Render.Has(html, "falling"), "no chip yet");

    var star = t.TokenOf(t.S.OnStageId!.Value);
    PlinkoChecks.Answer(t, star, right: true);
    html = Render.PrTv(t);
    Check.Equal(1, System.Text.RegularExpressions.Regex.Matches(html, "class=\"pr-plinko-actual\"").Count, "one real price, for the answered item");
    Check.True(Render.Has(html, PlinkoBoard.Cents(p.Items[0].ActualCents)) && Render.ShowsAnyCase(html, "✔"), "it is shown with a tick");
    PlinkoChecks.Answer(t, star, right: false);
    Check.True(Render.ShowsAnyCase(Render.PrTv(t), "✘"), "a wrong answer gets a cross");
    PlinkoChecks.Answer(t, star, right: true);
    PlinkoChecks.Answer(t, star, right: true);

    html = Render.PrTv(t);
    Check.True(Render.Has(html, "pr-chip aiming") && Render.Has(html, "pr-top-slot aimed"), "a chip waits at the top, over a lit slot");
    Check.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "class=\"pr-chip-icon").Count, "free chip plus three right answers");
    Check.Ok(t.Game.PlinkoAim(star, 2));
    Check.True(Render.Has(Render.PrTv(t), "pr-top-slot aimed\" transform=\"translate(168 0)"), "the lit slot follows the contestant's slider");
    Check.Ok(t.Game.PlinkoDrop(star, 2));
    html = Render.PrTv(t);
    Check.True(Render.Has(html, "pr-chip falling") && Render.Has(html, "--x0:168px") && Render.Has(html, "--x14:") && Render.Has(html, "--yend:668px"), "the chip falls along its path");
    Check.True(!Render.Has(html, " hit\"") && Render.Has(html, ">$0<"), "no slot lights up until the chip lands");
    Check.Ok(t.Game.PlinkoLand(1));
    html = Render.PrTv(t);
    Check.True(!Render.Has(html, "pr-chip falling") && Render.Has(html, "pr-chip\" transform"), "the chip comes to rest");
    Check.True(Render.Has(html, " hit\""), "and its slot lights up");
    Check.True(Render.Has(html, PriceGame.Money(t.S.Plinko!.Total)), "the winnings update");
    PlinkoChecks.DropAll(t, star, new Random(3));
    html = Render.PrTv(t);
    Check.True(Render.Has(html, "YOU WON"), "the game ends with a total");
    Check.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "pr-chip-icon used").Count, "every chip is used up");
});

Run("plinko phones: the contestant plays, everyone else watches and shouts", () =>
{
    var t = new PriceTable();
    t.GoOnStage();
    var starIndex = Array.IndexOf(t.Ids, t.S.OnStageId!.Value);
    var otherIndex = starIndex == 0 ? 1 : 0;
    var p = t.S.Plinko!;
    var mine = Render.PrAudience(t, starIndex);
    Check.True(Render.Has(mine, "HIGHER") && Render.Has(mine, "LOWER") && Render.Has(mine, PlinkoBoard.Cents(p.Items[0].ShownCents)), "the contestant gets the first tag and two buttons");
    Check.True(!Render.Has(mine, PlinkoBoard.Cents(p.Items[0].ActualCents)) || p.Items[0].ActualCents == p.Items[0].ShownCents, "and not the real price");
    var theirs = Render.PrAudience(t, otherIndex);
    Check.True(Render.Has(theirs, "is playing Plinko") && !Render.Has(theirs, "pr-hilo") && Render.Has(theirs, "Higher or lower? Tell them"), "watchers see the tag but have no buttons");
    Check.True(Render.Has(theirs, "pr-react-btn"), "watchers can still cheer and boo");

    var star = t.Tokens[starIndex];
    for (var i = 0; i < 4; i++) PlinkoChecks.Answer(t, star, right: i < 2);
    mine = Render.PrAudience(t, starIndex);
    Check.True(Render.Has(mine, "type=\"range\"") && Render.Has(mine, "DROP CHIP") && Render.Has(mine, "<strong>3</strong> chips"), "three chips and a slider");
    Check.True(!Render.Has(Render.PrAudience(t, otherIndex), "type=\"range\""), "only the contestant has the slider");
    Check.Ok(t.Game.PlinkoDrop(star, 4));
    Check.True(Render.Has(Render.PrAudience(t, starIndex), "Watch the screen") && !Render.Has(Render.PrAudience(t, starIndex), "DROP CHIP"), "no dropping while a chip falls");
    Check.Ok(t.Game.PlinkoLand(1));
    PlinkoChecks.DropAll(t, star, new Random(1));
    Check.True(Render.Has(Render.PrAudience(t, starIndex), "You won"), "the contestant sees their winnings");
});

Run("plinko emcee: the secret price, fallback buttons, and a way to skip", () =>
{
    var t = new PriceTable();
    t.GoOnStage();
    var p = t.S.Plinko!;
    var html = Render.PrEmcee(t);
    Check.True(Render.Has(html, "is playing Plinko") && Render.Has(html, "Actual price: " + PlinkoBoard.Cents(p.Items[0].ActualCents)), "the emcee sees the real price");
    Check.True(Render.Has(html, "Higher") && Render.Has(html, "Lower") && Render.Has(html, "Skip this pricing game"), "answer for them, or skip");
    Check.True(!Render.Has(html, "Next contestant"), "can't move on yet");
    for (var i = 0; i < 4; i++) Check.Ok(t.Game.PlinkoGuessFor(t.Emcee, true));
    html = Render.PrEmcee(t);
    Check.Equal(9, System.Text.RegularExpressions.Regex.Matches(html, "<button class=\"btn[^\"]*\"[^>]*>[1-9]</button>").Count, "nine slot buttons to drop for them");
    Check.Ok(t.Game.PlinkoDropFor(t.Emcee, 4));
    Check.True(Render.Has(Render.PrEmcee(t), "The chip has landed"), "the emcee can skip the wait for a chip");
    Check.Ok(t.Game.SkipPricingGame(t.Emcee));
    html = Render.PrEmcee(t);
    Check.True(Render.Has(html, "Plinko is over") && Render.Has(html, "(skipped)") && Render.Has(html, "Next contestant") && !Render.Has(html, "Skip this pricing game"), "after skipping, move on");
});

Run("plinko wiring: the real container deals grocery items", () =>
{
    var services = new ServiceCollection().AddPriceIsRight().BuildServiceProvider();
    var source = services.GetRequiredService<IPlinkoItemSource>();
    var items = source.CreateItems(new Random(1), 4);
    Check.Equal(4, items.Select(i => i.Name).Distinct().Count(), "four different items");
    Check.True(items.All(i => i.Cents is > 0 and < 2000), "small prices");
    var game = services.GetRequiredService<PriceRoomManager>().Create();
    Check.True(game.Code.Length == 4, "rooms still work");
});

Run("price live: every open screen survives a whole show, with reactions, comments, rerolls and swaps", () =>
{
    for (var seed = 1; seed <= 6; seed++)
    {
        var rnd = new Random(seed);
        var t = new PriceTable(seed, audience: 6, stageRounds: 3);
        Dictionary<string, object?> P(int who) => who switch
        {
            -2 => new() { ["Snapshot"] = t.S },
            -1 => new() { ["Game"] = t.Game, ["Snapshot"] = t.S, ["Token"] = t.Emcee },
            _ => new() { ["Game"] = t.Game, ["Snapshot"] = t.S, ["Token"] = t.Tokens[who], ["AudienceId"] = t.Ids[who] }
        };
        var views = new List<LiveView> { new("TV", typeof(PriceTvView), P(-2)), new("emcee", typeof(PriceEmceeView), P(-1)) };
        for (var i = 0; i < 6; i++) views.Add(new LiveView($"audience {i}", typeof(PriceAudienceView), P(i)));
        var who = new[] { -2, -1, 0, 1, 2, 3, 4, 5 };
        t.Game.Changed += () => { for (var v = 0; v < views.Count; v++) views[v].Update(P(who[v])); };

        for (var step = 0; step < 500 && t.S.Phase != PricePhase.ShowOver; step++)
        {
            var s = t.S;
            t.Advance(4);
            if (rnd.Next(3) == 0) t.Game.React(t.Tokens[rnd.Next(6)], (Reaction)rnd.Next(4));
            if (rnd.Next(6) == 0) t.Game.Comment(t.Tokens[rnd.Next(6)], "hello " + step);
            switch (s.Phase)
            {
                case PricePhase.Callout:
                    if (s.Row.All(r => r.AudienceId is not null)) Check.Ok(t.Game.ShowItem(t.Emcee));
                    else if (rnd.Next(5) == 0 && s.LastCalledId is not null) t.Game.Reroll(t.Emcee);
                    else Check.Ok(t.Game.CallDown(t.Emcee));
                    break;
                case PricePhase.Reveal:
                    if (rnd.Next(4) == 0) Check.Ok(t.Game.SwapItem(t.Emcee)); else Check.Ok(t.Game.OpenBidding(t.Emcee));
                    break;
                case PricePhase.Bidding:
                    var turn = s.Row[s.BidTurnPosition].AudienceId!.Value;
                    var max = s.BidCeiling > 0 ? s.BidCeiling - 1 : 900;
                    var amount = rnd.Next(1, max + 1);
                    if (rnd.Next(2) == 0) t.Game.Bid(t.TokenOf(turn), amount); else t.Game.BidFor(t.Emcee, amount);
                    break;
                case PricePhase.OnStage:
                    var p = s.Plinko!;
                    var star = t.TokenOf(s.OnStageId!.Value);
                    switch (p.Stage)
                    {
                        case PlinkoStage.Guessing: Check.Ok(t.Game.PlinkoGuess(star, rnd.Next(2) == 0)); break;
                        case PlinkoStage.Dropping:
                            t.Game.PlinkoAim(star, rnd.Next(9));
                            Check.Ok(rnd.Next(2) == 0 ? t.Game.PlinkoDrop(star, rnd.Next(9)) : t.Game.PlinkoDropFor(t.Emcee, rnd.Next(9)));
                            break;
                        case PlinkoStage.Falling: Check.Ok(t.Game.PlinkoLand(p.Falling!.Number)); break;
                        default: Check.Ok(t.Game.Continue(t.Emcee)); break;
                    }
                    break;
                default:
                    Check.Ok(t.Game.Continue(t.Emcee));
                    break;
            }
        }
        Check.Equal(PricePhase.ShowOver, t.S.Phase, $"seed {seed}: the show finished");
        foreach (var v in views) v.Dispose();
    }
});

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
