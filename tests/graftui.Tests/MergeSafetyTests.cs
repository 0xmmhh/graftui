using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;

namespace graftui.Tests;

/// <summary>One test per merge safety rule, run against real temp repos.</summary>
public class MergeSafetyTests
{
    [Fact]
    public void A_conflicting_merge_is_aborted_and_leaves_the_repo_clean()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var op = new MergeOperation(repo.Git);
        var head = repo.Head;

        var check = op.Check(new MergeRequest("feature"));
        Assert.True(check.CanMerge, check.Refusal);
        var outcome = op.Run(new MergeRequest("feature"), check);

        Assert.Equal(MergeKind.Conflict, outcome.Kind);
        Assert.Equal(new[] { "app.txt" }, outcome.ConflictFiles);
        Assert.Equal(head, repo.Head);
        Assert.True(repo.IsClean);
        Assert.False(repo.Merging);
        Assert.Null(outcome.Undo);
        var commands = outcome.Commands.Select(c => c.Command).ToList();
        Assert.Contains("git merge --no-edit feature", commands);
        Assert.Contains("git merge --abort", commands);
    }

    [Fact]
    public void A_modified_tracked_file_refuses_the_merge_and_keeps_its_content()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        File.WriteAllText(Path.Combine(repo.Dir, "c.txt"), "edited, not committed\n");

        AssertRefused(repo, "working tree has uncommitted changes");

        Assert.Equal("edited, not committed\n", File.ReadAllText(Path.Combine(repo.Dir, "c.txt")));
    }

    [Fact]
    public void An_untracked_file_refuses_the_merge_and_keeps_its_content()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        File.WriteAllText(Path.Combine(repo.Dir, "new.txt"), "untracked\n");

        AssertRefused(repo, "working tree has uncommitted changes");

        Assert.Equal("untracked\n", File.ReadAllText(Path.Combine(repo.Dir, "new.txt")));
    }

    [Fact]
    public void A_detached_head_refuses_the_merge()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Run("switch", "-q", "--detach");

        AssertRefused(repo, "HEAD is detached");
    }

    [Fact]
    public void An_interrupted_merge_is_rolled_back_to_the_starting_commit()
    {
        // Needs git >= 2.24 (pre-merge-commit hook). git exec's the hook directly, so $PPID is git,
        // and the temp dir must not be mounted noexec or the hook cannot run.
        using var repo = TempRepo.Diverged(conflict: false);
        // git runs this hook just before it commits a clean merge; killing git there leaves the merge staged.
        // SIGTERM rather than SIGINT: background jobs inherit SIGINT as ignored, so git would not die.
        repo.WriteHook("pre-merge-commit", "#!/bin/sh\nkill -TERM $PPID\nsleep 1\n");
        var head = repo.Head;
        var op = new MergeOperation(repo.Git);

        var check = op.Check(new MergeRequest("feature"));
        Assert.True(check.CanMerge, check.Refusal);
        var outcome = op.Run(new MergeRequest("feature"), check);

        Assert.Equal(MergeKind.Failed, outcome.Kind);
        var merge = Assert.Single(outcome.Commands, c => c.Command == "git merge --no-edit feature");
        Assert.True(merge.ExitCode > 128, $"expected a signal exit, got {merge.ExitCode}");
        Assert.Contains(outcome.Commands, c => c.Command == "git reset --merge");
        Assert.Equal(head, repo.Head);
        Assert.True(repo.IsClean);
        Assert.False(repo.Merging);
        Assert.False(File.Exists(Path.Combine(repo.Dir, "b.txt")));
    }

    [Fact]
    public void A_merge_that_starts_after_quit_never_runs()
    {
        using var repo = TempRepo.FastForwardable();
        var op = new MergeOperation(repo.Git);
        var head = repo.Head;
        var reflogBefore = ReflogCount(repo);
        var refused = 0;
        var starting = 0;

        var outcome = BranchesTab.RunGated(op, new MergeRequest("feature"), () => true, _ => refused++, _ => starting++);

        Assert.Null(outcome);
        Assert.Equal(0, refused);
        Assert.Equal(0, starting);
        Assert.Equal(head, repo.Head);
        Assert.Equal(reflogBefore, ReflogCount(repo));
    }

    [Fact]
    public void Without_quit_the_same_merge_runs_and_reports_the_ref_it_moved()
    {
        using var repo = TempRepo.FastForwardable();
        var op = new MergeOperation(repo.Git);
        var head = repo.Head;
        var featureTip = repo.Run("rev-parse", "feature");
        var refused = 0;
        var starting = 0;

        var outcome = BranchesTab.RunGated(op, new MergeRequest("feature"), () => false, _ => refused++, _ => starting++);

        Assert.NotNull(outcome);
        Assert.Equal(MergeKind.FastForward, outcome.Kind);
        Assert.Equal(0, refused);
        Assert.Equal(1, starting);
        Assert.NotNull(outcome.Undo);
        Assert.Equal(new RefChange("refs/heads/main", head, featureTip), Assert.Single(outcome.Undo.Changes));
    }

    private static void AssertRefused(TempRepo repo, string reason)
    {
        var head = repo.Head;
        var op = new MergeOperation(repo.Git);

        var check = op.Check(new MergeRequest("feature"));

        Assert.False(check.CanMerge);
        Assert.Equal(reason, check.Refusal);
        Assert.DoesNotContain(check.Commands, c => c.Command.StartsWith("git merge", StringComparison.Ordinal));
        Assert.Equal(head, repo.Head);
    }

    private static int ReflogCount(TempRepo repo) =>
        repo.Run("reflog", "show", "--format=%H").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
}
