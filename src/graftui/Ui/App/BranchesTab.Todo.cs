using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Input;

namespace graftui.Ui.App;

/// <summary>
/// The interactive rebase's todo list on the Branches tab (i). The art is the branches' preview with the plan's cursor on it, and the plan's
/// rows are listed under the rule (on a short pane the art is hidden so the rows fit). The keys edit the plan; Enter starts the rebase through
/// the same change as r, and a plan that may not start says why. The list is drawn again after every key.
/// </summary>
internal sealed partial class BranchesTab
{
    private TodoState? _todo;
    private RebaseCheck? _todoCheck;
    private GraphLayout? _todoLayout;

    /// <summary>The plan the todo list is editing, or null when the list is closed.</summary>
    internal TodoState? TodoPlan => _todo;

    // The caption of the last refused start. It is shown until the next key, as the pause's note is.
    private Caption? _todoNote;

    /// <summary>The todo list opens on a check that passed: its rows are the plan, and the cursor starts on the first row.</summary>
    private void OpenTodo(RebaseCheck check, GraphLayout? layout)
    {
        _todo = new TodoState(check.Rows);
        _todoCheck = check;
        _todoLayout = Or(layout, _app.Snapshot, check.Onto, check.OntoTip);
        _todoNote = null;
        Mode = BranchesMode.Todo;
        _app.Keys.Mode = KeyBarMode.Todo;
        _app.Keys.SetNeedsDraw();
        ShowTodo();
    }

    /// <summary>
    /// The todo list's art: the preview with the cursor, the plan's rows under the rule, and the caption (the last refusal, else the hint).
    /// The art is hidden when the pane has no room for the rows beside it. The rows are read from the state at each draw.
    /// </summary>
    private void ShowTodo()
    {
        var todo = _todo!;
        var check = _todoCheck!;
        var L = _todoLayout!;
        var art = _app.Art;
        art.Title = RebaseText.TodoTitle;
        art.Direction = TodoDirection(check.Branch, check.Onto);
        art.Panel = null;
        art.Below = null;
        art.Scene = geo => RebaseScenes.Preview(geo, L, todo.Rows, todo.Cursor);
        RefreshTodoArt();
        art.FallbackCaption = _todoNote ?? new Caption(RebaseText.TodoHint, Muted);
        art.BelowFor = (cols, rows) => SequenceLists.Todo(todo, cols, rows, BranchInks.Current(L), DateTimeOffset.UtcNow);
        art.SetNeedsDraw();
    }

    /// <summary>
    /// Hides the art when the pane has no room for the todo rows beside it. The pane's height is current only after a layout, so the app calls
    /// this from the art's viewport change (a resize lays the pane out again). Does nothing outside the todo list, and waits while the help
    /// popup is open: the popup is drawn over the art, so hiding the art would hide the popup too. The popup's close calls this again.
    /// </summary>
    internal void RefreshTodoArt()
    {
        if (Mode != BranchesMode.Todo || _app.HelpOpen)
            return;
        _app.Art.HideArt = ArtPaneView.ListNeedsWholePane(_app.Art.Viewport.Height, compact: true);
        _app.Art.SetNeedsDraw();
    }

    /// <summary>The direction line of the todo list: the rebase, then the dim "todo" tail.</summary>
    private Func<int, IReadOnlyList<Seg>> TodoDirection(string cur, string onto)
    {
        var line = RebaseDirection(cur, onto);
        return cols => DirectionLine.WithTail(line(cols), RebaseText.TodoTail, cols);
    }

    /// <summary>
    /// The keys of the todo list. The arrows move the cursor (← and ↑ up, → and ↓ down), Shift or Alt with them (or J and K) moves the row,
    /// a letter sets the row's action, Enter starts the rebase and Esc closes the list. Every other key is taken and ignored, so Tab does
    /// not leave the list.
    /// </summary>
    internal void HandleTodoKey(Key e)
    {
        e.Handled = true;
        _todoNote = null;
        if (_todo is null)
            return;

        if (e == Key.Esc)
        {
            CloseTodo();
            _app.Result = null;
            RequestPreview();
            return;
        }

        if (e == Key.Enter)
        {
            StartTodo();
            return;
        }

        TodoEdit(_todo, e);
        ShowTodo();
    }

    /// <summary>
    /// Moves the cursor or a row, or sets the cursor row's action, for a key that does one of those. Plain ← and ↑ move the cursor up,
    /// plain → and ↓ down; with Shift or Alt the arrows move the row instead, and the left and right arrows do nothing extra then.
    /// </summary>
    private static void TodoEdit(TodoState todo, Key e)
    {
        if (e == Key.CursorUp.WithShift || e == Key.CursorUp.WithAlt || e == Key.K.WithShift)
            todo.MoveUp();
        else if (e == Key.CursorDown.WithShift || e == Key.CursorDown.WithAlt || e == Key.J.WithShift)
            todo.MoveDown();
        else if (e == Key.CursorUp || e == Key.CursorLeft)
            todo.Up();
        else if (e == Key.CursorDown || e == Key.CursorRight)
            todo.Down();
        else if (!e.IsCtrl && !e.IsAlt)
        {
            // Only a plain letter sets an action: Ctrl+D or Alt+S must not drop or squash the row.
            var action = RebasePlan.ActionForKey(ChangesTab.CharOf(e));
            if (action is not null)
                todo.Set(action.Value);
        }
    }

    /// <summary>
    /// Enter: a plan that may not start keeps the list open with the reason as its caption, and the reason goes to the Commands pane. A plan
    /// that may start closes the list and runs the rebase on the rows as they are.
    /// </summary>
    private void StartTodo()
    {
        var todo = _todo!;
        var refusal = todo.StartRefusal();
        if (refusal is not null)
        {
            _todoNote = new Caption(refusal, WarnB);
            foreach (var line in RefusalText.LogLines(refusal))
                _app.LogMessage(line, WarnB);
            ShowTodo();
            return;
        }

        var check = _todoCheck!;
        var layout = _todoLayout;
        CloseTodo();
        StartRebase(check, layout, todo.Rows, interactive: true);
    }

    /// <summary>Gives the keys back to the Branches tab. The caller decides what shows next.</summary>
    private void CloseTodo()
    {
        _todo = null;
        _todoCheck = null;
        _todoLayout = null;
        _todoNote = null;
        Mode = BranchesMode.Browse;
        _app.Keys.Mode = KeyBarMode.Branches;
        _app.Keys.SetNeedsDraw();
    }
}
