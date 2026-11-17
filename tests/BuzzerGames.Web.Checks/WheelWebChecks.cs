using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Wheel;
using BuzzerGames.Web;

/// <summary>Wheel of Fortune's puzzles page: locked to strangers, and the board preview shows exactly the layout the game will use.</summary>
static class WheelWebChecks
{
    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine("Wheel of Fortune screens");

        run("puzzles page: locked without the passcode, and it never lists puzzles to a stranger", () =>
        {
            using var dir = new TempDir();
            var store = new FolderWheelContent(dir.Path);
            var gate = Render.WfPuzzles(store, new FloorAdminAccess("secret", false));
            Check.True(Render.Has(gate, "Passcode"), "asks for the passcode");
            Check.True(!Render.Has(gate, "PIECE OF CAKE") && !Render.Has(gate, "NIAGARA"), "no puzzles before unlocking");
            var locked = Render.WfPuzzles(store, new FloorAdminAccess(null, false));
            Check.True(Render.Has(locked, "FLOOR_ADMIN_PASSCODE"), "tells you how to unlock it");
            Check.True(!Render.Has(locked, "PIECE OF CAKE"), "and lists nothing");
        });

        run("board preview: an empty box is a full green board", () =>
        {
            var html = Render.WfPreview("");
            Check.Equal(56, Render.Count(html, "<span class=\"wf-tile"), "14 columns by 4 rows");
            Check.Equal(4, Render.Count(html, "wf-none"), "the four corner cells of the short rows are not tiles");
            Check.Equal(52, Render.Count(html, "wf-blank"), "52 green tiles");
            Check.Equal(0, Render.Count(html, "wf-letter"), "no letters");
            Check.True(!Render.Has(html, "class=\"error\""), "no error for a box you haven't typed in yet");
        });

        run("board preview: a puzzle shows its letters where the TV will put them, with green around them", () =>
        {
            var html = Render.WfPreview("A piece of cake");
            Check.Equal(12, Render.Count(html, "wf-letter"), "A PIECE OF CAKE is 12 letters");
            Check.Equal(40, Render.Count(html, "wf-blank"), "the rest of the 52 tiles are green");
            Check.Equal(4, Render.Count(html, "wf-none"), "corners");
            Check.True(Render.Has(html, "2 lines") && Render.Has(html, "A PIECE") && Render.Has(html, "OF CAKE"), "the wrapping is spelled out: " + html);
            foreach (var letter in "APIECOFK") Check.True(Render.Has(html, ">" + letter + "</span>"), $"{letter} is on a tile");
        });

        run("board preview: a forced line break is honored, and a puzzle that can't be shown says why", () =>
        {
            var html = Render.WfPreview("GOOD MORNING/SUNSHINE");
            Check.True(Render.Has(html, "GOOD MORNING") && Render.Has(html, "SUNSHINE") && Render.Has(html, "2 lines"), "two lines, as typed");
            Check.Equal(19, Render.Count(html, "wf-letter"), "GOOD MORNING is 11 letters and SUNSHINE is 8");

            var bad = Render.WfPreview("SUPERCALIFRAGILISTICEXPIALIDOCIOUS");
            Check.True(Render.Has(bad, "class=\"error\"") && Render.Has(bad, "34"), "the reason names the word's length");
            Check.Equal(0, Render.Count(bad, "wf-letter"), "nothing is drawn for a puzzle that can't be shown");

            var symbol = Render.WfPreview("HELLO @ WORLD");
            Check.True(Render.Has(symbol, "@"), "an unsupported character is named");
        });

        run("lobby: phones offer both seats, only the emcee gets Start, and the TV shows the room and the three colors", () =>
        {
            var game = new WheelGame("ROOM", new FixedWheelContent(), new Random(1));
            var tv = Render.WfLobby(game.Snapshot(), new Seat(Role.Spectator), true);
            Check.True(Render.Has(tv, "ROOM") && Render.Has(tv, "Red") && Render.Has(tv, "Yellow") && Render.Has(tv, "Blue"), "the TV names the room and the colors");
            Check.True(!Render.Has(tv, "Join as Emcee"), "the TV never offers a seat");

            var phone = Render.WfLobby(game.Snapshot(), new Seat(Role.Spectator), false);
            Check.True(Render.Has(phone, "Join as Emcee") && Render.Has(phone, "Join as Player"), "a phone offers both");

            var emcee = game.JoinEmcee("Host").Token;
            foreach (var n in new[] { "Ann", "Bob", "Cy" }) game.JoinPlayer(n);
            var asEmcee = Render.WfLobby(game.Snapshot(), game.GetSeat(emcee), false);
            Check.True(Render.Has(asEmcee, "Start Game"), "the emcee can start once all three are in");
            var player = game.JoinPlayer("Dee");
            Check.True(!player.Ok, "a fourth player is turned away");
        });

        run("TV: the board shows only what has been found, never the answer", () =>
        {
            var t = new WheelTable(["BREAK THE ICE", "X", "X", "X", "X", "X"]);
            var html = Render.WfTv(t);
            Check.True(!Render.Has(html, "BREAK THE ICE") && !Render.Has(html, "BREAK"), "the answer is nowhere on the TV");
            Check.Equal(11, Render.Count(html, "wf-t wf-letter"), "BREAK THE ICE has 11 letter tiles, all still blank");
            Check.Equal(0, Render.Count(html, "wf-show"), "no letters showing");
            Check.True(Render.Has(html, t.S.Category), "the category is shown");
            Check.Equal(3, Render.Count(html, "wf-score wf-"), "three score panels");

            t.Call('K');
            var after = Render.WfTv(t);
            Check.Equal(1, Render.Count(after, "wf-show"), "after the reveal there is one K tile showing");
            Check.True(Render.Has(after, "wf-show\">K</span>"), "and it is a K");
            Check.True(!Render.Has(after, "BREAK"), "still no answer text");
        });

        run("TV: lit tiles are blue and say nothing until the game reveals them", () =>
        {
            var t = new WheelTable(["BETTER LATE THAN NEVER", "X", "X", "X", "X", "X"]);
            t.SpinToCash();
            Check.Ok(t.Game.StageLetter(t.Emcee, 'T'));
            Check.Ok(t.Game.ConfirmLetter(t.Emcee));
            Check.Equal(WheelPhase.Reveal, t.S.Phase, "revealing");
            var html = Render.WfTv(t);
            Check.Equal(4, Render.Count(html, "wf-lit"), "the four T's are lit");
            Check.Equal(0, Render.Count(html, "wf-show"), "and they show no letter yet");
            Check.Equal('T', t.S.RevealLetter, "the snapshot says which letter is coming");
        });

        run("TV: scores, the active player and the Million wedge badge", () =>
        {
            var t = new WheelTable(["BETTER LATE THAN NEVER", "X", "X", "X", "X", "X"]);
            t.SpinTo(x => x.Kind == SliceKind.Million);
            var html = Render.WfTv(t);
            Check.True(Render.Has(html, "wf-score wf-red active"), "Ann (Red) is the one up");
            Check.True(Render.Has(html, "$1,000,000 wedge"), "the Million wedge badge shows for the holder");
            Check.True(Render.Has(html, "Ann") && Render.Has(html, "Bob") && Render.Has(html, "Cy"), "all three names");
        });

        run("wheel: 24 wedges drawn, three clickers, and the picked-up Million wedge becomes a plain Bankrupt", () =>
        {
            var t = new WheelTable();
            var disc = Render.WfDisc(t.S);
            Check.Equal(26, Render.Count(disc, "class=\"wf-slice "), "26 slices: 23 whole wedges plus the three-way wedge");
            Check.Equal(26, Render.Count(disc, "wf-peg"), "26 pegs");
            Check.Equal(3, Render.Count(disc, "wf-flap wf-flap-"), "three clickers");
            Check.True(Render.Has(disc, "wf-flap-red") && Render.Has(disc, "wf-flap-yellow") && Render.Has(disc, "wf-flap-blue"), "in the three colors");
            Check.True(Render.Has(disc, "$1,000,000"), "the Million slice is labeled");
            Check.Equal(1, Render.Count(disc, "wf-flap-red active"), "the first player's clicker is the active one");

            t.SpinTo(x => x.Kind == SliceKind.Million);
            var after = Render.WfDisc(t.S);
            Check.Equal(24, Render.Count(after, "class=\"wf-slice "), "24 slices once the wedge is picked up");
            Check.Equal(24, Render.Count(after, "wf-peg"), "24 pegs");
            Check.True(!Render.Has(after, "$1,000,000"), "no Million slice any more");
            Check.Equal(2, Render.Count(after, "wf-slice wf-bankrupt"), "two plain Bankrupt wedges");
        });

        run("player phone: the slider only for the player up, never for the others, and no hint where the sweet spot is", () =>
        {
            var t = new WheelTable();
            var up = Render.WfPlayer(t, 0);
            Check.True(Render.Has(up, "type=\"range\"") && Render.Has(up, "SPIN"), "the player up gets a slider and SPIN");
            Check.True(!Render.Has(up, "400") && !Render.Has(up.ToLowerInvariant(), "sweet"), "nothing gives the sweet spot away");
            var waiting = Render.WfPlayer(t, 1);
            Check.True(!Render.Has(waiting, "type=\"range\"") && !Render.Has(waiting, "SPIN"), "the others only wait");
            Check.True(Render.Has(waiting, "Ann"), "and see who is up");
        });

        run("emcee phone: sees the answer, keypad locks called letters and the wrong kind, Confirm appears only once a letter is typed", () =>
        {
            var t = new WheelTable(["BREAK THE ICE", "X", "X", "X", "X", "X"]);
            Check.True(Render.Has(Render.WfEmcee(t), "BREAK THE ICE"), "the emcee sees the answer");
            Check.True(Render.Has(Render.WfEmcee(t), "Buy a vowel") && Render.Has(Render.WfEmcee(t), "solving"), "turn buttons");
            t.SpinToCash();
            var keys = Render.WfEmcee(t);
            Check.True(!Render.Has(keys, "Confirm"), "no Confirm before a letter is typed");
            Check.True(Render.ButtonDisabled(keys, "A") && Render.ButtonDisabled(keys, "E"), "vowels are locked while calling a consonant");
            Check.True(!Render.ButtonDisabled(keys, "K"), "consonants are open");
            Check.Ok(t.Game.StageLetter(t.Emcee, 'K'));
            var staged = Render.WfEmcee(t);
            Check.True(Render.Has(staged, "Confirm K"), "Confirm K appears");
            Check.True(Render.Has(staged, "Wrong key"), "and a way to take it back");
        });

        run("emcee phone: solving, continuing, and Undo", () =>
        {
            var t = new WheelTable(["BREAK THE ICE", "X", "X", "X", "X", "X"]);
            Check.Ok(t.Game.BeginSolve(t.Emcee));
            var solving = Render.WfEmcee(t);
            Check.True(Render.Has(solving, "Correct") && Render.Has(solving, "Incorrect"), "the judging buttons");
            Check.Ok(t.Game.JudgeSolve(t.Emcee, true));
            var over = Render.WfEmcee(t);
            Check.True(Render.Has(over, "Next puzzle"), "continue to the next puzzle");
            Check.True(!Render.ButtonDisabled(over, "Undo"), "Undo is there");
        });

        run("game list: Wheel of Fortune is playable and points at /wheel-of-fortune", () =>
        {
            var tile = BuzzerGames.Web.Games.GameCatalog.All.Single(g => g.Name == "Wheel of Fortune");
            Check.True(tile.IsPlayable && tile.Url == "/wheel-of-fortune", "the tile points at /wheel-of-fortune");
        });
    }
}