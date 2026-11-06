using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// The shared runner of the branch changes, and the refusal screen they show. A change runs in the order of the gate: the layout is
/// read, Check, the refusal test, the quit gate, the animation starts, then Run. Its result is drawn when the animation ends, then
/// the snapshot is reloaded and the selection moves. Each change runs on a worker; every view change is on the UI thread.
/// </summary>
internal sealed partial class BranchesTab
{
    /// <summary>What a change shows when it starts: its animation, its direction line and the panel under the art.</summary>
    internal sealed record BranchStart(IBranchAnimation Anim, Func<int, IReadOnlyList<Seg>> Direction, CommitPanel? Commits);

    /// <summary>
    /// What a change shows when git has answered: the result, the commands it ran, the lines it adds to the log, the exit line,
    /// the snapshot read after it (null when that read failed) and the branch to select afterwards. The rest is set with initialisers:
    /// <c>ExitCode</c> is the process exit code the change asks for (1 when it conflicted or failed after a merge; 0 otherwise).
    /// <c>HoldWidth</c> keeps the art's width where it is when the snapshot is applied (a switch: its last frame must not reflow).
    /// <c>Hints</c> tells the Commands pane what the commands were for (a rebase's target). The exit line is null when a pause is left:
    /// the terminal's line is then read from the repo at exit.
    /// </summary>
    internal sealed record BranchFinish(BranchResult Result, IReadOnlyList<GitResult> Commands, IReadOnlyList<(string Text, Style Style)> Log,
        string? ExitLine, RepoSnapshot? After, string? Select)
    {
        public int ExitCode { get; init; }

        public bool HoldWidth { get; init; }

        public CommandHints? Hints { get; init; }
    }

    /// <summary>Marks the tab busy for a change or a check that reads git. Any preview still loading is stale now.</summary>
    private void StartWork()
    {
        _app.ReleaseBranchWidth();
        _app.Busy = true;
        _previewCts?.Cancel();
        _app.Keys.Busy = true;
        _app.Keys.SetNeedsDraw();
        _app.SnapshotSeq++;
    }

    /// <summary>Clears busy without drawing a result (a check that asked a question, or that failed before anything started).</summary>
    private void EndWork()
    {
        _app.Busy = false;
        _app.Keys.Busy = false;
        _app.Keys.SetNeedsDraw();
    }

    /// <summary>
    /// Runs one branch change on a worker: <paramref name="loadLayout"/> first (the preview's layout, for the animation and a refusal),
    /// then the operation's Check. A refusal from Check is shown at once; otherwise the animation starts (<paramref name="begin"/>) and
    /// Run follows. <paramref name="finish"/> turns the outcome into the result, reading the snapshot after it.
    /// </summary>
    private void StartBranchChange<TReq, TCheck, TOut>(IGitOperation<TReq, TCheck, TOut> op, TReq req, string verb,
        Func<TCheck, string?> refusal, Func<TCheck, IReadOnlyList<GitResult>> checkCommands,
        Func<GraphLayout?> loadLayout, Func<GraphLayout?, TCheck, BranchStart> begin,
        Func<TOut, RepoSnapshot?, BranchFinish> finish, Func<int, IReadOnlyList<Seg>> refusedDirection) where TOut : class
    {
        var animator = _app.BranchAnimator;
        StartWork();
        _app.BeginChangeOutcome();
        _app.ChangesTask = Task.Run<string?>(() =>
        {
            try
            {
                var loaded = loadLayout();
                var o = ChangesTab.RunGated(op, req, refusal, () => _app.Gate.IsQuitting,
                    check => _app.Ui(() => ShowBranchRefused(checkCommands(check), refusal(check) ?? "", loaded, refusedDirection)),
                    check => _app.Ui(() =>
                    {
                        var start = begin(loaded, check);
                        _app.LogCommands(checkCommands(check));
                        animator.Begin(start.Anim, start.Direction, start.Commits);
                    }));
                if (o is null)
                    return null;

                var f = finish(o, _app.TryLoadSnapshot());
                _app.SetChangeExitCode(f.ExitCode, LeftPause(f.Result));
                _app.Ui(() => animator.SetPending(f.Result, f.Commands, f.Log, () => EndBranchChange(f), f.Hints));
                return f.ExitLine;
            }
            catch (Exception ex)
            {
                _app.Ui(() => FailBranchChange(ex, verb));
                return $"graftui: {verb.ToLowerInvariant()} failed: {ex.Message}";
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Runs on the UI thread when a change's final frame is drawn: the new snapshot, the selection, and the counts. A change that left the
    /// repo paused then shows the pause screen (see <see cref="ShowPauseAfter"/>).
    /// </summary>
    internal void EndBranchChange(BranchFinish f)
    {
        if (f.HoldWidth)
            _app.HoldBranchWidth();
        if (f.After is not null)
            _app.ApplySnapshot(f.After);
        if (f.Select is { } name)
            SelectName(name);
        _app.Result = SelectedName();
        _app.ReloadCountsAsync(() => ShowPauseAfter(f.Result));
    }

    /// <summary>
    /// After a change, when the repo is still paused: the pause screen takes the art, in place of the change's last frame. A change that
    /// left the pause as it was (refused or failed) gives its caption to the pause screen, so the reason stays on screen. Call it after the
    /// snapshot has been read.
    /// </summary>
    internal void ShowPauseAfter(BranchResult result)
    {
        if (!_app.IsPaused)
            return;
        if (!LeftPause(result))
            _pauseNote = result.Caption;
        ShowPause();
    }

    /// <summary>True when the change's outcome is a rebase or cherry-pick left paused (so the pause screen is its screen).</summary>
    internal static bool LeftPause(BranchResult result) => result.Sequence?.Kind == SequenceOutcomeKind.Paused;

    /// <summary>An exception on the worker: shown on the status bar; the change's animation ends as a failure when one was running.</summary>
    private void FailBranchChange(Exception ex, string verb)
    {
        _app.ShowGitError(ex);
        if (_app.BranchAnimator.Running)
        {
            var text = $"{verb} failed: {ex.Message}";
            var failed = new BranchResult(false, "Failed", new Caption($"✕ {text}", BadB), null);
            _app.BranchAnimator.SetPending(failed, [], [(text, BadB)], () =>
            {
                _app.Result = SelectedName();
                _app.ReloadCountsAsync(() => ShowPauseAfter(failed));
            });
            return;
        }
        // The error is on screen now, so the exit path need not print this change's line again.
        _app.MarkChangeOutcomeShown();
        RestoreAfterError();
    }

    /// <summary>Gives the keys back after a check failed before anything started: the preview of the selection comes back.</summary>
    private void RestoreAfterError()
    {
        EndWork();
        _app.Art.SetNeedsDraw();
        _app.Result = null;
        RequestPreview();
    }

    /// <summary>
    /// The refusal screen: the commands that ran, the reason in the log, the art with the refusal over it, and the caption.
    /// Nothing changed, so the snapshot is reloaded and the selection stays where it was.
    /// </summary>
    private void ShowBranchRefused(IReadOnlyList<GitResult> commands, string reason, GraphLayout? layout, Func<int, IReadOnlyList<Seg>> direction)
    {
        _app.LogCommands(commands);
        foreach (var line in RefusalText.LogLines(reason))
            _app.LogMessage(line, WarnB);

        var art = _app.Art;
        art.Title = "Refused";
        art.Direction = direction;
        art.Below = null;
        art.Panel = Panel(layout);
        art.FallbackCaption = null;
        art.Scene = geo =>
        {
            var grid = layout is null ? new ArtGrid(geo.Width, geo.Height) : MergeScenes.Preview(geo, layout).Grid;
            MergeScenes.RefusedOverlay(grid, RefusalText.Overlay(reason));
            return new SceneFrame(grid, new Caption(RefusalText.Caption(reason), WarnB));
        };
        art.SetNeedsDraw();

        _app.Result = SelectedName();
        EndWork();
        _app.ReloadCountsAsync();
    }

    /// <summary>The result of a change that git refused after its animation had started: the refusal over the art, as a refusal screen.</summary>
    private static BranchResult RefusedResult(string reason) =>
        new(false, "Refused", new Caption(RefusalText.Caption(reason), WarnB), null, "Refused", RefusalText.Overlay(reason));

    private static BranchFinish RefusedFinish(string reason, IReadOnlyList<GitResult> commands, RepoSnapshot? after, string select,
        CommandHints? hints = null) =>
        new(RefusedResult(reason), commands, RefusalText.LogLines(reason).Select(l => (Text: l, Style: WarnB)).ToList(),
            $"graftui: refused: {reason}", after, select) { Hints = hints };

    /// <summary>A change that failed: the failure is the result. A null <paramref name="select"/> keeps the selection where it is.</summary>
    private static BranchFinish FailedFinish(string verb, string message, IReadOnlyList<GitResult> commands, RepoSnapshot? after, string? select,
        CommandHints? hints = null)
    {
        var text = $"{verb} failed: {message}";
        return new BranchFinish(new BranchResult(false, "Failed", new Caption($"✕ {text}", BadB), null), commands, [(text, BadB)],
            $"graftui: {verb.ToLowerInvariant()} failed: {message}", after, select) { Hints = hints };
    }

    /// <summary>The commit panel of a layout (the commit rows under the art), or null without a layout.</summary>
    private static CommitPanel? Panel(GraphLayout? layout) => layout is null ? null : CommitLists.Idle(layout);

    /// <summary>The layout to animate from: the loaded one, or the bare pair of branches when the load failed.</summary>
    private static GraphLayout Or(GraphLayout? loaded, RepoSnapshot snap, string name, string tip) =>
        loaded ?? GraphLayout.Minimal(snap.CurrentBranch, snap.CurrentBranch.Length == 0, name, snap.HeadHash ?? "", tip)
            .WithLanes(snap.Branches);
}
