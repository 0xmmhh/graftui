using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

/// <summary>
/// The commit lists under the rebase screens. Each list is one group: the current branch's header, then its rows newest first.
/// The last row is the branch the commits land on, where the screen shows it.
/// </summary>
public static class SequenceLists
{
    private static readonly Style Fg = new(Ink.Fg), Dim = new(Ink.Dim);

    /// <summary>The confirm screen: every commit the rebase will rewrite, then the new base.</summary>
    public static CommitPanel Confirm(GraphLayout L, IReadOnlyList<RebaseRow> rows)
    {
        List<CommitRow> list = [];
        foreach (var row in rows.Reverse())
            list.Add(Row(CurB(L), row.ShortHash, row.Subject + "  " + RebaseText.WillBeRewritten, Fg));
        list.Add(OntoRow(L, RebaseText.NewBase));
        return Group(L, list);
    }

    /// <summary>The running screen: each commit with the action it gets, newest first.</summary>
    public static CommitPanel Running(GraphLayout L, IReadOnlyList<RebaseRow> rows)
    {
        List<CommitRow> list = [];
        foreach (var row in rows.Reverse())
            list.Add(Row(CurB(L), row.ShortHash, row.Subject + "  " + RebaseText.ActionLabel(row.Action), Fg));
        return Group(L, list);
    }

    /// <summary>
    /// The done screen: each commit that landed, newest first, with the hash it had and any commit folded into it;
    /// then the commits dropped or skipped; then the new base.
    /// </summary>
    public static CommitPanel Rebased(SequenceOutcome o, IReadOnlyList<RebaseRow> rows, GraphLayout L)
    {
        var newHashes = o.Moves.ToDictionary(m => m.Old, m => m.New);
        List<Landing> landed = [];
        List<CommitRow> gone = [];
        foreach (var row in rows)
        {
            if (row.Action == RebaseAction.Drop)
            {
                gone.Add(Row(Dim, row.ShortHash, row.Subject + "  dropped", Dim, dot: '✕'));
                continue;
            }

            // A fold goes into the commit above it (the engine refuses a plan where there is none), so it adds no row of its own.
            if (row.Action == RebaseAction.Squash || row.Action == RebaseAction.Fixup)
            {
                if (landed.Count > 0)
                {
                    var last = landed[^1];
                    landed[^1] = last with { Folded = last.Folded + " + squashed " + row.ShortHash };
                }
                continue;
            }

            if (newHashes.TryGetValue(row.Hash, out var landedHash) && landedHash is not null)
                landed.Add(new Landing(landedHash, row.Subject, row.ShortHash, ""));
            else
                gone.Add(Row(Dim, row.ShortHash, row.Subject + "  skipped", Dim, dot: '✕'));
        }

        List<CommitRow> list = [];
        for (var i = landed.Count - 1; i >= 0; i--)
            list.Add(Row(CurB(L), MergeScenes.Short(landed[i].Hash), $"{landed[i].Subject}  was {landed[i].Old}{landed[i].Folded}", Fg));
        list.AddRange(gone);
        list.Add(OntoRow(L, ""));
        return Group(L, list);
    }

    /// <summary>
    /// The todo list under the art: a ▸ on the cursor's row, the action in its colour, the short hash, then the
    /// subject (dim for a drop). Only <paramref name="visible"/> rows are listed; the window keeps the cursor in view.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Seg>> Todo(TodoState state, int cols, int visible, Ink curInk)
    {
        var rows = state.Rows;
        var first = Window(rows.Count, state.Cursor, visible);
        List<IReadOnlyList<Seg>> lines = [];
        for (var i = first; i < rows.Count && lines.Count < visible; i++)
            lines.Add(TodoLine(rows[i], i == state.Cursor, cols, curInk));
        return lines;
    }

    /// <summary>
    /// The first row the todo list shows: the cursor sits in the middle of the visible rows, and the list never shows past its end.
    /// </summary>
    public static int Window(int count, int cursor, int visible) => Math.Clamp(cursor - visible / 2, 0, Math.Max(0, count - visible));

    private static IReadOnlyList<Seg> TodoLine(RebaseRow row, bool selected, int cols, Ink curInk)
    {
        var mark = selected ? "▸ " : "  ";
        var label = RebaseText.ActionLabel(row.Action).PadRight(7);
        var subject = TextFit.Fit(row.Subject, Math.Max(0, cols - TextFit.Columns(mark + label + row.ShortHash + " ")));
        var subjectStyle = row.Action == RebaseAction.Drop ? Dim : Fg;
        return
        [
            new Seg(mark, selected ? new Style(Ink.Fg, true) : Dim),
            new Seg(label, new Style(TodoInk(row.Action, curInk), true)),
            new Seg(row.ShortHash, new Style(Ink.Hash)),
            new Seg(" " + subject, subjectStyle),
        ];
    }

    // The colour of a todo action: a pick takes the current branch's ink, an edit or reword the warning ink, a fold the merge ink, a drop the bad ink.
    private static Ink TodoInk(RebaseAction action, Ink curInk)
    {
        if (action == RebaseAction.Reword || action == RebaseAction.Edit)
            return Ink.Warn;
        if (action == RebaseAction.Squash || action == RebaseAction.Fixup)
            return Ink.Mrg;
        if (action == RebaseAction.Drop)
            return Ink.Bad;
        return curInk;
    }

    // One landed commit: its new hash, the subject, the hash it had, and the commits folded into it.
    private sealed record Landing(string Hash, string Subject, string Old, string Folded);

    private static Style CurB(GraphLayout L) => new(BranchInks.Current(L), true);

    // The last row: the onto tip, with its note (empty for none).
    private static CommitRow OntoRow(GraphLayout L, string note)
    {
        var tip = L.SelectedTipCommit;
        var subject = note.Length == 0 ? tip.Subject : tip.Subject + "  " + note;
        return Row(new Style(Ink.Inc, true), tip.ShortHash, subject, Fg);
    }

    private static CommitRow Row(Style dotStyle, string hash, string subject, Style subjectStyle, char dot = '●') =>
        new(dot, dotStyle, hash, new Style(Ink.Hash), subject, subjectStyle);

    private static CommitPanel Group(GraphLayout L, List<CommitRow> rows) =>
        new(new CommitGroup([new Seg(L.Current, CurB(L)), new Seg(" (current)", Dim)], rows, rows.Count), null, []);
}
