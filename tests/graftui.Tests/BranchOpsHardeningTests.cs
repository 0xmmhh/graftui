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
}
