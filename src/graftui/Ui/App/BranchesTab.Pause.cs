using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Input;

namespace graftui.Ui.App;

/// <summary>
/// The pause of a rebase on the Branches tab: the screen a paused rebase shows, its keys, the question before an abort, the resume itself
/// (through the resume operation, which checks first), and the poll that notices a conflicted file being resolved in the editor.
/// While the pause lasts the branch keys are refused, Esc quits and the pause stays.
/// </summary>
internal sealed partial class BranchesTab
{
    private static readonly Style Ok = new(Ink.Ok), Bad = new(Ink.Bad);

    // The caption of the last refusal or the cancelled question. It is shown until the next key.
    private Caption? _pauseNote;

    // The conflicted files as the last poll read them; the pause screen draws their rows from these.
    private IReadOnlyList<ConflictFile>? _pauseFiles;
    private bool _pollArmed;
    private bool _pollBusy;

    // The marker-less files the open question asks about, and the commit the pause showed when it was asked. Its yes key resumes with exactly
    // these, and only while the pause is still that commit, so an answer given for one stop never continues another.
    private IReadOnlyList<KeptFile>? _askedKeep;
    private string? _askedCommit;

    /// <summary>
    /// The paused screen: the rebase's art with the progress in its direction line, the caption that says what stopped it (or the last
    /// refusal), and the conflicted files under the rule. Called only while a pause graftui started is in the snapshot.
    /// </summary>
    internal void ShowPause()
    {
        // A question can outlive the pause (the repo may have moved on): the normal preview is shown then.
        if (!_app.IsPaused)
        {
            RequestPreview();
            return;
        }

        var stop = _app.Snapshot.Paused!;
        var curInk = _app.InkOf(stop.Branch);
        var ontoInk = _app.InkOf(stop.Onto);
        var art = _app.Art;
        art.Title = RebaseText.PausedTitle;
        art.Direction = PausedDirection(stop, curInk, ontoInk);
        art.Panel = null;
        art.Below = null;
        art.Scene = geo => RebaseScenes.Paused(geo, stop, curInk, ontoInk);
        art.FallbackCaption = _pauseNote ?? SequenceStatus.Caption(stop);
        SetPauseRows(stop);
        _app.Result = null;
        _app.SyncBranchKeys();
        art.SetNeedsDraw();
        if (stop.Pause == PauseKind.Conflict)
            ArmConflictPoll();
    }

    /// <summary>The direction line of a paused rebase: the branches, then the progress as a dim tail.</summary>
    private static Func<int, IReadOnlyList<Seg>> PausedDirection(SequenceStop stop, Ink curInk, Ink ontoInk)
    {
        var tail = RebaseText.PausedTail(stop);
        return cols => DirectionLine.WithTail(DirectionLine.Rebase(stop.Branch, curInk, stop.Onto, ontoInk, cols), tail, cols);
    }

    /// <summary>The rows under the rule: a conflict lists its files; the other stops have none.</summary>
    private void SetPauseRows(SequenceStop stop)
    {
        if (stop.Pause == PauseKind.Conflict)
        {
            _pauseFiles = stop.Files;
            _app.Art.BelowFor = ConflictLines;
            return;
        }
        _app.Art.Below = [];
    }

    /// <summary>One row per conflicted file, cut to the pane's width and shown only as many as the pane has rows for.</summary>
    private IReadOnlyList<IReadOnlyList<Seg>> ConflictLines(int cols, int rows)
    {
        List<IReadOnlyList<Seg>> lines = [];
        if (_pauseFiles is null || !_app.IsPaused)
            return lines;
        foreach (var f in _pauseFiles)
            lines.Add([new Seg(TextFit.Fit(RebaseText.ConflictRow(f, _app.Snapshot.Paused!), cols), f.Resolved ? Ok : Bad)]);
        return lines.Take(rows).ToList();
    }

    /// <summary>Forgets the pause's note, so a refusal from before a tab switch is not shown again.</summary>
    internal void ForgetPauseNote() => _pauseNote = null;

    /// <summary>
    /// The keys of a paused rebase on the Branches tab. Enter continues, S skips, and X asks first. The branch keys are refused with the
    /// engine's in-progress text. Esc is not handled: it quits, and the pause stays. The arrows do nothing.
    /// </summary>
    internal void HandlePauseKey(Key e)
    {
        // The last refusal's note goes with the next key, so the screen is drawn again without it.
        var hadNote = _pauseNote is not null;
        _pauseNote = null;
        if (e == Key.Esc)
            return;

        e.Handled = true;
        if (_app.Busy)
            return;

        if (hadNote)
            ShowPause();

        var stop = _app.Snapshot.Paused!;
        if (e == Key.Enter || e == Key.S.WithShift)
        {
            var enter = e == Key.Enter;
            var action = PauseKeys.ActionFor(stop.Pause, enter ? '\0' : 'S', enter);
            if (action is not null)
                StartResume(action.Value, action == ResumeAction.Message ? stop.Message : null);
            return;
        }

        if (e == Key.X.WithShift)
            AskAbort(stop);
        else if (IsRefusedPauseKey(e))
            RefusePauseKey(stop);
    }

    /// <summary>The branch keys that a paused sequence refuses: merge, switch, new, rename, delete, rebase, interactive, cherry-pick, undo.</summary>
    private static bool IsRefusedPauseKey(Key e) =>
        e == Key.M || e == Key.S || e == Key.N || e == Key.R || e == Key.R.WithShift || e == Key.D.WithShift || e == Key.I || e == Key.C
        || e == Key.U;

    /// <summary>A refused key: the engine's words for a sequence in progress, in the log and as the caption of the pause screen.</summary>
    private void RefusePauseKey(SequenceStop stop)
    {
        var reason = ChangeRefusals.InProgress(stop.Kind == SequenceKind.CherryPick ? "cherry-pick" : "rebase");
        foreach (var line in RefusalText.LogLines(reason))
            _app.LogMessage(line, WarnB);
        _pauseNote = new Caption(RefusalText.Caption(reason), WarnB);
        ShowPause();
    }

    /// <summary>X asks before it aborts. The question is drawn over the pause, and n returns to it with "Still paused".</summary>
    private void AskAbort(SequenceStop stop)
    {
        var curInk = _app.InkOf(stop.Branch);
        var ontoInk = _app.InkOf(stop.Onto);
        var question = new BranchQuestion(RebaseText.AbortTitle, RebaseText.AbortText(stop.Branch, _app.Snapshot.PausedOldTip),
            'y', () => StartResume(ResumeAction.Abort), PausedDirection(stop, curInk, ontoInk), null)
        {
            YesLabel = " abort",
            NoLabel = " keep going",
            CancelText = RebaseText.StillPaused,
            OnNo = ShowStillPaused,
            Base = geo => RebaseScenes.Paused(geo, stop, curInk, ontoInk).Grid,
        };
        BeginQuestion(question);
        if (Mode == BranchesMode.Ask)
            SetPauseRows(stop);
    }

    /// <summary>The question was answered no: the pause screen comes back, with the "Still paused" note.</summary>
    private void ShowStillPaused()
    {
        _pauseNote = new Caption(RebaseText.StillPaused, Muted);
        ShowPause();
    }

    /// <summary>
    /// Asks which version to keep of the marker-less files, over the pause. y keeps the version the work tree holds and continues; n goes back
    /// to the pause with "Still paused".
    /// </summary>
    private void AskKeep(IReadOnlyList<KeptFile> files)
    {
        if (!_app.IsPaused)
        {
            RequestPreview();
            return;
        }

        var stop = _app.Snapshot.Paused!;
        var curInk = _app.InkOf(stop.Branch);
        var ontoInk = _app.InkOf(stop.Onto);
        var question = new BranchQuestion(RebaseText.KeepTitle, RebaseText.KeepText(stop, files), 'y', KeepAndContinue,
            PausedDirection(stop, curInk, ontoInk), null)
        {
            YesLabel = " keep & continue",
            NoLabel = " back",
            CancelText = RebaseText.StillPaused,
            ShortText = RebaseText.KeepShort(files),
            OnNo = KeepDeclined,
            Base = geo => RebaseScenes.Paused(geo, stop, curInk, ontoInk).Grid,
        };
        _askedKeep = files;
        _askedCommit = stop.Commit;
        BeginQuestion(question);
        if (Mode == BranchesMode.Ask)
            SetPauseRows(stop);
    }

    /// <summary>
    /// The yes key of the keep question: continue, keeping the files the question asked about, at the commit it asked about. The question is
    /// closed by then, so its memory goes with it.
    /// </summary>
    private void KeepAndContinue()
    {
        var keep = _askedKeep;
        var commit = _askedCommit;
        ForgetKeep();
        StartResume(ResumeAction.Continue, null, keep, commit);
    }

    /// <summary>The keep question was answered no (or cancelled): its memory goes, and the pause screen comes back with "Still paused".</summary>
    private void KeepDeclined()
    {
        ForgetKeep();
        ShowStillPaused();
    }

    private void ForgetKeep()
    {
        _askedKeep = null;
        _askedCommit = null;
    }

    /// <summary>
    /// Runs the paused sequence's next step: Continue, Skip, AmendAndContinue, Message (a reword or squash keeps its message) or Abort. The
    /// step runs on a worker and is checked first. A refusal keeps the pause screen and shows the refusal as its caption. An abort whose
    /// state file is gone runs the fallback abort: it aborts only a rebase that git holds from this branch's old tip (RebaseAbort.Force).
    /// Keep is the answer to the question about marker-less files (null when there is none). Anything else is left as it is, and the pause
    /// screen (or the failure over it) says so.
    /// </summary>
    internal void StartResume(ResumeAction action, string? message = null, IReadOnlyList<KeptFile>? keep = null, string? expectedCommit = null)
    {
        // The pause can be gone by now (a question was open while the repo moved on): the preview is shown instead.
        if (!_app.IsPaused)
        {
            _app.Result = null;
            RequestPreview();
            return;
        }

        // expectedCommit is the commit a question was asked about; the engine refuses the step when the pause has moved on since.
        var stop = _app.Snapshot.Paused!;
        var oldTip = _app.Snapshot.PausedOldTip;
        var request = new ResumeRequest(action, message, expectedCommit ?? stop.Commit, keep);
        var curInk = _app.InkOf(stop.Branch);
        var ontoInk = _app.InkOf(stop.Onto);
        var direction = PausedDirection(stop, curInk, ontoInk);
        var running = action == ResumeAction.Abort ? RebaseText.Aborting : RebaseText.Running(stop.Branch, stop.Onto);
        var engine = new RebaseEngine(_app.RootGit, _app.TryLoadSnapshot, _app.TryLoadLayout);
        var snap = _app.Snapshot;
        var bare = Or(null, snap, stop.Onto, TipOf(stop.Onto));
        var animator = _app.BranchAnimator;

        StartWork();
        _app.BeginChangeOutcome();
        _app.ChangesTask = Task.Run<string?>(() =>
        {
            try
            {
                var check = _app.ResumeOp.Check(request);
                // The fallback is for a rebase only: a cherry-pick has no rebase folder for it to find.
                var fallback = !check.CanRun && action == ResumeAction.Abort && oldTip is not null && stop.Kind == SequenceKind.Rebase
                    && SequenceGit.RebaseInProgress(check.GitDir);
                if (!check.CanRun && !fallback)
                {
                    var fresh = _app.TryLoadSnapshot();
                    _app.Ui(() => ResumeRefused(check, fresh));
                    return null;
                }

                // Quitting: the step does not start, so the pause stays as it is. Null means this change has no exit line of its own:
                // Program reads the pause from the repo at exit and prints the line for what it finds there.
                if (_app.Gate.IsQuitting)
                    return null;

                // The layout is read here, on the worker, once the step is known to run: a rebase's branch is read from its old tip, since
                // HEAD is detached while it runs.
                var layout = ResumeLayout(stop, oldTip, snap, bare);
                _app.Ui(() =>
                {
                    _app.LogCommands(check.Commands);
                    animator.Begin(ResumeAnimation(stop, action, running, curInk, ontoInk, layout), direction, null);
                });
                var rows = stop.Rows ?? [];
                var f = check.CanRun
                    ? SequenceFinish(_app.ResumeOp.Run(request, check), _app.TryLoadSnapshot(), rows, layout, engine, RebasePlan.IsEdited(rows))
                    : AbortPausedFinish(stop, oldTip!, check, engine);
                _app.SetChangeExitCode(f.ExitCode, LeftPause(f.Result));
                _app.Ui(() => animator.SetPending(f.Result, f.Commands, f.Log, () => EndBranchChange(f), f.Hints));
                return f.ExitLine;
            }
            catch (Exception ex)
            {
                _app.Ui(() => FailBranchChange(ex, "Rebase"));
                return "graftui: rebase failed: " + ex.Message;
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// The layout a resume animates from, read on the worker. A rebase's branch is read from its old tip against its onto, so the commits and
    /// the lanes are the ones the rebase started from. When the stop has no old tip (not graftui's own pause, or a cherry-pick), or the read
    /// fails, the bare pair is used, as it was before: the resume never fails or waits because of its art.
    /// </summary>
    internal GraphLayout ResumeLayout(SequenceStop stop, string? oldTip, RepoSnapshot snap, GraphLayout bare)
    {
        if (oldTip is null || stop.Kind != SequenceKind.Rebase)
            return bare;
        return _app.TryLoadRebaseLayout(snap, stop.Branch, oldTip, stop.Onto) ?? bare;
    }

    /// <summary>
    /// The art while a resume runs. A rebase that has its plan plays the todo scene from the stopped row (continue, skip or abort); anything
    /// else keeps the paused picture with the running caption.
    /// </summary>
    internal static IBranchAnimation ResumeAnimation(SequenceStop stop, ResumeAction action, string running, Ink curInk, Ink ontoInk,
        GraphLayout layout)
    {
        if (stop.Rows is null || stop.Rows.Count == 0)
            return new PausedAnimation(stop, running, curInk, ontoInk);

        var rows = stop.Rows;
        var from = TodoScenes.ResumeFrom(rows, stop, action);
        // A skip marks the stopped row. A stop that names no row of the plan has none to mark, so no row is skipped.
        var skipAt = action == ResumeAction.Skip && TodoScenes.StopIndex(rows, stop) >= 0 ? from : -1;
        var run = TodoScenes.Plan(rows, from, skipAt);
        if (action == ResumeAction.Abort)
            run = TodoScenes.Aborting(run, from, stop.Pause);
        return new TodoAnimation(layout, rows, run);
    }

    /// <summary>
    /// A step git refused before it ran: its commands and the reason are logged, the reason is the pause's caption, and the pause screen
    /// comes back. When the pause may have ended meanwhile (the sequence is gone, or another one is in progress), the snapshot is applied
    /// first, so the screen shows what is there now.
    /// </summary>
    internal void ResumeRefused(ResumeCheck check, RepoSnapshot? fresh)
    {
        // The one obstacle is marker-less files: that is a question, not a refusal, so nothing goes into the log as one.
        if (check.Keep is not null && check.Keep.Count > 0)
        {
            _app.LogCommands(check.Commands);
            if (fresh is not null)
                _app.ApplySnapshot(fresh);
            EndWork();
            AskKeep(check.Keep);
            return;
        }

        var reason = check.Refusal!;
        _app.LogCommands(check.Commands);
        foreach (var line in RefusalText.LogLines(reason))
            _app.LogMessage(line, WarnB);
        _pauseNote = new Caption(RefusalText.Caption(reason), WarnB);
        if (fresh is not null)
            _app.ApplySnapshot(fresh);
        EndWork();
        RequestPreview();
    }

    /// <summary>The tip of a listed branch, or "" when it is not listed (the layout then has a stub tip).</summary>
    private string TipOf(string name) => _app.Snapshot.Branches.FirstOrDefault(b => b.Name == name)?.Hash ?? "";

    // ----- the conflict poll -----

    // The pause screen notices a conflicted file being resolved in the editor: every second, while the pause screen is the one shown,
    // the files are read (no git, nothing logged) and the screen is drawn again when one of them became resolved.
    private void ArmConflictPoll()
    {
        if (_pollArmed)
            return;
        _pollArmed = true;
        _app.App.AddTimeout(TimeSpan.FromSeconds(1), PollConflicts);
    }

    /// <summary>The timer's tick. It stops (and re-arms on the next pause screen) once the pause screen is not the one shown.</summary>
    private bool PollConflicts()
    {
        if (!PollWanted())
        {
            _pollArmed = false;
            return false;
        }
        if (!_pollBusy && !_app.Busy)
            ReadConflicts();
        return true;
    }

    private bool PollWanted() =>
        _app.IsPaused && _app.Snapshot.Paused!.Pause == PauseKind.Conflict && _app.ActiveTab == AppController.TabKind.Branches
        && Mode == BranchesMode.Browse;

    /// <summary>
    /// Reads the conflicted files on a worker; the result is applied on the UI thread, tagged with the stop it was read for. The busy flag is
    /// always cleared there, so a read that failed does not stop the poll.
    /// </summary>
    private void ReadConflicts()
    {
        if (_pauseFiles is null)
            return;
        _pollBusy = true;
        var files = _pauseFiles;
        var root = _app.RootGit.RepoPath;
        var commit = _app.Snapshot.Paused!.Commit;
        Task.Run(() =>
        {
            var read = ReadConflictFiles(root, files);
            _app.Ui(() => ApplyConflictRead(read, commit));
        });
    }

    /// <summary>The files' resolved state read again from the work tree, or null when a read fails: the next tick reads again.</summary>
    internal static IReadOnlyList<ConflictFile>? ReadConflictFiles(string root, IReadOnlyList<ConflictFile> files)
    {
        try
        {
            return SequenceReader.Recheck(root, files);
        }
        catch (IOException)
        {
            // A file that cannot be read now only means this tick has no news.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
            return null;
        }
        catch (ArgumentException)
        {
            // A path git gave that the file system refuses: same as above.
            return null;
        }
    }

    /// <summary>
    /// On the UI thread: the new read replaces the old one, and the rows are drawn again when a file was resolved. A read for another stop
    /// (the pause moved on while it ran) or for other files is dropped.
    /// </summary>
    internal void ApplyConflictRead(IReadOnlyList<ConflictFile>? files, string commit)
    {
        _pollBusy = false;
        if (files is null || _pauseFiles is null || !_app.IsPaused || _app.Snapshot.Paused!.Commit != commit)
            return;
        var samePaths = _pauseFiles.Select(f => f.Path).SequenceEqual(files.Select(f => f.Path));
        if (!samePaths || !SequenceStatus.ResolvedChanged(_pauseFiles, files))
            return;
        _pauseFiles = files;
        _app.Art.SetNeedsDraw();
    }
}
