using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>The Changes tab's stage and unstage operations: which rows move, the selection afterwards, and the run.</summary>
internal sealed partial class ChangesTab
{
    // ---------- stage, unstage ----------

    private void StageSelected()
    {
        var row = SelectedRow;
        var snap = Snapshot;
        if (row is null || snap is null)
        {
            Flash("Nothing to stage", Warn);
            return;
        }

        if (row.Side == ChangeSide.Unstaged)
        {
            // The selection stays at the same position among the unstaged rows, or moves to the file's staged row.
            var position = Selected - snap.Status.Staged.Count;
            StartStage(new StageRequest(StageAction.Stage, row), "Stage", all: false,
                rows => AfterStage(rows, row.Path, position));
        }
        else
        {
            var position = Selected;
            StartStage(new StageRequest(StageAction.Unstage, row), "Unstage", all: false,
                rows => AfterUnstage(rows, row.Path, position));
        }
    }

    private void StageAll() =>
        StartStage(new StageRequest(StageAction.StageAll, null), "Stage", all: true, _ => 0);

    private void UnstageAll() =>
        StartStage(new StageRequest(StageAction.UnstageAll, null), "Unstage", all: true, _ => 0);

    private static int AfterStage(IReadOnlyList<FileChange> rows, string path, int position)
    {
        var staged = rows.Count(r => r.Side == ChangeSide.Staged);
        var unstagedCount = rows.Count - staged;
        if (unstagedCount > 0)
            return staged + Math.Clamp(position, 0, unstagedCount - 1);
        return Math.Max(0, IndexOfRow(rows, ChangeSide.Staged, path));
    }

    private static int AfterUnstage(IReadOnlyList<FileChange> rows, string path, int position)
    {
        var staged = rows.Count(r => r.Side == ChangeSide.Staged);
        if (staged > 0)
            return Math.Clamp(position, 0, staged - 1);
        return Math.Max(0, IndexOfRow(rows, ChangeSide.Unstaged, path));
    }

    /// <summary>
    /// One stage or unstage on a worker: Check, the quit gate, the animation starting, Run, then the result for the animation.
    /// Only one change runs at a time (Busy). Refusals and failures are reported; nothing is rolled back (StageOperation has no rollback).
    /// </summary>
    private void StartStage(StageRequest request, string verb, bool all, Func<IReadOnlyList<FileChange>, int> chooseAfter)
    {
        var snap = Snapshot;
        if (snap is null)
            return;

        var before = new AreasModel(snap.Status.Unstaged, snap.Status.Staged, snap.Head, snap.Stash);
        var staging = request.Action is StageAction.Stage or StageAction.StageAll;
        var areaOp = staging ? AreasOp.Stage : AreasOp.Unstage;
        var below = ChangesText.Summary(snap.Status, snap.Head, snap.Stash);
        var stageOp = _app.StageOp;
        var animator = _app.ChangesAnimator;
        BeginBusy();

        // Set on the UI thread when the animation starts, read there again when it ends.
        IAreasAnimation? anim = null;
        Func<int, IReadOnlyList<Seg>>? direction = null;
        _app.BeginChangeOutcome();
        _app.ChangesTask = Task.Run<string?>(() =>
        {
            try
            {
                var o = RunGated(stageOp, request, c => c.Refusal, () => _app.Gate.IsQuitting,
                    check => _app.Ui(() => Refuse(check.Commands, check.Refusal ?? "")),
                    check => _app.Ui(() =>
                    {
                        _app.LogCommands(check.Commands);
                        var what = ChangesText.What(check.Moving, all);
                        var target = staging ? "staging area" : "working tree";
                        anim = new ChangesAnimation(areaOp, before, check.Moving, $"{(staging ? "Staging" : "Unstaging")} {what}…");
                        direction = cols => ChangesText.OpDirection(verb.ToLowerInvariant(), what, [new Seg(target, new Style(Ink.Dim))], cols);
                        animator.Begin(anim, direction, below);
                    }));
                if (o is null)
                    return null;

                var moved = ChangesText.What(o.Moved, all);
                var after = o.Success && o.After is { } s ? new AreasModel(s.Unstaged, s.Staged, snap.Head, snap.Stash) : before;
                var result = o.Success
                    ? new AreasResult(true, after,
                        new Caption(staging
                            ? $"✔ Staged {moved}: it goes into the next commit"
                            : $"✔ Unstaged {moved}: the edit is still in your working tree", OkB),
                        staging ? "Staged" : "Unstaged")
                    : new AreasResult(false, before, new Caption($"✕ {verb} failed: {o.Message}", BadB), "Failed", o.Message);
                var failureLog = o.Success ? null : $"{verb} failed: {o.Message}";
                _app.Ui(() => animator.SetPending(result, o.Commands, failureLog,
                    () => AfterChange(o.Success, chooseAfter, anim, direction)));

                var head = HeadText(snap.Head.HeadHash);
                return o.Success
                    ? $"graftui: {verb.ToLowerInvariant()}d {moved}; HEAD unchanged at {head}"
                    : $"graftui: {verb.ToLowerInvariant()} failed: {o.Message}; HEAD unchanged at {head}";
            }
            catch (Exception ex)
            {
                _app.Ui(() => FailUnexpectedly(ex, verb));
                return $"graftui: {verb.ToLowerInvariant()} failed: {ex.Message}";
            }
        });
    }
}
