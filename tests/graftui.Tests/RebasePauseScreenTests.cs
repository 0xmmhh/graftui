using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The pause screen on the app's own parts, built without a terminal: the screen a paused rebase opens on, the keys the Branches tab takes
/// while it is paused (Enter, S, X and its question, the refused branch keys, the arrows, and Esc, which is not handled), and the conflict
/// rows. A key that starts git runs on a worker, and its test waits for that worker. They share a headless Terminal.Gui session.
/// </summary>
[Collection("TerminalGui")]
public sealed class RebasePauseScreenTests : IDisposable
{
    private readonly IApplication _ui;

    public RebasePauseScreenTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose() => _ui.Dispose();

    private static string GitDir(TempRepo repo) => Path.Combine(repo.Dir, ".git");

    /// <summary>Starts a rebase of the current branch onto main that must pause, and returns the app opened on the repo.</summary>
    private AppController OpenPaused(TempRepo repo, IReadOnlyList<RebaseRow>? plan = null)
    {
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main", plan);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(request, check).Kind);

        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        // The window is laid out at the screen's size, without starting a session, so the art has its size (a question needs it to fit).
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }

    private static string Caption(AppController app) => app.Art.FallbackCaption!.Text;

    [Fact]
    public void A_paused_rebase_opens_on_the_pause_screen_with_its_caption_and_the_conflicted_file()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);

        Assert.True(app.IsPaused);
        Assert.Equal(RebaseText.PausedTitle, app.Art.Title);
        Assert.Equal(RebaseText.PauseCaption(app.Snapshot.Paused!), Caption(app));
        Assert.Equal("feature", app.BranchList.PausedBranch);
        Assert.Equal(PauseKind.Conflict, app.Keys.BranchesState.Pause);
        Assert.Equal(new[] { "✖ app.txt  both modified" }, app.Art.BelowFor!(40, 3).Select(line => string.Concat(line.Select(s => s.Text))));
    }

    [Fact]
    public void The_branch_keys_are_refused_with_the_engine_text_and_nothing_runs()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);

        foreach (var key in new[] { Key.M, Key.S, Key.N, Key.R, Key.D.WithShift, Key.U, Key.C, Key.I })
        {
            _ui.Keyboard.RaiseKeyDownEvent(key);

            Assert.Equal("Refused: a rebase is in progress: finish or abort it first", Caption(app));
            Assert.False(app.Busy);
        }
        Assert.True(app.IsPaused);
        Assert.True(SequenceGit.RebaseInProgress(GitDir(repo)));
    }

    [Fact]
    public void Esc_is_not_handled_so_it_quits_and_the_pause_stays()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);

        var key = Key.Esc;
        app.BranchesTab.HandlePauseKey(key);

        Assert.False(key.Handled);
        Assert.True(app.IsPaused);
        Assert.True(SequenceGit.RebaseInProgress(GitDir(repo)));
    }

    [Fact]
    public void The_arrows_are_taken_and_do_nothing_while_paused()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);
        var selected = app.BranchesTab.Selected;

        var down = Key.CursorDown;
        app.BranchesTab.HandlePauseKey(down);

        Assert.True(down.Handled);
        Assert.Equal(selected, app.BranchesTab.Selected);
        Assert.False(app.Busy);
    }

    [Fact]
    public void X_asks_before_aborting_and_n_keeps_the_pause()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);

        _ui.Keyboard.RaiseKeyDownEvent(Key.X.WithShift);
        Assert.Equal(BranchesMode.Ask, app.BranchesTab.Mode);
        Assert.Equal(RebaseText.AbortTitle, app.Art.Title);

        _ui.Keyboard.RaiseKeyDownEvent(Key.N);

        Assert.Equal(BranchesMode.Browse, app.BranchesTab.Mode);
        Assert.Equal(RebaseText.StillPaused, Caption(app));
        Assert.True(app.IsPaused);
        Assert.True(SequenceGit.RebaseInProgress(GitDir(repo)));
    }

    [Fact]
    public void An_abort_question_that_does_not_fit_keeps_the_pause_art_under_its_caption()
    {
        using var repo = SequenceRepos.FeatureConflict();
        _ui.Driver!.SetScreenSize(40, 8);
        var app = OpenPaused(repo);

        _ui.Keyboard.RaiseKeyDownEvent(Key.X.WithShift);

        Assert.Equal(BranchesMode.Browse, app.BranchesTab.Mode);
        var frame = app.Art.Scene!(ArtGeometry.For(60, 9)!);
        Assert.Equal("Refused: widen the window to answer this question", frame.Caption!.Text);
        var cells = Enumerable.Range(0, frame.Grid.Height).SelectMany(y => Enumerable.Range(0, frame.Grid.Width).Select(x => frame.Grid[x, y].Ch));
        Assert.Contains('●', cells);
    }

    [Fact]
    public void The_conflicted_file_read_for_another_stop_is_dropped()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);

        app.BranchesTab.ApplyConflictRead([new ConflictFile("app.txt", true)], new string('0', 40));

        Assert.Equal(new[] { "✖ app.txt  both modified" }, app.Art.BelowFor!(40, 3).Select(line => string.Concat(line.Select(s => s.Text))));
    }

    [Fact]
    public async Task A_continue_asked_while_graftui_is_quitting_starts_nothing_and_the_pause_stays()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);
        repo.Write("app.txt", "line 1 (resolved)\n");
        app.MarkQuitting();

        app.BranchesTab.StartResume(ResumeAction.Continue);

        Assert.Null(await app.ChangesTask!);
        Assert.False(app.BranchAnimator.Running);
        Assert.True(SequenceGit.RebaseInProgress(GitDir(repo)));
        // Nothing was staged: the file is still unmerged in the index.
        Assert.NotEqual("", repo.Run("ls-files", "-u"));
    }

    [Fact]
    public void A_refusal_note_goes_with_the_next_key_and_the_pause_screen_is_drawn_again()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.M);
        Assert.Equal("Refused: a rebase is in progress: finish or abort it first", Caption(app));

        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorDown);

        Assert.Equal(RebaseText.PauseCaption(app.Snapshot.Paused!), Caption(app));
    }

    [Fact]
    public void A_failed_step_that_leaves_the_pause_puts_its_failure_on_the_pause_screen()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);
        var failed = new BranchResult(false, "Failed", new Caption("✕ Rebase failed: boom", new Style(Ink.Bad, true)), null);

        app.BranchesTab.ShowPauseAfter(failed);

        Assert.Equal(RebaseText.PausedTitle, app.Art.Title);
        Assert.Equal("✕ Rebase failed: boom", Caption(app));
    }

    [Fact]
    public void A_step_that_pauses_again_shows_the_stop_caption_and_no_note()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);
        var stop = app.Snapshot.Paused!;
        var outcome = new SequenceOutcome(SequenceOutcomeKind.Paused, SequenceKind.Rebase, "feature", "main", "", null, stop,
            [], [], [], [], [], [], "paused", []);
        var again = new BranchResult(true, RebaseText.PausedTitle, SequenceStatus.Caption(stop), null, Sequence: outcome);

        app.BranchesTab.ShowPauseAfter(again);

        Assert.Equal(RebaseText.PauseCaption(stop), Caption(app));
    }

    [Fact]
    public void A_conflicted_file_that_cannot_be_read_gives_no_read_so_the_poll_carries_on()
    {
        // Needs file permissions that Windows does not have, and a root user reads any file.
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            return;

        using var repo = TempRepo.Create();
        var path = Path.Combine(repo.Dir, "locked.txt");
        File.WriteAllText(path, "x\n");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            Assert.Null(BranchesTab.ReadConflictFiles(repo.Dir, ["locked.txt"]));
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task Continue_after_the_file_is_resolved_finishes_the_rebase_on_a_worker()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);
        repo.Write("app.txt", "line 1 (resolved)\n");

        _ui.Keyboard.RaiseKeyDownEvent(Key.Enter);
        Assert.True(app.Busy);
        await app.ChangesTask!;

        Assert.False(SequenceGit.RebaseInProgress(GitDir(repo)));
        Assert.Equal("WIP debug logging", repo.Run("log", "-1", "--format=%s"));
        Assert.Equal(0, app.ChangeExitCode);
        Assert.StartsWith("graftui: ", await app.ChangesTask, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enter_with_the_conflict_markers_still_there_is_refused_by_the_engine_and_the_pause_stays()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Enter);
        await app.ChangesTask!;

        Assert.True(SequenceGit.RebaseInProgress(GitDir(repo)));
        Assert.Null(await app.ChangesTask);
    }

    [Fact]
    public async Task Abort_asked_and_answered_yes_restores_the_branch_and_the_repo_is_clean()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        var app = OpenPaused(repo);

        _ui.Keyboard.RaiseKeyDownEvent(Key.X.WithShift);
        _ui.Keyboard.RaiseKeyDownEvent(Key.Y);
        await app.ChangesTask!;

        Assert.Equal(before, repo.Head);
        Assert.True(repo.IsClean);
        Assert.False(SequenceGit.RebaseInProgress(GitDir(repo)));
        Assert.False(File.Exists(Path.Combine(GitDir(repo), SequenceStateFile.FileName)));
    }

    [Fact]
    public void A_conflicted_file_resolved_on_disk_is_drawn_as_resolved_after_the_next_read()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);

        app.BranchesTab.ApplyConflictRead([new ConflictFile("app.txt", true)], app.Snapshot.Paused!.Commit);

        Assert.Equal(new[] { "✔ app.txt  resolved" }, app.Art.BelowFor!(40, 3).Select(line => string.Concat(line.Select(s => s.Text))));
    }

    [Fact]
    public async Task An_edit_stop_continues_with_enter_and_finishes_the_rebase()
    {
        using var repo = SequenceRepos.Feature4();
        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));
        var plan = check.Rows.Select((r, i) => i == 2 ? r with { Action = RebaseAction.Edit } : r).ToList();
        var app = OpenPaused(repo, plan);
        Assert.Equal(PauseKind.Edit, app.Snapshot.Paused!.Pause);
        Assert.Equal(PauseKind.Edit, app.Keys.BranchesState.Pause);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Enter);
        await app.ChangesTask!;

        Assert.False(SequenceGit.RebaseInProgress(GitDir(repo)));
        Assert.Equal(0, app.ChangeExitCode);
    }

    [Fact]
    public async Task S_skips_the_conflicted_commit_and_finishes_the_rebase()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);

        _ui.Keyboard.RaiseKeyDownEvent(Key.S.WithShift);
        await app.ChangesTask!;

        Assert.False(SequenceGit.RebaseInProgress(GitDir(repo)));
        Assert.Equal(0, app.ChangeExitCode);
    }

    [Fact]
    public async Task A_reword_pause_continues_with_its_own_message_and_finishes()
    {
        using var repo = SequenceRepos.Feature4();
        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));
        var plan = check.Rows.Select((r, i) => i == 0 ? r with { Action = RebaseAction.Reword } : r).ToList();
        var app = OpenPaused(repo, plan);
        Assert.Equal(PauseKind.Reword, app.Snapshot.Paused!.Pause);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Enter);
        await app.ChangesTask!;

        Assert.False(SequenceGit.RebaseInProgress(GitDir(repo)));
        // Four commits replayed; the reworded one is the oldest, so it is the fourth line of the log.
        Assert.Equal("Add login form", repo.Run("log", "-4", "--format=%s").Split('\n')[3]);
    }

    [Fact]
    public async Task An_abort_whose_state_file_is_gone_falls_back_to_git_abort_and_restores_the_branch()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        var app = OpenPaused(repo);
        // The snapshot still knows the pause and its old tip; the state file has gone since, so the resume operation refuses the abort.
        File.Delete(Path.Combine(GitDir(repo), SequenceStateFile.FileName));

        _ui.Keyboard.RaiseKeyDownEvent(Key.X.WithShift);
        _ui.Keyboard.RaiseKeyDownEvent(Key.Y);
        await app.ChangesTask!;

        Assert.Equal(before, repo.Head);
        Assert.True(repo.IsClean);
        Assert.False(SequenceGit.RebaseInProgress(GitDir(repo)));
    }

    [Fact]
    public void A_question_whose_pause_has_gone_leaves_the_normal_preview_when_yes_is_pressed()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.X.WithShift);

        // The repo moved on while the question was open: the snapshot no longer has the pause.
        var unpaused = new RepoSnapshot(app.Snapshot.CurrentBranch, app.Snapshot.HeadHash, true, app.Snapshot.Branches, app.Snapshot.Counts);
        app.ApplySnapshot(unpaused);
        app.BranchesTab.StartResume(ResumeAction.Abort);

        Assert.Null(app.ChangesTask);
        Assert.False(app.IsPaused);
        Assert.False(app.Busy);
    }

    /// <summary>A branch change's result that is not a pause of its own: the pause screen would take its caption as the note.</summary>
    private static BranchResult FailedResult() => new(false, "Failed", new Caption("✕ Rebase failed: boom", new Style(Ink.Bad, true)), null);

    [Fact]
    public void A_pause_drawn_after_a_counts_read_waits_when_the_user_has_moved_to_the_changes_tab_first()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);
        var seq = app.SnapshotSeq;
        _ui.Keyboard.RaiseKeyDownEvent(Key.Tab);
        Assert.Equal(AppController.TabKind.Changes, app.ActiveTab);
        app.Art.Title = "Changes";

        app.CountsRead(null, new InvalidOperationException("counts"), seq, () => app.BranchesTab.ShowPauseAfter(FailedResult()));

        Assert.Equal("Changes", app.Art.Title);
    }

    [Fact]
    public void A_pause_drawn_after_a_counts_read_waits_while_a_question_is_open()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);
        var seq = app.SnapshotSeq;
        _ui.Keyboard.RaiseKeyDownEvent(Key.X.WithShift);
        Assert.Equal(BranchesMode.Ask, app.BranchesTab.Mode);
        app.Art.Title = "question";

        app.CountsRead(null, new InvalidOperationException("counts"), seq, () => app.BranchesTab.ShowPauseAfter(FailedResult()));

        Assert.Equal("question", app.Art.Title);
    }

    [Fact]
    public void A_pause_drawn_after_a_counts_read_waits_while_a_change_is_running()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);
        var seq = app.SnapshotSeq;
        app.Busy = true;
        app.Art.Title = "running";

        app.CountsRead(null, new InvalidOperationException("counts"), seq, () => app.BranchesTab.ShowPauseAfter(FailedResult()));

        Assert.Equal("running", app.Art.Title);
    }

    [Fact]
    public void A_counts_read_that_fails_shows_its_error_and_still_runs_the_pause_that_is_due()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var app = OpenPaused(repo);
        var seq = app.SnapshotSeq;
        app.Art.Title = "stale";

        app.CountsRead(null, new InvalidOperationException("counts failed"), seq, () => app.BranchesTab.ShowPauseAfter(FailedResult()));

        Assert.Equal("counts failed", app.Status.ErrorText);
        Assert.Equal(RebaseText.PausedTitle, app.Art.Title);
        Assert.True(app.IsPaused);
    }
}
