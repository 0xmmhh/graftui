using graftui.Model;

namespace graftui.Ui.Text;

/// <summary>
/// The todo list's pure reads and edits: moving a row, setting a row's action, the key that names each action, and whether a plan is more than
/// all picks. Nothing here runs git, and the rows are never changed in place. Whether a plan may start is <see cref="TodoState.StartRefusal"/>.
/// </summary>
public static class RebasePlan
{
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
