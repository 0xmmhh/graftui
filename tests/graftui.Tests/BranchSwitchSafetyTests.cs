using System.IO;
using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>SwitchOperation against throwaway repos: clean switches, changes that come along, and the refusals that protect them.</summary>
public class BranchSwitchSafetyTests
{
    private static SwitchOutcome Switch(TempRepo repo, string branch)
    {
        var op = new SwitchOperation(repo.Git);
        var check = op.Check(new SwitchRequest(branch));
        return check.CanRun ? op.Run(new SwitchRequest(branch), check)
            : new SwitchOutcome(SwitchKind.Refused, check.From, check.Target, 0, [], check.Refusal!, []);
    }

    private static SwitchCheck CheckOnly(TempRepo repo, string branch) => new SwitchOperation(repo.Git).Check(new SwitchRequest(branch));

    [Fact]
    public void A_clean_switch_moves_HEAD_and_names_the_no_guess_command()
    {
        using var repo = TempRepo.FastForwardable();

        var outcome = Switch(repo, "feature");

        Assert.Equal(SwitchKind.Switched, outcome.Kind);
        Assert.Equal("feature", repo.Run("symbolic-ref", "--short", "HEAD"));
        Assert.True(repo.IsClean);
        Assert.Contains(outcome.Commands, r => r.Command == "git switch --no-guess feature");
    }

    [Fact]
    public void Staged_unstaged_and_untracked_changes_that_do_not_collide_come_along()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Write("app.txt", "edited\n");
        repo.Write("staged.txt", "s\n");
        repo.Run("add", "staged.txt");
        repo.Write("untracked.txt", "u\n");
        var before = repo.Status();

        var outcome = Switch(repo, "feature");

        Assert.Equal(SwitchKind.Switched, outcome.Kind);
        Assert.Equal(3, outcome.CameAlong);
        Assert.Equal("feature", repo.Run("symbolic-ref", "--short", "HEAD"));
        Assert.Equal(before, repo.Status());
    }

    [Fact]
    public void A_modified_file_that_differs_between_the_branches_is_refused_in_Check_before_any_switch()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Write("app.txt", "local\n");
        var head = repo.Head;
        var before = repo.Status();

        var check = CheckOnly(repo, "feature");

        Assert.False(check.CanRun);
        Assert.Equal(BranchRefusals.WouldOverwrite(["app.txt"]), check.Refusal);
        Assert.DoesNotContain(check.Commands, c => c.Args.Count > 0 && c.Args[0] == "switch");
        Assert.Equal("main", repo.Run("symbolic-ref", "--short", "HEAD"));
        Assert.Equal(head, repo.Head);
        Assert.Equal(before, repo.Status());
    }

    [Fact]
    public void An_untracked_file_the_target_tracks_is_refused_in_Check_and_named()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        // feature adds b.txt; on main it is an untracked file in the way.
        repo.Write("b.txt", "mine\n");
        var before = repo.Status();

        var check = CheckOnly(repo, "feature");

        Assert.False(check.CanRun);
        Assert.Equal(BranchRefusals.WouldOverwrite(["b.txt"]), check.Refusal);
        Assert.Equal("main", repo.Run("symbolic-ref", "--short", "HEAD"));
        Assert.Equal(before, repo.Status());
    }

    [Fact]
    public void A_change_that_appears_after_Check_is_named_by_Run_when_git_refuses()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var op = new SwitchOperation(repo.Git);
        var check = op.Check(new SwitchRequest("feature"));
        Assert.True(check.CanRun);

        repo.Write("app.txt", "local\n");
        var before = repo.Status();
        var outcome = op.Run(new SwitchRequest("feature"), check);

        Assert.Equal(SwitchKind.Refused, outcome.Kind);
        Assert.Equal(["app.txt"], outcome.Blocking);
        Assert.Equal(BranchRefusals.WouldOverwrite(["app.txt"]), outcome.Message);
        Assert.Equal("main", repo.Run("symbolic-ref", "--short", "HEAD"));
        Assert.Equal(before, repo.Status());
    }

    [Fact]
    public void Switching_to_the_current_branch_is_AlreadyOn()
    {
        using var repo = TempRepo.FastForwardable();

        var check = CheckOnly(repo, "main");

        Assert.Equal(BranchRefusals.AlreadyOn("main"), check.Refusal);
    }

    [Fact]
    public void A_missing_branch_is_NoBranch()
    {
        using var repo = TempRepo.FastForwardable();

        Assert.Equal(BranchRefusals.NoBranch("ghost"), CheckOnly(repo, "ghost").Refusal);
    }

    [Fact]
    public void A_merge_in_progress_is_refused_before_anything_runs()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Git.Run("merge", "--no-edit", "feature");
        Assert.True(repo.Merging);

        var check = CheckOnly(repo, "feature");

        Assert.Equal(ChangeRefusals.InProgress("merge"), check.Refusal);
    }

    [Fact]
    public void An_unmerged_index_without_a_merge_is_refused_as_conflicts()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Git.Run("merge", "--no-edit", "feature");
        // Keep the unmerged entries, drop the merge state: the index still has conflicts.
        File.Delete(Path.Combine(repo.Dir, ".git", "MERGE_HEAD"));
        Assert.Contains("UU app.txt", repo.Status(), StringComparison.Ordinal);

        Assert.Equal(ChangeRefusals.Conflicts, CheckOnly(repo, "feature").Refusal);
    }

    [Fact]
    public void A_detached_HEAD_with_commits_on_no_branch_is_refused_as_DetachedOrphans()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("checkout", "-q", "--detach");
        repo.Commit("orphan.txt", "o\n", "Commit on no branch");
        var head = repo.Head;

        var check = CheckOnly(repo, "feature");

        Assert.Equal(BranchRefusals.DetachedOrphans(1), check.Refusal);
        Assert.Equal(head, repo.Head);
    }

    [Fact]
    public void A_detached_HEAD_whose_commits_are_on_a_branch_can_switch()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("checkout", "-q", "--detach", "main");

        var outcome = Switch(repo, "feature");

        Assert.Equal(SwitchKind.Switched, outcome.Kind);
        Assert.Equal("feature", repo.Run("symbolic-ref", "--short", "HEAD"));
    }
}
