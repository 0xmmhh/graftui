using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// The branch changes that start from the list: switch (s, not undoable), new (n), rename (R), delete (D: asked first; a merged branch
/// gets one question, an unmerged one two, on different keys) and stash, merge, pop (m over uncommitted changes, asked first).
/// Each runs through <see cref="StartBranchChange"/>.
/// </summary>
internal sealed partial class BranchesTab
{
    private const string DeleteCancelled = "Delete cancelled: nothing changed";
    // Two names for one word: the question box's title and the operation's verb are separate roles, so each can change alone.
    private const string DeleteTitle = "Delete";
    private const string DeleteVerb = "Delete";

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
        var fromInk = _app.InkOf(from);
        var toInk = _app.InkOf(name);
        Func<int, IReadOnlyList<Seg>> direction = cols => DirectionLine.Switch(from, fromInk, name, toInk, cols);
        // The layout is read on the worker before the check, so the finish (later, on the same worker) uses the same one.
        GraphLayout? read = null;
        StartBranchChange(_app.SwitchOp, new SwitchRequest(name), "Switch",
            c => c.Refusal, c => c.Commands,
            () => read = cached ?? _app.TryLoadLayout(snap, b),
            (loaded, _) =>
            {
                var before = Or(loaded, snap, name, b.Hash);
                return new BranchStart(new BranchAnimation(BranchOp.Switch, before, name, null, 0, $"Switching to {name}…"), direction, Panel(before));
            },
            (o, after) => SwitchFinish(o, name, after, Or(read, snap, name, b.Hash)),
            direction);
    }

    private static BranchFinish SwitchFinish(SwitchOutcome o, string name, RepoSnapshot? after, GraphLayout before)
    {
        if (o.Kind == SwitchKind.Refused)
            return RefusedFinish(o.Message, o.Commands, after, name);
        if (o.Kind == SwitchKind.Failed)
            return FailedFinish("Switch", o.Message, o.Commands, after, name);

        // No layout after the switch: the animation keeps the pre-switch graph on screen, with the labels updated, until the selection moves.
        // The commit panel moves its "(current)" header the same way.
        var came = o.CameAlong > 0 ? $"your {MergeScenes.Plural(o.CameAlong, "changed file")} came along" : null;
        var caption = came is null ? $"✔ Switched to {name}" : $"✔ Switched to {name} · {came}";
        var exit = $"graftui: switched to {name}" + (came is null ? "" : $"; {came}");
        var result = new BranchResult(true, "Switched", new Caption(caption, OkB), null, Panel: CommitLists.Switched(before));
        return new BranchFinish(result, o.Commands, [], exit, after, name, HoldWidth: true);
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
                Title: DeleteTitle,
                Text: $"Delete {name}? Everything on it is already in {into}, so nothing is lost; u brings it back.",
                YesKey: 'y', YesLabel: " delete", NoLabel: " keep", CancelText: DeleteCancelled,
                Direction: direction, Layout: loaded,
                OnYes: () => StartDelete(check, force: false, loaded)));
            return;
        }

        var commits = MergeScenes.Plural(check.Ahead, "commit");
        BeginQuestion(new BranchQuestion(
            Title: DeleteTitle,
            Text: $"{name} has {commits} that {into} doesn't have. Deleting it needs git branch -D; "
                + "graftui records where it pointed, so u can bring it back.",
            YesKey: 'y', YesLabel: " continue", NoLabel: " keep", CancelText: DeleteCancelled,
            Direction: direction, Layout: loaded,
            OnYes: () => AskDeleteForGood(check, loaded),
            ShortText: $"{name}: {commits} {into} doesn't have. Needs git branch -D; u brings it back."));
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
        StartBranchChange(_app.DeleteBranchOp, new DeleteBranchRequest(name, check.Tip, force), DeleteVerb,
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
            return FailedFinish(DeleteVerb, o.Message, o.Commands, after, name);

        // The branch that takes the deleted one's row (clamped), so the art shows where the selection lands.
        var next = after is { Branches.Count: > 0 } ? after.Branches[Math.Clamp(index, 0, after.Branches.Count - 1)] : null;
        var layout = next is null ? null : LayoutFor(after, next.Name);
        var tip = BranchGit.Short(o.Tip);
        var caption = $"✔ Deleted {name} (was {tip}), u brings it back";
        if (!check.Merged && o.Lost > 0)
            caption = $"✔ Deleted {name} (was {tip}): {MergeScenes.Plural(o.Lost, "commit")} {(o.Lost == 1 ? "is" : "are")} on no branch now, u brings it back";
        return new BranchFinish(new BranchResult(true, "Deleted", new Caption(caption, OkB), layout), o.Commands, [],
            $"graftui: {o.Message}", after, next?.Name);
    }

    // ---------- new and rename (the name box has checked the name) ----------

    /// <summary>Creates the typed branch at the current tip and switches to it. The name box has already checked it.</summary>
    private void StartCreate(string name)
    {
        var snap = _app.Snapshot;
        var from = snap.CurrentBranch;
        var tip = _nameTip;
        var layout = _nameLayout;
        Func<int, IReadOnlyList<Seg>> direction = cols => DirectionLine.NewBranch(name, BranchGit.Short(tip), cols);
        StartBranchChange(_app.CreateBranchOp, new CreateBranchRequest(name), "Create",
            c => c.Refusal, c => c.Commands,
            () => layout,
            (loaded, _) =>
            {
                var before = Or(loaded, snap, from, tip);
                return new BranchStart(new BranchAnimation(BranchOp.Create, before, name, null, 0, $"Creating {name}…"), direction, Panel(before));
            },
            (o, after) => CreateFinish(o, name, after),
            direction);
    }

    private BranchFinish CreateFinish(BranchChangeOutcome o, string name, RepoSnapshot? after)
    {
        if (o.Kind == BranchChangeKind.Failed)
            return FailedFinish("Create", o.Message, o.Commands, after, null);

        var tip = BranchGit.Short(o.Tip);
        var layout = after is null ? null : LayoutFor(after, name);
        var caption = o.Undo is null ? $"✔ {o.Message}" : $"✔ Created {name} at {tip} and switched to it (u undoes it)";
        return new BranchFinish(new BranchResult(true, "Branch created", new Caption(caption, OkB), layout), o.Commands, [],
            $"graftui: {o.Message}", after, name);
    }

    /// <summary>Renames <paramref name="old"/> to <paramref name="nu"/>, the current branch included. The name box has checked the name.</summary>
    private void StartRename(string old, string nu)
    {
        var snap = _app.Snapshot;
        var tip = _nameTip;
        var layout = _nameLayout;
        var oldInk = _app.InkOf(old);
        Func<int, IReadOnlyList<Seg>> direction = cols => DirectionLine.Rename(old, oldInk, nu, cols);
        StartBranchChange(_app.RenameBranchOp, new RenameBranchRequest(old, nu), "Rename",
            c => c.Refusal, c => c.Commands,
            () => layout,
            (loaded, _) =>
            {
                var before = Or(loaded, snap, old, tip);
                return new BranchStart(new BranchAnimation(BranchOp.Rename, before, old, nu, 0, $"Renaming {old}…"), direction, Panel(before));
            },
            (o, after) => RenameFinish(o, old, nu, after),
            direction);
    }

    private BranchFinish RenameFinish(BranchChangeOutcome o, string old, string nu, RepoSnapshot? after)
    {
        if (o.Kind == BranchChangeKind.Failed)
            return FailedFinish("Rename", o.Message, o.Commands, after, old);

        var layout = after is null ? null : LayoutFor(after, nu);
        var caption = o.Undo is null ? $"✔ {o.Message}" : $"✔ Renamed {old} to {nu} (u undoes it)";
        return new BranchFinish(new BranchResult(true, "Renamed", new Caption(caption, OkB), layout), o.Commands, [],
            $"graftui: {o.Message}", after, nu);
    }

    // ---------- stash, merge, pop (m over uncommitted changes) ----------

    /// <summary>
    /// m was refused because of uncommitted changes. Runs on the merge's worker: the stash-merge check is made there, and its refusal is
    /// shown, or the question is asked on the UI thread.
    /// </summary>
    private void OfferStashMerge(string branch, MergeCheck merge, GraphLayout? loaded)
    {
        var check = _app.StashMergeOp.Check(new StashMergeRequest(branch));
        _app.Ui(() =>
        {
            if (check.CanRun)
                AskStashMerge(check, branch, loaded);
            else
            {
                var branchInk = _app.InkOf(branch);
                var curInk = _app.InkOf(merge.CurrentBranch);
                ShowBranchRefused(check.Commands, check.Refusal!, loaded,
                    cols => DirectionLine.Merge(branch, branchInk, merge.CurrentBranch, curInk, false, cols));
            }
        });
    }

    /// <summary>The question: stash the changes, merge the branch, pop them back. Its commands are logged here; the run logs its own.</summary>
    private void AskStashMerge(StashMergeCheck check, string branch, GraphLayout? layout)
    {
        EndWork();
        _app.LogCommands(check.Commands);
        var into = check.Into;
        var changed = check.Changed;
        var branchInk = _app.InkOf(branch);
        var intoInk = _app.InkOf(into);
        BeginQuestion(new BranchQuestion(
            Title: "Uncommitted changes",
            Text: $"You have {MergeScenes.Plural(changed, "uncommitted change")}. Stash them, merge {branch} into {into}, then pop them back?",
            YesKey: 'y', YesLabel: " stash, merge, pop", NoLabel: " cancel", CancelText: "Merge cancelled: nothing changed",
            Direction: cols => DirectionLine.StashMerge(branch, branchInk, into, intoInk, cols), Layout: layout,
            OnYes: () => StartStashMerge(branch, into, layout, changed)));
    }

    /// <summary>
    /// Runs stash, merge, pop. The run's own check is not logged again (the question logged it). The animation stashes, plays the merge,
    /// then pops the changes back.
    /// </summary>
    private void StartStashMerge(string branch, string into, GraphLayout? layout, int changed)
    {
        var snap = _app.Snapshot;
        var tip = snap.Branches.FirstOrDefault(b => b.Name == branch)?.Hash ?? "";
        var branchInk = _app.InkOf(branch);
        var intoInk = _app.InkOf(into);
        Func<int, IReadOnlyList<Seg>> direction = cols => DirectionLine.StashMerge(branch, branchInk, into, intoInk, cols);
        StartBranchChange(_app.StashMergeOp, new StashMergeRequest(branch), "Stash, merge, pop",
            c => c.Refusal, _ => Array.Empty<GitResult>(),
            () => layout,
            (loaded, _) =>
            {
                var before = Or(loaded, snap, branch, tip);
                return new BranchStart(new StashMergeAnimation(before, changed), direction, Panel(before));
            },
            (o, after) => StashMergeFinish(o, branch, into, changed, LayoutFor(after, branch), after),
            direction);
    }

    /// <summary>
    /// The result of stash, merge, pop. A merge that stands keeps the layout after it; a conflict keeps the layout before (the repo is
    /// as it was), and the merge's own conflict scene plays out.
    /// </summary>
    internal static BranchFinish StashMergeFinish(StashMergeOutcome o, string branch, string into, int changed, GraphLayout? layout,
        RepoSnapshot? after)
    {
        var files = MergeScenes.Plural(changed, "changed file");
        var back = changed == 1 ? "is back" : "are back";
        switch (o.Kind)
        {
            case StashMergeKind.Merged when o.Merge?.Kind == MergeKind.UpToDate:
                return new BranchFinish(new BranchResult(true, "Merged", new Caption("Already up to date: your changes are back", Muted), null,
                        Merge: o.Merge),
                    o.Commands, [], $"graftui: stashed, found {branch} already in {into}, and popped {files} back", after, branch);

            case StashMergeKind.Merged when o.Undo is null:
                // The merge stands and the changes are back, but its undo step could not be saved: the message says so and where the
                // state from before is kept.
                return new BranchFinish(new BranchResult(true, "Merged", new Caption(o.Message, Warn), layout, Merge: o.Merge),
                    o.Commands, [(o.Message, WarnB)], $"graftui: {o.Message}", after, branch);

            case StashMergeKind.Merged:
            {
                var merge = o.Merge;
                var fast = merge?.Kind == MergeKind.FastForward;
                var at = BranchGit.Short(merge?.NewHead ?? "");
                var text = fast
                    ? $"✔ Fast-forward: {into} is now at {at}, your {files} {back}"
                    : $"✔ Merge commit {at} created on {into}, your {files} {back}";
                return new BranchFinish(new BranchResult(true, fast ? "Fast-forwarded" : "Merged", new Caption(text, OkB), layout, Merge: merge),
                    o.Commands, [], $"graftui: stashed, merged {branch} into {into} and popped {files} back", after, branch);
            }

            case StashMergeKind.MergeConflict:
            {
                var conflicts = o.ConflictFiles;
                var text = conflicts.Count > 0
                    ? $"✕ CONFLICT in {conflicts[0]}: merge aborted, your changes are back as they were"
                    : "✕ CONFLICT: merge aborted, your changes are back as they were";
                IReadOnlyList<(string Text, Style Style)> log = conflicts.Count > 0
                    ? [($"Conflicting files: {string.Join(", ", conflicts)}", BadB)]
                    : [];
                return new BranchFinish(new BranchResult(true, "Aborted", new Caption(text, BadB), null, Merge: o.Merge), o.Commands, log,
                    $"graftui: merge of {branch} conflicted{(conflicts.Count > 0 ? $" in {conflicts[0]}" : "")}; aborted, your changes are back",
                    after, branch, ExitCode: 1);
            }

            case StashMergeKind.PopConflict:
            {
                // Without an undo step the overlay and the log do not offer u: the message says what was saved instead.
                var conflicts = o.ConflictFiles;
                var undoable = o.Undo is not null;
                var caption = o.Message;
                if (undoable)
                    caption = conflicts.Count > 0
                        ? $"✕ Merged, but the pop conflicted in {conflicts[0]}: changes kept in the stash"
                        : "✕ Merged, but the pop failed: changes kept in the stash";
                var tail = undoable ? "  Z tries again · u undoes the merge" : "  Z tries again";
                var overlay = conflicts.Count > 0
                    ? $"Merged {branch} into {into}. Your changes conflict with it, so they stay in the stash: {string.Join(", ", conflicts)}" + tail
                    : $"Merged {branch} into {into}. Your changes could not be popped back, so they stay in the stash." + tail;
                IReadOnlyList<(string Text, Style Style)> log = undoable
                    ? [(caption, BadB), (overlay, BadB)]
                    : [(o.Message, WarnB)];
                var popOutcome = conflicts.Count > 0 ? $"conflicted in {conflicts[0]}" : "failed";
                var exit = undoable
                    ? $"graftui: merged {branch} into {into}; the pop {popOutcome}, your changes are in stash@{{0}}"
                    : $"graftui: {o.Message}";
                return new BranchFinish(
                    new BranchResult(true, "Pop conflict", new Caption(caption, undoable ? BadB : Warn), layout, "Pop conflict", overlay, o.Merge),
                    o.Commands, log, exit, after, branch, ExitCode: 1);
            }

            default:
                return FailedFinish("Stash, merge, pop", o.Message, o.Commands, after, branch) with { ExitCode = 1 };
        }
    }
}
