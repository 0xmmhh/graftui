using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// The branch changes that start from the list: switch (s, not undoable) and delete (D: asked first; a merged branch gets one
/// question, an unmerged one two, on different keys). Each runs through <see cref="StartBranchChange"/>.
/// </summary>
internal sealed partial class BranchesTab
{
    private const string DeleteCancelled = "Delete cancelled: nothing changed";

    /// <summary>
    /// s: switches to the selected branch. Uncommitted changes come along when git can carry them; git's refusal is explained.
    /// Nothing happens when the selection is the current branch (the bar hides the key then).
    /// </summary>
    internal void StartSwitch()
    {
        var layout = _app.CurrentLayout();
        var snap = _app.Snapshot;
        if (_app.Busy || layout.TooSmall || snap.Branches.Count == 0)
            return;

        var b = snap.Branches[_selected];
        if (b.IsCurrent)
            return;

        var name = b.Name;
        var from = snap.CurrentBranch.Length == 0 ? "HEAD" : snap.CurrentBranch;
        var cached = CachedLayout(snap, b);
        Func<int, IReadOnlyList<Seg>> direction = cols => DirectionLine.Switch(from, name, cols);
        StartBranchChange(_app.SwitchOp, new SwitchRequest(name), "Switch",
            c => c.Refusal, c => c.Commands,
            () => cached ?? _app.TryLoadLayout(snap, b),
            (loaded, _) =>
            {
                var before = Or(loaded, snap, name, b.Hash);
                return new BranchStart(new BranchAnimation(BranchOp.Switch, before, name, null, 0, $"Switching to {name}…"), direction, Panel(before));
            },
            (o, after) => SwitchFinish(o, name, after),
            direction);
    }

    private BranchFinish SwitchFinish(SwitchOutcome o, string name, RepoSnapshot? after)
    {
        if (o.Kind == SwitchKind.Refused)
            return RefusedFinish(o.Message, o.Commands, after, name);
        if (o.Kind == SwitchKind.Failed)
            return FailedFinish("Switch", o.Message, o.Commands, after, name);

        var layout = after is null ? null : LayoutFor(after, name);
        var came = o.CameAlong > 0 ? $"your {MergeScenes.Plural(o.CameAlong, "changed file")} came along" : null;
        var caption = came is null ? $"✔ Switched to {name}" : $"✔ Switched to {name} · {came}";
        var exit = $"graftui: switched to {name}" + (came is null ? "" : $"; {came}");
        return new BranchFinish(new BranchResult(true, "Switched", new Caption(caption, OkB), layout), o.Commands, [], exit, after, name);
    }

    /// <summary>
    /// D: asks before deleting the selected branch. The current branch is refused at once, without git. Otherwise the delete is checked
    /// on a worker: a refusal is shown, a merged branch gets the one question, an unmerged one the first of the two.
    /// </summary>
    internal void AskDelete()
    {
        var layout = _app.CurrentLayout();
        var snap = _app.Snapshot;
        if (_app.Busy || layout.TooSmall || snap.Branches.Count == 0)
            return;

        var b = snap.Branches[_selected];
        var name = b.Name;
        var cached = CachedLayout(snap, b);
        if (b.IsCurrent)
        {
            ShowBranchRefused([], BranchRefusals.Current(name), cached, cols => DirectionLine.Delete(name, cols));
            return;
        }

        StartWork();
        Task.Run(() =>
        {
            try
            {
                var loaded = cached ?? _app.TryLoadLayout(snap, b);
                var check = _app.DeleteBranchOp.Check(new DeleteBranchRequest(name));
                _app.Ui(() => AfterDeleteCheck(check, loaded));
            }
            catch (Exception ex)
            {
                _app.Ui(() =>
                {
                    _app.ShowGitError(ex);
                    RestoreAfterError();
                });
            }
        }, CancellationToken.None);
    }

    /// <summary>Runs on the UI thread after the delete's check: the refusal, or the first question (merged: the only one).</summary>
    private void AfterDeleteCheck(DeleteBranchCheck check, GraphLayout? loaded)
    {
        EndWork();
        var name = check.Name;
        Func<int, IReadOnlyList<Seg>> direction = cols => DirectionLine.Delete(name, cols);
        if (!check.CanRun)
        {
            ShowBranchRefused(check.Commands, check.Refusal!, loaded, direction);
            return;
        }

        _app.LogCommands(check.Commands);
        var into = check.Current.Length == 0 ? "HEAD" : check.Current;
        if (check.Merged)
        {
            BeginQuestion(new BranchQuestion(
                Title: "Delete",
                Text: $"Delete {name}? Everything on it is already in {into}, so nothing is lost; u brings it back.",
                YesKey: 'y', YesLabel: " delete", NoLabel: " keep", CancelText: DeleteCancelled,
                Direction: direction, Layout: loaded,
                OnYes: () => StartDelete(check, force: false, loaded)));
            return;
        }

        BeginQuestion(new BranchQuestion(
            Title: "Delete",
            Text: $"{name} has {MergeScenes.Plural(check.Ahead, "commit")} that {into} doesn't have. Deleting it needs git branch -D; "
                + "graftui records where it pointed, so u can bring it back.",
            YesKey: 'y', YesLabel: " continue", NoLabel: " keep", CancelText: DeleteCancelled,
            Direction: direction, Layout: loaded,
            OnYes: () => AskDeleteForGood(check, loaded)));
    }

    /// <summary>The second question for an unmerged branch. It is answered with 1, so a held y cannot answer both.</summary>
    private void AskDeleteForGood(DeleteBranchCheck check, GraphLayout? loaded)
    {
        var lost = check.Lost == 0
            ? "Its commits are all on other branches too."
            : $"{MergeScenes.Plural(check.Lost, "commit")} would be on no branch.";
        var name = check.Name;
        BeginQuestion(new BranchQuestion(
            Title: "Delete for good?",
            Text: $"Really delete {name}? {lost}",
            YesKey: '1', YesLabel: " delete", NoLabel: " keep", CancelText: DeleteCancelled,
            Direction: cols => DirectionLine.Delete(name, cols), Layout: loaded,
            OnYes: () => StartDelete(check, force: true, loaded)));
    }

    /// <summary>
    /// Runs the delete the user confirmed. The tip the user was asked about is sent along, so a branch that moved since the question
    /// is refused. The animation's last frame shows the branch that is selected afterwards (the one at the same row).
    /// </summary>
    private void StartDelete(DeleteBranchCheck check, bool force, GraphLayout? loaded)
    {
        var snap = _app.Snapshot;
        var name = check.Name;
        var index = _selected;
        var lost = force ? check.Lost : 0;
        Func<int, IReadOnlyList<Seg>> direction = cols => DirectionLine.Delete(name, cols);
        StartBranchChange(_app.DeleteBranchOp, new DeleteBranchRequest(name, check.Tip, force), "Delete",
            c => c.Refusal, c => c.Commands,
            () => loaded,
            (before, _) =>
            {
                var layout = Or(before, snap, name, check.Tip);
                return new BranchStart(new BranchAnimation(BranchOp.Delete, layout, name, null, lost, $"Deleting {name}…"), direction, Panel(layout));
            },
            (o, after) => DeleteFinish(o, check, index, after),
            direction);
    }

    private BranchFinish DeleteFinish(BranchChangeOutcome o, DeleteBranchCheck check, int index, RepoSnapshot? after)
    {
        var name = check.Name;
        if (o.Kind == BranchChangeKind.Failed)
            return FailedFinish("Delete", o.Message, o.Commands, after, name);

        // The branch that takes the deleted one's row (clamped), so the art shows where the selection lands.
        var next = after is { Branches.Count: > 0 } ? after.Branches[Math.Clamp(index, 0, after.Branches.Count - 1)] : null;
        var layout = next is null ? null : LayoutFor(after, next.Name);
        var tip = BranchGit.Short(o.Tip);
        var caption = !check.Merged && o.Lost > 0
            ? $"✔ Deleted {name} (was {tip}): {MergeScenes.Plural(o.Lost, "commit")} {(o.Lost == 1 ? "is" : "are")} on no branch now, u brings it back"
            : $"✔ Deleted {name} (was {tip}), u brings it back";
        return new BranchFinish(new BranchResult(true, "Deleted", new Caption(caption, OkB), layout), o.Commands, [],
            $"graftui: {o.Message}", after, next?.Name);
    }
}
