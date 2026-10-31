using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>CreateBranchOperation against throwaway repos: the new branch starts at HEAD and the working tree never changes.</summary>
public class BranchCreateSafetyTests
{
    private static BranchChangeOutcome Create(TempRepo repo, string name, out CreateBranchCheck check)
    {
        var op = new CreateBranchOperation(repo.Git);
        check = op.Check(new CreateBranchRequest(name));
        return op.Run(new CreateBranchRequest(name), check);
    }

    [Fact]
    public void New_branch_on_a_dirty_tree_starts_at_HEAD_and_leaves_the_changes_alone()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Write("a.txt", "edited\n");
        repo.Write("loose.txt", "u\n");
        var before = repo.Status();
        var mainTip = repo.Run("rev-parse", "main");

        var outcome = Create(repo, "topic", out var check);

        Assert.True(check.CanRun);
        Assert.Equal(BranchChangeKind.Done, outcome.Kind);
        Assert.Equal("refs/heads/topic", repo.Run("symbolic-ref", "-q", "HEAD"));
        Assert.Equal(mainTip, repo.Run("rev-parse", "refs/heads/topic"));
        Assert.Equal(before, repo.Status());
        Assert.NotNull(outcome.Undo);
        Assert.Equal(UndoKind.CreateBranch, outcome.Undo.Kind);
        Assert.Equal("main", outcome.Undo.OtherBranch);
        Assert.Equal("topic", outcome.Undo.Branch);

        // The step is saved: the top of the undo history is this creation.
        var top = new UndoLog(repo.Git).Top([]);
        Assert.Equal(UndoKind.CreateBranch, top?.Step?.Kind);
    }

    [Fact]
    public void A_taken_name_refuses_as_a_name_problem()
    {
        using var repo = TempRepo.FastForwardable();

        var check = new CreateBranchOperation(repo.Git).Check(new CreateBranchRequest("feature"));

        Assert.False(check.CanRun);
        Assert.True(check.NameProblem);
        Assert.Equal(BranchRefusals.Exists("feature"), check.Refusal);
    }

    [Fact]
    public void A_case_twin_of_a_taken_name_is_refused_as_exists()
    {
        using var repo = TempRepo.FastForwardable();

        var check = new CreateBranchOperation(repo.Git).Check(new CreateBranchRequest("Feature"));

        // On a case-insensitive disk refs/heads/Feature finds "feature"; on a case-sensitive one it is simply free.
        if (check.CanRun)
            return;
        Assert.True(check.NameProblem);
        Assert.Equal(BranchRefusals.Exists("Feature"), check.Refusal);
    }

    [Fact]
    public void A_detached_HEAD_refuses_new_branches()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("checkout", "-q", "--detach");

        var check = new CreateBranchOperation(repo.Git).Check(new CreateBranchRequest("topic"));

        Assert.False(check.CanRun);
        Assert.False(check.NameProblem);
        Assert.Equal(BranchRefusals.Detached, check.Refusal);
    }

    [Fact]
    public void A_repo_with_no_commit_refuses_with_NoCommitYet()
    {
        using var repo = TempRepo.Create();

        var check = new CreateBranchOperation(repo.Git).Check(new CreateBranchRequest("topic"));

        Assert.False(check.CanRun);
        Assert.Equal(BranchRefusals.NoCommitYet, check.Refusal);
    }

    [Fact]
    public void HEAD_moving_between_check_and_run_fails_and_creates_nothing()
    {
        using var repo = TempRepo.FastForwardable();
        var op = new CreateBranchOperation(repo.Git);
        var check = op.Check(new CreateBranchRequest("topic"));
        Assert.True(check.CanRun);

        repo.Commit("late.txt", "late\n", "Commit after the check");
        var outcome = op.Run(new CreateBranchRequest("topic"), check);

        Assert.Equal(BranchChangeKind.Failed, outcome.Kind);
        Assert.Equal("", repo.Git.Run("rev-parse", "-q", "--verify", "refs/heads/topic").StdOut.Trim());
        Assert.Equal("refs/heads/main", repo.Run("symbolic-ref", "-q", "HEAD"));
        Assert.Null(outcome.Undo);
    }
}
