using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>
/// Draws a <see cref="LanePlacement"/> into an <see cref="ArtGrid"/>: the lane labels, the hashes, the lines and forks, the merges,
/// the dots and the ghost. The rules are the mock's drawGraph. The passes run in a fixed order: a line only fills blank cells, so
/// the labels, hashes and forks drawn earlier win; the dots and the ghost are drawn last and replace what is under them.
/// </summary>
public static class LaneScene
{
    // A point where a line comes in from the left (or from a gap), with the style of the lane it belongs to.
    private readonly record struct LeadIn(int X, int Y, Style Style);

    /// <summary>Draws the placement into a new grid of p.Width × p.Height. An empty placement gives a grid with no rows.</summary>
    public static ArtGrid Render(LanePlacement p, Glyphs glyphs)
    {
        var grid = new ArtGrid(p.Width, p.Height);

        // The lead-in points found while drawing the lines. They get their dash mark after the lines are drawn.
        var leadIns = new List<LeadIn>();

        DrawLabels(grid, p);
        DrawHashes(grid, p);
        DrawLines(grid, p, glyphs, leadIns);
        DrawMerges(grid, p, glyphs);
        DrawGapMarks(grid, p, glyphs, leadIns);
        DrawDots(grid, p, glyphs);
        DrawGhost(grid, p, glyphs);
        DrawMore(grid, p);
        return grid;
    }

    // The ink of a lane's line, dots and corners: its branch colour when it is on, else the faint ink.
    private static Style LaneStyle(LanePlacement p, int lane)
    {
        if (p.IsOn(lane))
            return new Style(BranchInks.Of(p.Graph.Lanes[lane].ColorLane));
        return new Style(Ink.Faint);
    }

    // Pass 1: each kept lane's name. The selected branch's label gets the selection background, pad column included.
    private static void DrawLabels(ArtGrid grid, LanePlacement p)
    {
        for (var lane = 0; lane < p.Graph.Lanes.Count; lane++)
        {
            if (!p.IsKept(lane))
                continue;

            var info = p.Graph.Lanes[lane];
            var y = p.YOf(lane);
            var ink = Ink.Dim;
            if (p.IsOn(lane) && info.Kind != LaneKind.Side)
                ink = BranchInks.Of(info.ColorLane);

            grid.Text(LaneFit.LabelX, y, LaneFit.Label(info.Name, p.LabelWidth), new Style(ink));
            if (lane == p.SelectedLane)
            {
                for (var x = 0; x <= p.LabelWidth; x++)
                    grid.SetBg(x, y, CellBg.Sel);
            }
        }
    }

    // Pass 2: the short hash of each drawn commit, on the line above its row. Only when the placement has hashes.
    private static void DrawHashes(ArtGrid grid, LanePlacement p)
    {
        if (p.HashWidth <= 0)
            return;

        for (var lane = 0; lane < p.Graph.Lanes.Count; lane++)
        {
            if (!p.IsKept(lane))
                continue;

            var style = new Style(p.IsOn(lane) ? Ink.Hash : Ink.Faint);
            foreach (var commit in p.Graph.Lanes[lane].Commits)
            {
                if (commit.Column < p.FirstColumn)
                    continue;

                var x = p.XOf(commit.Column) - p.HashWidth / 2;
                grid.Text(x, p.YOf(lane) - 1, Shorten(commit.ShortHash, p.HashWidth), style);
            }
        }
    }

    // Pass 3: the lines. The lanes in colour go first, so their connectors win where two lines cross; the other lanes follow.
    private static void DrawLines(ArtGrid grid, LanePlacement p, Glyphs glyphs, List<LeadIn> leadIns)
    {
        for (var lane = 0; lane < p.Graph.Lanes.Count; lane++)
        {
            if (p.IsKept(lane) && p.IsOn(lane))
                DrawLane(grid, p, glyphs, lane, leadIns);
        }

        for (var lane = 0; lane < p.Graph.Lanes.Count; lane++)
        {
            if (p.IsKept(lane) && !p.IsOn(lane))
                DrawLane(grid, p, glyphs, lane, leadIns);
        }
    }

    // One lane's line: from where it starts (its fork, a lead-in from the left, or its first commit) to its newest commit.
    private static void DrawLane(ArtGrid grid, LanePlacement p, Glyphs glyphs, int lane, List<LeadIn> leadIns)
    {
        var info = p.Graph.Lanes[lane];
        var y = p.YOf(lane);
        var style = LaneStyle(p, lane);
        var fork = ForkInto(p.Graph, lane);

        // The lane's own gap (its hidden commits) is in the window: then the line starts at the gap mark, so it runs unbroken to the dot.
        var gapInWindow = info.Hidden > 0 && info.Gap >= p.FirstColumn;
        var start = gapInWindow ? p.XOf(info.Gap) : p.XOf(info.Commits[0].Column);
        var baseHasOlder = lane == 0 && p.Graph.BaseMore;
        var firstCut = info.Commits[0].Column < p.FirstColumn;

        if (fork != null && IsDrawnFork(p, fork))
        {
            DrawForkConnector(grid, p, glyphs, fork, y, style);
            start = p.XOf(fork.FromColumn) + 1;
        }
        else if (fork != null && fork.FromColumn < 0)
        {
            // The fork comes from older history than the window: the line comes in from the left, at the gap.
            start = info.Gap >= p.FirstColumn ? p.XOf(info.Gap) : p.GraphX - 1;
            leadIns.Add(new LeadIn(start, y, style));
        }
        else if (fork != null || baseHasOlder || firstCut)
        {
            // The fork is not drawn (its row is dropped, or it is cut off), or the base has older history: a lead-in from the left.
            // A lane with hidden commits keeps the gap mark it started at (set above), so the line runs unbroken from the gap to the first dot.
            if (!gapInWindow)
                start = Math.Max(p.GraphX - 1, p.XOf(info.Commits[0].Column) - 1);
            leadIns.Add(new LeadIn(start, y, style));
        }

        var lastX = LastX(p, info);
        for (var x = start; x <= lastX; x++)
            grid.PutIfBlank(x, y, glyphs.Line, style);
    }

    // The fork that starts a lane: the Fork link that lands on it. The base and the unrelated lanes have none (null).
    private static LaneLink? ForkInto(LaneGraph graph, int lane)
    {
        foreach (var link in graph.Links)
        {
            if (link.Kind == LinkKind.Fork && link.ToLane == lane)
                return link;
        }

        return null;
    }

    // A fork is drawn when its commit is in the window, its row is kept and that row is above the lane.
    private static bool IsDrawnFork(LanePlacement p, LaneLink fork) =>
        fork.FromColumn >= p.FirstColumn && p.IsKept(fork.FromLane) && p.YOf(fork.FromLane) < p.YOf(fork.ToLane);

    // The connector of a fork: a vertical line down from the parent row, then a corner into this lane's row.
    private static void DrawForkConnector(ArtGrid grid, LanePlacement p, Glyphs glyphs, LaneLink fork, int y, Style style)
    {
        var px = p.XOf(fork.FromColumn);
        var py = p.YOf(fork.FromLane);
        for (var v = py + 1; v < y; v++)
            grid.PutIfBlank(px, v, glyphs.VLine, style);

        // A tee when another lane also forks from this commit and lies further down: the line goes on past this corner.
        var corner = glyphs.ForkEnd;
        if (HasAnotherForkBelow(p, fork, y))
            corner = glyphs.ForkMid;
        grid.Put(px, y, corner, style);
    }

    // Whether another lane forks from the same commit and is kept below this row.
    private static bool HasAnotherForkBelow(LanePlacement p, LaneLink fork, int y)
    {
        foreach (var other in p.Graph.Links)
        {
            if (other.Kind != LinkKind.Fork || other.ToLane == fork.ToLane)
                continue;
            if (other.FromLane != fork.FromLane || other.FromColumn != fork.FromColumn)
                continue;
            if (p.IsKept(other.ToLane) && p.YOf(other.ToLane) > y)
                return true;
        }

        return false;
    }

    // Where a lane's line stops: its newest commit, or the left edge of the graph when that commit is cut off.
    private static int LastX(LanePlacement p, Lane info)
    {
        var column = info.Commits[info.Commits.Count - 1].Column;
        if (column < p.FirstColumn)
            return p.GraphX - 1;
        return p.XOf(column);
    }

    // Pass 4: the merges. The line from the merged lane runs to the merge commit, then turns up or down into it.
    private static void DrawMerges(ArtGrid grid, LanePlacement p, Glyphs glyphs)
    {
        // Coloured connectors claim shared cells before faint connectors, as in DrawLines.
        for (var pass = 0; pass < 2; pass++)
        {
            var on = pass == 0;
            foreach (var link in p.Graph.Links)
            {
                if (link.Kind != LinkKind.Merge || !p.IsKept(link.FromLane) || !p.IsKept(link.ToLane))
                    continue;
                if (link.ToColumn < p.FirstColumn || p.IsOn(link.FromLane) != on)
                    continue;

                DrawMerge(grid, p, glyphs, link);
            }
        }
    }

    private static void DrawMerge(ArtGrid grid, LanePlacement p, Glyphs glyphs, LaneLink link)
    {
        var fy = p.YOf(link.FromLane);
        var ty = p.YOf(link.ToLane);
        var mx = p.XOf(link.ToColumn);
        var fromX = Math.Max(p.XOf(link.FromColumn), p.GraphX - 1);
        var style = LaneStyle(p, link.FromLane);

        for (var x = fromX + 1; x < mx; x++)
            grid.PutIfBlank(x, fy, glyphs.Line, style);

        if (fy > ty)
        {
            // The merged row is below the merge commit: the line goes up into it.
            PutCorner(grid, mx, fy, glyphs.MergeUp, glyphs.TeeUp, glyphs, style);
            for (var v = ty + 1; v < fy; v++)
                grid.PutIfBlank(mx, v, glyphs.VLine, style);
        }
        else if (fy < ty)
        {
            // The merged row is above the merge commit (merging the base into a branch): the line goes down into it.
            PutCorner(grid, mx, fy, glyphs.MergeDown, glyphs.TeeDown, glyphs, style);
            for (var v = fy + 1; v < ty; v++)
                grid.PutIfBlank(mx, v, glyphs.VLine, style);
        }
    }

    // A corner where a merge line meets the merge commit's column: on a blank cell it is the corner, on a line it is a tee.
    private static void PutCorner(ArtGrid grid, int x, int y, char corner, char tee, Glyphs glyphs, Style style)
    {
        var ch = grid[x, y].Ch;
        if (ch == ' ')
            grid.Put(x, y, corner, style);
        else if (ch == glyphs.Line)
            grid.Put(x, y, tee, style);
        else if ((ch == glyphs.MergeUp && corner == glyphs.MergeDown) ||
                 (ch == glyphs.MergeDown && corner == glyphs.MergeUp))
            grid.Put(x, y, glyphs.MergeMid, grid[x, y].Style);
    }

    // Pass 5: the dashed marks where history is left out: every lead-in point, and the gap of each lane with hidden commits.
    private static void DrawGapMarks(ArtGrid grid, LanePlacement p, Glyphs glyphs, List<LeadIn> leadIns)
    {
        foreach (var mark in leadIns)
            grid.Put(mark.X, mark.Y, glyphs.Gap, mark.Style);

        for (var lane = 0; lane < p.Graph.Lanes.Count; lane++)
        {
            var info = p.Graph.Lanes[lane];
            if (p.IsKept(lane) && info.Hidden > 0 && info.Gap >= p.FirstColumn)
                grid.Put(p.XOf(info.Gap), p.YOf(lane), glyphs.Gap, LaneStyle(p, lane));
        }
    }

    // Pass 6: the dots, one per drawn commit. Merge commits are dots too.
    private static void DrawDots(ArtGrid grid, LanePlacement p, Glyphs glyphs)
    {
        for (var lane = 0; lane < p.Graph.Lanes.Count; lane++)
        {
            if (!p.IsKept(lane))
                continue;

            var style = LaneStyle(p, lane);
            var y = p.YOf(lane);
            foreach (var commit in p.Graph.Lanes[lane].Commits)
            {
                if (commit.Column >= p.FirstColumn)
                    grid.Put(p.XOf(commit.Column), y, glyphs.Dot, style);
            }
        }
    }

    // Pass 7: the ghost at the right end. Its dashed trail runs from the base's newest commit, and from the selected branch's tip
    // when that tip is drawn; then the glyph, and the label after it.
    private static void DrawGhost(ArtGrid grid, LanePlacement p, Glyphs glyphs)
    {
        if (p.Ghost == GhostKind.None)
            return;

        var style = new Style(GhostInk(p.Ghost));
        var gx = p.GhostX;
        var my = p.YOf(0);

        var bx = LastX(p, p.Graph.Lanes[0]);
        for (var x = bx + 1; x < gx; x++)
            grid.PutIfBlank(x, my, glyphs.DashH, style);

        if (p.TipLane > 0 && p.IsKept(p.TipLane) && p.TipColumn >= 0)
            DrawGhostTip(grid, p, glyphs, style, my, gx);

        grid.Put(gx, my, GhostGlyph(p.Ghost, glyphs), style);
        if (p.GhostLabel != "")
            grid.Text(gx + 2, my, p.GhostLabel, style);
    }

    // The selected branch's tip, joined to the ghost: a dashed run along its row, a dashed line down to the base row, and a corner.
    private static void DrawGhostTip(ArtGrid grid, LanePlacement p, Glyphs glyphs, Style style, int my, int gx)
    {
        var ty = p.YOf(p.TipLane);
        var tx = p.TipColumn >= p.FirstColumn ? p.XOf(p.TipColumn) : p.GraphX - 1;
        for (var x = tx + 1; x < gx; x++)
            grid.PutIfBlank(x, ty, glyphs.DashH, style);
        for (var v = my + 1; v < ty; v++)
            grid.PutIfBlank(gx, v, glyphs.DashV, style);
        grid.Put(gx, ty, glyphs.MergeUp, style);
    }

    private static Ink GhostInk(GhostKind kind)
    {
        if (kind == GhostKind.Merge)
            return Ink.Mrg;
        if (kind == GhostKind.FastForward)
            return Ink.Ok;
        return Ink.Bad;
    }

    private static char GhostGlyph(GhostKind kind, Glyphs glyphs)
    {
        if (kind == GhostKind.Merge)
            return glyphs.GhostEmpty;
        if (kind == GhostKind.FastForward)
            return glyphs.FastForward;
        return glyphs.Conflict;
    }

    // Pass 8: the "+N more branches" line under the last row, when it fits.
    private static void DrawMore(ArtGrid grid, LanePlacement p)
    {
        if (!p.ShowMore)
            return;

        var last = p.RowY[p.RowY.Count - 1];
        grid.Text(LaneFit.LabelX, last + 1, LaneFit.MoreText(p.More), new Style(Ink.Dim));
    }

    // A hash cut to the placement's hash width.
    private static string Shorten(string hash, int width) => hash.Length <= width ? hash : hash.Substring(0, width);
}
