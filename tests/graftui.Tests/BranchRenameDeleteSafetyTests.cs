using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>RenameBranchOperation and DeleteBranchOperation against throwaway repos.</summary>
public class BranchRenameDeleteSafetyTests
{
    private static BranchChangeOutcome Rename(TempRepo repo, string old, string nu, out RenameBranchCheck check)
    {
        var op = new RenameBranchOperation(repo.Git);
        check = op.Check(new RenameBranchRequest(old, nu));
        return op.Run(new RenameBranchRequest(old, nu), check);
    }

    private static DeleteBranchCheck DeleteCheck(TempRepo repo, string name, string? expectedTip = null, bool force = false) =>
        new DeleteBranchOperation(repo.Git).Check(new DeleteBranchRequest(name, expectedTip, force));

    private static BranchChangeOutcome Delete(TempRepo repo, string name, out DeleteBranchCheck check, string? expectedTip = null,
        bool force = false)
    {
        var op = new DeleteBranchOperation(repo.Git);
        var request = new DeleteBranchRequest(name, expectedTip, force);
        check = op.Check(request);
        return op.Run(request, check);
    }

    private static string RefOf(TempRepo repo, string branch) =>
        repo.Git.Run("rev-parse", "-q", "--verify", "refs/heads/" + branch).StdOut.Trim();

    // ---- rename ----

    [Fact]
    public void Renaming_a_branch_that_is_not_current_keeps_its_tip_and_records_an_undo_step()
    {
        using var repo = TempRepo.FastForwardable();
        var tip = RefOf(repo, "feature");

        var outcome = Rename(repo, "feature", "topic", out var check);

        Assert.True(check.CanRun);
        Assert.False(check.IsCurrent);
        Assert.Equal(BranchChangeKind.Done, outcome.Kind);
        Assert.Equal(tip, RefOf(repo, "topic"));
        Assert.Equal("", RefOf(repo, "feature"));
        Assert.Equal("refs/heads/main", repo.Run("symbolic-ref", "-q", "HEAD"));
        Assert.NotNull(outcome.Undo);
        Assert.Equal(UndoKind.RenameBranch, outcome.Undo.Kind);
        Assert.Equal("topic", outcome.Undo.Branch);
        Assert.Equal("feature", outcome.Undo.OtherBranch);
        Assert.Equal(UndoKind.RenameBranch, new UndoLog(repo.Git).Top([])?.Step?.Kind);
    }

    [Fact]
    public void Renaming_the_current_branch_moves_HEAD_with_it()
    {
        using var repo = TempRepo.FastForwardable();

        var outcome = Rename(repo, "main", "trunk", out var check);

        Assert.True(check.IsCurrent);
        Assert.Equal(BranchChangeKind.Done, outcome.Kind);
        Assert.Equal("refs/heads/trunk", repo.Run("symbolic-ref", "-q", "HEAD"));
        Assert.Equal("", RefOf(repo, "main"));
    }

    [Fact]
    public void Renaming_onto_a_taken_name_is_a_name_problem()
    {
        using var repo = TempRepo.FastForwardable();

        var check = new RenameBranchOperation(repo.Git).Check(new RenameBranchRequest("feature", "main"));

        Assert.False(check.CanRun);
        Assert.True(check.NameProblem);
        Assert.Equal(BranchRefusals.Exists("main"), check.Refusal);
    }

    [Fact]
    public void Renaming_to_the_same_name_is_refused_as_a_name_problem_and_changes_nothing()
    {
        using var repo = TempRepo.FastForwardable();

        var check = new RenameBranchOperation(repo.Git).Check(new RenameBranchRequest("feature", "feature"));

        Assert.False(check.CanRun);
        Assert.True(check.NameProblem);
        Assert.Equal(BranchRefusals.SameName, check.Refusal);
    }

    [Fact]
    public void Renaming_a_missing_branch_is_NoBranch()
    {
        using var repo = TempRepo.FastForwardable();

        var check = new RenameBranchOperation(repo.Git).Check(new RenameBranchRequest("ghost", "topic"));

        Assert.Equal(BranchRefusals.NoBranch("ghost"), check.Refusal);
    }

    [Fact]
    public void Renaming_carries_the_branch_config_to_the_new_name()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("config", "branch.feature.remote", "origin");
        repo.Run("config", "branch.feature.merge", "refs/heads/feature");

        Rename(repo, "feature", "topic", out _);

        Assert.Equal("origin", repo.Run("config", "branch.topic.remote"));
        Assert.Equal("refs/heads/feature", repo.Run("config", "branch.topic.merge"));
    }

    // ---- delete ----

    [Fact]
    public void A_merged_branch_is_deleted_with_git_branch_d_and_records_its_tip()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("branch", "merged-x", "main");
        var tip = RefOf(repo, "merged-x");

        var outcome = Delete(repo, "merged-x", out var check);

        Assert.True(check.Merged);
        Assert.Equal(0, check.Ahead);
        Assert.Equal(0, check.Lost);
        Assert.Equal(BranchChangeKind.Done, outcome.Kind);
        Assert.Equal("", RefOf(repo, "merged-x"));
        Assert.Contains(outcome.Commands, r => r.Command == "git branch -d merged-x");
        Assert.NotNull(outcome.Undo);
        Assert.Equal(UndoKind.DeleteBranch, outcome.Undo.Kind);
        Assert.Equal(tip, outcome.Undo.Head);
        Assert.Equal(UndoKind.DeleteBranch, new UndoLog(repo.Git).Top([])?.Step?.Kind);
    }

    [Fact]
    public void An_unmerged_branch_needs_Force_and_is_deleted_with_git_branch_D()
    {
        using var repo = TempRepo.FastForwardable();
        var tip = RefOf(repo, "feature");

        var check = DeleteCheck(repo, "feature");
        Assert.False(check.Merged);
        Assert.Equal(1, check.Ahead);
        Assert.Equal(1, check.Lost);

        // Without Force the branch is kept, even though the check ran.
        var kept = Delete(repo, "feature", out _);
        Assert.Equal(BranchChangeKind.Failed, kept.Kind);
        Assert.Equal(BranchRefusals.NotMergedNow("feature"), kept.Message);
        Assert.Equal(tip, RefOf(repo, "feature"));

        var gone = Delete(repo, "feature", out _, force: true);
        Assert.Equal(BranchChangeKind.Done, gone.Kind);
        Assert.Equal("", RefOf(repo, "feature"));
        Assert.Contains(gone.Commands, r => r.Command == "git branch -D feature");
        Assert.Equal(1, gone.Lost);
        Assert.Equal(tip, gone.Undo?.Changes[0].Before);
    }

    [Fact]
    public void Lost_is_zero_when_another_branch_holds_the_commits()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("branch", "twin", "feature");

        Assert.Equal(0, DeleteCheck(repo, "feature").Lost);
    }

    [Fact]
    public void Lost_counts_correctly_for_a_name_with_a_slash()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        // feature/x has one commit of its own; main and feature/y (at main) do not hold it.
        repo.Run("switch", "-q", "-c", "feature/x");
        repo.Commit("x.txt", "x\n", "Add x");
        repo.Run("switch", "-q", "main");
        repo.Run("branch", "feature/y", "main");

        Assert.Equal(1, DeleteCheck(repo, "feature/x").Lost);

        repo.Run("branch", "twin", "feature/x");
        Assert.Equal(0, DeleteCheck(repo, "feature/x").Lost);
    }

    [Fact]
    public void The_current_branch_is_refused_as_Current()
    {
        using var repo = TempRepo.FastForwardable();

        var check = DeleteCheck(repo, "main");

        Assert.False(check.CanRun);
        Assert.Equal(BranchRefusals.Current("main"), check.Refusal);
    }

    [Fact]
    public void A_stale_expected_tip_is_refused_as_TipMoved()
    {
        using var repo = TempRepo.FastForwardable();

        var check = DeleteCheck(repo, "feature", expectedTip: new string('0', 40));

        Assert.False(check.CanRun);
        Assert.Equal(BranchRefusals.TipMoved("feature"), check.Refusal);
    }
}
