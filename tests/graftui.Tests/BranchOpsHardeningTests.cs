using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>Extra branch-operation tests for the safety-audit fixes (PR 7), on throwaway temp repos.</summary>
public sealed class BranchOpsHardeningTests
{
    [Fact]
    public void A_deleted_branch_with_a_multiline_config_value_comes_back_with_it()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("merge", "-q", "feature");
        repo.Run("config", "branch.feature.remote", "origin");
        repo.Run("config", "branch.feature.merge", "refs/heads/feature");
        repo.Run("config", "branch.feature.note", "line1\nline2");
        var op = new DeleteBranchOperation(repo.Git);

        var check = op.Check(new DeleteBranchRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(BranchChangeKind.Done, op.Run(new DeleteBranchRequest("feature", check.Tip), check).Kind);

        Assert.Equal(UndoOutcomeKind.Undone, Undo(repo.Git).Kind);

        Assert.Equal("origin", repo.Run("config", "branch.feature.remote"));
        Assert.Equal("refs/heads/feature", repo.Run("config", "branch.feature.merge"));
        Assert.Equal("line1\nline2", repo.Run("config", "branch.feature.note"));
    }

    [Fact]
    public void A_branch_deletion_step_saved_without_config_still_undoes()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("merge", "-q", "feature");
        var tip = repo.Run("rev-parse", "feature");
        var log = new List<GitResult>();
        var step = new UndoRecord(UndoKind.DeleteBranch, "deletion of feature",
            [new RefChange("refs/heads/feature", tip, "")], Branch: "feature", Head: tip);
        Assert.Null(new UndoLog(repo.Git).Push(step, log));
        repo.Run("branch", "-D", "feature");

        Assert.Equal(UndoOutcomeKind.Undone, Undo(repo.Git).Kind);

        Assert.Equal(tip, repo.Run("rev-parse", "feature"));
    }

    [Fact]
    public void A_switch_that_git_really_refused_is_still_refused()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var op = new SwitchOperation(repo.Git);
        var check = op.Check(new SwitchRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);
        var head = repo.Head;
        repo.Write("b.txt", "local\n");

        var outcome = op.Run(new SwitchRequest("feature"), check);

        Assert.Equal(SwitchKind.Refused, outcome.Kind);
        Assert.Equal(head, repo.Head);
        Assert.Equal("refs/heads/main", repo.Run("symbolic-ref", "HEAD"));
        Assert.Contains("b.txt", outcome.Blocking);
    }

    [Fact]
    public void A_branch_ahead_of_its_upstream_is_not_deleted_without_force()
    {
        using var repo = MergedButAheadOfUpstream();
        var op = new DeleteBranchOperation(repo.Git);
        var check = op.Check(new DeleteBranchRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);
        var status = repo.Status();
        var head = repo.Head;
        var stash = repo.Run("stash", "list");

        var outcome = op.Run(new DeleteBranchRequest("feature", check.Tip), check);

        Assert.Equal(BranchChangeKind.Failed, outcome.Kind);
        Assert.Equal(BranchRefusals.NotMergedUpstream("feature"), outcome.Message);
        Assert.Equal(status, repo.Status());
        Assert.Equal(head, repo.Head);
        Assert.Equal(stash, repo.Run("stash", "list"));
        Assert.Equal(check.Tip, repo.Run("rev-parse", "feature"));
    }

    [Fact]
    public void A_merged_branch_checked_out_in_a_linked_worktree_is_refused_without_a_force_hint()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("merge", "-q", "feature");
        var worktree = repo.Dir + "-wt";
        repo.Run("worktree", "add", "-q", worktree, "feature");
        var op = new DeleteBranchOperation(repo.Git);
        var check = op.Check(new DeleteBranchRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);

        var outcome = op.Run(new DeleteBranchRequest("feature", check.Tip), check);

        // git refuses this deletion for its own reason (the worktree owns the branch), and Force does not answer it.
        Assert.Equal(BranchChangeKind.Failed, outcome.Kind);
        Assert.StartsWith("git refused to delete feature", outcome.Message);
        Assert.DoesNotContain("Force", outcome.Message);
        Assert.Equal(check.Tip, repo.Run("rev-parse", "feature"));
        repo.Run("worktree", "remove", "--force", worktree);
    }

    [Fact]
    public void A_bare_rebase_setting_survives_delete_and_undo_as_true()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("merge", "-q", "feature");
        // A valueless entry: git lists it as the key alone, and reads it as true.
        File.AppendAllText(Path.Combine(repo.Dir, ".git", "config"), "[branch \"feature\"]\n\trebase\n");
        var op = new DeleteBranchOperation(repo.Git);
        var check = op.Check(new DeleteBranchRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(BranchChangeKind.Done, op.Run(new DeleteBranchRequest("feature", check.Tip), check).Kind);

        Assert.Equal(UndoOutcomeKind.Undone, Undo(repo.Git).Kind);

        Assert.Equal("true", repo.Run("config", "--get", "branch.feature.rebase"));
    }

    [Fact]
    public void Deleting_a_branch_does_not_restore_the_config_of_a_longer_branch_name()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("merge", "-q", "feature");
        repo.Run("config", "branch.feature.remote", "origin");
        // feature.extra is its own branch name: git splits the variable at the LAST dot of the key.
        repo.Run("config", "branch.feature.extra.remote", "other");
        var op = new DeleteBranchOperation(repo.Git);
        var check = op.Check(new DeleteBranchRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(BranchChangeKind.Done, op.Run(new DeleteBranchRequest("feature", check.Tip), check).Kind);

        Assert.Equal(UndoOutcomeKind.Undone, Undo(repo.Git).Kind);

        // The branch's own setting came back; the longer name's was never saved, so the undo did not add a copy.
        Assert.Equal("origin", repo.Run("config", "--get", "branch.feature.remote"));
        Assert.Equal("other", repo.Git.Run("config", "--get-all", "branch.feature.extra.remote").StdOut.Trim());
    }

    [Fact]
    public void A_hook_that_moves_the_base_branch_back_keeps_the_created_branch_and_its_undo_step()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Commit("b.txt", "b\n", "Add b");
        var mainTip = repo.Head;
        var baseCommit = repo.Run("rev-parse", "main~1");
        var op = new CreateBranchOperation(repo.Git);
        var check = op.Check(new CreateBranchRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(BranchChangeKind.Done, op.Run(new CreateBranchRequest("feature"), check).Kind);
        // The undo switches back to main; the hook moves main off feature's commit and exits non-zero.
        repo.WriteHook("post-checkout", "#!/bin/sh\ngit update-ref refs/heads/main refs/heads/main~1\nexit 1\n");

        var outcome = Undo(repo.Git);

        Assert.Equal(UndoOutcomeKind.Failed, outcome.Kind);
        Assert.Contains(BranchRefusals.UndoWouldLose("main", "feature"), outcome.Message);
        Assert.Equal(mainTip, repo.Run("rev-parse", "refs/heads/feature"));
        Assert.Equal("refs/heads/main", repo.Run("symbolic-ref", "HEAD"));
        Assert.Equal(baseCommit, repo.Run("rev-parse", "main"));
        // The step is still on top, and it now refuses: removing feature would lose the commit.
        var again = new UndoOperation(repo.Git).Check(new UndoRequest(null));
        Assert.False(again.CanRun);
        Assert.Contains(BranchRefusals.UndoWouldLose("main", "feature"), again.Refusal);
    }

    [Fact]
    public void A_failed_config_restore_rolls_the_branch_back_so_the_undo_can_be_retried()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("merge", "-q", "feature");
        repo.Run("config", "branch.feature.remote", "origin");
        repo.Run("config", "branch.feature.merge", "refs/heads/feature");
        var op = new DeleteBranchOperation(repo.Git);
        var check = op.Check(new DeleteBranchRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(BranchChangeKind.Done, op.Run(new DeleteBranchRequest("feature", check.Tip), check).Kind);
        // A stale config lock makes every `git config` write fail, so the restore fails after the branch came back.
        var configLock = Path.Combine(repo.Dir, ".git", "config.lock");
        File.WriteAllText(configLock, "");

        var failed = Undo(repo.Git);

        Assert.Equal(UndoOutcomeKind.Failed, failed.Kind);
        Assert.Contains("feature was removed again", failed.Message);
        Assert.Equal("", repo.Git.Run("rev-parse", "-q", "--verify", "refs/heads/feature").StdOut.Trim());

        File.Delete(configLock);
        Assert.Equal(UndoOutcomeKind.Undone, Undo(repo.Git).Kind);
        Assert.Equal(check.Tip, repo.Run("rev-parse", "feature"));
        Assert.Equal("origin", repo.Run("config", "--get", "branch.feature.remote"));
        Assert.Equal("refs/heads/feature", repo.Run("config", "--get", "branch.feature.merge"));
    }

    /// <summary>feature is merged into main (HEAD), but its upstream origin/feature is one commit behind it.</summary>
    private static TempRepo MergedButAheadOfUpstream()
    {
        var repo = TempRepo.FastForwardable();
        repo.Run("merge", "-q", "feature");
        repo.Run("update-ref", "refs/remotes/origin/feature", "HEAD~1");
        repo.Run("config", "remote.origin.url", "/nonexistent");
        repo.Run("config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*");
        repo.Run("config", "branch.feature.remote", "origin");
        repo.Run("config", "branch.feature.merge", "refs/heads/feature");
        return repo;
    }

    private static UndoOutcome Undo(GitClient git)
    {
        var op = new UndoOperation(git);
        var check = op.Check(new UndoRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new UndoRequest(check.StepRef), check);
    }

    [Fact]
    public void Undo_of_a_deletion_does_not_double_settings_that_survived_the_delete()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("merge", "-q", "feature");
        repo.Run("config", "branch.feature.remote", "origin");
        var op = new DeleteBranchOperation(repo.Git);
        var check = op.Check(new DeleteBranchRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(BranchChangeKind.Done, op.Run(new DeleteBranchRequest("feature", check.Tip), check).Kind);
        // As if .git/config had been locked during the delete: the setting is still there.
        repo.Run("config", "branch.feature.remote", "origin");

        Assert.Equal(UndoOutcomeKind.Undone, Undo(repo.Git).Kind);

        Assert.Equal("origin", repo.Run("config", "--get-all", "branch.feature.remote").Trim());
    }
}
