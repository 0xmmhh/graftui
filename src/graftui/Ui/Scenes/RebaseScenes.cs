using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>
/// The rebase scenes: the branches before a rebase, drawn from a layout, the plan rows and a cursor, so a frame is a function of its
/// inputs only.
/// </summary>
public static class RebaseScenes
{
    /// <summary>The most landing ghosts drawn on row B; a longer landing ends in a dim ┄ tail instead.</summary>
    private const int MaxLanding = 3;

    private static readonly Style Dim = new(Ink.Dim), FgB = new(Ink.Fg, true);
    private static readonly Style Bad = new(Ink.Bad), BadB = new(Ink.Bad, true), MrgB = new(Ink.Mrg, true), WarnB = new(Ink.Warn, true);

    /// <summary>
    /// The branches before a rebase. Row A is the current branch's own commits, and each dot takes its row's action: a drop is ✕, a fold a
    /// bold merge dot, a reword or edit a bold warning dot. Row B is the branch it goes onto, with one dim ◌ for each commit that lands
    /// after its tip. <paramref name="cursor"/> is the plan row the cursor is on (-1 for none). <paramref name="dim"/> draws row A dim, for
    /// a rebase that is running.
    /// </summary>
    public static SceneFrame Preview(ArtGeometry g, GraphLayout L, IReadOnlyList<RebaseRow> rows, int cursor, bool dim = false)
    {
        var grid = new ArtGrid(g.Width, g.Height);
        var cur = new Style(BranchInks.Current(L), true);
        var sel = new Style(BranchInks.Selected(L));
        MergeScenes.DrawShared(grid, g, L);
        MergeScenes.DrawRowA(grid, g, L.CurrentOwn, dim ? Dim : cur, L.CurrentOwnMore);
        if (!dim)
            MarkActions(grid, g, L, rows);
        MergeScenes.DrawRowB(grid, g, L.SelectedOwn, sel, L.SelectedOwnMore);
        DrawLanding(grid, g, L, rows);
        if (cursor >= 0 && cursor < rows.Count)
        {
            var at = IndexOf(L.CurrentOwn, rows[cursor].Hash);
            if (at >= 0)
                grid.Put(g.TipX(at + 1), g.A + 1, '▴', FgB);
        }

        var spots = MergeScenes.LabelSpots(g, L);
        MergeScenes.CurLabel(grid, g, spots.CurX, spots.CurRow, L);
        MergeScenes.SelLabel(grid, g, spots.SelX, spots.SelRow, L);
        return new SceneFrame(grid, null);
    }

    /// <summary>
    /// A paused rebase, drawn from its stop alone: its plan rows are in the stop, so the picture is the same after a restart. Row A is the
    /// base and the plan's commits as ghosts, up to the commits the art has room for; the window moves on when the stop is further along,
    /// so the stop's ghost is always drawn. Row B is the onto tip, the commits that have landed (at most MaxLanding are drawn, then a dim
    /// tail), and the stop: a conflict is a ✕ one cell short of the next slot, an edit is a warm dot in that slot, and a reword or a squash
    /// is its own ghost on row A, turned warm.
    /// </summary>
    public static SceneFrame Paused(ArtGeometry g, SequenceStop stop, Ink branchInk, Ink ontoInk)
    {
        var grid = new ArtGrid(g.Width, g.Height);
        var rows = stop.Rows ?? [];
        if (rows.Count == 0)
            return new SceneFrame(grid, null);

        var branch = new Style(branchInk);
        var onto = new Style(ontoInk);
        var stopAt = Math.Clamp(stop.Step - 1, 0, rows.Count - 1);
        var landed = LandedBefore(rows, stopAt);

        // The window of ghosts on row A: it starts at the base, unless the stop is past the last ghost the art has room for.
        var slots = Math.Max(1, (g.Width - 1 - g.XB) / g.Step);
        var first = Math.Max(0, stopAt + 1 - slots);
        var ghosts = Math.Min(rows.Count - first, slots);

        // Row A: the base (or a dim ┄ when the window has moved on), then each planned commit as a dim ghost on a dashed line.
        if (first == 0)
            grid.Put(g.XB, g.A, '●', Dim);
        else
            grid.Put(g.XB, g.A, '┄', Dim);
        var px = g.XB;
        for (var i = 0; i < ghosts; i++)
        {
            var x = g.TipX(i + 1);
            grid.HLine(px + 1, x - 1, g.A, Dim, '┄');
            grid.Put(x, g.A, '◌', Dim);
            px = x;
        }

        var warmOnA = stop.Pause == PauseKind.Reword || stop.Pause == PauseKind.Squash;
        var stopSlot = stopAt - first + 1;
        if (warmOnA)
            grid.Put(g.TipX(stopSlot), g.A, '●', WarnB);

        // Row B: the line down from row A, the onto tip, then the landed commits in their branch's colour. At most MaxLanding are drawn; a
        // longer landing ends in a dim tail, so the stop's slot is still on the art.
        grid.VLine(g.XB, g.A + 1, g.B - 1, branch);
        grid.Put(g.XB, g.B, '╰', branch);
        MergeScenes.Label(grid, g, g.TipX(1), g.B, up: false, stop.Onto, null, ontoInk);
        grid.Put(g.TipX(1), g.B, '●', onto);
        var drawn = Math.Min(landed, MaxLanding);
        var last = g.TipX(1);
        for (var k = 1; k <= drawn; k++)
        {
            var x = g.TipX(k + 1);
            grid.HLine(last + 1, x - 1, g.B, branch);
            grid.Put(x, g.B, '●', branch);
            last = x;
        }

        var slot = g.TipX(drawn + 2);
        var truncated = landed > drawn;
        if (truncated)
            grid.HLine(last + 1, slot - 1, g.B, Dim, '┄');

        // The stop's own line runs from the last landed commit to the stop, or, after a tail, only its last cell.
        var from = truncated ? slot - 2 : last + 1;
        var labelX = warmOnA ? g.TipX(stopSlot) : slot;
        if (stop.Pause == PauseKind.Conflict)
        {
            grid.HLine(from, slot - 2, g.B, Bad, '━');
            grid.Put(slot - 1, g.B, '✕', BadB);
            labelX = slot - 1;
        }
        else if (stop.Pause == PauseKind.Edit)
        {
            grid.HLine(truncated ? slot - 1 : last + 1, slot - 1, g.B, branch);
            grid.Put(slot, g.B, '●', WarnB);
        }

        var labelRow = warmOnA ? g.A : g.B;
        MergeScenes.Label(grid, g, labelX, labelRow, up: true, stop.Branch, " " + RebaseText.Rebasing(stop.Step, stop.Total), branchInk);
        return new SceneFrame(grid, null);
    }

    // How many commits before the stop have landed on the onto tip: the ones git replays as picks, rewords and edits.
    private static int LandedBefore(IReadOnlyList<RebaseRow> rows, int stopAt)
    {
        var n = 0;
        for (var i = 0; i < stopAt; i++)
        {
            var action = rows[i].Action;
            if (action == RebaseAction.Pick || action == RebaseAction.Reword || action == RebaseAction.Edit)
                n++;
        }
        return n;
    }

    // Each commit of row A takes the colour of its action. The drop's dot becomes ✕; a pick keeps the row's own dot.
    private static void MarkActions(ArtGrid grid, ArtGeometry g, GraphLayout L, IReadOnlyList<RebaseRow> rows)
    {
        for (var i = 0; i < L.CurrentOwn.Count; i++)
        {
            var row = rows.FirstOrDefault(r => r.Hash == L.CurrentOwn[i].Hash);
            if (row is null)
                continue;

            var x = g.TipX(i + 1);
            if (row.Action == RebaseAction.Drop)
                grid.Put(x, g.A, '✕', Bad);
            else if (row.Action == RebaseAction.Squash || row.Action == RebaseAction.Fixup)
                grid.Put(x, g.A, '●', MrgB);
            else if (row.Action == RebaseAction.Reword || row.Action == RebaseAction.Edit)
                grid.Put(x, g.A, '●', WarnB);
        }
    }

    // One dim ◌ on row B for each commit that lands on the onto tip, joined to it by dim ┄. A fold lands on the commit above it, and a drop never lands.
    // At most MaxLanding ghosts are drawn; a longer landing ends in a dim ┄ tail after the last one.
    private static void DrawLanding(ArtGrid grid, ArtGeometry g, GraphLayout L, IReadOnlyList<RebaseRow> rows)
    {
        var landing = rows.Count(r => r.Action == RebaseAction.Pick || r.Action == RebaseAction.Reword || r.Action == RebaseAction.Edit);
        var shown = Math.Min(landing, MaxLanding);
        var last = g.TipX(L.SelectedOwn.Count);
        for (var k = 1; k <= shown; k++)
        {
            var x = g.TipX(L.SelectedOwn.Count + k);
            grid.HLine(last + 1, x - 1, g.B, Dim, '┄');
            grid.Put(x, g.B, '◌', Dim);
            last = x;
        }
        if (landing > shown)
            grid.HLine(last + 1, last + g.Step - 1, g.B, Dim, '┄');
    }

    private static int IndexOf(IReadOnlyList<CommitInfo> commits, string hash)
    {
        for (var i = 0; i < commits.Count; i++)
        {
            if (commits[i].Hash == hash)
                return i;
        }
        return -1;
    }
}
