using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Input;

namespace graftui.Ui.App;

/// <summary>
/// The rebase on the Branches tab (r): the current branch is put onto the selected one. A refusal is shown at once, or after the check.
/// A check that passes opens the confirm screen; Enter runs the rebase through <see cref="StartBranchChange"/>, as the other branch
/// changes run. A conflict is aborted at once for now: the pause screen comes in a later step.
/// </summary>
internal sealed partial class BranchesTab
{
    private RebaseCheck? _confirm;
    private GraphLayout? _confirmLayout;

    /// <summary>
    /// r: rebases the current branch onto the selected one. The current branch itself is refused at once. Otherwise a worker reads the
    /// layout, refuses a fast-forward (it has no commits of its own), and runs the check.
    /// </summary>
    internal void BeginRebase()
    {
        var layout = _app.CurrentLayout();
        var snap = _app.Snapshot;
        if (_app.Busy || layout.TooSmall || snap.Branches.Count == 0 || Mode != BranchesMode.Browse)
            return;

        var b = snap.Branches[_selected];
        var cur = CurrentName(snap);
        var cached = CachedLayout(snap, b);
        var direction = RebaseDirection(cur, b.Name);
        if (b.IsCurrent)
        {
            ShowBranchRefused([], RebaseText.OntoItself(cur), cached, direction);
            return;
        }

        StartWork();
        Task.Run(() =>
        {
            try
            {
                var loaded = cached ?? _app.TryLoadLayout(snap, b);
                var early = RebaseText.EarlyRefusal(loaded, cur, b.Name);
                if (early is not null)
                {
                    _app.Ui(() => ShowBranchRefused([], early, loaded, direction));
                    return;
                }

                var check = _app.RebaseOp.Check(new RebaseRequest(b.Name));
                _app.Ui(() => AfterRebaseCheck(check, loaded));
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

    /// <summary>On the UI thread after the check: its refusal is shown, and a check that passes opens the confirm screen.</summary>
    private void AfterRebaseCheck(RebaseCheck check, GraphLayout? layout)
    {
        if (!check.CanRun)
        {
            ShowBranchRefused(check.Commands, check.Refusal!, layout, RebaseDirection(check.Branch, check.Onto));
            return;
        }

        EndWork();
        _app.LogCommands(check.Commands);
        OpenConfirm(check, layout);
    }

    /// <summary>The confirm screen: the current branch's commits, each marked as rewritten, above the new base. Enter runs it.</summary>
    private void OpenConfirm(RebaseCheck check, GraphLayout? layout)
    {
        var L = Or(layout, _app.Snapshot, check.Onto, check.OntoTip);
        _confirm = check;
        _confirmLayout = L;
        Mode = BranchesMode.Confirm;
        _app.Keys.Mode = KeyBarMode.Confirm;
        _app.Keys.SetNeedsDraw();

        var caption = new Caption(RebaseText.Confirm(check.Rows.Count), Warn);
        var art = _app.Art;
        art.Title = RebaseText.ConfirmTitle;
        art.Direction = RebaseDirection(check.Branch, check.Onto);
        art.Below = null;
        art.Panel = SequenceLists.Confirm(L, check.Rows);
        art.FallbackCaption = null;
        art.Scene = geo => new SceneFrame(RebaseScenes.Preview(geo, L, check.Rows, -1).Grid, caption);
        art.SetNeedsDraw();
    }

    /// <summary>
    /// The keys on the confirm screen: Enter runs the rebase, Esc goes back to the preview, and every other key is ignored, so Tab
    /// does not switch the tab while the screen is open.
    /// </summary>
    internal void HandleConfirmKey(Key e)
    {
        e.Handled = true;
        var check = _confirm;
        if (e == Key.Enter && check is not null)
        {
            var layout = _confirmLayout;
            CloseConfirm();
            StartRebase(check, layout);
            return;
        }

        if (e == Key.Esc)
        {
            CloseConfirm();
            _app.Result = null;
            RequestPreview();
        }
    }

    /// <summary>Gives the keys back to the Branches tab. The caller decides what shows next.</summary>
    private void CloseConfirm()
    {
        _confirm = null;
        _confirmLayout = null;
        Mode = BranchesMode.Browse;
        _app.Keys.Mode = KeyBarMode.Branches;
        _app.Keys.SetNeedsDraw();
    }

    /// <summary>Runs the rebase: the operation checks again, the animation starts, then git rebases the branch.</summary>
    private void StartRebase(RebaseCheck check, GraphLayout? layout)
    {
        var onto = check.Onto;
        var rows = check.Rows;
        var L = Or(layout, _app.Snapshot, onto, check.OntoTip);
        var direction = RebaseDirection(check.Branch, onto);
        StartBranchChange(_app.RebaseOp, new RebaseRequest(onto, ExpectedHead: check.Head), "Rebase",
            c => c.Refusal, c => c.Commands,
            () => layout,
            (_, _) => new BranchStart(new RebaseAnimation(L, rows), direction, SequenceLists.Running(L, rows)),
            (o, after) => RebaseFinish(o, after, check, L),
            direction);
    }

    /// <summary>The finish of a rebase, on the worker: git's outcome becomes the result, its refusal or its failure, or the done screen.</summary>
    private BranchFinish RebaseFinish(SequenceOutcome o, RepoSnapshot? after, RebaseCheck check, GraphLayout L)
    {
        if (o.Kind == SequenceOutcomeKind.Refused)
            return RefusedFinish(o.Message, o.Commands, after, check.Onto);

        if (o.Kind == SequenceOutcomeKind.Failed)
        {
            var failed = FailedFinish("Rebase", o.Message, o.Commands, after, null);
            return failed with { ExitCode = 1 };
        }

        if (o.Kind == SequenceOutcomeKind.Paused)
            return AbortConflict(o, check, L);

        return RebaseDoneFinish(o, after, check, L);
    }

    /// <summary>A rebase that ran: the caption names the replayed commits, the panel lists what became of each commit.</summary>
    private BranchFinish RebaseDoneFinish(SequenceOutcome o, RepoSnapshot? after, RebaseCheck check, GraphLayout L)
    {
        var done = RebaseText.Done(o, check.Branch, check.Onto);
        var log = new List<(string Text, Style Style)> { (done, OkB) };
        if (RebaseText.UndoSaveFailed(o))
            log.Add((o.Message, WarnB));

        var result = new BranchResult(true, RebaseText.DoneTitle, new Caption(done, OkB), LayoutOf(after, check.Onto),
            Panel: SequenceLists.Rebased(o, check.Rows, L), Sequence: o);
        return new BranchFinish(result, o.Commands, log, "graftui: " + o.Message, after, check.Onto,
            Hints: new CommandHints(Onto: check.Onto));
    }

    /// <summary>
    /// A conflict stops the rebase. For now it is aborted at once, so the branch is exactly as it was, and the result says so. The pause
    /// screen replaces this step.
    /// </summary>
    private BranchFinish AbortConflict(SequenceOutcome o, RebaseCheck check, GraphLayout L)
    {
        var stop = o.Stop!;
        var commands = o.Commands.ToList();
        var request = new ResumeRequest(ResumeAction.Abort, null, stop.Commit);
        var abort = _app.ResumeOp.Check(request);
        commands.AddRange(abort.Commands);
        if (!abort.CanRun)
            return FailedFinish("Rebase", abort.Refusal!, commands, _app.TryLoadSnapshot(), null) with { ExitCode = 1 };

        var aborted = _app.ResumeOp.Run(request, abort);
        commands.AddRange(aborted.Commands);
        var after = _app.TryLoadSnapshot();
        if (aborted.Kind != SequenceOutcomeKind.Aborted)
            return FailedFinish("Rebase", aborted.Message, commands, after, null) with { ExitCode = 1 };

        var cur = check.Branch;
        var log = new List<(string Text, Style Style)>
        {
            (RebaseText.ConflictLine(stop), BadB),
            (RebaseText.Aborted(cur), BadB),
        };
        var result = new BranchResult(false, RebaseText.AbortedTitle, new Caption(RebaseText.Aborted(cur), BadB), L);
        return new BranchFinish(result, commands, log, RebaseText.ExitAborted(cur, check.Onto), after, null,
            Hints: new CommandHints(Onto: check.Onto));
    }

    /// <summary>The layout of a branch in a snapshot read after a change; null when that read failed or the branch is not listed.</summary>
    private GraphLayout? LayoutOf(RepoSnapshot? snap, string name)
    {
        if (snap is null)
            return null;
        var b = snap.Branches.FirstOrDefault(x => x.Name == name);
        return b is null ? null : _app.TryLoadLayout(snap, b);
    }

    /// <summary>The name of the current branch, or HEAD when HEAD is detached.</summary>
    private static string CurrentName(RepoSnapshot snap) => snap.CurrentBranch.Length == 0 ? "HEAD" : snap.CurrentBranch;

    /// <summary>The direction line of a rebase: the current branch, then the branch it goes onto. Built on the UI thread.</summary>
    private Func<int, IReadOnlyList<Seg>> RebaseDirection(string cur, string onto)
    {
        var curInk = _app.InkOf(cur);
        var ontoInk = _app.InkOf(onto);
        return cols => DirectionLine.Rebase(cur, curInk, onto, ontoInk, cols);
    }
}
