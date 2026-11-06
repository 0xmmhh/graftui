using graftui.Model;
using graftui.Operations;

namespace graftui.Ui.Text;

/// <summary>
/// One kept row of a rebase plan and the fold rows (squash or fixup) that land on it. Target and Folds are indices into the plan rows,
/// oldest first. HasSquash is true when a squash, not only a fixup, is in the group: such a group pauses for its combined message.
/// </summary>
public sealed record PlanFold(int Target, IReadOnlyList<int> Folds, bool HasSquash);

/// <summary>
/// What a plan does, read from its rows. Kept and Dropped are row indices (kept = pick, reword and edit). Groups has one entry per kept
/// row, in the same order as Kept. Error is the first refusal in the engine's words, or "" when the plan can run.
/// </summary>
public sealed record PlanAnalysis(IReadOnlyList<int> Kept, IReadOnlyList<int> Dropped, IReadOnlyList<PlanFold> Groups, string Error);

/// <summary>
/// The todo list's pure reads and edits. <see cref="Analyze"/> mirrors the engine's plan check, so the todo list refuses exactly what the
/// engine would refuse. Nothing here runs git, and the rows are never changed in place.
/// </summary>
public static class RebasePlan
{
    /// <summary>
    /// Walks the plan oldest first. A fold with no kept row above it is refused, and an all-drop plan is refused; the first refusal wins,
    /// the fold one before the empty one. Folds that come after a refusal are still grouped, so the list can show them.
    /// </summary>
    public static PlanAnalysis Analyze(IReadOnlyList<RebaseRow> rows, string branch)
    {
        var kept = new List<int>();
        var dropped = new List<int>();
        var folds = new List<List<int>>();
        var squash = new List<bool>();
        var error = "";

        for (var i = 0; i < rows.Count; i++)
        {
            var action = rows[i].Action;
            if (action == RebaseAction.Drop)
            {
                dropped.Add(i);
                continue;
            }

            if (IsFold(action))
            {
                if (kept.Count == 0)
                {
                    if (error.Length == 0)
                        error = SequenceRefusals.FoldFirst(action == RebaseAction.Squash ? "squash" : "fixup");
                    continue;
                }

                folds[^1].Add(i);
                squash[^1] |= action == RebaseAction.Squash;
                continue;
            }

            kept.Add(i);
            folds.Add([]);
            squash.Add(false);
        }

        if (error.Length == 0 && kept.Count == 0)
            error = SequenceRefusals.EmptyPlan(branch);

        var groups = new List<PlanFold>();
        for (var k = 0; k < kept.Count; k++)
            groups.Add(new PlanFold(kept[k], folds[k], squash[k]));

        return new PlanAnalysis(kept, dropped, groups, error);
    }

    /// <summary>
    /// The rows with the one at <paramref name="from"/> moved so it ends at <paramref name="to"/> (both are indices into the rows as they
    /// are before the move). Throws <see cref="ArgumentOutOfRangeException"/> for an index outside the rows.
    /// </summary>
    public static IReadOnlyList<RebaseRow> Move(IReadOnlyList<RebaseRow> rows, int from, int to)
    {
        if (from < 0 || from >= rows.Count)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (to < 0 || to >= rows.Count)
            throw new ArgumentOutOfRangeException(nameof(to));

        var list = rows.ToList();
        var row = list[from];
        list.RemoveAt(from);
        list.Insert(to, row);
        return list;
    }

    /// <summary>The rows with the action of one row replaced. Throws <see cref="ArgumentOutOfRangeException"/> for an index outside the rows.</summary>
    public static IReadOnlyList<RebaseRow> WithAction(IReadOnlyList<RebaseRow> rows, int index, RebaseAction action)
    {
        if (index < 0 || index >= rows.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var list = rows.ToList();
        list[index] = list[index] with { Action = action };
        return list;
    }

    /// <summary>The action a letter key sets on the selected row: p pick, r reword, e edit, s squash, f fixup, d drop. Null for any other key.</summary>
    public static RebaseAction? ActionForKey(char key)
    {
        var letter = char.ToLowerInvariant(key);
        if (letter == 'p')
            return RebaseAction.Pick;
        if (letter == 'r')
            return RebaseAction.Reword;
        if (letter == 'e')
            return RebaseAction.Edit;
        if (letter == 's')
            return RebaseAction.Squash;
        if (letter == 'f')
            return RebaseAction.Fixup;
        if (letter == 'd')
            return RebaseAction.Drop;
        return null;
    }

    /// <summary>
    /// True when the plan does more than pick every commit (a reword, edit, fold or drop). A paused rebase that is resumed reads its
    /// kind from this, since the state file does not say whether it was started from the todo list.
    /// </summary>
    public static bool IsEdited(IReadOnlyList<RebaseRow> rows) => rows.Any(r => r.Action != RebaseAction.Pick);

    /// <summary>True for a squash or a fixup: a row that folds into the kept row above it.</summary>
    public static bool IsFold(RebaseAction action) => action == RebaseAction.Squash || action == RebaseAction.Fixup;
}
