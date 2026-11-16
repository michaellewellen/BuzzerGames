using System.Text;
using System.Text.RegularExpressions;

namespace BuzzerGames.Core.Wheel;

/// <summary>A tile with something on it. Spaces and unused tiles are simply not listed.</summary>
public sealed record BoardTile(int Row, int Column, char Letter)
{
    /// <summary>A to Z can be called by a player. Digits and punctuation are given away from the start.</summary>
    public bool IsLetter => Letter is >= 'A' and <= 'Z';
}

/// <summary>A puzzle placed on the board: which rows its lines use, and where every tile sits.</summary>
public sealed class BoardLayout
{
    public BoardLayout(string text, IReadOnlyList<string> lines, IReadOnlyList<int> rows, IReadOnlyList<BoardTile> tiles)
    {
        Text = text;
        Lines = lines;
        Rows = rows;
        Tiles = tiles;
    }

    /// <summary>The puzzle in capitals with single spaces (and "/" where an author forced a line break).</summary>
    public string Text { get; }

    public IReadOnlyList<string> Lines { get; }

    /// <summary>The board row (0 to 3) each line sits on.</summary>
    public IReadOnlyList<int> Rows { get; }

    /// <summary>Every tile that has something on it, in reading order.</summary>
    public IReadOnlyList<BoardTile> Tiles { get; }

    /// <summary>The puzzle as plain words, for showing to the emcee: lines joined by spaces.</summary>
    public string Answer => string.Join(' ', Lines);
}

/// <summary>
/// The puzzle board: four rows of 12, 14, 14 and 12 tiles. The two short rows are centered over the long ones. Short puzzles sit on the
/// middle rows, longer ones spread out to the top and bottom. This is shared by the admin page (so you see what the TV will show) and the game.
/// </summary>
public static class WheelBoard
{
    public const int Rows = 4;
    public const int Columns = 14;
    public static readonly IReadOnlyList<int> RowWidths = [12, 14, 14, 12];
    public static readonly IReadOnlyList<int> RowOffsets = [1, 0, 0, 1];
    public const int Capacity = 12 + 14 + 14 + 12;

    /// <summary>The rows a puzzle uses for 1, 2, 3 or 4 lines. Three lines can go on the top three rows or the bottom three.</summary>
    private static readonly int[][][] RowChoices =
    [
        [[1]],
        [[1, 2]],
        [[0, 1, 2], [1, 2, 3]],
        [[0, 1, 2, 3]]
    ];

    private const string Punctuation = "&'-.,!?:\"";

    public static bool IsAllowed(char c) => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') || Punctuation.Contains(c);

    /// <summary>Capitals, single spaces, nothing at the ends, and any "/" (a forced line break) hugging its neighbors.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var collapsed = Regex.Replace(text.ToUpperInvariant(), @"\s+", " ").Trim();
        return Regex.Replace(collapsed, @"\s*/\s*", "/").Trim('/');
    }

    /// <summary>
    /// Places a puzzle on the board. Without a "/" the words are wrapped into as few lines as will fit, as evenly as possible; with one,
    /// each "/" starts a new line exactly where the author put it. Fails with a plain explanation if the puzzle can't be shown.
    /// </summary>
    public static bool TryLayout(string? text, out BoardLayout layout, out string error)
    {
        layout = null!;
        error = "";
        var normalized = Normalize(text);
        if (normalized.Length == 0) { error = "The puzzle is empty."; return false; }

        foreach (var c in normalized)
        {
            if (c is ' ' or '/' || IsAllowed(c)) continue;
            error = $"The board can't show \"{c}\". Use letters, numbers and & ' - . , ! ? : \" only.";
            return false;
        }

        List<string> lines;
        int[] rows;
        if (normalized.Contains('/'))
        {
            lines = normalized.Split('/').Select(l => l.Trim()).ToList();
            if (lines.Any(l => l.Length == 0)) { error = "A line break can't leave an empty line."; return false; }
            if (lines.Count > Rows) { error = $"That is {lines.Count} lines; the board has {Rows} rows."; return false; }
            var fitting = RowChoices[lines.Count - 1].FirstOrDefault(choice => choice.Select((r, i) => lines[i].Length <= RowWidths[r]).All(ok => ok));
            if (fitting is null)
            {
                var longest = lines.OrderByDescending(l => l.Length).First();
                error = $"\"{longest}\" is {longest.Length} tiles, too long for its row.";
                return false;
            }
            rows = fitting;
        }
        else
        {
            var words = normalized.Split(' ');
            var tooLong = words.FirstOrDefault(w => w.Length > Columns);
            if (tooLong is not null) { error = $"\"{tooLong}\" is {tooLong.Length} letters; the widest row holds {Columns}."; return false; }

            var best = int.MaxValue;
            lines = [];
            rows = [];
            for (var k = 1; k <= Rows && lines.Count == 0; k++)
            {
                foreach (var choice in RowChoices[k - 1])
                {
                    var widths = choice.Select(r => RowWidths[r]).ToArray();
                    if (TryPartition(words, widths, out var candidate, out var cost) && cost < best)
                    {
                        best = cost;
                        lines = candidate;
                        rows = choice;
                    }
                }
            }
            if (lines.Count == 0)
            {
                error = $"That doesn't fit on the board ({normalized.Replace(" ", "").Length} letters; the board holds {Capacity} tiles and words can't split across rows).";
                return false;
            }
        }

        var tiles = new List<BoardTile>();
        for (var i = 0; i < lines.Count; i++)
        {
            var row = rows[i];
            var left = (RowWidths[row] - lines[i].Length) / 2;
            for (var p = 0; p < lines[i].Length; p++)
                if (lines[i][p] != ' ') tiles.Add(new BoardTile(row, RowOffsets[row] + left + p, lines[i][p]));
        }

        layout = new BoardLayout(normalized, lines, rows, tiles);
        return true;
    }

    /// <summary>Splits the words into exactly one line per width, in order, each line fitting its width, with the smallest total unevenness.</summary>
    private static bool TryPartition(string[] words, int[] widths, out List<string> lines, out int totalCost)
    {
        const int Impossible = int.MaxValue / 2;
        var n = words.Length;
        var k = widths.Length;
        var cost = new int[n + 1, k + 1];
        var next = new int[n + 1, k + 1];
        for (var i = 0; i <= n; i++)
            for (var j = 0; j <= k; j++) cost[i, j] = Impossible;
        cost[n, k] = 0;

        for (var j = k - 1; j >= 0; j--)
        {
            for (var i = n - 1; i >= 0; i--)
            {
                var length = -1;
                for (var e = i; e < n; e++)
                {
                    length += words[e].Length + 1;
                    if (length > widths[j]) break;
                    var rest = cost[e + 1, j + 1];
                    if (rest >= Impossible) continue;
                    var slack = widths[j] - length;
                    var c = slack * slack + rest;
                    if (c < cost[i, j])
                    {
                        cost[i, j] = c;
                        next[i, j] = e + 1;
                    }
                }
            }
        }

        lines = [];
        totalCost = cost[0, 0];
        if (totalCost >= Impossible) return false;
        var at = 0;
        for (var j = 0; j < k; j++)
        {
            var to = next[at, j];
            lines.Add(string.Join(' ', words[at..to]));
            at = to;
        }
        return true;
    }

    /// <summary>How the board should look before anyone has called a letter, as text: one string per row, "#" for a tile with a letter and "." for an unused one. Handy for checks.</summary>
    public static IReadOnlyList<string> Sketch(BoardLayout layout)
    {
        var grid = Enumerable.Range(0, Rows).Select(_ => new StringBuilder(new string('.', Columns))).ToArray();
        foreach (var t in layout.Tiles) grid[t.Row][t.Column] = '#';
        return grid.Select(sb => sb.ToString()).ToList();
    }
}
