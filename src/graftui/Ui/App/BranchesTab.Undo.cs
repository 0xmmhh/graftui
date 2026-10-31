using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// u on the Branches tab: undoes the newest step (the same operation as on the Changes tab), then redraws the preview of the
/// selected branch from a fresh snapshot with the undo caption under it. There is no animation.
/// </summary>
internal sealed partial class BranchesTab
{
    private static readonly Style OkB = new(Ink.Ok, true), BadB = new(Ink.Bad, true), WarnB = new(Ink.Warn, true);

    /// <summary>What the preview shows after an undo: the layout (null for an empty art), the title, the caption and its style.</summary>
    private sealed record UndoView(GraphLayout? Layout, string Title, string Direction, string Caption, Style Style, string? Overlay,
        IReadOnlyList<GitResult> Commands, IReadOnlyList<string> LogLines);

    /// <summary>Undoes the newest step on a worker, then shows the result. Busy meanwhile; the change's exit line is printed if the screen closes first.</summary>
    internal void StartUndo()
    {
        var layout = _app.CurrentLayout();
        if (_app.Busy || layout.TooSmall)
            return;

        _app.ReleaseBranchWidth();
        _app.Busy = true;
        _previewCts?.Cancel();
        _app.Keys.Busy = true;
        _app.Keys.SetNeedsDraw();
        _app.SnapshotSeq++;
        _app.BeginChangeOutcome();

        var snapshot = _app.Snapshot;
        var selected = SelectedName();
        _app.ChangesTask = Task.Run<string?>(() =>
        {
            try
            {
                var check = _app.UndoOp.Check(new UndoRequest(null));
                var stepText = check.Step is null ? "nothing to undo" : $"the {check.Step.What}";

                if (check.NothingToUndo)
                {
                    var view = new UndoView(LayoutFor(snapshot, selected), "Undo", "nothing to undo", "Nothing to undo", Muted, null,
                        check.Commands, []);
                    _app.Ui(() => ShowUndoResult(view, selected));
                    return null;
                }
                if (!check.CanRun)
                {
                    var reason = check.Refusal ?? "";
                    var view = new UndoView(LayoutFor(snapshot, selected), "Refused", stepText, RefusalText.Caption(reason), WarnB,
                        RefusalText.Overlay(reason), check.Commands, RefusalText.LogLines(reason));
                    _app.Ui(() => ShowUndoResult(view, selected));
                    return null;
                }
                if (_app.Gate.IsQuitting)
                    return null;

                var o = _app.UndoOp.Run(new UndoRequest(check.StepRef), check);
                var after = _app.TryLoadSnapshot();
                var failed = o.Kind == UndoOutcomeKind.Failed;
                var what = o.Step is null ? stepText : $"the {o.Step.What}";
                var result = failed
                    ? new UndoView(LayoutFor(after ?? snapshot, selected), "Failed", what, $"✕ Undo failed: {o.Message}", BadB, null,
                        o.Commands, [$"✕ Undo failed: {o.Message}"])
                    : new UndoView(LayoutFor(after ?? snapshot, selected), "Undone", what, o.Caption, OkB, null, o.Commands, []);

                _app.Ui(() =>
                {
                    if (after is not null)
                        _app.ApplySnapshot(after);
                    ShowUndoResult(result, selected);
                });
                return failed ? $"graftui: undo failed: {o.Message}" : $"graftui: undid {what}";
            }
            catch (Exception ex)
            {
                _app.Ui(() =>
                {
                    _app.ShowGitError(ex);
                    _app.Busy = false;
                    _app.Keys.Busy = false;
                    _app.Keys.SetNeedsDraw();
                });
                return $"graftui: undo failed: {ex.Message}";
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// The layout of the named branch in a snapshot, read on the worker. When the name is gone (undoing a create removes the branch
    /// that was selected), the current branch's layout is used instead; null only when there is no branch to show.
    /// </summary>
    private GraphLayout? LayoutFor(RepoSnapshot? snap, string? name)
    {
        if (snap is null || name is null)
            return null;
        var branch = snap.Branches.FirstOrDefault(b => b.Name == name) ?? snap.Branches.FirstOrDefault(b => b.IsCurrent);
        return branch is null ? null : _app.TryLoadLayout(snap, branch);
    }

    /// <summary>
    /// Runs on the UI thread. Draws the preview (or an empty art), the undo's title and caption, and an optional refusal overlay.
    /// The selection stays on the branch, so the frame stays until the selection moves, as after a merge.
    /// </summary>
    private void ShowUndoResult(UndoView v, string? selected)
    {
        _app.LogCommands(v.Commands);
        foreach (var line in v.LogLines)
            _app.LogMessage(line, v.Style);

        var art = _app.Art;
        art.Title = v.Title;
        art.Direction = cols => ChangesText.PlainDirection("undo", v.Direction, cols);
        art.Panel = v.Layout is null ? null : CommitLists.Idle(v.Layout);
        art.FallbackCaption = null;
        art.Scene = geo =>
        {
            var grid = v.Layout is null ? new ArtGrid(geo.Width, geo.Height) : MergeScenes.Preview(geo, v.Layout).Grid;
            if (v.Overlay is not null)
                MergeScenes.RefusedOverlay(grid, v.Overlay);
            return new SceneFrame(grid, new Caption(v.Caption, v.Style));
        };
        art.SetNeedsDraw();

        _app.Result = selected;
        _app.Busy = false;
        _app.Keys.Busy = false;
        _app.Keys.SetNeedsDraw();
        _app.MarkChangeOutcomeShown();
        _app.ReloadCountsAsync();
    }
}
