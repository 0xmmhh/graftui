using Graft.Model;
using Graft.Ui.Scenes;

namespace Graft.Ui.Text;

/// <summary>One row of the commit list: a dot, a short hash and a subject, each in its own style.</summary>
public sealed record CommitRow(char Dot, Style DotStyle, string Hash, Style HashStyle, string Subject, Style SubjectStyle);

/// <summary>The commit list under the art: what the branches look like before a merge and what they became after it.</summary>
public static class CommitLists
{
    private static readonly Style Fg = new(Ink.Fg);

    /// <summary>Before the merge: the selected branch's own commits (newest first), then the current tip.</summary>
    public static IReadOnlyList<CommitRow> Idle(GraphLayout L)
    {
        var rows = new List<CommitRow>();
        if (L.Kind is GraphKind.Same or GraphKind.UpToDate)
            return [R(L.CurrentTipCommit, new Style(Ink.Cur, true))];

        foreach (var c in L.SelectedOwn.Reverse())
            rows.Add(R(c, new Style(Ink.Inc, true)));
        rows.Add(R(L.CurrentTipCommit, new Style(Ink.Cur, true)));
        return rows;
    }

    /// <summary>After the merge, by outcome. <paramref name="mergeCommit"/> is the new merge commit, when there is one.</summary>
    public static IReadOnlyList<CommitRow> After(GraphLayout L, MergeOutcome o, CommitInfo? mergeCommit)
    {
        var cur = new Style(Ink.Cur, true);
        var inc = new Style(Ink.Inc, true);
        var mrg = new Style(Ink.Mrg, true);
        var bad = new Style(Ink.Bad, true);
        var rows = new List<CommitRow>();

        switch (o.Kind)
        {
            case MergeKind.MergeCommit:
                rows.Add(new CommitRow('◉', mrg, MergeScenes.Short(o.NewHead), new Style(Ink.Hash), mergeCommit?.Subject ?? "", mrg));
                rows.Add(R(L.SelectedTipCommit, inc));
                rows.Add(R(L.CurrentTipCommit, cur));
                break;

            case MergeKind.FastForward:
                foreach (var c in L.SelectedOwn.Reverse())
                    rows.Add(R(c, cur));
                break;

            case MergeKind.Conflict:
                rows.Add(R(L.SelectedTipCommit, inc));
                rows.Add(R(L.CurrentTipCommit, cur));
                foreach (var file in o.ConflictFiles)
                    rows.Add(new CommitRow('✕', bad, file, bad, "conflict (merge aborted)", bad));
                break;

            default: // UpToDate and Failed
                rows.Add(R(L.CurrentTipCommit, cur));
                break;
        }
        return rows;
    }

    private static CommitRow R(CommitInfo c, Style ink) =>
        new(MergeScenes.Dot(c), ink, c.ShortHash, new Style(Ink.Hash), c.Subject, Fg);
}
