using BuzzerGames.Core.Jeopardy;
using BuzzerGames.Core.Wheel;

/// <summary>
/// A Wheel game with a stacked puzzle list and no spin jitter, so a check can ask for "a spin that lands on Bankrupt" and get exactly that.
/// Players are Ann (Red), Bob (Yellow) and Cy (Blue). The first puzzle is "BREAK THE ICE" unless told otherwise.
/// </summary>
sealed class WheelTable
{
    public WheelGame Game { get; }
    public Guid Emcee { get; }
    public List<Guid> Tokens { get; } = [];
    public FixedWheelContent Content { get; }

    public WheelTable(IEnumerable<string>? puzzles = null, int seed = 1, double jitter = 0, bool start = true,
        IEnumerable<WheelQuickSet>? quick = null, IEnumerable<WheelPuzzle>? bonus = null)
    {
        Content = new FixedWheelContent(puzzles, quick, bonus);
        Game = new WheelGame("TEST", Content, new Random(seed), jitter, quickTickMillis: 0);
        Emcee = Game.JoinEmcee("Host").Token;
        foreach (var name in new[] { "Ann", "Bob", "Cy" }) Tokens.Add(Game.JoinPlayer(name).Token);
        if (start) Check.Ok(Game.Start(Emcee));
    }

    public WheelSnapshot S => Game.Snapshot();
    public int Bank(int player) => S.Players[player].RoundBank;
    public int Total(int player) => S.Players[player].Total;

    /// <summary>The power (0 to 1000) that makes the player up land on a slice you want. Worked out from the snapshot alone, as an independent check on the engine.</summary>
    public int PowerFor(Func<WheelSlice, bool> want)
    {
        var s = S;
        var clicker = WheelRules.ClickerAngles[s.Active];
        for (var power = 0; power <= WheelRules.PowerMax; power++)
        {
            var end = s.WheelAngle + WheelPhysics.TravelFor(power);
            var under = WheelLayout.Normalize(clicker - end);
            var slice = s.Slices.First(x => under >= x.Start && under < x.End);
            if (want(slice)) return power;
        }
        throw new Exception("No spin power lands on that slice.");
    }

    /// <summary>The player up spins to land on a matching slice and the wheel finishes turning. Returns the slice.</summary>
    public WheelSlice SpinTo(Func<WheelSlice, bool> want)
    {
        var power = PowerFor(want);
        var up = S.Active;
        Check.Ok(Game.Spin(Tokens[up], power));
        var landed = S.Spin!.Landed;
        Check.Ok(Game.FinishSpin());
        return landed;
    }

    public WheelSlice SpinToCash() => SpinTo(x => x.Kind == SliceKind.Cash);

    /// <summary>Spin to cash, then call this consonant and let the reveal finish.</summary>
    public void Call(char consonant)
    {
        SpinToCash();
        Check.Ok(Game.StageLetter(Emcee, consonant));
        Check.Ok(Game.ConfirmLetter(Emcee));
        if (S.Phase == WheelPhase.Reveal) Check.Ok(Game.FinishReveal());
    }

    /// <summary>The player up solves correctly.</summary>
    public void Solve()
    {
        Check.Ok(Game.BeginSolve(Emcee));
        Check.Ok(Game.JudgeSolve(Emcee, true));
    }

    public void NextPuzzle() => Check.Ok(Game.Continue(Emcee));
}

static class WheelChecks
{
    private static readonly Random Dice = new(2026);

    public static void Run(Action<string, Action> run)
    {
        Console.WriteLine("Wheel: the wheel");

        run("wheel: 24 wedges, with Bankrupt / Million / Bankrupt as one wedge cut in three", () =>
        {
            var wheel = WheelLayout.Create(new Random(1));
            Check.Equal(26, wheel.Slices.Count, "21 cash + Bankrupt + Lose a Turn + 3 slices of the trio");
            Check.Equal(24, wheel.Slices.Select(s => s.Wedge).Distinct().Count(), "wedges");
            Check.Equal(21, wheel.Slices.Count(s => s.Kind == SliceKind.Cash), "cash slices");
            Check.Equal(3, wheel.Slices.Count(s => s.Kind == SliceKind.Bankrupt), "Bankrupt slices: one wedge plus the two beside the Million");
            Check.Equal(1, wheel.Slices.Count(s => s.Kind == SliceKind.LoseATurn), "Lose a Turn");
            Check.Equal(1, wheel.Slices.Count(s => s.Kind == SliceKind.Million), "the Million slice");
            Check.Equal(1500, wheel.Slices.Single(s => s.Kind == SliceKind.Million).Value, "the Million slice pays $1,500 a letter");

            var trio = wheel.Slices.Where(s => s.Wedge == wheel.Slices.Single(m => m.Kind == SliceKind.Million).Wedge).OrderBy(s => s.Start).ToList();
            Check.Equal(3, trio.Count, "the trio has three slices");
            Check.True(trio.Select(s => s.Kind).SequenceEqual([SliceKind.Bankrupt, SliceKind.Million, SliceKind.Bankrupt]), "Bankrupt, Million, Bankrupt in that order");
            Check.True(trio.All(s => Math.Abs(s.Width - 5) < 1e-9), "each slice is 5 degrees");

            Check.Equal(0.0, wheel.Slices[0].Start, "starts at the top");
            Check.Equal(360.0, wheel.Slices[^1].End, "ends at the top");
            for (var i = 1; i < wheel.Slices.Count; i++) Check.True(Math.Abs(wheel.Slices[i].Start - wheel.Slices[i - 1].End) < 1e-9, "slices touch with no gaps");
            Check.Equal(72, wheel.Pegs.Count, "a peg at every 5 degrees: three per wedge");
            Check.True(wheel.Pegs.Select((p, i) => Math.Abs(p - 5.0 * i) < 1e-9).All(x => x), "evenly spaced");
        });

        run("wheel: the Bankrupt is opposite the trio, Lose a Turn is a quarter turn from both, and the spot moves around (500 wheels)", () =>
        {
            var trioSpots = new HashSet<int>();
            for (var seed = 0; seed < 500; seed++)
            {
                var wheel = WheelLayout.Create(new Random(seed));
                var trio = wheel.Slices.Single(s => s.Kind == SliceKind.Million).Wedge;
                var lone = wheel.Slices.Single(s => s.Kind == SliceKind.Bankrupt && wheel.Slices.Count(x => x.Wedge == s.Wedge) == 1).Wedge;
                var lose = wheel.Slices.Single(s => s.Kind == SliceKind.LoseATurn).Wedge;
                int Apart(int a, int b) { var d = Math.Abs(a - b) % 24; return Math.Min(d, 24 - d); }
                Check.Equal(12, Apart(trio, lone), "Bankrupt is 180 degrees from the trio");
                Check.Equal(6, Apart(lose, trio), "Lose a Turn is 90 degrees from the trio");
                Check.Equal(6, Apart(lose, lone), "and 90 degrees from the Bankrupt");
                trioSpots.Add(trio);
            }
            Check.True(trioSpots.Count > 12, "the whole arrangement lands in many different places, not always the same one");
        });

        run("wheel: cash values are the agreed set (mostly under $1,500, one $3,500, one $5,000) and the shuffle keeps its rules (500 wheels)", () =>
        {
            Check.Equal(21, WheelRules.CashValues.Count, "21 cash wedges");
            Check.True(WheelRules.CashValues.Min() == 500 && WheelRules.CashValues.Max() == 5000, "from $500 to $5,000");
            Check.True(WheelRules.CashValues.Count(v => v < 1500) >= 11, "most are under $1,500");
            Check.Equal(1, WheelRules.CashValues.Count(v => v == 3500), "one $3,500");
            Check.Equal(1, WheelRules.CashValues.Count(v => v == 5000), "one $5,000");

            var sorted = WheelRules.CashValues.OrderBy(v => v).ToList();
            for (var seed = 0; seed < 500; seed++)
            {
                var wheel = WheelLayout.Create(new Random(seed));
                var wedges = wheel.Slices.GroupBy(s => s.Wedge).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
                Check.True(wedges.SelectMany(w => w).Where(s => s.Kind == SliceKind.Cash).Select(s => s.Value).OrderBy(v => v).SequenceEqual(sorted), $"seed {seed}: the cash values are the agreed set");
                for (var i = 0; i < 24; i++)
                {
                    var a = wedges[i];
                    var b = wedges[(i + 1) % 24];
                    var aCash = a.Count == 1 && a[0].Kind == SliceKind.Cash;
                    var bCash = b.Count == 1 && b[0].Kind == SliceKind.Cash;
                    Check.True(aCash || bCash, $"seed {seed}: two non-cash wedges touch at {i}");
                    if (aCash && bCash)
                    {
                        Check.True(a[0].Value != b[0].Value, $"seed {seed}: equal values touch at {i}");
                        Check.True(!(a[0].Value >= 3500 && b[0].Value >= 3500), $"seed {seed}: the two big values touch at {i}");
                    }
                }
            }
        });

        run("wheel: the slice at an angle wraps around and follows the slices", () =>
        {
            var wheel = WheelLayout.Create(new Random(3));
            foreach (var slice in wheel.Slices)
            {
                Check.Equal(slice, wheel.SliceAt(slice.Mid), "the middle of a slice is in it");
                Check.Equal(slice, wheel.SliceAt(slice.Mid + 360), "one turn later is the same");
                Check.Equal(slice, wheel.SliceAt(slice.Mid - 720), "two turns earlier is the same");
                Check.Equal(slice, wheel.SliceAt(slice.Start), "a boundary belongs to the slice that starts there");
            }
        });

        Console.WriteLine("Wheel: the spin");

        run("spin: power 0 turns the wheel half a turn, full power one and three-quarters, and the sweet spot is 40% (exactly one turn)", () =>
        {
            Check.Equal(180.0, WheelPhysics.TravelFor(0), "lightest spin");
            Check.Equal(630.0, WheelPhysics.TravelFor(1000), "hardest spin");
            Check.Equal(400, WheelPhysics.SweetSpotPower, "sweet spot on the slider");
            Check.Equal(360.0, WheelPhysics.TravelFor(WheelPhysics.SweetSpotPower), "the sweet spot is exactly one turn");
            Check.Equal(180.0, WheelPhysics.TravelFor(-50), "power is held to 0 at the bottom");
            Check.Equal(630.0, WheelPhysics.TravelFor(5000), "power is held to 1000 at the top");
            Check.True(Enumerable.Range(0, 1000).All(p => WheelPhysics.TravelFor(p + 1) > WheelPhysics.TravelFor(p)), "pushing harder always goes farther");
            Check.True(Math.Abs(WheelPhysics.DurationFor(180) - 2.5) < 1e-9 && Math.Abs(WheelPhysics.DurationFor(630) - 5.0) < 1e-9, "2.5 to 5 seconds");
        });

        run("spin: the wheel eases out (fast, then slower) and ends at exactly the full distance", () =>
        {
            Check.Equal(0.0, WheelPhysics.ProgressAt(0), "no progress at the start");
            Check.Equal(1.0, WheelPhysics.ProgressAt(1), "all of it at the end");
            var last = 0.0;
            var lastStep = double.MaxValue;
            for (var i = 1; i <= 100; i++)
            {
                var p = WheelPhysics.ProgressAt(i / 100.0);
                Check.True(p > last, "always moving forward");
                Check.True(p - last <= lastStep + 1e-12, "each moment covers less distance than the one before");
                lastStep = p - last;
                last = p;
            }
        });

        run("spin: the sweet spot lands on the slice the clicker started on (every slice, every clicker, 200 wheels)", () =>
        {
            for (var seed = 0; seed < 200; seed++)
            {
                var wheel = WheelLayout.Create(new Random(seed));
                var slice = wheel.Slices[seed % wheel.Slices.Count];
                var clicker = WheelRules.ClickerAngles[seed % 3];
                var start = clicker - slice.Mid + 360.0 * (seed % 5);
                var plan = WheelPhysics.Plan(wheel, start, clicker, WheelPhysics.SweetSpotPower, 0);
                Check.Equal(slice, plan.Landed, $"seed {seed}: the sweet spot comes back to the same slice");
            }
        });

        run("spin: near the sweet spot still lands on the same wedge; further away does not", () =>
        {
            var wheel = WheelLayout.Create(new Random(5));
            foreach (var slice in wheel.Slices)
            {
                var clicker = WheelRules.ClickerAngles[1];
                var start = clicker - slice.Mid;
                var reach = slice.Width / 2 / 0.45;                       // power units that move the wheel half a slice (one unit is 0.45 degrees)
                var inside = (int)Math.Floor(reach) - 1;
                foreach (var d in new[] { -inside, inside })
                    Check.Equal(slice, WheelPhysics.Plan(wheel, start, clicker, 400 + d, 0).Landed, $"{slice.Label}: {d:+#;-#} from the sweet spot is still the same slice");
                foreach (var d in new[] { -45, 45 })
                    Check.True(WheelPhysics.Plan(wheel, start, clicker, 400 + d, 0).Landed != slice, $"{slice.Label}: {d:+#;-#} from the sweet spot (20 degrees away) is a different slice");
            }
            // A normal wedge gives a window of about 3.3% of the slider; the Million slice only about 1.1%.
            var wide = wheel.Slices.First(s => s.Kind == SliceKind.Cash);
            var narrow = wheel.Slices.First(s => s.Kind == SliceKind.Million);
            Check.True(wide.Width / 0.45 > 30 && wide.Width / 0.45 < 35, "a cash wedge's window is about 33 slider units wide");
            Check.True(narrow.Width / 0.45 > 10 && narrow.Width / 0.45 < 12, "the Million slice's window is about 11 slider units wide");
        });

        run("spin: the wheel settles with the clicker between two pegs, in the middle of the 5-degree gap it landed in", () =>
        {
            for (var seed = 0; seed < 200; seed++)
            {
                var wheel = WheelLayout.Create(new Random(seed));
                var clicker = WheelRules.ClickerAngles[seed % 3];
                var start = Dice.NextDouble() * 720 - 360;
                var plan = WheelPhysics.Plan(wheel, start, clicker, Dice.Next(0, 1001), (Dice.NextDouble() * 2 - 1) * 1.5);
                var under = WheelLayout.Normalize(clicker - plan.SettledAngle);
                Check.Equal(plan.Landed, wheel.SliceAt(under), "the settled wheel shows the landed slice");
                var intoGap = under % WheelRules.PegDegrees;
                Check.True(Math.Abs(intoGap - WheelRules.PegDegrees / 2) < 1e-6, "dead centre between two pegs");
                Check.True(under > plan.Landed.Start && under < plan.Landed.End, "that gap is inside the landed wedge");
                Check.True(Math.Abs(plan.SettledAngle - plan.EndAngle) <= WheelRules.PegDegrees / 2 + 1e-6, "the settle is only a small nudge, never past the neighbouring peg");
            }
        });

        run("spin: a 15-degree wedge can be landed on at any of its three gaps, and the Million slice at its one", () =>
        {
            var wheel = WheelLayout.Create(new Random(8));
            var clicker = WheelRules.ClickerAngles[1];
            var gapsByWedge = new Dictionary<int, HashSet<int>>();
            for (var power = 0; power <= WheelRules.PowerMax; power++)
            {
                var plan = WheelPhysics.Plan(wheel, 0, clicker, power, 0);
                var gap = (int)Math.Floor(WheelLayout.Normalize(clicker - plan.SettledAngle) / WheelRules.PegDegrees);
                if (!gapsByWedge.TryGetValue(plan.Landed.Wedge, out var set)) gapsByWedge[plan.Landed.Wedge] = set = [];
                set.Add(gap);
            }
            foreach (var slice in wheel.Slices)
            {
                var expected = (int)Math.Round(slice.Width / WheelRules.PegDegrees);
                var reached = gapsByWedge.TryGetValue(slice.Wedge, out var got) ? got.Count(g => g * WheelRules.PegDegrees >= slice.Start - 1e-9 && (g + 1) * WheelRules.PegDegrees <= slice.End + 1e-9) : 0;
                Check.Equal(expected, reached, $"every gap of the {slice.Label} slice at {slice.Start}° can be reached");
            }
        });

        run("spin: the clicks match the pegs the wheel really passes (100 random spins, counted by brute force)", () =>
        {
            for (var trial = 0; trial < 100; trial++)
            {
                var wheel = WheelLayout.Create(new Random(trial));
                var clicker = WheelRules.ClickerAngles[trial % 3];
                var start = Dice.NextDouble() * 1000;
                var plan = WheelPhysics.Plan(wheel, start, clicker, Dice.Next(0, 1001), 0);

                var changes = 0;
                // A click is the clicker moving from one 5 degree gap between pegs into the next.
                int Gap(double a) => (int)Math.Floor(WheelLayout.Normalize(clicker - a) / WheelRules.PegDegrees);
                var previous = Gap(start);
                for (var angle = start + 0.02; angle <= plan.EndAngle; angle += 0.02)
                {
                    var now = Gap(angle);
                    if (now != previous) changes++;
                    previous = now;
                }
                Check.Equal(changes, plan.PegClickSeconds.Count, $"trial {trial}: clicks");
                Check.True(plan.PegClickSeconds.SequenceEqual(plan.PegClickSeconds.OrderBy(t => t)), "clicks are in time order");
                Check.True(plan.PegClickSeconds.All(t => t > 0 && t <= plan.DurationSeconds + 1e-9), "every click is during the spin");
            }
        });

        run("spin: a full-turn spin clicks every peg once, and the clicks slow down toward the end", () =>
        {
            var wheel = WheelLayout.Create(new Random(2));
            var clicker = WheelRules.ClickerAngles[0];
            var plan = WheelPhysics.Plan(wheel, clicker - wheel.Slices[4].Mid, clicker, WheelPhysics.SweetSpotPower, 0);
            Check.Equal(72, plan.PegClickSeconds.Count, "72 pegs, 72 clicks");
            var gaps = plan.PegClickSeconds.Zip(plan.PegClickSeconds.Skip(1), (a, b) => b - a).ToList();
            Check.True(gaps[^1] > gaps[0] * 3, "the last click is much slower than the first");
            // The pegs are not evenly spaced (the Million trio has two extra), so single gaps wobble; the trend is what matters.
            Check.True(gaps.TakeLast(6).Average() > gaps.Take(6).Average() * 3, "the last few clicks are far slower than the first few");
        });

        Console.WriteLine("Wheel: the board");

        run("board: rows are 12, 14, 14 and 12 tiles, with the short rows centered over the long ones", () =>
        {
            Check.True(WheelBoard.RowWidths.SequenceEqual([12, 14, 14, 12]), "row widths");
            Check.True(WheelBoard.RowOffsets.SequenceEqual([1, 0, 0, 1]), "the short rows start one tile in");
            Check.Equal(52, WheelBoard.Capacity, "tiles on the board");
        });

        run("board: a short puzzle goes on one row, a medium one on the two middle rows, evenly split", () =>
        {
            Check.True(WheelBoard.TryLayout("break the ice", out var one, out _), "13 letters fit on one line");
            Check.True(one.Lines.SequenceEqual(["BREAK THE ICE"]) && one.Rows.SequenceEqual([1]), "one line, on the second row");

            Check.True(WheelBoard.TryLayout("A PIECE OF CAKE", out var two, out _), "15 characters need two lines");
            Check.True(two.Lines.SequenceEqual(["A PIECE", "OF CAKE"]), "split as evenly as the words allow");
            Check.True(two.Rows.SequenceEqual([1, 2]), "the middle rows, like the TV board");
        });

        run("board: three and four lines use the top and bottom rows too", () =>
        {
            Check.True(WheelBoard.TryLayout("THE EARLY BIRD CATCHES THE WORM", out var three, out _), "31 characters fit");
            Check.Equal(3, three.Lines.Count, "three lines");
            Check.True(three.Rows.SequenceEqual([0, 1, 2]) || three.Rows.SequenceEqual([1, 2, 3]), "three lines sit on three adjacent rows including both middle ones");
            for (var i = 0; i < three.Lines.Count; i++) Check.True(three.Lines[i].Length <= WheelBoard.RowWidths[three.Rows[i]], "each line fits its row");

            Check.True(WheelBoard.TryLayout("THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG", out var four, out var problem), "a 4-line puzzle fits: " + problem);
            Check.True(four.Rows.SequenceEqual([0, 1, 2, 3]), "four lines use every row");
            Check.Equal("THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG", string.Join(' ', four.Lines), "the lines read back as the puzzle");
            for (var i = 0; i < 4; i++) Check.True(four.Lines[i].Length <= WheelBoard.RowWidths[i], $"line {i + 1} fits its row of {WheelBoard.RowWidths[i]}");
        });

        run("board: lines are centered, and the tiles land on the right columns", () =>
        {
            Check.True(WheelBoard.TryLayout("GOOD MORNING/SUNSHINE", out var layout, out _), "forced break");
            Check.True(layout.Lines.SequenceEqual(["GOOD MORNING", "SUNSHINE"]) && layout.Rows.SequenceEqual([1, 2]), "lines exactly where the author broke them");
            var first = layout.Tiles.Where(t => t.Row == 1).Select(t => t.Column).ToList();
            Check.Equal(1, first.Min(), "12 letters on a 14-wide row start at column 1");
            Check.Equal(12, first.Max(), "and end at column 12 (the space between the words has no tile)");
            Check.Equal(11, first.Count, "11 letters and one space");
            var second = layout.Tiles.Where(t => t.Row == 2).Select(t => t.Column).ToList();
            Check.Equal(3, second.Min(), "8 letters on a 14-wide row start at column 3");

            Check.True(WheelBoard.TryLayout("ABCDEFGHIJKL/ABCDEFGHIJKL/ABCDEFGHIJKL/ABCDEFGHIJKL", out var full, out _), "four lines of 12 fit");
            Check.Equal(1, full.Tiles.Where(t => t.Row == 0).Min(t => t.Column), "the top row is 12 wide, starting at column 1");
            Check.Equal(12, full.Tiles.Where(t => t.Row == 3).Max(t => t.Column), "the bottom row ends at column 12");
        });

        run("board: letters are guessable, digits and punctuation are given, and the rest is turned away with a reason", () =>
        {
            Check.True(WheelBoard.TryLayout("Rock 'n' Roll!", out var layout, out _), "punctuation is fine");
            Check.True(layout.Text == "ROCK 'N' ROLL!", "capitals");
            Check.Equal(9, layout.Tiles.Count(t => t.IsLetter), "R O C K N R O L L: nine letters to call");
            Check.True(layout.Tiles.Any(t => t.Letter == '\'' && !t.IsLetter), "an apostrophe is a given tile");
            Check.True(layout.Tiles.Any(t => t.Letter == '!' && !t.IsLetter), "an exclamation mark is a given tile");

            Check.True(WheelBoard.TryLayout("ROUTE 66", out var digits, out _), "digits are fine");
            Check.True(digits.Tiles.Where(t => !t.IsLetter).All(t => char.IsDigit(t.Letter)), "digits are not letters to call");

            Check.True(!WheelBoard.TryLayout("", out _, out var empty) && empty.Length > 0, "empty is refused with a reason");
            Check.True(!WheelBoard.TryLayout("   ", out _, out _), "blank is refused");
            Check.True(!WheelBoard.TryLayout("HELLO @ WORLD", out _, out var bad) && bad.Contains('@'), "an unsupported character is named");
            Check.True(!WheelBoard.TryLayout("SUPERCALIFRAGILISTICEXPIALIDOCIOUS", out _, out var tooLong) && tooLong.Contains("34"), "a word wider than the board is refused with its length");
            Check.True(!WheelBoard.TryLayout("ABCDEFGHIJKLM NOPQRSTUVWXYZ ABCDEFGHIJKLM NOPQRSTUVWXYZ ABCDEFGHIJKLM", out _, out var big), "more than the board holds is refused: " + big);
            Check.True(!WheelBoard.TryLayout("A/B/C/D/E", out _, out var lines) && lines.Contains('5'), "five forced lines are refused");
        });

        run("board: 2,000 random puzzles either fit properly or say why not", () =>
        {
            var rng = new Random(99);
            var fitted = 0;
            for (var i = 0; i < 2000; i++)
            {
                var words = Enumerable.Range(0, rng.Next(1, 11)).Select(_ => new string(Enumerable.Range(0, rng.Next(1, 11)).Select(_ => (char)('A' + rng.Next(26))).ToArray())).ToList();
                var text = string.Join(' ', words);
                if (!WheelBoard.TryLayout(text, out var layout, out var problem))
                {
                    Check.True(problem.Length > 0, "a refusal always has a reason");
                    continue;
                }
                fitted++;
                Check.Equal(text.Replace(" ", "").Length, layout.Tiles.Count, "one tile per letter");
                Check.Equal(string.Join(' ', words), layout.Answer, "the lines read back as the puzzle");
                var seen = new HashSet<(int, int)>();
                foreach (var t in layout.Tiles)
                {
                    Check.True(t.Row is >= 0 and <= 3, "row in range");
                    Check.True(t.Column >= WheelBoard.RowOffsets[t.Row] && t.Column < WheelBoard.RowOffsets[t.Row] + WheelBoard.RowWidths[t.Row], "column inside its row");
                    Check.True(seen.Add((t.Row, t.Column)), "no two tiles on one spot");
                }
                for (var r = 1; r < layout.Rows.Count; r++) Check.Equal(layout.Rows[r - 1] + 1, layout.Rows[r], "lines sit on consecutive rows");
            }
            Check.True(fitted > 1000, $"most random puzzles should fit ({fitted} did)");
        });

        Console.WriteLine("Wheel: the puzzle bank");

        run("bank: the starter set fits the board, and a first run writes it to the data folder", () =>
        {
            using var dir = new TempDir();
            var bank = new FolderWheelContent(dir.Path);
            Check.True(File.Exists(Path.Combine(dir.Path, "wheel", "puzzles.json")), "the file is created");
            var c = bank.Counts();
            Check.Equal(20, c.Regular, "starter regular puzzles");
            Check.Equal(4, c.Quick, "starter quick rounds");
            Check.Equal(9, c.Bonus, "starter bonus puzzles");
            Check.Equal(3, c.BonusCategories, "three bonus categories to choose between");
            foreach (var p in bank.Regular().Concat(bank.Bonus())) Check.True(WheelBoard.TryLayout(p.Text, out _, out _), $"'{p.Text}' fits");
            foreach (var q in bank.Quick()) Check.Equal(3, q.Texts.Count, $"'{q.Theme}' has three puzzles");

            var reopened = new FolderWheelContent(dir.Path);
            Check.Equal(20, reopened.Counts().Regular, "it survives a restart without reseeding");
        });

        run("bank: adding checks the board, the category and duplicates, and keeps what it accepts", () =>
        {
            using var dir = new TempDir();
            var bank = new FolderWheelContent(dir.Path, seedStarters: false);
            Check.Equal(0, bank.Counts().Regular, "empty without starters");

            var ok = bank.AddRegular("Phrase", "  the   cat's   meow ");
            Check.True(ok.Ok && ok.Id is not null, "added");
            Check.Equal("THE CAT'S MEOW", bank.Regular()[0].Text, "stored in capitals with single spaces");
            Check.True(!bank.AddRegular("Phrase", "THE CAT'S MEOW").Ok && bank.AddRegular("Phrase", "the cat's meow").Duplicate, "the same puzzle again is a duplicate");
            Check.True(!bank.AddRegular("", "SOMETHING").Ok, "a category is required");
            Check.True(!bank.AddRegular("Phrase", "BAD # CHARACTER").Ok, "unsupported characters are refused");
            Check.True(!bank.AddRegular("Phrase", "SUPERCALIFRAGILISTICEXPIALIDOCIOUS").Ok, "too wide is refused");
            Check.Equal(1, bank.Counts().Regular, "refused puzzles aren't kept");

            var quick = bank.AddQuick("Things That Spin", ["TOP", "RECORD PLAYER", "CEILING FAN"]);
            Check.True(quick.Ok, "a quick round");
            Check.True(!bank.AddQuick("Dupes", ["TOP", "A", "B"]).Ok, "a puzzle already in the bank can't be reused in a quick round");
            Check.True(!bank.AddQuick("Two Only", ["ONE", "TWO"]).Ok, "exactly three puzzles");
            Check.True(!bank.AddQuick("Same", ["ONE", "ONE", "THREE"]).Ok, "the three must differ");

            Check.True(bank.AddBonus("Place", "NIAGARA FALLS").Ok, "a bonus puzzle");
            Check.True(bank.Delete(ok.Id!).Ok && !bank.Delete(ok.Id!).Ok, "deleting works once");
            Check.Equal(0, bank.Counts().Regular, "gone");

            var reopened = new FolderWheelContent(dir.Path, seedStarters: false);
            Check.Equal(1, reopened.Counts().Quick, "changes are saved to disk");
            Check.Equal(1, reopened.Counts().Bonus, "changes are saved to disk");
        });

        run("bank: pasted imports add what they can and report the rest line by line", () =>
        {
            using var dir = new TempDir();
            var bank = new FolderWheelContent(dir.Path, seedStarters: false);
            var report = bank.Import("""
                # comment lines and blank lines are ignored

                Phrase | A STITCH IN TIME
                Food | PEANUT BUTTER SANDWICH
                Phrase | a stitch in time
                NO PIPE ON THIS LINE
                Thing | RED WINE GLASS
                Thing | BAD @ CHARACTER
                # quick
                Things That Fly | KITE | PAPER AIRPLANE | HOT AIR BALLOON
                Broken Quick | ONLY | TWO
                # bonus
                Place | LAKE TAHOE
                # regular
                Phrase | THE LAST ONE
                """);
            Check.Equal(6, report.Added, "four regular + one quick + one bonus (the wine one counts, with a warning)");
            Check.Equal(1, report.Duplicates, "the repeated puzzle");
            Check.Equal(3, report.Problems.Count, "the bad character, the pipe-less line and the short quick round");
            Check.True(report.Problems.Any(p => p.StartsWith("Line 8:")), "problems name the pasted line: " + string.Join(" / ", report.Problems));
            Check.True(report.Warnings.Any(w => w.Contains("wine")), "alcohol gets a warning, not a block");
            var c = bank.Counts();
            Check.True(c.Regular == 4 && c.Quick == 1 && c.Bonus == 1, "counts match what was added");
        });

        run("bank: the bonus round avoids clue-style categories with a warning", () =>
        {
            using var dir = new TempDir();
            var bank = new FolderWheelContent(dir.Path, seedStarters: false);
            var result = bank.AddBonus("Same Name", "JOHN SMITH");
            Check.True(result.Ok && result.Warnings!.Count == 1, "allowed, but flagged");
            Check.True(bank.AddBonus("Person", "ABRAHAM LINCOLN").Warnings!.Count == 0, "plain categories aren't flagged");
        });

        run("bank: a game's deal is 6 regular puzzles, 2 quick rounds and 3 bonus choices in different categories, fresh ones first", () =>
        {
            using var dir = new TempDir();
            var bank = new FolderWheelContent(dir.Path);
            var seen = new HashSet<string>();
            for (var game = 0; game < 3; game++)
            {
                Check.True(bank.TryCreateDeal(new Random(game), out var deal, out var error), error);
                Check.Equal(6, deal.Regular.Count, "regular puzzles");
                Check.Equal(2, deal.Quick.Count, "quick rounds");
                Check.Equal(3, deal.BonusChoices.Count, "bonus choices");
                Check.Equal(3, deal.BonusChoices.Select(p => p.Category).Distinct().Count(), "three different categories");
                foreach (var p in deal.Regular) Check.True(seen.Add(p.Id), "no regular puzzle repeats while fresh ones remain");
                bank.MarkUsed(deal.Regular.Select(p => p.Id));
            }
            Check.Equal(2, bank.Counts().RegularUnused, "20 - 18 are still fresh");
            Check.True(bank.TryCreateDeal(new Random(7), out var fourth, out _), "a fourth game still deals");
            Check.Equal(2, fourth.Regular.Count(p => !bank.IsUsed(p.Id)), "the 2 fresh puzzles come first, then used ones top it up");
            bank.ClearUsed();
            Check.Equal(20, bank.Counts().RegularUnused, "clearing makes everything fresh");
        });

        run("bank: too few regular puzzles means no game, with a message", () =>
        {
            using var dir = new TempDir();
            var bank = new FolderWheelContent(dir.Path, seedStarters: false);
            bank.Import("Phrase | ONE\nPhrase | TWO");
            Check.True(!bank.TryCreateDeal(new Random(1), out _, out var error) && error.Contains('2') && error.Contains('6'), "the message says how many there are and how many are needed: " + error);
        });

        run("bank: an unreadable file is kept aside and the bank starts over instead of crashing", () =>
        {
            using var dir = new TempDir();
            Directory.CreateDirectory(Path.Combine(dir.Path, "wheel"));
            File.WriteAllText(Path.Combine(dir.Path, "wheel", "puzzles.json"), "{ this is not json");
            var bank = new FolderWheelContent(dir.Path);
            Check.Equal(20, bank.Counts().Regular, "starts over with the starters");
            Check.True(Directory.GetFiles(Path.Combine(dir.Path, "wheel"), "puzzles.json.broken-*").Length == 1, "the bad file was kept");
        });

        Console.WriteLine("Wheel: the game");

        run("lobby: an emcee and exactly three players, Red / Yellow / Blue in the order they join", () =>
        {
            var game = new WheelGame("TEST", new FixedWheelContent(), new Random(1), 0);
            Check.Fails(game.Start(Guid.NewGuid()), "starting with nobody there");
            var emcee = game.JoinEmcee("Host").Token;
            Check.Fails(game.JoinEmcee("Other"), "a second emcee");
            Check.Fails(game.JoinPlayer("   "), "a blank name");
            Check.Ok(game.JoinPlayer("Ann"));
            Check.Fails(game.JoinPlayer("ANN"), "a duplicate name");
            Check.Ok(game.JoinPlayer("Bob"));
            Check.Fails(game.Start(emcee), "starting with two players");
            Check.True(!game.Snapshot().CanStart, "no Start button yet");
            Check.Ok(game.JoinPlayer("Cy"));
            Check.True(game.Snapshot().CanStart, "Start appears with a full table");
            Check.Fails(game.JoinPlayer("Dee"), "a fourth player");
            Check.True(game.Snapshot().Players.Select(p => p.Color).SequenceEqual(["Red", "Yellow", "Blue"]), "colors in join order");
            Check.Fails(game.Start(game.JoinPlayer("x").Token), "a player starting the game");
            Check.Ok(game.Start(emcee));
            Check.Fails(game.Start(emcee), "starting twice");
            Check.Fails(game.JoinPlayer("Late"), "joining after the start");
        });

        run("lobby: tokens map back to seats", () =>
        {
            var table = new WheelTable();
            Check.Equal(Role.Emcee, table.Game.GetSeat(table.Emcee).Role, "emcee");
            Check.Equal(Role.Player, table.Game.GetSeat(table.Tokens[1]).Role, "player");
            Check.Equal(table.S.Players[1].Id, table.Game.GetSeat(table.Tokens[1]).PlayerId!.Value, "player id");
            Check.Equal(Role.Spectator, table.Game.GetSeat(Guid.NewGuid()).Role, "a stranger");
        });

        run("start: needs enough puzzles; the game opens on puzzle 1 with Red up, nothing hidden leaked, and the dealt puzzles marked used", () =>
        {
            var thin = new WheelGame("TEST", new FixedWheelContent(["ONE", "TWO"]), new Random(1), 0);
            var emcee = thin.JoinEmcee("Host").Token;
            foreach (var n in new[] { "A", "B", "C" }) thin.JoinPlayer(n);
            Check.Fails(thin.Start(emcee), "starting with only two puzzles");
            Check.Equal(WheelPhase.Lobby, thin.Snapshot().Phase, "still in the lobby");

            var table = new WheelTable();
            var s = table.S;
            Check.Equal(WheelPhase.Turn, s.Phase, "phase");
            Check.Equal(1, s.PuzzleNumber, "puzzle number");
            Check.Equal(6, s.PuzzleCount, "regular puzzles in a game");
            Check.Equal(0, s.Active, "Red is up first");
            Check.Equal("Phrase", s.Category, "category is shown");
            Check.True(s.Board.All(t => t.State == TileState.Hidden && t.Letter is null), "no letter is shown");
            Check.Equal(13 - 2, s.Board.Count, "11 letter tiles");
            Check.True(s.CanSpin && s.CanSolve && !s.CanBuyVowel, "you can spin or solve, but not buy a vowel with no money");
            Check.Equal(6, table.Content.Used.Count, "the six dealt puzzles are marked used");
            Check.Equal(26, s.Slices.Count, "the wheel is dealt");
            Check.True(s.Players.All(p => p.RoundBank == 0 && p.Total == 0), "everyone starts at zero");
        });

        run("spin: only the player up (or the emcee) can spin, only on their turn, and the power must be on the slider", () =>
        {
            var table = new WheelTable();
            Check.Fails(table.Game.Spin(table.Tokens[1], 400), "Yellow spinning on Red's turn");
            Check.Fails(table.Game.Spin(Guid.NewGuid(), 400), "a stranger spinning");
            Check.Fails(table.Game.Spin(table.Tokens[0], -1), "power below 0");
            Check.Fails(table.Game.Spin(table.Tokens[0], 1001), "power above 1000");
            Check.Fails(table.Game.FinishSpin(), "finishing a spin that never started");
            Check.Ok(table.Game.Spin(table.Emcee, 500));
            Check.Equal(WheelPhase.Spinning, table.S.Phase, "the wheel is turning");
            Check.True(table.S.Spin is not null && table.S.Spin.Power == 500 && table.S.Spin.DurationSeconds >= 2.5, "the TV gets the plan to play back");
            Check.Fails(table.Game.Spin(table.Tokens[0], 500), "spinning while spinning");
            Check.Fails(table.Game.BeginSolve(table.Emcee), "solving while the wheel turns");
            Check.Ok(table.Game.FinishSpin());
        });

        run("spin: the sweet spot returns to the same slice in the real game (no jitter), and jitter nudges it without breaking it", () =>
        {
            var table = new WheelTable();
            var before = table.S.Slices.First(s => WheelLayout.Normalize(WheelRules.ClickerAngles[0] - table.S.WheelAngle) >= s.Start && WheelLayout.Normalize(WheelRules.ClickerAngles[0] - table.S.WheelAngle) < s.End);
            Check.Ok(table.Game.Spin(table.Tokens[0], WheelPhysics.SweetSpotPower));
            Check.Equal(before, table.S.Spin!.Landed, "the same slice it started on");

            var hits = 0;
            for (var seed = 0; seed < 200; seed++)
            {
                var jittery = new WheelTable(seed: seed, jitter: 1.5);
                var start = jittery.S.Slices.First(s => WheelLayout.Normalize(WheelRules.ClickerAngles[0] - jittery.S.WheelAngle) >= s.Start && WheelLayout.Normalize(WheelRules.ClickerAngles[0] - jittery.S.WheelAngle) < s.End);
                Check.Ok(jittery.Game.Spin(jittery.Tokens[0], WheelPhysics.SweetSpotPower));
                if (jittery.S.Spin!.Landed == start) hits++;
            }
            Check.True(hits >= 150, $"with 1.5 degrees of wobble the sweet spot should usually still work ({hits} of 200)");
        });

        run("cash: landing on a dollar value asks for a consonant, and each one in the puzzle pays that much", () =>
        {
            var table = new WheelTable(["BETTER LATE THAN NEVER", "X", "X", "X", "X", "X"]);
            var landed = table.SpinTo(x => x.Kind == SliceKind.Cash && x.Value >= 500);
            Check.Equal(WheelPhase.Consonant, table.S.Phase, "phase");
            Check.Equal(landed.Value, table.S.SpinValue, "the value is on the table");
            Check.True(!table.S.CanSpin, "no spinning mid-turn");
            Check.Ok(table.Game.StageLetter(table.Emcee, 't'));
            Check.Equal('T', table.S.StagedLetter, "the typed letter is shown as capital T");
            Check.Equal(0, table.Bank(0), "nothing happens until Confirm");
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            Check.Equal(WheelPhase.Reveal, table.S.Phase, "the tiles light up");
            Check.Equal(landed.Value * 4, table.Bank(0), "four T's in BETTER LATE THAN NEVER");
            Check.Equal(4, table.S.Board.Count(t => t.State == TileState.Lit), "four lit tiles");
            Check.True(table.S.Board.All(t => t.Letter is null), "lit tiles don't show their letter yet");
            Check.Fails(table.Game.StageLetter(table.Emcee, 'H'), "calling a letter while tiles are being revealed");
            Check.Ok(table.Game.FinishReveal());
            Check.Equal(4, table.S.Board.Count(t => t.State == TileState.Revealed && t.Letter == 'T'), "now the T's are showing");
            Check.Equal(WheelPhase.Turn, table.S.Phase, "same player continues");
            Check.Equal(0, table.S.Active, "still Red");
            Check.True(table.S.CalledLetters == "T", "T is called");
        });

        run("letters: the emcee must confirm, vowels and repeats are refused, and cancel takes a typo back", () =>
        {
            var table = new WheelTable(["BREAK THE ICE", "X", "X", "X", "X", "X"]);
            table.SpinToCash();
            Check.Fails(table.Game.ConfirmLetter(table.Emcee), "confirming with nothing typed");
            Check.Fails(table.Game.StageLetter(table.Tokens[0], 'B'), "a player typing for the emcee");
            Check.Fails(table.Game.StageLetter(table.Emcee, 'E'), "a vowel after a spin");
            Check.Fails(table.Game.StageLetter(table.Emcee, '7'), "a digit");
            Check.Ok(table.Game.StageLetter(table.Emcee, 'Z'));
            Check.Ok(table.Game.CancelLetter(table.Emcee));
            Check.Equal(null, table.S.StagedLetter, "the typo is gone");
            Check.Equal(WheelPhase.Consonant, table.S.Phase, "the player still has to call a consonant");
            Check.Fails(table.Game.CancelLetter(table.Emcee), "nothing left to cancel while a consonant is owed");
            Check.Ok(table.Game.StageLetter(table.Emcee, 'B'));
            Check.Ok(table.Game.StageLetter(table.Emcee, 'K'));
            Check.Equal('K', table.S.StagedLetter, "typing again replaces the staged letter");
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            Check.Equal(1, table.S.Board.Count(t => t.State == TileState.Lit), "only the K, not the B that was typed first");
            Check.Ok(table.Game.FinishReveal());
            table.SpinToCash();
            Check.Fails(table.Game.StageLetter(table.Emcee, 'K'), "a letter that was already called");
        });

        run("letters: a consonant that isn't in the puzzle passes the turn and pays nothing", () =>
        {
            var table = new WheelTable(["BREAK THE ICE", "X", "X", "X", "X", "X"]);
            table.SpinToCash();
            Check.Ok(table.Game.StageLetter(table.Emcee, 'Z'));
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            Check.Equal(WheelPhase.Turn, table.S.Phase, "back to a turn");
            Check.Equal(1, table.S.Active, "Yellow is up");
            Check.Equal(0, table.Bank(0), "Red earned nothing");
            Check.Equal("Z", table.S.CalledLetters, "Z is used up");
        });

        run("vowels: cost $250 from this round's money, need the money, and are charged even when the letter misses", () =>
        {
            var table = new WheelTable(["BREAK THE ICE", "X", "X", "X", "X", "X"]);
            Check.Fails(table.Game.BuyVowel(table.Emcee), "a vowel with no money");
            Check.Fails(table.Game.BuyVowel(table.Tokens[0]), "a player buying for the emcee");
            table.Call('T');
            var bank = table.Bank(0);
            Check.True(bank >= 500, "the T paid out");
            Check.True(table.S.CanBuyVowel, "now a vowel is possible");

            Check.Ok(table.Game.BuyVowel(table.Emcee));
            Check.Equal(WheelPhase.Vowel, table.S.Phase, "phase");
            Check.Equal(bank, table.Bank(0), "nothing is charged until Confirm");
            Check.Fails(table.Game.StageLetter(table.Emcee, 'R'), "a consonant when buying a vowel");
            Check.Ok(table.Game.CancelLetter(table.Emcee));      // no letter typed: calls the purchase off
            Check.Equal(WheelPhase.Turn, table.S.Phase, "back to the turn");
            Check.Equal(bank, table.Bank(0), "a cancelled purchase costs nothing");

            Check.Ok(table.Game.BuyVowel(table.Emcee));
            Check.Ok(table.Game.StageLetter(table.Emcee, 'e'));
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            Check.Equal(bank - 250, table.Bank(0), "the vowel costs $250 and pays nothing");
            Check.Equal(3, table.S.Board.Count(t => t.State == TileState.Lit), "three E's light up");
            Check.Ok(table.Game.FinishReveal());
            Check.Equal(0, table.S.Active, "a right vowel keeps the turn");

            Check.Ok(table.Game.BuyVowel(table.Emcee));
            Check.Ok(table.Game.StageLetter(table.Emcee, 'U'));
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            Check.Equal(bank - 500, table.Bank(0), "a second vowel, a second $250, even though there is no U");
            Check.Equal(1, table.S.Active, "a wrong vowel passes the turn");
        });

        run("vowels: not when the round's money is under $250 or no vowels remain; spins are refused when no consonants remain", () =>
        {
            var table = new WheelTable(["AB", "X", "X", "X", "X", "X"]);
            table.Call('B');
            Check.True(!table.S.CanSpin, "the only consonant is revealed, so there is nothing left to spin for");
            Check.Fails(table.Game.Spin(table.Tokens[0], 400), "spinning with no consonants left");
            Check.True(table.S.CanBuyVowel, "but the A can still be bought");
            Check.Ok(table.Game.BuyVowel(table.Emcee));
            Check.Ok(table.Game.StageLetter(table.Emcee, 'A'));
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            Check.Ok(table.Game.FinishReveal());
            Check.True(!table.S.CanBuyVowel && !table.S.CanSpin && table.S.CanSolve, "the board is full: solving is all that is left");
            Check.True(table.S.Message!.Contains("solve"), "the emcee is told: " + table.S.Message);
        });

        run("bankrupt: the round's money is gone and the turn passes; money banked in earlier puzzles is safe", () =>
        {
            var table = new WheelTable(["BREAK THE ICE", "BETTER LATE THAN NEVER", "X", "X", "X", "X"]);
            table.Call('T');
            table.Solve();
            table.NextPuzzle();
            var banked = table.Total(0);
            Check.True(banked > 0, "Red banked something");
            Check.Equal(1, table.S.Active, "puzzle 2 starts with Yellow");
            table.Call('T');
            Check.True(table.Bank(1) > 0, "Yellow has round money");
            var landed = table.SpinTo(x => x.Kind == SliceKind.Bankrupt);
            Check.Equal(SliceKind.Bankrupt, landed.Kind, "landed on Bankrupt");
            Check.Equal(0, table.Bank(1), "the round's money is gone");
            Check.Equal(2, table.S.Active, "Cy is up");
            Check.Equal(banked, table.Total(0), "Red's banked money is untouched");
            Check.True(table.S.Message!.Contains("BANKRUPT"), "the message says so");
        });

        run("grab and drop: a finger on the pad is visible to the TV, is not a game action, and goes away on a spin, a drop or a phase change", () =>
        {
            var table = new WheelTable();
            Check.True(!table.S.Swirling, "nobody swirling at first");
            Check.Fails(table.Game.Grab(table.Tokens[1]), "Yellow isn't up");
            Check.Ok(table.Game.Grab(table.Tokens[0]));
            Check.True(table.S.Swirling, "the TV can see the finger");
            Check.True(!table.S.CanUndo, "and it is not something Undo would take back");
            Check.Ok(table.Game.Drop(table.Tokens[0]));
            Check.True(!table.S.Swirling, "dropped: the TV swings back");

            Check.Ok(table.Game.Grab(table.Tokens[0]));
            Check.Ok(table.Game.Spin(table.Tokens[0], 400));
            Check.True(!table.S.Swirling, "spinning ends it");
            Check.Fails(table.Game.Grab(table.Tokens[0]), "no grabbing while the wheel turns");
            Check.Ok(table.Game.FinishSpin());

            var other = new WheelTable();
            Check.Ok(other.Game.Grab(other.Tokens[0]));
            Check.Ok(other.Game.BeginSolve(other.Emcee));
            Check.True(!other.S.Swirling, "going to solve ends it");
            Check.Ok(other.Game.JudgeSolve(other.Emcee, false));
            Check.True(!other.S.Swirling, "and it does not come back by itself");
        });

        run("a deal never uses the same category twice while there are enough different ones, and still deals when there aren't", () =>
        {
            using var dir = new TempDir();
            var bank = new FolderWheelContent(dir.Path, seedStarters: false);
            // 12 puzzles in 6 categories: every deal of 6 has to take exactly one from each.
            var cats = new[] { "Same Name", "Phrase", "Place", "Thing", "Person", "Food & Drink" };
            for (var i = 0; i < 12; i++) Check.True(bank.AddRegular(cats[i % 6], "PUZZLE " + (char)('A' + i)).Ok, "added");
            for (var seed = 0; seed < 100; seed++)
            {
                Check.True(bank.TryCreateDeal(new Random(seed), out var deal, out _), "deals");
                Check.Equal(6, deal.Regular.Select(p => p.Category.ToUpperInvariant()).Distinct().Count(), "six different categories");
            }

            using var dir2 = new TempDir();
            var thin = new FolderWheelContent(dir2.Path, seedStarters: false);
            for (var i = 0; i < 8; i++) Check.True(thin.AddRegular("Same Name", "PUZZLE " + (char)('A' + i)).Ok, "added");
            Check.True(thin.TryCreateDeal(new Random(1), out var repeated, out _) && repeated.Regular.Count == 6, "one category only: it still deals six rather than failing");
        });

        run("lose a turn: the turn passes and money stays", () =>
        {
            var table = new WheelTable(["BREAK THE ICE", "X", "X", "X", "X", "X"]);
            table.Call('T');
            var bank = table.Bank(0);
            table.SpinTo(x => x.Kind == SliceKind.LoseATurn);
            Check.Equal(1, table.S.Active, "Yellow is up");
            Check.Equal(bank, table.Bank(0), "Red keeps the round's money");
            Check.Equal(WheelPhase.Turn, table.S.Phase, "phase");
        });

        run("million dollar wedge: landing picks it up and pays $1,500 a letter; the wedge becomes a full Bankrupt; Bankrupt loses it for good", () =>
        {
            var table = new WheelTable(["BETTER LATE THAN NEVER", "X", "X", "X", "X", "X"]);
            var landed = table.SpinTo(x => x.Kind == SliceKind.Million);
            Check.Equal(SliceKind.Million, landed.Kind, "landed on the Million slice");
            Check.True(table.S.Players[0].HoldsMillion, "Red holds the wedge");
            Check.Equal(WheelPhase.Consonant, table.S.Phase, "and still calls a consonant");
            Check.True(!table.S.Slices.Any(x => x.Kind == SliceKind.Million), "the Million slice is gone from the wheel");
            Check.Equal(24, table.S.Slices.Count, "24 slices now, one per wedge");
            Check.Equal(2, table.S.Slices.Count(x => x.Kind == SliceKind.Bankrupt), "two Bankrupt wedges: the original and the old Million wedge");
            Check.True(table.S.Slices.All(x => Math.Abs(x.Width - 15) < 1e-9), "every slice is 15 degrees");
            Check.Ok(table.Game.StageLetter(table.Emcee, 'T'));
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            Check.Equal(4 * 1500, table.Bank(0), "$1,500 for each of four T's");
            Check.Ok(table.Game.FinishReveal());

            table.SpinTo(x => x.Kind == SliceKind.Bankrupt);
            Check.True(!table.S.Players.Any(p => p.HoldsMillion), "Bankrupt takes the wedge");
            Check.True(table.S.Message!.Contains("Million Dollar Wedge"), "the message says so");
            Check.Equal(1, table.S.Active, "Yellow is up");
            Check.True(!table.S.Slices.Any(x => x.Kind == SliceKind.Million), "it does not go back on the wheel");
        });

        run("solving: right shows the whole board at once, banks the round's money for the solver only, and starts the next puzzle with the next player", () =>
        {
            var table = new WheelTable(["BREAK THE ICE", "A PIECE OF CAKE", "X", "X", "X", "X"]);
            table.Call('K');
            var bank = table.Bank(0);
            Check.Ok(table.Game.BeginSolve(table.Emcee));
            Check.Equal(WheelPhase.Solving, table.S.Phase, "phase");
            Check.Fails(table.Game.BeginSolve(table.Emcee), "solving twice");
            Check.Fails(table.Game.JudgeSolve(table.Tokens[0], true), "a player judging their own answer");
            Check.Ok(table.Game.JudgeSolve(table.Emcee, true));

            var s = table.S;
            Check.Equal(WheelPhase.PuzzleOver, s.Phase, "phase");
            Check.True(s.Board.All(t => t.State == TileState.Revealed && t.Letter is not null), "every tile shows at once");
            // The celebration: the solver is named and their money is still showing as this round's money, not yet in their total.
            Check.Equal(s.Players[0].Id, s.PuzzleWinner, "the solver is named so the TV can celebrate them");
            Check.Equal(bank, s.Players[0].RoundBank, "their round money is still on display");
            Check.Equal(0, s.Players[0].Total, "and not in their total yet");
            Check.Equal(0, s.Players[1].Total + s.Players[2].Total, "nobody else got anything");
            Check.True(s.Message!.Contains("Ann"), "the message names the solver");
            Check.Fails(table.Game.Spin(table.Tokens[0], 400), "spinning when the puzzle is over");

            table.NextPuzzle();
            s = table.S;
            Check.Equal(bank, s.Players[0].Total, "continuing moves Red's round money into their total");
            Check.True(s.Players.All(p => p.RoundBank == 0), "round money is cleared");
            Check.True(s.PuzzleWinner is null, "no celebration in the new puzzle");
            Check.Equal(2, s.PuzzleNumber, "puzzle 2");
            Check.Equal(1, s.Active, "Yellow starts puzzle 2");
            Check.Equal(WheelPhase.Turn, s.Phase, "phase");
            Check.Equal(bank, s.Players[0].Total, "totals carry over");
            Check.True(s.Board.All(t => t.State == TileState.Hidden), "a fresh board");
            Check.Equal("", s.CalledLetters, "no letters called yet");
        });

        run("solving: wrong passes the turn and costs nothing; a mis-tap on Solve can be taken back", () =>
        {
            var table = new WheelTable(["BREAK THE ICE", "X", "X", "X", "X", "X"]);
            table.Call('T');
            var bank = table.Bank(0);
            Check.Ok(table.Game.BeginSolve(table.Emcee));
            Check.Ok(table.Game.CancelSolve(table.Emcee));
            Check.Equal(WheelPhase.Turn, table.S.Phase, "back to the turn");
            Check.Equal(0, table.S.Active, "still Red");
            Check.Fails(table.Game.CancelSolve(table.Emcee), "cancelling when nobody is solving");

            Check.Ok(table.Game.BeginSolve(table.Emcee));
            Check.Ok(table.Game.JudgeSolve(table.Emcee, false));
            Check.Equal(WheelPhase.Turn, table.S.Phase, "the game goes on");
            Check.Equal(1, table.S.Active, "Yellow is up");
            Check.Equal(bank, table.Bank(0), "Red keeps the money in the round");
            Check.True(table.S.Board.Any(t => t.State == TileState.Hidden), "the board stays as it was");
        });

        run("million dollar wedge: held when you solve it counts for good; held when someone else solves it is lost", () =>
        {
            var table = new WheelTable(["BETTER LATE THAN NEVER", "BREAK THE ICE", "X", "X", "X", "X"]);
            table.SpinTo(x => x.Kind == SliceKind.Million);
            Check.Ok(table.Game.StageLetter(table.Emcee, 'T'));
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            Check.Ok(table.Game.FinishReveal());
            table.Solve();
            Check.True(table.S.Players[0].WonMillion, "Red won the wedge by solving the puzzle");
            Check.True(table.S.Message!.Contains("Million Dollar Wedge"), "the message says so");
            table.NextPuzzle();
            Check.True(table.S.Players[0].WonMillion, "it stays with Red into the next puzzle");

            // Puzzle 2: Yellow starts, takes the wedge, then Cy solves.
            table.SpinTo(x => x.Kind == SliceKind.Million);
            Check.True(table.S.Players[1].HoldsMillion, "Yellow took the wedge");
            Check.Ok(table.Game.StageLetter(table.Emcee, 'B'));
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            Check.Ok(table.Game.FinishReveal());
            Check.Ok(table.Game.BeginSolve(table.Emcee));
            Check.Ok(table.Game.JudgeSolve(table.Emcee, false));
            Check.Equal(2, table.S.Active, "Cy is up");
            table.Solve();
            Check.True(!table.S.Players[1].WonMillion && !table.S.Players[1].HoldsMillion, "Yellow lost the wedge because someone else solved it");
            Check.True(table.S.Players[0].WonMillion, "Red's is untouched");
        });

        run("skipping: the answer shows, no money changes hands, and a held wedge goes back", () =>
        {
            var table = new WheelTable(["BREAK THE ICE", "X", "X", "X", "X", "X"]);
            table.SpinTo(x => x.Kind == SliceKind.Million);
            Check.Fails(table.Game.SkipPuzzle(table.Tokens[0]), "a player skipping");
            Check.Ok(table.Game.StageLetter(table.Emcee, 'T'));
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            Check.Ok(table.Game.FinishReveal());
            Check.Ok(table.Game.SkipPuzzle(table.Emcee));
            var s = table.S;
            Check.Equal(WheelPhase.PuzzleOver, s.Phase, "phase");
            Check.True(s.Board.All(t => t.State == TileState.Revealed), "everything shows");
            Check.True(s.Players.All(p => p.Total == 0 && p.RoundBank == 0 && !p.HoldsMillion && !p.WonMillion), "nothing banked, no wedge");
        });

        run("undo: steps back one emcee action at a time, restores the wheel, and isn't offered mid-spin", () =>
        {
            var table = new WheelTable(["BETTER LATE THAN NEVER", "X", "X", "X", "X", "X"]);
            Check.Fails(table.Game.Undo(table.Emcee), "nothing to undo at the start");
            Check.True(!table.S.CanUndo, "no Undo button yet");
            var angle = table.S.WheelAngle;

            table.SpinToCash();
            Check.Ok(table.Game.StageLetter(table.Emcee, 'T'));
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            Check.Fails(table.Game.Undo(table.Emcee), "undoing while the tiles are being revealed");
            Check.True(!table.S.CanUndo, "not offered during the reveal");
            Check.Ok(table.Game.FinishReveal());
            Check.True(table.Bank(0) > 0, "Red has money");

            Check.Fails(table.Game.Undo(table.Tokens[0]), "a player pressing Undo");
            Check.Ok(table.Game.Undo(table.Emcee));
            Check.Equal(0, table.Bank(0), "the confirmed letter is taken back");
            Check.Equal(WheelPhase.Consonant, table.S.Phase, "the player is asked for a consonant again");
            Check.Equal('T', table.S.StagedLetter, "with the same letter typed, ready to confirm or cancel");
            Check.True(table.S.Board.All(t => t.State == TileState.Hidden), "no tiles lit");
            Check.Ok(table.Game.Undo(table.Emcee));
            Check.Equal(WheelPhase.Turn, table.S.Phase, "the spin is taken back");
            Check.Equal(angle, table.S.WheelAngle, "and the wheel is where it was");
            Check.Equal(0, table.S.Active, "Red is up");
            Check.Fails(table.Game.Undo(table.Emcee), "nothing further back");

            Check.Ok(table.Game.Spin(table.Tokens[0], 700));
            Check.Fails(table.Game.Undo(table.Emcee), "undoing mid-spin");
            Check.True(!table.S.CanUndo, "not offered mid-spin");
        });

        run("undo: a wrong ruling on a solve can be taken back, including the money and the puzzle number", () =>
        {
            var table = new WheelTable();
            table.Call('K');
            var bank = table.Bank(0);
            table.Solve();
            table.NextPuzzle();
            Check.Equal(2, table.S.PuzzleNumber, "on puzzle 2");
            Check.Equal(bank, table.Total(0), "the money moved into the total");
            Check.Ok(table.Game.Undo(table.Emcee));
            Check.Equal(1, table.S.PuzzleNumber, "back on puzzle 1");
            Check.Equal(WheelPhase.PuzzleOver, table.S.Phase, "waiting at the end of the puzzle");
            Check.Equal(0, table.Total(0), "the money is back out of the total");
            Check.Equal(bank, table.Bank(0), "and waiting in the round, with the celebration on");
            Check.True(table.S.PuzzleWinner is not null, "the solver is celebrated again");
            Check.Ok(table.Game.Undo(table.Emcee));
            Check.Equal(WheelPhase.Solving, table.S.Phase, "the ruling is taken back");
            Check.Equal(0, table.Total(0), "still not banked");
            Check.Equal(bank, table.Bank(0), "and still in the round");
            Check.True(table.S.PuzzleWinner is null, "no celebration while the ruling is open");
            Check.True(table.S.Board.Any(t => t.State == TileState.Hidden), "the board is hidden again");
            Check.Ok(table.Game.JudgeSolve(table.Emcee, false));
            Check.Equal(1, table.S.Active, "now it is Yellow's turn instead");
        });

        run("the TV snapshot never carries a hidden or lit letter", () =>
        {
            var table = new WheelTable(["BREAK THE ICE", "X", "X", "X", "X", "X"]);
            table.SpinToCash();
            Check.Ok(table.Game.StageLetter(table.Emcee, 'T'));
            Check.Ok(table.Game.ConfirmLetter(table.Emcee));
            foreach (var phase in new[] { "reveal", "after" })
            {
                var s = table.S;
                Check.True(s.Board.Where(t => t.State != TileState.Revealed).All(t => t.Letter is null), $"{phase}: hidden and lit tiles carry no letter");
                Check.Equal("BREAK THE ICE", s.Answer, "the emcee's copy has the answer");
                if (phase == "reveal") Check.Ok(table.Game.FinishReveal());
            }
        });

        run("a whole game: six puzzles, totals add up, the best total wins, and nothing works after the end", () =>
        {
            var table = new WheelTable();
            var expected = new int[3];
            for (var puzzle = 1; puzzle <= 6; puzzle++)
            {
                var solver = table.S.Active;
                table.Call('R');       // may or may not be in the puzzle, but the call is legal either way
                solver = table.S.Active;
                var bank = table.Bank(solver);
                table.Solve();
                expected[solver] += bank;
                if (puzzle < 6)
                {
                    table.NextPuzzle();
                    Check.Equal(expected[solver], table.Total(solver), $"puzzle {puzzle}: the solver's total");
                }
            }
            Check.Equal(WheelPhase.PuzzleOver, table.S.Phase, "waiting after the last puzzle");
            Check.Ok(table.Game.Continue(table.Emcee));
            var s = table.S;
            Check.Equal(WheelPhase.GameOver, s.Phase, "the game is over");
            Check.True(s.Players.Select(p => p.Total).SequenceEqual(expected), "totals match");
            var best = expected.Max();
            Check.True(s.Winners.SequenceEqual(s.Players.Where(p => p.Total == best).Select(p => p.Id)), "the top total wins");
            Check.Fails(table.Game.Spin(table.Emcee, 400), "spinning after the game");
            Check.Fails(table.Game.Continue(table.Emcee), "continuing after the game");
            Check.True(!s.CanSpin && !s.CanSolve && !s.CanBuyVowel, "no game actions are offered");
            Check.True(s.CanUndo, "but the emcee can still take back the Continue");
            Check.Ok(table.Game.Undo(table.Emcee));
            Check.Equal(WheelPhase.PuzzleOver, table.S.Phase, "undone: waiting after the last puzzle again");
            Check.Equal(6, table.S.PuzzleNumber, "still puzzle 6");
            Check.True(table.S.Winners.Count == 0, "no winner announced yet");
        });

        // ---- Quick rounds and the bonus round ----

        static IEnumerable<WheelQuickSet> QuickSets() =>
        [
            new WheelQuickSet("q1", "Things With Wheels", ["BICYCLE", "SKATEBOARD", "ROLLER COASTER"]),
            new WheelQuickSet("q2", "Kitchen Tools", ["WOODEN SPOON", "CHEESE GRATER", "MIXING BOWL"])
        ];
        static IEnumerable<WheelPuzzle> BonusChoices() =>
        [
            new WheelPuzzle("b1", "Place", "THE GRAND CANYON"),
            new WheelPuzzle("b2", "Thing", "CHOCOLATE CAKE"),
            new WheelPuzzle("b3", "Person", "ABRAHAM LINCOLN")
        ];
        string[] SixPuzzles() => ["BREAK THE ICE", "A PIECE OF CAKE", "FRESH FALLEN SNOW", "THE GRAND CANYON", "HOMEMADE APPLE PIE", "A QUIET LAKE CABIN"];

        /// <summary>Plays on without scoring: skips whatever is on until the bonus round's card pick comes up.</summary>
        void SkipToBonus(WheelTable t)
        {
            for (var guard = 0; guard < 40 && t.S.Phase != WheelPhase.BonusPick; guard++)
            {
                if (t.S.Phase == WheelPhase.PuzzleOver) Check.Ok(t.Game.Continue(t.Emcee));
                else Check.Ok(t.Game.SkipPuzzle(t.Emcee));
            }
            Check.Equal(WheelPhase.BonusPick, t.S.Phase, "reached the bonus round");
        }

        run("the plan: two puzzles, a toss-up of three, two puzzles, a second toss-up, two puzzles, then the bonus; the bonus winner is whoever leads", () =>
        {
            var t = new WheelTable(SixPuzzles(), quick: QuickSets(), bonus: BonusChoices());
            var seen = new List<string>();
            for (var guard = 0; guard < 40 && t.S.Phase != WheelPhase.BonusPick; guard++)
            {
                if (seen.Count == 0 || seen[^1] != t.S.StepLabel) seen.Add(t.S.StepLabel);
                if (t.S.Phase == WheelPhase.PuzzleOver) Check.Ok(t.Game.Continue(t.Emcee));
                else Check.Ok(t.Game.SkipPuzzle(t.Emcee));
            }
            Check.True(seen.SequenceEqual(["Puzzle 1 of 6", "Puzzle 2 of 6", "Toss-up 1 of 3", "Toss-up 2 of 3", "Toss-up 3 of 3", "Puzzle 3 of 6", "Puzzle 4 of 6",
                "Toss-up 1 of 3", "Toss-up 2 of 3", "Toss-up 3 of 3", "Puzzle 5 of 6", "Puzzle 6 of 6"]), "steps in order: " + string.Join(", ", seen));
            Check.Equal("Bonus round", t.S.StepLabel, "then the bonus");

            var plain = new WheelTable(SixPuzzles());
            SkipThroughRegular(plain);
            Check.Equal(WheelPhase.GameOver, plain.S.Phase, "a bank with no quick sets or bonus puzzles just plays the six puzzles");
            void SkipThroughRegular(WheelTable x)
            {
                for (var i = 0; i < 6; i++) { Check.Ok(x.Game.SkipPuzzle(x.Emcee)); Check.Ok(x.Game.Continue(x.Emcee)); }
            }
        });

        run("toss-up: letters appear at random and the emcee starts it; buzz in, a wrong answer locks that buzzer, a right one wins the prize", () =>
        {
            var t = new WheelTable(SixPuzzles(), quick: QuickSets(), bonus: BonusChoices());
            Check.Ok(t.Game.SkipPuzzle(t.Emcee)); Check.Ok(t.Game.Continue(t.Emcee));
            Check.Ok(t.Game.SkipPuzzle(t.Emcee)); Check.Ok(t.Game.Continue(t.Emcee));
            var s = t.S;
            Check.Equal(WheelPhase.QuickReady, s.Phase, "the toss-up is waiting");
            Check.Equal(1000, s.Prize, "the first is worth $1,000");
            Check.Equal("Things With Wheels", s.Category, "the theme is the category");
            Check.Equal(-1, s.Active, "nobody is up");
            Check.True(s.Board.Where(x => x.Letter is not null && x.State != TileState.Revealed).Count() == 0 && s.Board.Count(x => x.State == TileState.Hidden) == 7, "all seven letters hidden, none leaked");
            Check.Fails(t.Game.Buzz(t.Tokens[0]), "no buzzing before the emcee starts it");
            Check.Fails(t.Game.StartQuickRound(t.Tokens[0]), "a player starting it");
            Check.Ok(t.Game.StartQuickRound(t.Emcee));

            Check.Ok(t.Game.QuickTick());
            Check.Equal(1, t.S.Board.Count(x => x.State == TileState.Revealed && x.Letter is not null), "one letter showed");
            Check.Ok(t.Game.QuickTick());
            Check.Equal(2, t.S.Board.Count(x => x.State == TileState.Revealed && x.Letter is not null), "then another");

            Check.Fails(t.Game.Buzz(Guid.NewGuid()), "a stranger can't buzz");
            Check.Fails(t.Game.Buzz(t.Emcee), "the emcee has no buzzer of their own");
            Check.Ok(t.Game.Buzz(t.Tokens[1]));
            Check.Equal(WheelPhase.QuickAnswer, t.S.Phase, "Bob buzzed in");
            Check.Equal(1, t.S.Active, "and is the one up");
            Check.Fails(t.Game.Buzz(t.Tokens[0]), "too late, Ann");
            Check.Fails(t.Game.QuickTick(), "the letters stop while someone is answering");
            Check.Fails(t.Game.JudgeQuick(t.Tokens[1], true), "a player can't judge");
            Check.Ok(t.Game.JudgeQuick(t.Emcee, false));
            Check.Equal(WheelPhase.QuickPlay, t.S.Phase, "back to the letters");
            Check.True(t.S.Players[1].LockedOut && !t.S.Players[0].LockedOut, "Bob's buzzer is dead");
            Check.Fails(t.Game.Buzz(t.Tokens[1]), "Bob can't buzz again");

            Check.Ok(t.Game.Buzz(t.Tokens[0]));
            Check.Ok(t.Game.JudgeQuick(t.Emcee, true));
            s = t.S;
            Check.Equal(WheelPhase.PuzzleOver, s.Phase, "solved");
            Check.True(s.Board.All(x => x.State == TileState.Revealed), "the whole board shows");
            Check.Equal(s.Players[0].Id, s.PuzzleWinner, "Ann is celebrated");
            Check.Equal(1000, s.Players[0].RoundBank, "her prize is on display");
            Check.Equal(0, s.Players[0].Total, "not banked yet");
            Check.True(s.Message!.Contains("solved it") && s.Message.Contains("$1,000"), "the message says so");

            Check.Ok(t.Game.Continue(t.Emcee));
            Check.Equal(1000, t.Total(0), "now it's in her total");
            Check.Equal(WheelPhase.QuickReady, t.S.Phase, "the second toss-up");
            Check.Equal(2000, t.S.Prize, "worth $2,000");
            Check.True(!t.S.Players[1].LockedOut, "everyone's buzzer is live again");
        });

        run("toss-up: everyone locked out ends it, running out of letters ends it, Undo can take back a buzz but not stop the letters", () =>
        {
            var t = new WheelTable(SixPuzzles(), quick: QuickSets(), bonus: BonusChoices());
            for (var i = 0; i < 2; i++) { Check.Ok(t.Game.SkipPuzzle(t.Emcee)); Check.Ok(t.Game.Continue(t.Emcee)); }
            Check.Ok(t.Game.StartQuickRound(t.Emcee));
            Check.True(!t.S.CanUndo, "no Undo while the letters are coming");
            Check.Fails(t.Game.Undo(t.Emcee), "and the game says so");
            Check.Ok(t.Game.Buzz(t.Tokens[2]));
            Check.True(t.S.CanUndo, "but a buzz can be taken back");
            Check.Ok(t.Game.Undo(t.Emcee));
            Check.Equal(WheelPhase.QuickPlay, t.S.Phase, "back to the letters");
            Check.Equal(-1, t.S.Active, "with nobody buzzed in");

            for (var i = 0; i < 3; i++)
            {
                Check.Ok(t.Game.BuzzFor(t.Emcee, i));
                Check.Ok(t.Game.JudgeQuick(t.Emcee, false));
            }
            Check.Equal(WheelPhase.PuzzleOver, t.S.Phase, "all three locked out");
            Check.True(t.S.PuzzleWinner is null, "no winner");
            Check.True(t.S.Players.All(p => p.RoundBank == 0 && p.Total == 0), "no money moved");
            Check.Ok(t.Game.Continue(t.Emcee));

            Check.Ok(t.Game.StartQuickRound(t.Emcee));
            var guard = 0;
            while (t.S.Phase == WheelPhase.QuickPlay && guard++ < 100) Check.Ok(t.Game.QuickTick());
            Check.Equal(WheelPhase.PuzzleOver, t.S.Phase, "nobody buzzed: the letters ran out");
            Check.True(t.S.Message!.Contains("Nobody got it"), "and it says so");
            Check.True(t.S.Board.All(x => x.State == TileState.Revealed), "with the answer showing");
        });

        run("bonus: the leader picks a card, the emcee picks the category, RSTLNE show, three consonants and a vowel, a 10 second clock, then the card turns over", () =>
        {
            var t = new WheelTable(SixPuzzles(), quick: QuickSets(), bonus: BonusChoices());
            t.Call('K');                       // Ann earns round money in puzzle 1...
            Check.Ok(t.Game.BeginSolve(t.Emcee)); Check.Ok(t.Game.JudgeSolve(t.Emcee, true)); Check.Ok(t.Game.Continue(t.Emcee));   // ...and banks it
            var lead = t.Total(0);
            Check.True(lead > 0, "Ann leads");
            SkipToBonus(t);
            var s = t.S;
            Check.Equal(0, s.Active, "Ann plays the bonus");
            Check.True(s.Bonus is not null && s.Bonus.Prize is null && s.Bonus.Picked == -1, "no card picked, and no prize shown");
            Check.Equal("", s.Answer, "no puzzle yet");

            Check.Fails(t.Game.PickCard(t.Tokens[1], 3), "Bob can't pick Ann's card");
            Check.Fails(t.Game.PickCard(t.Tokens[0], 24), "there's no card 25");
            Check.Fails(t.Game.PickCard(t.Tokens[0], -1), "a player has to choose; only the emcee may pick at random");
            Check.Ok(t.Game.PickCard(t.Tokens[0], 6));
            s = t.S;
            Check.Equal(WheelPhase.BonusCategory, s.Phase, "now the category");
            Check.Equal(6, s.Bonus!.Picked, "card 7 is hers");
            Check.True(s.Bonus.Prize is null, "still nothing about what's on it");
            Check.True(s.Bonus.Choices.SequenceEqual(["Place", "Thing", "Person"]), "three categories to choose from");
            Check.Fails(t.Game.ChooseBonusCategory(t.Tokens[0], 0), "only the emcee taps the category");
            Check.Fails(t.Game.ChooseBonusCategory(t.Emcee, 3), "there's no fourth");
            Check.Ok(t.Game.ChooseBonusCategory(t.Emcee, 1));

            s = t.S;
            Check.Equal(WheelPhase.BonusLetters, s.Phase, "letters");
            Check.Equal("Thing", s.Category, "the chosen category");
            Check.Equal("RSTLNE", string.Concat("RSTLNE".Where(c => s.CalledLetters.Contains(c))), "R S T L N E count as called");
            Check.True(s.Board.Where(x => x.Letter is not null).All(x => "RSTLNE".Contains(x.Letter!.Value)), "only those letters show on the board");
            Check.Equal(3, s.Bonus!.ConsonantsLeft, "three consonants to call"); Check.Equal(1, s.Bonus.VowelsLeft, "and one vowel");
            Check.Fails(t.Game.StageLetter(t.Emcee, 'R'), "R is already given");
            Check.Ok(t.Game.StageLetter(t.Emcee, 'C'));
            Check.Ok(t.Game.ConfirmLetter(t.Emcee));
            Check.Equal(WheelPhase.Reveal, t.S.Phase, "C is in CHOCOLATE CAKE: tiles light up");
            Check.Ok(t.Game.FinishReveal());
            Check.Equal(WheelPhase.BonusLetters, t.S.Phase, "back to calling letters");
            Check.Ok(t.Game.StageLetter(t.Emcee, 'Z'));
            Check.Ok(t.Game.ConfirmLetter(t.Emcee));
            Check.Equal(WheelPhase.BonusLetters, t.S.Phase, "a letter that isn't there just uses up the pick");
            Check.Ok(t.Game.StageLetter(t.Emcee, 'H')); Check.Ok(t.Game.ConfirmLetter(t.Emcee)); Check.Ok(t.Game.FinishReveal());
            Check.Equal(0, t.S.Bonus!.ConsonantsLeft, "three consonants used");
            Check.Fails(t.Game.StageLetter(t.Emcee, 'K'), "no fourth consonant");
            Check.Fails(t.Game.StartBonusClock(t.Emcee), "the vowel hasn't been called yet");
            Check.Ok(t.Game.StageLetter(t.Emcee, 'O')); Check.Ok(t.Game.ConfirmLetter(t.Emcee)); Check.Ok(t.Game.FinishReveal());
            Check.Equal(WheelPhase.BonusReady, t.S.Phase, "all four are in");
            Check.Fails(t.Game.StageLetter(t.Emcee, 'U'), "no second vowel");

            Check.Fails(t.Game.StartBonusClock(t.Tokens[0]), "only the emcee starts the clock");
            Check.Ok(t.Game.StartBonusClock(t.Emcee));
            s = t.S;
            Check.Equal(WheelPhase.BonusSolve, s.Phase, "the clock is running");
            Check.True(s.Bonus!.SecondsLeft is > 9 and <= 10, "ten seconds on it");
            Check.Fails(t.Game.BonusTimeUp(), "it can't be called early");
            Check.True(s.Bonus.Prize is null, "the card is still face down");

            Check.Ok(t.Game.JudgeBonus(t.Emcee, true));
            s = t.S;
            Check.Equal(WheelPhase.BonusOver, s.Phase, "judged");
            Check.True(s.Bonus!.Prize is >= 40000 and <= 100000, "the card was worth $40,000 to $100,000");
            Check.True(s.Board.All(x => x.State == TileState.Revealed), "the whole puzzle shows");
            Check.Equal(s.Players[0].Id, s.PuzzleWinner, "Ann is celebrated");
            Check.Equal(lead, s.Players[0].Total, "not banked until the emcee finishes");
            var prize = s.Bonus.Prize!.Value;
            Check.Ok(t.Game.Continue(t.Emcee));
            s = t.S;
            Check.Equal(WheelPhase.GameOver, s.Phase, "and that's the game");
            Check.Equal(lead + prize, s.Players[0].Total, "the card's money is in Ann's final total");
        });

        run("bonus: winning the Million Dollar Wedge turns exactly one of the 24 cards into $1,000,000; without it none is", () =>
        {
            int Prize(bool withWedge, int card)
            {
                var t = new WheelTable(["BETTER LATE THAN NEVER", "A", "A", "A", "A", "A"], quick: QuickSets(), bonus: BonusChoices());
                if (withWedge)
                {
                    t.SpinTo(x => x.Kind == SliceKind.Million);
                    Check.Ok(t.Game.StageLetter(t.Emcee, 'T')); Check.Ok(t.Game.ConfirmLetter(t.Emcee)); Check.Ok(t.Game.FinishReveal());
                }
                else t.Call('T');
                t.Solve();
                Check.True(t.S.Players[0].WonMillion == withWedge, "Ann's wedge status");
                Check.Ok(t.Game.Continue(t.Emcee));
                SkipToBonus(t);
                Check.Equal(0, t.S.Active, "Ann leads");
                Check.Ok(t.Game.PickCard(t.Tokens[0], card));
                Check.Ok(t.Game.ChooseBonusCategory(t.Emcee, 0));
                foreach (var letter in "DGCA")
                {
                    Check.Ok(t.Game.StageLetter(t.Emcee, letter)); Check.Ok(t.Game.ConfirmLetter(t.Emcee));
                    if (t.S.Phase == WheelPhase.Reveal) Check.Ok(t.Game.FinishReveal());
                }
                Check.Ok(t.Game.StartBonusClock(t.Emcee));
                Check.Ok(t.Game.JudgeBonus(t.Emcee, false));
                Check.True(t.S.Players[0].RoundBank == 0 && t.S.PuzzleWinner is null, "a wrong answer pays nothing and nobody is celebrated");
                return t.S.Bonus!.Prize!.Value;
            }

            var withWedge = Enumerable.Range(0, 24).Select(c => Prize(true, c)).ToList();
            Check.Equal(1, withWedge.Count(p => p == 1_000_000), "one million-dollar card");
            Check.Equal(23, withWedge.Count(p => p is >= 40000 and <= 100000), "and 23 ordinary ones");
            var without = Enumerable.Range(0, 24).Select(c => Prize(false, c)).ToList();
            Check.True(without.All(p => p is >= 40000 and <= 100000), "no million card without the wedge");
            Check.True(without.OrderBy(x => x).SequenceEqual(WheelRules.BonusPrizes.OrderBy(x => x)), "the 24 cards are exactly the 24 prizes");
        });

        run("a tie at the end names every winner", () =>
        {
            var table = new WheelTable();
            for (var puzzle = 1; puzzle <= 6; puzzle++)
            {
                table.Solve();        // nobody spins, so every solve banks $0 and every total stays tied
                if (puzzle < 6) table.NextPuzzle();
            }
            Check.Ok(table.Game.Continue(table.Emcee));
            Check.Equal(3, table.S.Winners.Count, "all three tie at $0");
            Check.True(table.S.Message!.Contains("tie"), "the message says so: " + table.S.Message);
        });

        run("Changed fires on every success and stays quiet on failures", () =>
        {
            var table = new WheelTable(start: false);
            var fired = 0;
            table.Game.Changed += () => fired++;
            Check.Ok(table.Game.Start(table.Emcee));
            Check.Equal(1, fired, "after Start");
            Check.Fails(table.Game.Start(table.Emcee), "starting twice");
            Check.Fails(table.Game.Spin(table.Tokens[1], 400), "the wrong player spinning");
            Check.Fails(table.Game.ConfirmLetter(table.Emcee), "confirming nothing");
            Check.Equal(1, fired, "failures don't notify anyone");
        });

        run("rooms: four-letter codes with no vowels, found by code in any case", () =>
        {
            var manager = new WheelRoomManager(new FixedWheelContent());
            var games = Enumerable.Range(0, 100).Select(_ => manager.Create()).ToList();
            Check.Equal(100, games.Select(g => g.Code).Distinct().Count(), "distinct codes");
            Check.True(games.All(g => g.Code.Length == 4 && g.Code.All(c => char.IsUpper(c) && "AEIOU".IndexOf(c) < 0)), "four capitals, no vowels");
            Check.True(ReferenceEquals(games[0], manager.Find(games[0].Code.ToLowerInvariant())), "lookup ignores case");
            Check.True(manager.Find("ZZZZ-nope") is null && manager.Find(null) is null, "unknown codes");
        });

        run("fuzz: 300 whole games of random (legal and illegal) actions keep every rule", () =>
        {
            for (var seed = 0; seed < 300; seed++) Fuzz(seed);
        });
    }

    private static void Fuzz(int seed)
    {
        var rng = new Random(seed);
        var table = new WheelTable(seed: seed, jitter: seed % 2 == 0 ? 1.5 : 0);
        var game = table.Game;
        var steps = 0;

        while (table.S.Phase != WheelPhase.GameOver)
        {
            if (++steps > 6000) throw new Exception($"seed {seed}: the game never finished");
            var s = table.S;
            var active = Math.Max(0, s.Active);
            switch (s.Phase)
            {
                case WheelPhase.Turn:
                    var roll = rng.Next(100);
                    if (roll < 50) game.Spin(rng.Next(2) == 0 ? table.Tokens[active] : table.Emcee, rng.Next(-5, 1006));
                    else if (roll < 65) game.BuyVowel(table.Emcee);
                    else if (roll < 80) { game.BeginSolve(table.Emcee); if (table.S.Phase == WheelPhase.Solving) game.JudgeSolve(table.Emcee, rng.Next(100) < 55); }
                    else if (roll < 83) game.SkipPuzzle(table.Emcee);
                    else if (roll < 90) game.Undo(table.Emcee);
                    else game.Spin(table.Tokens[(active + 1) % 3], 400);   // the wrong player: must be refused
                    break;
                case WheelPhase.Spinning: game.FinishSpin(); break;
                case WheelPhase.Consonant:
                case WheelPhase.Vowel:
                    var letter = (char)('A' + rng.Next(26));
                    game.StageLetter(table.Emcee, letter);
                    var r = rng.Next(10);
                    if (r < 7) game.ConfirmLetter(table.Emcee);
                    else if (r < 9) game.CancelLetter(table.Emcee);
                    else game.Undo(table.Emcee);
                    break;
                case WheelPhase.Reveal: game.FinishReveal(); break;
                case WheelPhase.Solving: game.JudgeSolve(table.Emcee, rng.Next(100) < 55); break;
                case WheelPhase.PuzzleOver: game.Continue(table.Emcee); break;
            }

            var now = table.S;
            Check.True(now.Players.All(p => p.RoundBank >= 0 && p.Total >= 0), $"seed {seed}: money never goes negative");
            Check.True(now.Active is >= 0 and <= 2, $"seed {seed}: someone is always up");
            Check.True(now.PuzzleNumber is >= 1 and <= 6, $"seed {seed}: puzzle number in range");
            Check.True(now.Players.Count(p => p.HoldsMillion) <= 1, $"seed {seed}: at most one player holds the wedge");
            Check.True(now.Phase == WheelPhase.Reveal || now.Board.All(t => t.State != TileState.Lit), $"seed {seed}: tiles are only lit during a reveal");
            Check.True(now.Board.Where(t => t.State != TileState.Revealed).All(t => t.Letter is null), $"seed {seed}: no hidden letter leaks");
            if (now.Phase is not (WheelPhase.PuzzleOver or WheelPhase.GameOver))
            {
                var called = now.CalledLetters;
                Check.True(now.Board.Where(t => t.Letter is not null && t.Letter is >= 'A' and <= 'Z').All(t => called.Contains(t.Letter!.Value)), $"seed {seed}: a revealed letter was never called");
            }
            Check.True((!now.CanSpin && !now.CanSolve && !now.CanBuyVowel) || now.Phase == WheelPhase.Turn, $"seed {seed}: actions are only offered on a turn");
        }

        var end = table.S;
        Check.True(end.Winners.Count >= 1, $"seed {seed}: a finished game has a winner");
        Check.True(end.Players.Where(p => end.Winners.Contains(p.Id)).Select(p => p.Total).Distinct().Count() == 1, $"seed {seed}: winners are tied on total");
        Check.True(end.Players.All(p => p.Total <= end.Players.Where(w => end.Winners.Contains(w.Id)).First().Total), $"seed {seed}: nobody beats the winner");
    }

}

/// <summary>A scratch folder that deletes itself.</summary>
sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wheel-checks-" + Guid.NewGuid().ToString("N")[..8]);

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* a scratch folder; nothing depends on it */ }
    }
}
