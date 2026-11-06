using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Input;

namespace graftui.Ui.App;

/// <summary>What the confirm screen does with a key: Enter runs the rebase, Esc goes back to the preview, and any other key does nothing.</summary>
internal enum ConfirmKeyAction { Ignore, Run, Back }

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
    /// What a rebase's finish reads from the app: the resume operation (it aborts a conflict), the root repo (the fallback abort runs
    /// there), and the reads of the snapshot after the rebase and of a branch's layout in it.
    /// </summary>
    internal sealed record RebaseEngine(ResumeSequenceOperation Resume, GitClient Root, Func<RepoSnapshot?> ReadSnapshot,
        Func<RepoSnapshot, Branch, GraphLayout?> ReadLayout);

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
        if (b.IsCurrent)
        {
            ShowBranchRefused([], RebaseText.OntoItself(cur), cached, RebaseDirection(cur, b.Name));
            return;
        }

        StartWork();
        Task.Run(() =>
        {
            try
            {
                var loaded = cached ?? _app.TryLoadLayout(snap, b);
                var check = CheckRebase(_app.RebaseOp, cur, b.Name, loaded);
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

    /// <summary>
    /// The worker's part of r, given the layout the preview read (null when that read failed). A fast-forward is refused without a git call,
    /// so its check has no commands; any other branch is checked by the operation.
    /// </summary>
    internal static RebaseCheck CheckRebase(RebaseOperation op, string cur, string onto, GraphLayout? layout)
    {
        var early = RebaseText.EarlyRefusal(layout, cur, onto);
        return early is null ? op.Check(new RebaseRequest(onto)) : new RebaseCheck(early, cur, onto, "", "", [], "", []);
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
        ShowConfirmArt(_app.Art, check, L, RebaseDirection(check.Branch, check.Onto));
    }

    /// <summary>
    /// The confirm screen's art: the commits that will be rewritten above the new base, and the caption that says what Enter does. No
    /// fallback caption and no lines below the art.
    /// </summary>
    internal static void ShowConfirmArt(ArtPaneView art, RebaseCheck check, GraphLayout L, Func<int, IReadOnlyList<Seg>> direction)
    {
        var caption = new Caption(RebaseText.Confirm(check.Rows.Count), Warn);
        art.Title = RebaseText.ConfirmTitle;
        art.Direction = direction;
        art.Below = null;
        art.Panel = SequenceLists.Confirm(L, check.Rows);
        art.FallbackCaption = null;
        art.Scene = geo => new SceneFrame(RebaseScenes.Preview(geo, L, check.Rows, -1).Grid, caption);
        art.SetNeedsDraw();
    }

    /// <summary>
    /// The keys on the confirm screen: Enter runs the rebase when there is a check, Esc goes back to the preview, and every other key is
    /// ignored, so Tab does not switch the tab while the screen is open.
    /// </summary>
    internal void HandleConfirmKey(Key e)
    {
        e.Handled = true;
        var check = _confirm;
        var action = ConfirmKeyFor(e, check is not null);
        if (action == ConfirmKeyAction.Run && check is not null)
        {
            var layout = _confirmLayout;
            CloseConfirm();
            StartRebase(check, layout);
            return;
        }

        if (action == ConfirmKeyAction.Back)
        {
            CloseConfirm();
            _app.Result = null;
            RequestPreview();
        }
    }

    /// <summary>The action for a key on the confirm screen. Enter runs only when there is a check to run.</summary>
    internal static ConfirmKeyAction ConfirmKeyFor(Key e, bool hasCheck)
    {
        if (e == Key.Enter && hasCheck)
            return ConfirmKeyAction.Run;
        if (e == Key.Esc)
            return ConfirmKeyAction.Back;
        return ConfirmKeyAction.Ignore;
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

    /// <summary>
    /// Runs the rebase: the operation checks again, the animation starts, then git rebases the branch. The plan is the rows the confirm
    /// screen showed, so a change since then is refused and the rows the finish uses are the rows that ran.
    /// </summary>
    private void StartRebase(RebaseCheck check, GraphLayout? layout)
    {
        var onto = check.Onto;
        var L = Or(layout, _app.Snapshot, onto, check.OntoTip);
        var engine = new RebaseEngine(_app.ResumeOp, _app.RootGit, _app.TryLoadSnapshot, _app.TryLoadLayout);
        StartBranchChange(_app.RebaseOp, new RebaseRequest(onto, ExpectedHead: check.Head, Plan: check.Rows), "Rebase",
            c => c.Refusal, c => c.Commands,
            () => layout,
            (loaded, fresh) => RebaseStart(loaded, _app.Snapshot, fresh, _app.InkOf),
            (o, after) => RebaseFinish(o, after, check, L, engine),
            RebaseDirection(check.Branch, onto));
    }

    /// <summary>
    /// The animation and the running panel, drawn from the check the operation made just before git starts (not the confirm's, which is
    /// older). Called on the UI thread.
    /// </summary>
    internal static BranchStart RebaseStart(GraphLayout? loaded, RepoSnapshot snap, RebaseCheck fresh, Func<string, Ink> inkOf)
    {
        var L = Or(loaded, snap, fresh.Onto, fresh.OntoTip);
        return new BranchStart(new RebaseAnimation(L, fresh.Rows), DirectionFor(fresh.Branch, fresh.Onto, inkOf),
            SequenceLists.Running(L, fresh.Rows));
    }

    /// <summary>The finish of a rebase, on the worker: git's outcome becomes the result, its refusal or its failure, or the done screen.</summary>
    internal static BranchFinish RebaseFinish(SequenceOutcome o, RepoSnapshot? after, RebaseCheck check, GraphLayout L, RebaseEngine engine)
    {
        var hints = new CommandHints(Onto: check.Onto);
        if (o.Kind == SequenceOutcomeKind.Refused)
            return RefusedFinish(o.Message, o.Commands, after, check.Onto, hints);

        if (o.Kind == SequenceOutcomeKind.Failed)
            return FailedFinish("Rebase", o.Message, o.Commands, after, null, hints) with { ExitCode = 1 };

        if (o.Kind == SequenceOutcomeKind.Paused)
            return AbortConflict(o, check, L, engine);

        return RebaseDoneFinish(o, after, check, L, engine);
    }

    /// <summary>A rebase that ran: the caption names the replayed commits, the panel lists what became of each commit.</summary>
    internal static BranchFinish RebaseDoneFinish(SequenceOutcome o, RepoSnapshot? after, RebaseCheck check, GraphLayout L, RebaseEngine engine)
    {
        var done = RebaseText.Done(o, check.Branch, check.Onto);
        var log = new List<(string Text, Style Style)> { (done, OkB) };
        if (RebaseText.UndoSaveFailed(o))
            log.Add((o.Message, WarnB));

        // The layout after the rebase shows the onto tip as it is now; the confirm's layout is the fallback when the snapshot failed.
        var landed = LayoutAfter(after, check.Onto, engine.ReadLayout);
        var result = new BranchResult(true, RebaseText.DoneTitle, new Caption(done, OkB), landed,
            Panel: SequenceLists.Rebased(o, check.Rows, landed ?? L), Sequence: o);
        return new BranchFinish(result, o.Commands, log, "graftui: " + o.Message, after, check.Onto,
            Hints: new CommandHints(Onto: check.Onto));
    }

    /// <summary>
    /// A conflict stops the rebase. For now it is aborted at once, so the branch is exactly as it was, and the result says so. The resume
    /// operation aborts it. When that cannot be verified, git's own abort runs and is checked; if the repo is still mid-rebase, the
    /// failure says so. The exit code is 1, as for a stash-merge-pop that conflicted. The pause screen replaces this step.
    /// </summary>
    internal static BranchFinish AbortConflict(SequenceOutcome o, RebaseCheck check, GraphLayout L, RebaseEngine engine)
    {
        var stop = o.Stop!;
        var commands = o.Commands.ToList();
        var request = new ResumeRequest(ResumeAction.Abort, null, stop.Commit);
        var abort = engine.Resume.Check(request);
        commands.AddRange(abort.Commands);
        var aborted = abort.CanRun ? engine.Resume.Run(request, abort) : null;
        if (aborted is not null)
            commands.AddRange(aborted.Commands);

        var after = engine.ReadSnapshot();
        var hints = new CommandHints(Onto: check.Onto);
        if (aborted is null || aborted.Kind != SequenceOutcomeKind.Aborted)
        {
            if (!RebaseAbort.Force(engine.Root, check.GitDir, check.Branch, check.Head, commands))
                return FailedFinish("Rebase", RebaseText.NotAborted, commands, after, null, hints) with { ExitCode = 1 };

            after = engine.ReadSnapshot();
        }

        var cur = check.Branch;
        var log = new List<(string Text, Style Style)>
        {
            (RebaseText.ConflictLine(stop), BadB),
            (RebaseText.Aborted(cur), BadB),
        };
        var result = new BranchResult(false, RebaseText.AbortedTitle, new Caption(RebaseText.Aborted(cur), BadB), L);
        return new BranchFinish(result, commands, log, RebaseText.ExitAborted(cur, check.Onto), after, null,
            ExitCode: 1, Hints: hints);
    }

    /// <summary>The layout of a branch in a snapshot read after a change; null when that read failed or the branch is not listed.</summary>
    internal static GraphLayout? LayoutAfter(RepoSnapshot? snap, string name, Func<RepoSnapshot, Branch, GraphLayout?> load)
    {
        if (snap is null)
            return null;
        var b = snap.Branches.FirstOrDefault(x => x.Name == name);
        return b is null ? null : load(snap, b);
    }

    /// <summary>The name of the current branch, or HEAD when HEAD is detached.</summary>
    internal static string CurrentName(RepoSnapshot snap) => snap.CurrentBranch.Length == 0 ? "HEAD" : snap.CurrentBranch;

    /// <summary>The direction line of a rebase: the current branch, then the branch it goes onto. Built on the UI thread.</summary>
    private Func<int, IReadOnlyList<Seg>> RebaseDirection(string cur, string onto) => DirectionFor(cur, onto, _app.InkOf);

    /// <summary>The direction line of a rebase, given the ink of each branch's name.</summary>
    internal static Func<int, IReadOnlyList<Seg>> DirectionFor(string cur, string onto, Func<string, Ink> inkOf)
    {
        var curInk = inkOf(cur);
        var ontoInk = inkOf(onto);
        return cols => DirectionLine.Rebase(cur, curInk, onto, ontoInk, cols);
    }
}
