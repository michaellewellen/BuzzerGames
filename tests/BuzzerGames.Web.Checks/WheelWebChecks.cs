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
    }
}
