using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The finish of a rebase, decided by BranchesTab's static functions against real throwaway repos: a rebase that ran, one that git refused
/// after its check, a failure, a conflict that stops it on the pause screen, and the fallback abort of a paused rebase.
/// </summary>
public class RebaseFinishTests
{
    private static readonly Style OkB = new(Ink.Ok, true), WarnB = new(Ink.Warn, true), BadB = new(Ink.Bad, true);

    private static string GitDir(TempRepo repo) => Path.Combine(repo.Dir, ".git");

    private static string StateFile(TempRepo repo) => Path.Combine(GitDir(repo), SequenceStateFile.FileName);

    private static RepoReader ReaderOf(TempRepo repo) => new(repo.Git);

    /// <summary>The engine the app gives a rebase's finish, reading the repo the test made.</summary>
    private static BranchesTab.RebaseEngine Engine(TempRepo repo)
    {
        var reader = ReaderOf(repo);
        return new BranchesTab.RebaseEngine(repo.Git,
            () => reader.LoadSnapshot(withCounts: true, CancellationToken.None),
            (snap, b) => reader.LoadLayout(snap, b, CancellationToken.None));
    }

    /// <summary>Checks and runs the rebase of the current branch onto main, which must stop at a conflict.</summary>
    private static (RebaseCheck Check, SequenceOutcome Outcome) Conflict(TempRepo repo)
    {
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(request, check);
        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        return (check, outcome);
    }

    [Fact]
    public void A_rebase_that_ran_selects_the_onto_branch_and_lists_each_commit_with_the_hash_it_had()
    {
        using var repo = SequenceRepos.Feature4();
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        var outcome = op.Run(request, check);
        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);

        var f = BranchesTab.RebaseFinish(outcome, ReaderOf(repo).LoadSnapshot(withCounts: true, CancellationToken.None), check,
            Layouts.Diverged(), Engine(repo));

        Assert.True(f.Result.Success);
        Assert.Equal(RebaseText.DoneTitle, f.Result.Title);
        Assert.Equal("✔ Rebased feature onto main: 4 commits replayed, old ones kept as backup", f.Result.Caption.Text);
        Assert.Equal(OkB, f.Result.Caption.Style);
        Assert.Contains((f.Result.Caption.Text, OkB), f.Log);
        Assert.Equal("main", f.Select);
        Assert.Equal(new CommandHints(Onto: "main"), f.Hints);
        Assert.Equal(0, f.ExitCode);
        Assert.Equal("graftui: " + outcome.Message, f.ExitLine);
        Assert.NotNull(f.After);
        Assert.NotNull(f.Result.After);

        // Each landed commit names the hash it had before the rebase.
        var rows = f.Result.Panel!.Left.Rows;
        Assert.All(check.Rows, row => Assert.Contains(rows, r => r.Subject.Contains("was " + row.ShortHash, StringComparison.Ordinal)));
    }

    [Fact]
    public void A_rebase_whose_undo_step_could_not_be_saved_shows_the_message_as_a_warning_and_exits_0()
    {
        using var repo = SequenceRepos.Feature4();
        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));
        var message = "rebased feature onto main; could not save the undo step: disk full";
        var done = new SequenceOutcome(SequenceOutcomeKind.Done, SequenceKind.Rebase, "feature", "main", check.Head, "newtip", null,
            [], [], [], [], [], [], message, []);

        var f = BranchesTab.RebaseFinish(done, null, check, Layouts.Diverged(), Engine(repo));

        Assert.Contains((message, WarnB), f.Log);
        Assert.Equal("graftui: " + message, f.ExitLine);
        Assert.Equal(0, f.ExitCode);
    }

    [Fact]
    public void A_rebase_git_refuses_after_its_check_is_a_refusal_that_selects_the_onto_branch()
    {
        using var repo = SequenceRepos.Feature4();
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        // The branch moves after the check, so the operation refuses the run.
        repo.Commit("late.txt", "late\n", "Commit after the check");
        var outcome = op.Run(request, check);
        Assert.Equal(SequenceOutcomeKind.Refused, outcome.Kind);

        var f = BranchesTab.RebaseFinish(outcome, null, check, Layouts.Diverged(), Engine(repo));

        Assert.False(f.Result.Success);
        Assert.Equal("Refused", f.Result.Title);
        Assert.Equal("main", f.Select);
        Assert.Equal(new CommandHints(Onto: "main"), f.Hints);
        Assert.Equal(0, f.ExitCode);
        Assert.Equal("graftui: refused: " + outcome.Message, f.ExitLine);
        Assert.All(RefusalText.LogLines(outcome.Message), line => Assert.Contains((line, WarnB), f.Log));
    }

    [Fact]
    public void A_failed_rebase_is_a_failure_that_exits_1_and_keeps_the_selection()
    {
        using var repo = SequenceRepos.Feature4();
        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));
        var failed = new SequenceOutcome(SequenceOutcomeKind.Failed, SequenceKind.Rebase, "feature", "main", check.Head, null, null,
            [], [], [], [], [], [], "boom", []);

        var f = BranchesTab.RebaseFinish(failed, null, check, Layouts.Diverged(), Engine(repo));

        Assert.False(f.Result.Success);
        Assert.Equal("✕ Rebase failed: boom", f.Result.Caption.Text);
        Assert.Equal(BadB, f.Result.Caption.Style);
        Assert.Equal("graftui: rebase failed: boom", f.ExitLine);
        Assert.Null(f.Select);
        Assert.Equal(1, f.ExitCode);
    }

    [Fact]
    public void A_conflicting_rebase_stops_on_the_pause_screen_and_leaves_the_repo_paused_with_exit_1()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var (check, outcome) = Conflict(repo);

        var f = BranchesTab.RebaseFinish(outcome, null, check, Layouts.Diverged(), Engine(repo));

        Assert.True(SequenceGit.RebaseInProgress(GitDir(repo)));
        Assert.True(File.Exists(StateFile(repo)));
        Assert.True(f.Result.Success);
        Assert.Equal(RebaseText.PausedTitle, f.Result.Title);
        Assert.Equal(SequenceStatus.Caption(outcome.Stop!), f.Result.Caption);
        Assert.Same(outcome, f.Result.Sequence);
        Assert.Contains((RebaseText.ConflictLine(outcome.Stop!), BadB), f.Log);
        Assert.Null(f.ExitLine);
        Assert.Null(f.Select);
        Assert.Equal(1, f.ExitCode);
        Assert.Equal(new CommandHints(Onto: "main"), f.Hints);
        Assert.DoesNotContain(f.Commands, c => c.Args.SequenceEqual(["rebase", "--abort"]));
    }

    [Fact]
    public void A_fallback_abort_of_a_paused_rebase_restores_the_branch_when_the_state_file_is_gone()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        var (_, outcome) = Conflict(repo);
        // Without the state file the resume operation refuses the abort, so the fallback runs git's own abort and checks it.
        File.Delete(StateFile(repo));
        var check = new ResumeSequenceOperation(repo.Git).Check(new ResumeRequest(ResumeAction.Abort, null, outcome.Stop!.Commit));
        Assert.False(check.CanRun);

        var f = BranchesTab.AbortPausedFinish(outcome.Stop!, before, check, Engine(repo));

        Assert.Equal(before, repo.Head);
        Assert.True(repo.IsClean);
        Assert.False(SequenceGit.RebaseInProgress(GitDir(repo)));
        Assert.Equal(RebaseText.AbortedTitle, f.Result.Title);
        Assert.Equal("✕ Rebase aborted: feature unchanged", f.Result.Caption.Text);
        Assert.Equal(0, f.ExitCode);
        Assert.Contains(f.Commands, c => c.Args.SequenceEqual(["rebase", "--abort"]));
    }

    [Fact]
    public void When_the_branch_is_not_back_on_its_expected_tip_the_fallback_abort_is_a_failure_that_exits_1()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var (_, outcome) = Conflict(repo);
        File.Delete(StateFile(repo));
        var check = new ResumeSequenceOperation(repo.Git).Check(new ResumeRequest(ResumeAction.Abort, null, outcome.Stop!.Commit));

        var f = BranchesTab.AbortPausedFinish(outcome.Stop!, "0000000000000000000000000000000000000000", check, Engine(repo));

        Assert.False(f.Result.Success);
        Assert.Equal(RebaseText.Failed(RebaseText.NotAborted), f.Result.Caption.Text);
        Assert.Equal(BadB, f.Result.Caption.Style);
        Assert.Null(f.Select);
        Assert.Equal(1, f.ExitCode);
    }
}
