namespace BuzzerGames.Core.Floor;

/// <summary>One square of the floor, by row (0 is the top) and column.</summary>
public readonly record struct FloorCell(int Row, int Col);

/// <summary>
/// The shape of the floor: one square per player, packed into a block that suits a wide TV. When the player count doesn't
/// divide neatly (13, say), the <b>top</b> row is only partly filled and sits centered, so every square still has a neighbor
/// and the whole floor is one connected piece. Two squares are neighbors when they share an edge.
/// </summary>
public sealed class FloorShape
{
    // A TV is wider than it is tall, so a block a little wider than square looks best.
    private const double IdealAspect = 1.7;

    private readonly List<int>[] _neighbors;

    public int Rows { get; }
    public int Cols { get; }
    public IReadOnlyList<FloorCell> Cells { get; }

    private FloorShape(int rows, int cols, List<FloorCell> cells)
    {
        Rows = rows;
        Cols = cols;
        Cells = cells;

        var at = new Dictionary<FloorCell, int>();
        for (var i = 0; i < cells.Count; i++) at[cells[i]] = i;

        _neighbors = new List<int>[cells.Count];
        for (var i = 0; i < cells.Count; i++)
        {
            var list = new List<int>();
            foreach (var (dr, dc) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                if (at.TryGetValue(new FloorCell(cells[i].Row + dr, cells[i].Col + dc), out var n)) list.Add(n);
            _neighbors[i] = list;
        }
    }

    /// <summary>The squares that share an edge with this one.</summary>
    public IReadOnlyList<int> Neighbors(int cell) => _neighbors[cell];

    /// <summary>How far apart two squares are, counting steps across edges as if the floor were solid.</summary>
    public int Distance(int a, int b) =>
        Math.Abs(Cells[a].Row - Cells[b].Row) + Math.Abs(Cells[a].Col - Cells[b].Col);

    /// <summary>The best block for this many players.</summary>
    public static FloorShape For(int players)
    {
        if (players < 1) throw new ArgumentOutOfRangeException(nameof(players), "The floor needs at least one square.");

        var bestCols = 1;
        var bestCost = double.MaxValue;
        for (var cols = 1; cols <= players; cols++)
        {
            var rows = (players + cols - 1) / cols;
            var empty = rows * cols - players;
            // Empty squares make a ragged top row; a lopsided block wastes the screen. Weigh the two.
            var cost = empty + 2 * Math.Abs(Math.Log((double)cols / rows) - Math.Log(IdealAspect));
            if (cost < bestCost - 1e-9)
            {
                bestCost = cost;
                bestCols = cols;
            }
        }

        var bestRows = (players + bestCols - 1) / bestCols;
        var topCount = players - (bestRows - 1) * bestCols;
        var offset = (bestCols - topCount) / 2;

        var cells = new List<FloorCell>(players);
        for (var c = 0; c < topCount; c++) cells.Add(new FloorCell(0, offset + c));
        for (var r = 1; r < bestRows; r++)
            for (var c = 0; c < bestCols; c++)
                cells.Add(new FloorCell(r, c));

        return new FloorShape(bestRows, bestCols, cells);
    }
}
