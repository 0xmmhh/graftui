using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Input;

namespace graftui.Ui.App;

/// <summary>
/// Discard (x), stash (z), pop (Z) and undo (u) on the Changes tab. Each runs as a change: Check on a worker, the quit gate, the
/// animation, Run, then the result on the art and the exit line. The stash message is typed in a one-line box over the art.
/// </summary>
internal sealed partial class ChangesTab
{
    /// <summary>What a change shows when it starts: its animation and its direction line.</summary>
    private sealed record ChangeStart(IAreasAnimation Anim, Func<int, IReadOnlyList<Seg>> Direction);

    /// <summary>What a change shows when git has answered: the result, the log's failure line, whether it succeeded, and the exit line.</summary>
    private sealed record ChangeFinish(AreasResult Result, IReadOnlyList<GitResult> Commands, string? FailureLog, bool Success, string ExitLine);

    // The stash message box is open: its field is the only one shown, and the keys go to it.
    private bool _stashForm;

    // ---------- discard (x) ----------

    /// <summary>
    /// x: asks before discarding the selected row's unstaged change. Rows that can never be backed up are refused here, before the
    /// question; the rest are checked again when the answer comes.
    /// </summary>
    private void AskDiscard()
    {
        var row = SelectedRow;
        var snap = Snapshot;
        if (row is null || snap is null)
        {
            RefuseNow(ChangeRefusals.NothingToDiscard);
            return;
        }

        var path = row.Path;
        if (row.Side == ChangeSide.Staged)
            RefuseNow(ChangeRefusals.NoUnstaged(path));
        else if (row.Kind == ChangeKind.Conflicted)
            RefuseNow(ChangeRefusals.ConflictFile(path));
        else if (row.IsNestedRepo)
            RefuseNow(ChangeRefusals.NestedRepoDiscard(path));
        else if (row.IsSubmodule)
            RefuseNow(ChangeRefusals.Submodule(path));
        else if (row.Kind == ChangeKind.Added)
            RefuseNow(ChangeRefusals.IntentToAdd(path));
        else
        {
            var untracked = row.Kind == ChangeKind.Untracked;
            var partlyStaged = snap.Status.Staged.Any(f => f.Path == path);
            var question = untracked ? $"Delete the new file {path}?"
                : partlyStaged ? $"Discard the unstaged changes to {path}? Its staged changes stay."
                : $"Discard your changes to {path}?";
            BeginYesNo(new YesNo(
                Title: "Discard",
                Question: question,
                Context: untracked ? "graftui saves it in the stash first, so u can bring it back."
                    : "graftui saves them in the stash first, so u can bring them back.",
                YesLabel: " discard",
                NoLabel: " keep",
                CancelText: "Discard cancelled: nothing changed",
                Direction: cols => ChangesText.OpDirection("discard", path, [new Seg("stash", Dim)], cols),
                OnYes: () => StartDiscard(row)));
        }
    }

    private void StartDiscard(FileChange row)
    {
        var snap = Snapshot;
        if (snap is null)
            return;

        var path = row.Path;
        var before = Before();
        var head = HeadText(snap.Head.HeadHash);
        StartChange(_app.DiscardOp, new DiscardRequest(row), "Discard",
            c => c.Refusal, c => c.Commands,
            c => new ChangeStart(new StashAnimation(StashOp.Discard, before, [c.Row ?? row], $"Discarding {path}…"),
                cols => ChangesText.OpDirection("discard", path, [new Seg("stash", Dim)], cols)),
            o => o.Kind == DiscardKind.Discarded
                ? new ChangeFinish(
                    new AreasResult(true, AreasAfter(before), new Caption(DiscardedCaption(path, o.Undo is not null), OkB), "Discarded"),
                    o.Commands, null, true,
                    $"graftui: discarded {path} (saved as stash \"graftui: discarded {path}\"); HEAD unchanged at {head}")
                : new ChangeFinish(
                    new AreasResult(false, before, new Caption($"✕ Discard failed: {o.Message}", BadB), "Failed", o.Message),
                    o.Commands, $"Discard failed: {o.Message}", false, $"graftui: discard failed: {o.Message}; HEAD unchanged at {head}"));
    }

    private static string DiscardedCaption(string path, bool canUndo) =>
        $"✔ Discarded {path}: saved as stash \"graftui: discarded {path}\", "
        + (canUndo ? "u brings it back" : "u can't bring it back automatically");

    // ---------- stash (z) ----------

    /// <summary>
    /// z: checks that there is something to stash (a refusal is flashed, as commit does), then opens the one-line message box.
    /// </summary>
    internal void BeginStashInput()
    {
        if (_app.Busy)
            return;
        if (!ArtFits())
        {
            _app.LogMessage("Refused: widen the window to type a stash message", WarnB);
            return;
        }

        BeginBusy();
        var op = _app.StashOp;
        Task.Run(() =>
        {
            try
            {
                var check = op.Check(new StashRequest(null));
                _app.Ui(() =>
                {
                    if (!check.CanRun)
                    {
                        Refuse(check.Commands, check.Refusal!);
                        return;
                    }
                    _app.LogCommands(check.Commands);
                    EndBusy();
                    OpenStashForm(check.Moving.Count);
                });
            }
            catch (Exception ex)
            {
                _app.Ui(() =>
                {
                    _app.ShowGitError(ex);
                    EndBusy();
                });
            }
        }, CancellationToken.None);
    }

    /// <summary>The message box: one field over the art, with the hint. An empty message means git's default.</summary>
    private void OpenStashForm(int count)
    {
        _stashForm = true;
        Mode = ChangesMode.Input;
        _app.Keys.Mode = KeyBarMode.Message;
        _app.Keys.SetNeedsDraw();

        var art = _app.Art;
        art.CanFocus = true;
        var subject = _app.SubjectField;
        subject.CanFocus = true;
        subject.Visible = true;
        subject.Text = "";
        subject.InsertionPoint = 0;
        _app.BodyField.Visible = false;
        _app.BodyField.CanFocus = false;

        art.Title = "Stash";
        art.Direction = cols => ChangesText.OpDirection("stash", MergeScenes.Plural(count, "changed file"), [new Seg("stash", Dim)], cols);
        art.Below = Summary();
        art.Panel = null;
        art.FallbackCaption = null;
        art.Scene = geo => new SceneFrame(AreasScenes.StashForm(geo, Before()).Grid,
            new Caption("Enter stashes · empty uses git's default message · Esc cancels", Dim));
        art.SetNeedsDraw();
        subject.SetFocus();
    }

    /// <summary>Keys in the stash box: Esc cancels, Tab is swallowed, Enter or Ctrl+Enter stashes. Anything else edits the field.</summary>
    private void HandleStashKey(Key e)
    {
        if (e == Key.Esc)
        {
            e.Handled = true;
            CloseForm();
            _flash = new Caption("Stash cancelled: nothing changed", Dim);
            ShowBrowse();
            return;
        }
        if (e == Key.Tab || e == Key.Tab.WithShift)
        {
            e.Handled = true;
            return;
        }
        if (e == Key.Enter || e == Key.Enter.WithCtrl || e == Key.J.WithCtrl)
        {
            e.Handled = true;
            SubmitStashForm();
        }
    }

    private void SubmitStashForm()
    {
        var message = _app.SubjectField.Text ?? "";
        CloseForm();
        StartStash(new StashRequest(message));
    }

    private void StartStash(StashRequest request)
    {
        var snap = Snapshot;
        if (snap is null)
            return;

        var before = Before();
        var head = HeadText(snap.Head.HeadHash);
        StartChange(_app.StashOp, request, "Stash",
            c => c.Refusal, c => c.Commands,
            c => new ChangeStart(
                new StashAnimation(StashOp.Stash, before, c.Moving, $"Stashing {MergeScenes.Plural(c.Moving.Count, "file")}…"),
                cols => ChangesText.OpDirection("stash", MergeScenes.Plural(c.Moving.Count, "changed file"), [new Seg("stash", Dim)], cols)),
            o => o.Kind == StashKind.Stashed
                ? new ChangeFinish(
                    new AreasResult(true, AreasAfter(before),
                        new Caption($"✔ Stashed {MergeScenes.Plural(o.Moved.Count, "file")}: the working tree is clean, Z brings them back", OkB),
                        "Stashed"),
                    o.Commands, null, true,
                    $"graftui: stashed {MergeScenes.Plural(o.Moved.Count, "file")} as stash@{{0}}")
                : new ChangeFinish(
                    new AreasResult(false, before, new Caption($"✕ Stash failed: {o.Message}", BadB), "Failed", o.Message),
                    o.Commands, $"Stash failed: {o.Message}", false, $"graftui: stash failed: {o.Message}; HEAD unchanged at {head}"));
    }

    // ---------- pop (Z) ----------

    /// <summary>Z: pops the newest entry, as the stash list showed it. The check refuses when the entry has moved since.</summary>
    private void StartPop()
    {
        var snap = Snapshot;
        if (snap is null)
            return;

        var before = Before();
        StartChange(_app.PopOp, new PopRequest(snap.Stash.TopHash), "Pop",
            c => c.Refusal, c => c.Commands,
            c => new ChangeStart(new StashAnimation(StashOp.Pop, before, [], "Bringing the stash back…"),
                cols => ChangesText.PlainDirection("stash pop", c.TopMessage, cols)),
            o => FinishPop(o, before));
    }

    private ChangeFinish FinishPop(PopOutcome o, AreasModel before)
    {
        switch (o.Kind)
        {
            case PopKind.Popped:
            {
                var after = AreasAfter(before);
                var arrived = ArrivedRows(before, after);
                var caption = arrived.Count > 0
                    ? $"✔ Restored {MergeScenes.Plural(arrived.Count, "file")} from the stash"
                    : "✔ Popped the stash";
                return new ChangeFinish(new AreasResult(true, after, new Caption(caption, OkB), "Popped", null, arrived),
                    o.Commands, null, true, $"graftui: popped \"{o.TopMessage}\"");
            }
            case PopKind.Conflict:
            {
                // The pop was rolled back by PopOperation: the areas are the ones from before.
                var first = o.ConflictFiles[0];
                var listed = MergeScenes.ConflictFiles(o.ConflictFiles);
                return new ChangeFinish(
                    new AreasResult(false, before, new Caption($"✕ CONFLICT in {listed}: pop undone, the stash entry is kept", BadB), "Conflict",
                        $"{first} changed both in the stash and on main; the pop was undone and the stash entry is kept"),
                    o.Commands, $"Pop conflicted in {first}; rolled back, the stash entry is kept", false,
                    $"graftui: pop conflicted in {first}; rolled back, the stash entry is kept");
            }
            case PopKind.Refused:
                return new ChangeFinish(
                    new AreasResult(false, before, new Caption("Can't pop: git refused it. The stash entry is kept.", Warn), "Refused", o.Message),
                    o.Commands, null, false, $"graftui: pop refused: {o.Message}");
            default:
                return new ChangeFinish(
                    new AreasResult(false, before, new Caption($"✕ Pop failed: {o.Message}", BadB), "Failed", o.Message),
                    o.Commands, $"Pop failed: {o.Message}", false, $"graftui: pop failed: {o.Message}");
        }
    }

    /// <summary>The rows of <paramref name="after"/> that are not in <paramref name="before"/>, compared by side and path.</summary>
    private static IReadOnlyList<FileChange> ArrivedRows(AreasModel before, AreasModel after)
    {
        var known = before.Unstaged.Concat(before.Staged).Select(f => (f.Side, f.Path)).ToHashSet();
        return after.Unstaged.Concat(after.Staged).Where(f => !known.Contains((f.Side, f.Path))).ToList();
    }

    // ---------- undo (u) ----------

    /// <summary>
    /// u on the Changes tab: undoes the newest step. Nothing to undo is a dim note, and a refusal is shown as one; otherwise the
    /// undo runs like any change, with the same animation as the other stash operations.
    /// </summary>
    private void StartUndo()
    {
        var before = Before();
        StartChange(_app.UndoOp, new UndoRequest(null), "Undo",
            c => c.Refusal, c => c.Commands,
            c =>
            {
                var what = c.Step?.What ?? "last step";
                return new ChangeStart(new StashAnimation(StashOp.Undo, before, [], $"Undoing the {what}…"),
                    cols => ChangesText.PlainDirection("undo", $"the {what}", cols));
            },
            o => o.Kind == UndoOutcomeKind.Undone
                ? new ChangeFinish(new AreasResult(true, AreasAfter(before), new Caption(o.Caption, OkB), "Undone"),
                    o.Commands, null, true, $"graftui: undid the {o.Step?.What}")
                : new ChangeFinish(new AreasResult(false, before, new Caption($"✕ Undo failed: {o.Message}", BadB), "Failed", o.Message),
                    o.Commands, $"Undo failed: {o.Message}", false, $"graftui: undo failed: {o.Message}"),
            refused: check => _app.Ui(() => UndoRefused(check)));
    }

    /// <summary>Runs on the UI thread. "Nothing to undo" is a dim note; any other refusal is shown as usual.</summary>
    private void UndoRefused(UndoCheck check)
    {
        if (check.NothingToUndo)
        {
            _app.LogCommands(check.Commands);
            Flash(ChangeRefusals.NothingToUndo, Dim);
            EndBusy();
            RequestRefresh();
            return;
        }
        Refuse(check.Commands, check.Refusal ?? "");
    }

    // ---------- the shared runner ----------

    /// <summary>
    /// Shows a refusal found on the UI thread (before any git work), as Refuse does for a git refusal. Nothing runs.
    /// </summary>
    private void RefuseNow(string reason)
    {
        foreach (var line in RefusalText.LogLines(reason))
            _app.LogMessage(line, WarnB);
        Flash(RefusalText.Caption(reason), Warn);
    }

    /// <summary>
    /// The state of the areas now, read after a change: the stash count and newest entry included. A failed read keeps the areas
    /// from before the change, and the next refresh corrects them.
    /// </summary>
    private AreasModel AreasAfter(AreasModel before)
    {
        try
        {
            var snap = _app.RootReader.LoadChanges(CancellationToken.None);
            return new AreasModel(snap.Status.Unstaged, snap.Status.Staged, snap.Head, snap.Stash);
        }
        catch
        {
            return before;
        }
    }

    /// <summary>
    /// One discard, stash, pop or undo on a worker: Check (a refusal is shown), the quit gate, the animation starting, Run, then
    /// the result for the animation. <paramref name="refused"/> replaces the default refusal display (undo needs its own).
    /// </summary>
    private void StartChange<TReq, TCheck, TOut>(IGitOperation<TReq, TCheck, TOut> op, TReq req, string verb,
        Func<TCheck, string?> refusal, Func<TCheck, IReadOnlyList<GitResult>> checkCommands,
        Func<TCheck, ChangeStart> begin, Func<TOut, ChangeFinish> finish, Action<TCheck>? refused = null) where TOut : class
    {
        var animator = _app.ChangesAnimator;
        var below = Summary();
        BeginBusy();

        // Set on the UI thread when the animation starts, read there again when it ends.
        IAreasAnimation? anim = null;
        Func<int, IReadOnlyList<Seg>>? direction = null;
        _app.BeginChangeOutcome();
        _app.ChangesTask = Task.Run<string?>(() =>
        {
            try
            {
                var o = RunGated(op, req, refusal, () => _app.Gate.IsQuitting,
                    refused ?? (check => _app.Ui(() => Refuse(checkCommands(check), refusal(check) ?? ""))),
                    check => _app.Ui(() =>
                    {
                        var start = begin(check);
                        _app.LogCommands(checkCommands(check));
                        anim = start.Anim;
                        direction = start.Direction;
                        animator.Begin(start.Anim, start.Direction, below);
                    }));
                if (o is null)
                    return null;

                var f = finish(o);
                _app.Ui(() => animator.SetPending(f.Result, f.Commands, f.FailureLog, () => AfterChange(f.Success, null, anim, direction)));
                return f.ExitLine;
            }
            catch (Exception ex)
            {
                _app.Ui(() => FailUnexpectedly(ex, verb));
                return $"graftui: {verb.ToLowerInvariant()} failed: {ex.Message}";
            }
        });
    }
}
