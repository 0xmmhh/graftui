using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>Extra merge-hardening tests for the safety-audit fixes (PR 4), on throwaway temp repos.</summary>
public sealed class MergeHardeningTests
{
    [Fact]
    public void Branch_mergeoptions_do_not_leave_the_merge_uncommitted()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Run("config", "branch.main.mergeoptions", "--no-commit");
        var head = repo.Head;
        var op = new MergeOperation(repo.Git);

        var outcome = op.Run(new MergeRequest("feature"), op.Check(new MergeRequest("feature")));

        Assert.Equal(MergeKind.MergeCommit, outcome.Kind);
        Assert.NotNull(outcome.Undo);
        Assert.NotEqual(head, repo.Head);
        Assert.False(repo.Merging);
        Assert.True(repo.IsClean, repo.Status());
    }

    [Fact]
    public void A_conflict_in_a_file_whose_name_has_a_newline_lists_one_name()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var repo = TempRepo.Create();
        repo.Commit("a\nb.txt", "1\n", "Add odd file");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("a\nb.txt", "feature\n", "Change on feature");
        repo.Run("switch", "-q", "main");
        repo.Commit("a\nb.txt", "main\n", "Change on main");
        var op = new MergeOperation(repo.Git);

        var outcome = op.Run(new MergeRequest("feature"), op.Check(new MergeRequest("feature")));

        Assert.Equal(MergeKind.Conflict, outcome.Kind);
        Assert.Equal(new[] { "a\nb.txt" }, outcome.ConflictFiles);
    }
}
