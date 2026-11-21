using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>
/// What the ghost at the right end shows. None: nothing. Merge (◌), FastForward (⇢) and Conflict (✕): the dashed previews of what m
/// would do. Merged, FastForwarded and Stopped: the done states after m, the merge commit drawn solid, the branch's lane drawn on the
/// base row, and the conflict's ✕ with "stopped".
/// </summary>
public enum GhostKind { None, Merge, FastForward, Conflict, Merged, FastForwarded, Stopped }

/// <summary>
/// Where everything of a lane graph goes in a grid. Made by <see cref="LaneFit.Fit"/>, drawn by the lane scene.
/// Width is the grid's width. Height is the rows actually used (0 for an empty placement), not the height that was asked for.
/// Rows: the kept lanes, ascending. RowY: the y of each kept lane (same order). OnRows: the lanes drawn in colour (the base, and what
/// would come in), ascending. SelectedLane: the selected branch's own lane (its label gets the selection background), or -1.
/// TipLane and TipColumn: where the selected branch's tip is, or -1. HashWidth: 7, 5 or 0 (no hashes). GhostLabel: the text right of the
/// ghost glyph ("" = none). More: branches not drawn (dropped, plus the ones git left out). ShowMore: the "+N more" line fits.
/// </summary>
public sealed record LanePlacement(
    LaneGraph Graph, string Selected, GhostKind Ghost, int Width, int Height,
    IReadOnlyList<int> Rows, IReadOnlyList<int> RowY, IReadOnlyList<int> OnRows,
    int SelectedLane, int TipLane, int TipColumn,
    int LabelWidth, int GraphX, int Step, int FirstColumn, int RowGap, int HashWidth,
    string GhostLabel, int More, bool ShowMore)
{
    /// <summary>The x of a column: GraphX + (column - FirstColumn) * Step.</summary>
    public int XOf(int column) => GraphX + (column - FirstColumn) * Step;

    /// <summary>The y of a lane's row, or -1 when the lane is not kept.</summary>
    public int YOf(int lane)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i] == lane)
                return RowY[i];
        }

        return -1;
    }

    /// <summary>Whether the lane has a row in this placement.</summary>
    public bool IsKept(int lane) => YOf(lane) >= 0;

    /// <summary>Whether the lane is drawn in its colour.</summary>
    public bool IsOn(int lane) => OnRows.Contains(lane);

    /// <summary>The x of the ghost, which sits in the column after the newest commit.</summary>
    public int GhostX => XOf(Graph.Columns);

    /// <summary>
    /// The lane drawn on the base row after a fast-forward (FastForwarded): the selected branch's own lane, when it forks from the base
    /// inside the window. -1 when there is none: another ghost kind, a branch with no lane of its own (a pointer), a tip that is not on
    /// its lane, a lane that is not kept, hidden commits, or a fork from older history or from another lane.
    /// </summary>
    public int LiftedLane
    {
        get
        {
            if (Ghost != GhostKind.FastForwarded || SelectedLane <= 0 || TipLane != SelectedLane || !IsKept(SelectedLane))
                return -1;
            if (Graph.Lanes[SelectedLane].Hidden > 0)
                return -1;

            foreach (var link in Graph.Links)
            {
                if (link.Kind == LinkKind.Fork && link.ToLane == SelectedLane)
                    return link.FromLane == 0 && link.FromColumn >= FirstColumn ? SelectedLane : -1;
            }

            return -1;
        }
    }

    /// <summary>The y a lane's line, dots and hashes are drawn on: the base row for the lifted lane, else the lane's own row (YOf).</summary>
    public int LineYOf(int lane)
    {
        var lifted = LiftedLane;
        if (lane >= 0 && lane == lifted)
            return YOf(0);
        return YOf(lane);
    }
}

/// <summary>
/// Fits a lane graph into a grid of a given size: how wide the labels are, how far apart the columns and rows are, which lanes are
/// kept when there is no room for all of them. Pure: no screen and no git. The rules are the mock's, in the order they run here.
/// </summary>
public static class LaneFit
{
    /// <summary>The column of the lane labels.</summary>
    public const int LabelX = 1;

    /// <summary>Label width: the shortest and the longest a label column may be.</summary>
    public const int MinLabel = 4;
    public const int MaxLabel = 14;

    /// <summary>Column step: the least, the most, and the step the columns want when there is room.</summary>
    public const int MinStep = 2;
    public const int ComfortStep = 4;
    public const int MaxStep = 16;

    /// <summary>The largest gap between two rows.</summary>
    public const int MaxRowGap = 3;

    /// <summary>Narrower than this, nothing is drawn.</summary>
    public const int MinWidth = 12;

    /// <summary>The narrowest the ghost's label may be before it is left out.</summary>
    public const int GhostLabelMin = 8;

    // The room kept to the right of the newest column for the ghost: its glyph and a space (GhostLead), then the longest ghost label,
    // which is at least GhostTextFloor wide and otherwise the base's name plus BaseNameRoom (" moves here").
    private const int GhostLead = 3;
    private const int GhostTextFloor = 16;
    private const int BaseNameRoom = 11;

    /// <summary>
    /// The placement of the graph for a grid of width × height, with <paramref name="selected"/> highlighted and the ghost of kind
    /// <paramref name="ghost"/>. <paramref name="ghostHash"/> is the merge commit's hash for the Merged ghost ("" when there is none).
    /// <paramref name="ghostVerified"/> is false when graftui could not verify a stopped merge: its label then says "stopped" only.
    /// An empty placement (no lanes, too narrow, or no height) has no rows.
    /// </summary>
    public static LanePlacement Fit(LaneGraph graph, string selected, GhostKind ghost, int width, int height, string ghostHash = "",
        bool ghostVerified = true)
    {
        if (graph.Lanes.Count == 0 || width < MinWidth || height < 1)
            return EmptyPlacement(graph, selected);

        // The base has no ghost and no lane of its own to light; any other branch lights its own lane and its tip.
        var isBase = selected == graph.BaseName;
        var shownGhost = ghost;
        var selectedLane = -1;
        var tipLane = -1;
        var tipColumn = -1;
        if (isBase)
        {
            shownGhost = GhostKind.None;
        }
        else
        {
            selectedLane = graph.LaneOf(selected);
            var tip = graph.TipOf(selected);
            tipLane = tip.Lane;
            tipColumn = tip.Column;
        }

        var onRows = OnRowsFor(graph, selected, selectedLane, tipLane);
        var labelWidth = LabelWidthFor(graph, width);
        var graphX = LabelX + labelWidth + 2;
        var room = RoomFor(graph, width, graphX);
        var firstColumn = FirstColumnFor(graph.Columns, room);
        var step = StepFor(graph.Columns, firstColumn, room);
        var hashWidth = HashWidthFor(step);

        var plan = PlanRows(graph, tipLane, onRows, hashWidth, height);
        var rows = new List<int>();
        var rowY = new List<int>();
        for (var lane = 0; lane < graph.Lanes.Count; lane++)
        {
            if (plan.Kept[lane])
            {
                rows.Add(lane);
                rowY.Add(plan.Offset + (rows.Count - 1) * plan.RowGap);
            }
        }

        var more = MoreFor(graph, plan.Kept);
        var lastY = rowY[rowY.Count - 1];
        var showMore = more > 0 && lastY + 1 < height;
        var usedHeight = lastY + 1 + (showMore ? 1 : 0);

        // The hashes sit on the line above each row, so they need that line (offset 1) to be drawn at all.
        var shownHashWidth = plan.Offset == 1 ? hashWidth : 0;

        var placement = new LanePlacement(
            graph, selected, shownGhost, width, usedHeight,
            rows, rowY, onRows,
            selectedLane, tipLane, tipColumn,
            labelWidth, graphX, step, firstColumn, plan.RowGap, shownHashWidth,
            "", more, showMore);

        // The ghost label goes last: it depends on where the ghost sits.
        var ghostLabel = GhostLabelFor(graph, shownGhost, width, placement.GhostX, ghostHash, ghostVerified);
        return placement with { GhostLabel = ghostLabel };
    }

    /// <summary>The lane label: the part after the last '/' ("feature/conflict" gives "conflict"), then cut to width.</summary>
    internal static string Label(string name, int width)
    {
        var part = name;
        var slash = name.LastIndexOf('/');
        if (slash >= 0 && slash < name.Length - 1)
            part = name.Substring(slash + 1);

        return TextFit.Fit(part, width);
    }

    /// <summary>
    /// The text after the ghost glyph: "new merge commit", "&lt;base&gt; moves here", "no commit", "merge commit &lt;hash7&gt;" (Merged),
    /// "stopped · &lt;base&gt; unchanged" (Stopped), or "" for no ghost and for a fast-forward (its lane has no text).
    /// </summary>
    internal static string GhostText(GhostKind kind, string baseName, string hash = "")
    {
        if (kind == GhostKind.Merge)
            return "new merge commit";
        if (kind == GhostKind.FastForward)
            return baseName + " moves here";
        if (kind == GhostKind.Conflict)
            return "no commit";
        if (kind == GhostKind.Merged)
            return MergedText(hash);
        if (kind == GhostKind.Stopped)
            return "stopped · " + baseName + " unchanged";
        return "";
    }

    /// <summary>
    /// The full text of the ghost's label. A stop that graftui could not verify says "stopped" only: "main unchanged" is a claim that
    /// only a verified merge makes. Every other case is <see cref="GhostText"/>.
    /// </summary>
    internal static string GhostFullText(GhostKind kind, string baseName, string hash, bool verified)
    {
        if (kind == GhostKind.Stopped && !verified)
            return GhostShortText(kind, baseName);
        return GhostText(kind, baseName, hash);
    }

    /// <summary>
    /// The shorter text for a narrow pane: "merge commit" (Merged) and "stopped" (Stopped). Every other kind has the same text as
    /// <see cref="GhostText"/>.
    /// </summary>
    internal static string GhostShortText(GhostKind kind, string baseName)
    {
        if (kind == GhostKind.Merged)
            return "merge commit";
        if (kind == GhostKind.Stopped)
            return "stopped";
        return GhostText(kind, baseName);
    }

    // "merge commit" and the hash cut to seven characters after it. Without a hash, the words alone.
    private static string MergedText(string hash)
    {
        if (hash.Length == 0)
            return "merge commit";
        var seven = hash.Length > 7 ? hash.Substring(0, 7) : hash;
        return "merge commit " + seven;
    }

    /// <summary>"+1 more branch", or "+N more branches".</summary>
    internal static string MoreText(int count)
    {
        if (count == 1)
            return "+1 more branch";
        return "+" + count + " more branches";
    }

    private static LanePlacement EmptyPlacement(LaneGraph graph, string selected) =>
        new(graph, selected, GhostKind.None, Width: 0, Height: 0, Rows: [], RowY: [], OnRows: [],
            SelectedLane: -1, TipLane: -1, TipColumn: -1, LabelWidth: 0, GraphX: 0, Step: 0, FirstColumn: 0,
            RowGap: 0, HashWidth: 0, GhostLabel: "", More: 0, ShowMore: false);

    // The lanes drawn in colour: the base, the selected branch's incoming lanes, and its tip lane. A pointer branch (no lane of its
    // own) lights the lane its tip sits on, and what would come in with that lane.
    private static List<int> OnRowsFor(LaneGraph graph, string selected, int selectedLane, int tipLane)
    {
        var on = new SortedSet<int> { 0 };
        if (selectedLane >= 0)
        {
            on.UnionWith(graph.IncomingLanes(selected));
        }
        else if (tipLane > 0 && graph.Lanes[tipLane].Kind == LaneKind.Branch)
        {
            on.UnionWith(graph.IncomingLanes(graph.Lanes[tipLane].Name));
        }

        if (tipLane >= 0)
            on.Add(tipLane);

        return on.ToList();
    }

    // The label column is as wide as the widest label of any lane (dropped ones too, so it does not move), within the limits.
    private static int LabelWidthFor(LaneGraph graph, int width)
    {
        var widest = 0;
        foreach (var lane in graph.Lanes)
            widest = Math.Max(widest, TextFit.Columns(Label(lane.Name, int.MaxValue)));

        var cap = Math.Max(MinLabel, Math.Min(MaxLabel, width / 3));
        return Math.Clamp(widest, MinLabel, cap);
    }

    // The room for the columns: the width less the label column and the space the ghost needs. The ghost is always reserved, so the
    // graph does not move when the selection changes. When the columns would be cramped, the ghost label gives its space up.
    private static int RoomFor(LaneGraph graph, int width, int graphX)
    {
        var reserve = GhostLead + Math.Max(GhostTextFloor, TextFit.Columns(graph.BaseName) + BaseNameRoom);
        var room = width - graphX - reserve;
        if (graph.Columns > 0 && room / graph.Columns < ComfortStep)
            room = width - graphX - 1;
        return room;
    }

    // When even the minimum step does not fit, the oldest columns are cut from the left.
    private static int FirstColumnFor(int lastColumn, int room)
    {
        if (lastColumn > 0 && room < MinStep * lastColumn)
            return lastColumn - Math.Max(0, room / MinStep);
        return 0;
    }

    private static int StepFor(int lastColumn, int firstColumn, int room)
    {
        var span = lastColumn - firstColumn;
        if (span == 0)
            return MaxStep;
        return Math.Clamp(room / span, MinStep, MaxStep);
    }

    // Hashes are wider than the step allows below 6 columns, so the step decides how many characters show.
    private static int HashWidthFor(int step)
    {
        if (step >= 8)
            return 7;
        if (step >= 6)
            return 5;
        return 0;
    }

    // How the rows are spaced: the gap between two rows, the line above the first row (1 when the hashes need it), and the lanes kept.
    private readonly record struct RowPlan(int RowGap, int Offset, bool[] Kept);

    // The largest gap that keeps every lane. When none fits, the gap is 1 and the lanes are chosen by priority, leaving one line for "+N more".
    private static RowPlan PlanRows(LaneGraph graph, int tipLane, IReadOnlyList<int> onRows, int hashWidth, int height)
    {
        var lanes = graph.Lanes.Count;
        var extra = graph.LeftOut > 0 ? 1 : 0;
        for (var gap = MaxRowGap; gap >= 1; gap--)
        {
            var offset = gap >= 2 && hashWidth > 0 ? 1 : 0;
            var need = offset + (lanes - 1) * gap + 1 + extra;
            if (need <= height)
                return new RowPlan(gap, offset, AllKept(lanes));
        }

        var limit = height >= 2 ? height - 1 : 1;
        return new RowPlan(1, 0, ChooseLanes(graph, tipLane, onRows, limit, height));
    }

    private static bool[] AllKept(int lanes)
    {
        var kept = new bool[lanes];
        for (var lane = 0; lane < lanes; lane++)
            kept[lane] = true;
        return kept;
    }

    // Keeps up to limit lanes: the base, the tip lane and the lanes that light up come first; then the other lanes in row order.
    // A side or stacked lane is kept only when the row it hangs under is kept, except for a priority lane, see KeepPriorityLane.
    private static bool[] ChooseLanes(LaneGraph graph, int tipLane, IReadOnlyList<int> onRows, int limit, int height)
    {
        var lanes = graph.Lanes.Count;
        var kept = new bool[lanes];
        var count = 0;

        var priority = new List<int> { 0 };
        if (tipLane >= 0)
            priority.Add(tipLane);
        priority.AddRange(onRows);
        foreach (var lane in priority)
        {
            if (!kept[lane])
                count = KeepPriorityLane(graph, lane, kept, count, limit, height);
        }

        for (var lane = 0; lane < lanes; lane++)
        {
            if (count < limit && !kept[lane] && ParentKept(graph, lane, kept))
            {
                kept[lane] = true;
                count++;
            }
        }

        return kept;
    }

    // A priority lane whose parent rows are all kept is kept while there is room under the limit. A priority lane with a parent row
    // missing (a stacked branch) brings those rows with it when the lane and them fit under the limit. When they do not fit, the lane
    // is kept anyway, as long as the rows still fit the height: its parent row is then not drawn, and 2b draws the lane with a lead-in
    // from the left. Returns the new count of kept lanes.
    private static int KeepPriorityLane(LaneGraph graph, int lane, bool[] kept, int count, int limit, int height)
    {
        var missing = MissingParents(graph, lane, kept);
        if (missing.Count == 0)
        {
            if (count < limit)
            {
                kept[lane] = true;
                return count + 1;
            }
            return count;
        }

        if (count + missing.Count + 1 <= limit)
        {
            foreach (var parent in missing)
                kept[parent] = true;
            kept[lane] = true;
            return count + missing.Count + 1;
        }

        if (count < height)
        {
            kept[lane] = true;
            return count + 1;
        }
        return count;
    }

    // The parent rows of a lane that are not kept yet, nearest first. Stops at the first kept parent, at the base and at the unrelated lanes.
    private static List<int> MissingParents(LaneGraph graph, int lane, bool[] kept)
    {
        var missing = new List<int>();
        var parent = graph.Lanes[lane].Parent;
        while (parent > 0 && !kept[parent] && missing.Count < graph.Lanes.Count)
        {
            missing.Add(parent);
            parent = graph.Lanes[parent].Parent;
        }

        return missing;
    }

    // A lane whose parent is a branch row (Parent > 0) needs that row kept. The base (0) and the unrelated lanes (-1) need nothing.
    private static bool ParentKept(LaneGraph graph, int lane, bool[] kept)
    {
        var parent = graph.Lanes[lane].Parent;
        return parent <= 0 || kept[parent];
    }

    // The branches not drawn: the dropped branch lanes (side lanes are not branches), plus the ones git left out.
    private static int MoreFor(LaneGraph graph, bool[] kept)
    {
        var more = graph.LeftOut;
        for (var lane = 0; lane < graph.Lanes.Count; lane++)
        {
            if (!kept[lane] && graph.Lanes[lane].Kind == LaneKind.Branch)
                more++;
        }

        return more;
    }

    // The ghost's label: the whole text when it fits to the right of the ghost, else the short text when that fits, else the short
    // text cut when at least GhostLabelMin columns are left, else none.
    private static string GhostLabelFor(LaneGraph graph, GhostKind kind, int width, int ghostX, string hash, bool verified)
    {
        if (kind == GhostKind.None)
            return "";

        var avail = width - (ghostX + 2);
        var text = GhostFullText(kind, graph.BaseName, hash, verified);
        if (TextFit.Columns(text) <= avail)
            return text;

        var shortText = GhostShortText(kind, graph.BaseName);
        if (TextFit.Columns(shortText) <= avail)
            return shortText;
        if (avail >= GhostLabelMin)
            return TextFit.Fit(shortText, avail);
        return "";
    }
}
