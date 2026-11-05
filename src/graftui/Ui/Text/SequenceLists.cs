using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

/// <summary>
/// The commit lists under the rebase screens (the mock's lists). Each list is one group: the current branch's header, then its rows
/// newest first. The last row is the branch the commits land on, where the screen shows it.
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
    /// The done screen (the mock's afterList): each commit that landed, newest first, with the hash it had and any commit folded into it;
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

    private static CommitPanel Group(GraphLayout L, IReadOnlyList<CommitRow> rows) =>
        new(new CommitGroup([new Seg(L.Current, CurB(L)), new Seg(" (current)", Dim)], rows, rows.Count), null, []);
}
