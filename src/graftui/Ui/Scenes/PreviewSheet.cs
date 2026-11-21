using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>One text line of the sheet: its sheet row and its segments. The view draws it at the text column, cut to the text width.</summary>
public sealed record SheetLine(int Row, IReadOnlyList<Seg> Segs);

/// <summary>
/// The right pane's browse content: text lines, plus the lane graph grid drawn at sheet row GraphRow, one column left of the text
/// (the grid's column 0 is the pad column that holds the selection background).
/// </summary>
public sealed record PreviewSheet(IReadOnlyList<SheetLine> Lines, ArtGrid Graph, int GraphRow);

/// <summary>Everything the sheet is made from: the layout, the forecast (null while it is read), the lane graph, and the clean flag.</summary>
public sealed record PreviewSheetInput(GraphLayout Layout, MergeForecast? Forecast, LaneGraph Graph, bool Clean);

/// <summary>What the merge card's sheet is made from: the pre-merge layout, the pre-merge lane graph, and the card.</summary>
public sealed record DigestSheetInput(GraphLayout Layout, LaneGraph Graph, MergeCard Card);

/// <summary>
/// Makes the browse sheet of the Branches preview: the head lines, the lane graph and the incoming commits. Pure: no git, no views.
/// Sheet rows: 0 the forecast, 1 the command, 2 blank, then the graph, a blank row, and the incoming list.
/// </summary>
public static class PreviewSheets
{
    /// <summary>Rows before the graph: forecast, command, blank.</summary>
    public const int HeadRows = 3;

    /// <summary>Rows the graph gives up its row spacing for: blank, title, three commits.</summary>
    public const int ListReserve = 5;

    /// <summary>The fewest rows the list is shown with: blank, title, one commit.</summary>
    public const int ListMin = 3;

    /// <summary>The column of a commit's subject: the 7-character hash and two spaces.</summary>
    public const int SubjectX = 9;

    // The hash column of the incoming list is cut to this many characters.
    private const int HashCut = 7;

    private static readonly Style Dim = new(Ink.Dim);
    private static readonly Style Fg = new(Ink.Fg);
    private static readonly Style HashStyle = new(Ink.Hash);

    // The gap between a commit's hash and its subject: SubjectX columns in all, less the hash's width.
    private static readonly string HashGap = new string(' ', SubjectX - HashCut);

    /// <summary>
    /// The sheet for a text width and a number of rows (the pane's inner size). Never throws for any size of zero or more.
    /// </summary>
    public static PreviewSheet Build(PreviewSheetInput input, int textWidth, int rows, Glyphs glyphs)
    {
        var L = input.Layout;
        var lines = new List<SheetLine>();
        AddHeadLine(lines, 0, PreviewText.Forecast(L, input.Forecast, input.Clean), rows);
        AddHeadLine(lines, 1, PreviewText.Command(L), rows);

        var ghost = PreviewText.Ghost(L, input.Forecast);
        return Compose(lines, L, input.Graph, ghost, "", !PreviewText.IsCurrent(L), PreviewText.IncomingTitle, textWidth, rows, glyphs);
    }

    /// <summary>
    /// The card's sheet after m: row 0 the headline with the key hint at its right edge, row 1 the detail, then the pre-merge graph with the
    /// done ghost, and the commits that came in (or the usual incoming list, for a refusal or a conflict).
    /// </summary>
    public static PreviewSheet BuildDigest(DigestSheetInput input, int textWidth, int rows, Glyphs glyphs)
    {
        var card = input.Card;
        var L = input.Layout;
        var lines = new List<SheetLine>();
        AddHeadLine(lines, 0, DigestText.WithHint(DigestText.Headline(card), DigestText.Hint(card), textWidth), rows);
        AddHeadLine(lines, 1, DigestText.Detail(card), rows);

        // A merge that did nothing has no list; the others list the commits the branch brought (or would bring).
        var listShown = card.Kind != DigestKind.UpToDate && !PreviewText.IsCurrent(L);
        var hash = card.Hash ?? "";
        return Compose(lines, L, input.Graph, DigestText.Ghost(card), hash, listShown, DigestText.ListTitle(card), textWidth, rows, glyphs);
    }

    /// <summary>
    /// The sheet while git runs: the browse sheet, with row 0 replaced by the running line. Row 1 stays "$ git merge X" and the ghost
    /// stays the preview's.
    /// </summary>
    public static PreviewSheet BuildRunning(PreviewSheetInput input, int textWidth, int rows, Glyphs glyphs)
    {
        var L = input.Layout;
        var lines = new List<SheetLine>();
        var into = L.Detached ? "" : L.Current;
        AddHeadLine(lines, 0, DigestText.Running(L.Selected, into), rows);
        AddHeadLine(lines, 1, PreviewText.Command(L), rows);

        var ghost = PreviewText.Ghost(L, input.Forecast);
        return Compose(lines, L, input.Graph, ghost, "", !PreviewText.IsCurrent(L), PreviewText.IncomingTitle, textWidth, rows, glyphs);
    }

    /// <summary>
    /// Adds the graph and the list below the head lines. The graph takes the rows left after the head; the list is shown when it is
    /// listShown, the branch has commits to list, and there is room for its blank row, title and one commit.
    /// </summary>
    private static PreviewSheet Compose(List<SheetLine> lines, GraphLayout L, LaneGraph graph, GhostKind ghost, string ghostHash,
        bool listShown, string listTitle, int textWidth, int rows, Glyphs glyphs)
    {
        // The grid is one column wider than the text: its column 0 is the pad column.
        var gridWidth = textWidth + 1;
        var budget = rows - HeadRows;
        if (budget < 1)
            return new PreviewSheet(lines, new ArtGrid(gridWidth, 0), HeadRows);

        var placement = FitGraph(graph, L.Selected, ghost, gridWidth, budget, ghostHash);
        var grid = LaneScene.Render(placement, glyphs, ghostHash);

        // The incoming list: not for the current branch, and only when there is room for its blank row, title and one commit.
        if (listShown && L.SelectedNew.Count > 0)
        {
            var free = budget - placement.Height;
            if (free >= ListMin)
            {
                var firstRow = HeadRows + placement.Height + 1;
                lines.AddRange(Incoming(L, firstRow, rows - firstRow, listTitle));
            }
        }

        return new PreviewSheet(lines, grid, HeadRows);
    }

    /// <summary>
    /// The graph's placement for a budget of rows. It keeps the spacing of the full budget when the list would still have its reserve;
    /// otherwise it takes the closer spacing that keeps the same rows. The spacing does not depend on the selection, so the graph
    /// does not move while the branches are browsed.
    /// </summary>
    internal static LanePlacement FitGraph(LaneGraph g, string selected, GhostKind ghost, int gridWidth, int budget, string ghostHash = "")
    {
        var full = LaneFit.Fit(g, selected, ghost, gridWidth, budget, ghostHash);
        if (budget - full.Height >= ListReserve)
            return full;

        var close = LaneFit.Fit(g, selected, ghost, gridWidth, budget - ListReserve, ghostHash);
        if (close.Height > 0 && close.Rows.Count == full.Rows.Count)
            return close;
        return full;
    }

    /// <summary>
    /// The incoming list with the usual title: see <see cref="Incoming(GraphLayout, int, int, string)"/>.
    /// </summary>
    internal static IReadOnlyList<SheetLine> Incoming(GraphLayout L, int firstRow, int rows) =>
        Incoming(L, firstRow, rows, PreviewText.IncomingTitle);

    /// <summary>
    /// The incoming list: a title at firstRow, then the selected branch's new commits, newest first, all within rows rows. When
    /// there are more commits than fit, the last row (or the title in a two-row list) says how many are left out.
    /// </summary>
    internal static IReadOnlyList<SheetLine> Incoming(GraphLayout L, int firstRow, int rows, string title)
    {
        var lines = new List<SheetLine>();
        if (rows < 1)
            return lines;

        lines.Add(new SheetLine(firstRow, new List<Seg> { new Seg(title, Dim) }));

        // The title takes one row; the rest are commit slots.
        var slots = rows - 1;
        if (slots < 1)
            return lines;

        var count = L.SelectedNew.Count;
        var total = Math.Max(L.Ahead, count);
        var shown = count;
        var more = 0;
        if (total > slots || count < total)
        {
            // Reserve a count row when possible, keeping at least one commit even when git's list is capped.
            shown = Math.Min(count, Math.Max(1, slots - 1));
            more = total - shown;
        }

        for (var i = 0; i < shown; i++)
            lines.Add(CommitLine(L.SelectedNew[i], firstRow + 1 + i));

        if (more > 0 && slots == 1 && count > 0)
        {
            // Keep a commit visible; put the count first so narrow panes retain it when the title is clipped.
            lines[0] = new SheetLine(firstRow, new List<Seg> { new Seg("+" + more + " more · " + title, Dim) });
        }
        else if (more > 0)
            lines.Add(new SheetLine(firstRow + 1 + shown, new List<Seg> { new Seg("+" + more + " more", Dim) }));
        return lines;
    }

    // One commit of the list: its short hash, two spaces, and its subject.
    private static SheetLine CommitLine(CommitInfo commit, int row)
    {
        var hash = commit.ShortHash;
        if (hash.Length > HashCut)
            hash = hash.Substring(0, HashCut);

        var segs = new List<Seg>
        {
            new Seg(hash, HashStyle),
            new Seg(HashGap, Fg),
            new Seg(commit.Subject, Fg),
        };
        return new SheetLine(row, segs);
    }

    // Adds a head line when it has segments and its row is inside the rows. A row with no room gets no line.
    private static void AddHeadLine(List<SheetLine> lines, int row, IReadOnlyList<Seg> segs, int rows)
    {
        if (segs.Count > 0 && row < rows)
            lines.Add(new SheetLine(row, segs));
    }
}
