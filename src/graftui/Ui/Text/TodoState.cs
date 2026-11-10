using graftui.Model;

namespace graftui.Ui.Text;

/// <summary>
/// The interactive rebase's todo list: the plan the user is editing, the cursor on it, and whether it may start. The plan's first row runs
/// first. The cursor wraps when it moves; a row moved with the keys stops at either end. Nothing here runs git.
/// </summary>
public sealed class TodoState(IReadOnlyList<RebaseRow> rows)
{
    /// <summary>The plan as it is now, oldest first: the first row runs first.</summary>
    public IReadOnlyList<RebaseRow> Rows { get; private set; } = rows;

    /// <summary>The row the cursor is on. Zero when the plan is empty.</summary>
    public int Cursor { get; private set; }

    /// <summary>Moves the cursor up one row, wrapping from the top to the bottom.</summary>
    public void Up()
    {
        if (Rows.Count > 0)
            Cursor = (Cursor - 1 + Rows.Count) % Rows.Count;
    }

    /// <summary>Moves the cursor down one row, wrapping from the bottom to the top.</summary>
    public void Down()
    {
        if (Rows.Count > 0)
            Cursor = (Cursor + 1) % Rows.Count;
    }

    /// <summary>Swaps the cursor's row with the one above it, and the cursor follows. Does nothing at the top.</summary>
    public void MoveUp()
    {
        if (Cursor <= 0)
            return;
        Rows = RebasePlan.Move(Rows, Cursor, Cursor - 1);
        Cursor--;
    }

    /// <summary>Swaps the cursor's row with the one below it, and the cursor follows. Does nothing at the bottom.</summary>
    public void MoveDown()
    {
        if (Cursor >= Rows.Count - 1)
            return;
        Rows = RebasePlan.Move(Rows, Cursor, Cursor + 1);
        Cursor++;
    }

    /// <summary>Gives the cursor's row an action. Does nothing when the plan is empty.</summary>
    public void Set(RebaseAction action)
    {
        if (Rows.Count > 0)
            Rows = RebasePlan.WithAction(Rows, Cursor, action);
    }

    /// <summary>
    /// Why the plan may not start, or null when it may. The first row that is not a drop decides: when every row is dropped that is
    /// KeepOne; when it is a squash or a fixup, nothing is above it to fold into. The engine refuses exactly these plans (see
    /// TodoRebaseRunTests).
    /// </summary>
    public string? StartRefusal()
    {
        var first = Rows.FirstOrDefault(r => r.Action != RebaseAction.Drop);
        if (first is null)
            return RebaseText.KeepOne;
        if (first.Action == RebaseAction.Squash)
            return RebaseText.FirstSquash;
        if (first.Action == RebaseAction.Fixup)
            return RebaseText.FirstFixup;
        return null;
    }
}
