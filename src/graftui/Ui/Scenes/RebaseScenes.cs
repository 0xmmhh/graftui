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
    private static readonly Style Bad = new(Ink.Bad), MrgB = new(Ink.Mrg, true), WarnB = new(Ink.Warn, true);

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
