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
/// changes run. A rebase that stops (a conflict, an edit, a message) leaves the pause screen, which is in BranchesTab.Pause.cs.
/// </summary>
internal sealed partial class BranchesTab
{
    private RebaseCheck? _confirm;
    private GraphLayout? _confirmLayout;

    /// <summary>
    /// What a rebase's finish reads from the app: the root repo (the fallback abort of a paused rebase runs there), and the reads of the
    /// snapshot after the change and of a branch's layout in it.
    /// </summary>
    internal sealed record RebaseEngine(GitClient Root, Func<RepoSnapshot?> ReadSnapshot, Func<RepoSnapshot, Branch, GraphLayout?> ReadLayout);

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
    internal void AfterRebaseCheck(RebaseCheck check, GraphLayout? layout)
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
        var engine = new RebaseEngine(_app.RootGit, _app.TryLoadSnapshot, _app.TryLoadLayout);
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

    /// <summary>The finish of a rebase that was run from the confirm screen.</summary>
    internal static BranchFinish RebaseFinish(SequenceOutcome o, RepoSnapshot? after, RebaseCheck check, GraphLayout L, RebaseEngine engine) =>
        SequenceFinish(o, after, check.Rows, L, engine);

    /// <summary>
    /// The finish of any rebase step, on the worker: git's outcome becomes the result. A refusal or a failure is shown as before; a pause
    /// stops the rebase on its screen; a finished rebase (or one that was aborted) gives its result. The rows are the plan the run used.
    /// </summary>
    internal static BranchFinish SequenceFinish(SequenceOutcome o, RepoSnapshot? after, IReadOnlyList<RebaseRow> rows, GraphLayout L,
        RebaseEngine engine)
    {
        var onto = o.Onto;
        var hints = new CommandHints(Onto: onto);
        if (o.Kind == SequenceOutcomeKind.Refused)
            return RefusedFinish(o.Message, o.Commands, after, onto, hints);

        if (o.Kind == SequenceOutcomeKind.Failed)
            return FailedFinish("Rebase", o.Message, o.Commands, after, null, hints) with { ExitCode = 1 };

        if (o.Kind == SequenceOutcomeKind.Paused)
            return PausedFinish(o, after);

        if (o.Kind == SequenceOutcomeKind.Aborted)
            return AbortedFinish(o.Branch, onto, o.Commands, after, LayoutAfter(after, onto, engine.ReadLayout));

        return RebaseDoneFinish(o, after, rows, L, engine);
    }

    /// <summary>A rebase that ran: the caption names the replayed commits, the panel lists what became of each commit.</summary>
    internal static BranchFinish RebaseDoneFinish(SequenceOutcome o, RepoSnapshot? after, IReadOnlyList<RebaseRow> rows, GraphLayout L,
        RebaseEngine engine)
    {
        var done = RebaseText.Done(o, o.Branch, o.Onto);
        var log = new List<(string Text, Style Style)> { (done, OkB) };
        if (RebaseText.UndoSaveFailed(o))
            log.Add((o.Message, WarnB));

        // The layout after the rebase shows the onto tip as it is now; the layout the change started from is the fallback when the snapshot failed.
        var landed = LayoutAfter(after, o.Onto, engine.ReadLayout);
        var result = new BranchResult(true, RebaseText.DoneTitle, new Caption(done, OkB), landed,
            Panel: SequenceLists.Rebased(o, rows, landed ?? L), Sequence: o);
        return new BranchFinish(result, o.Commands, log, "graftui: " + o.Message, after, o.Onto,
            Hints: new CommandHints(Onto: o.Onto));
    }

    /// <summary>
    /// A rebase that stopped: the pause screen takes over. The log says what stopped it (a conflict only, the other stops show their caption).
    /// The exit line is null: when graftui exits, the line is read from the repo then, since the pause may have ended by then.
    /// The exit code is 1 for a conflict left paused, as a conflicted rebase exits 1; an edit or a message exits 0.
    /// </summary>
    internal static BranchFinish PausedFinish(SequenceOutcome o, RepoSnapshot? after)
    {
        var stop = o.Stop!;
        var conflict = stop.Pause == PauseKind.Conflict;
        var log = new List<(string Text, Style Style)>();
        if (conflict)
            log.Add((RebaseText.ConflictLine(stop), BadB));
        var result = new BranchResult(true, RebaseText.PausedTitle, SequenceStatus.Caption(stop), null, Sequence: o);
        return new BranchFinish(result, o.Commands, log, null, after, null, ExitCode: conflict ? 1 : 0,
            Hints: new CommandHints(Onto: o.Onto));
    }

    /// <summary>A rebase that was aborted: the branch is back on its old tip, and the selection stays where it was.</summary>
    internal static BranchFinish AbortedFinish(string cur, string onto, IReadOnlyList<GitResult> commands, RepoSnapshot? after,
        GraphLayout? landed)
    {
        var text = RebaseText.Aborted(cur);
        var result = new BranchResult(false, RebaseText.AbortedTitle, new Caption(text, BadB), landed);
        return new BranchFinish(result, commands, [(text, BadB)], RebaseText.ExitAborted(cur, onto), after, null,
            Hints: new CommandHints(Onto: onto));
    }

    /// <summary>
    /// The fallback abort of a paused rebase, for when the resume operation refuses because its state file is gone: git's own abort runs and
    /// is checked against the old tip the snapshot read. When that cannot be verified, the failure says the rebase must be aborted by hand.
    /// </summary>
    internal static BranchFinish AbortPausedFinish(SequenceStop stop, string oldTip, ResumeCheck check, RebaseEngine engine)
    {
        var commands = check.Commands.ToList();
        var restored = RebaseAbort.Force(engine.Root, check.GitDir, stop.Branch, oldTip, commands);
        var after = engine.ReadSnapshot();
        if (!restored)
            return FailedFinish("Rebase", RebaseText.NotAborted, commands, after, null, new CommandHints(Onto: stop.Onto)) with { ExitCode = 1 };

        return AbortedFinish(stop.Branch, stop.Onto, commands, after, LayoutAfter(after, stop.Onto, engine.ReadLayout));
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
