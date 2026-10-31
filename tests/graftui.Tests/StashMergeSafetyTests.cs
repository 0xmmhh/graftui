using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>Stash, merge, pop (m over uncommitted changes) against real temp repos. Never the graftui repo or the sandbox.</summary>
public class StashMergeSafetyTests
{
    [Fact]
    public void A_fast_forward_keeps_the_changes_with_their_index_and_saves_one_undo_step()
    {
        using var repo = FastForward();
        repo.Write("a.txt", "a staged\n");
        repo.Run("add", "a.txt");
        repo.Write("README.md", "readme\nunstaged\n");
        repo.Write("notes.md", "note\n");
        var status = repo.Status();
        var op = new StashMergeOperation(repo.Git);

        var check = op.Check(new StashMergeRequest("feature/ff"));
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(new StashMergeRequest("feature/ff"), check);

        Assert.Equal(StashMergeKind.Merged, outcome.Kind);
        Assert.Equal(MergeKind.FastForward, outcome.Merge!.Kind);
        Assert.Equal("main", repo.Run("symbolic-ref", "--short", "HEAD"));
        Assert.Equal(repo.Run("rev-parse", "feature/ff"), repo.Head);
        Assert.Equal("Add ff file", repo.Run("log", "-1", "--format=%s"));
        Assert.Equal(status, repo.Status());
        Assert.Equal("", repo.Run("stash", "list"));
        Assert.True(File.Exists(Path.Combine(repo.Dir, "ff.txt")));
        AssertNoRescue(repo);

        var undo = Assert.IsType<UndoRecord>(outcome.Undo);
        Assert.Equal(UndoKind.StashMerge, undo.Kind);
        Assert.Equal(new RefChange("refs/heads/main", check.Head, repo.Head), Assert.Single(undo.Changes));

        var commands = outcome.Commands.Select(c => c.Command).ToList();
        Assert.Contains(commands, c => c.StartsWith("git stash push -u -m \"graftui: before merging feature/ff into main\"", StringComparison.Ordinal));
        Assert.Contains("git merge --no-edit feature/ff", commands);
        Assert.Contains("git stash pop --index", commands);
    }

    [Fact]
    public void A_merge_commit_is_reported_as_one_and_keeps_the_changes()
    {
        using var repo = Diverged();
        repo.Write("README.md", "readme\nmine\n");
        var op = new StashMergeOperation(repo.Git);

        var outcome = op.Run(new StashMergeRequest("feature/merge"), op.Check(new StashMergeRequest("feature/merge")));

        Assert.Equal(StashMergeKind.Merged, outcome.Kind);
        Assert.Equal(MergeKind.MergeCommit, outcome.Merge!.Kind);
        Assert.Equal(" M README.md", repo.Status());
        Assert.Equal(2, repo.Run("rev-list", "--parents", "-n", "1", "HEAD").Split(' ').Length - 1);
        Assert.Equal("", repo.Run("stash", "list"));
        AssertNoRescue(repo);
    }

    [Fact]
    public void A_merge_conflict_aborts_the_merge_and_leaves_the_repo_exactly_as_it_was()
    {
        using var repo = Conflict();
        repo.Write("README.md", "readme\nmine\n");
        repo.Write("a.txt", "a staged\n");
        repo.Run("add", "a.txt");
        var head = repo.Head;
        var status = repo.Status();
        var tree = repo.Run("write-tree");
        var op = new StashMergeOperation(repo.Git);

        var check = op.Check(new StashMergeRequest("feature/conflict"));
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(new StashMergeRequest("feature/conflict"), check);

        Assert.Equal(StashMergeKind.MergeConflict, outcome.Kind);
        Assert.Equal(new[] { "app.txt" }, outcome.ConflictFiles);
        Assert.Equal(head, repo.Head);
        Assert.Equal(status, repo.Status());
        Assert.Equal(tree, repo.Run("write-tree"));
        Assert.False(repo.Merging);
        Assert.Equal("", repo.Run("stash", "list"));
        Assert.Null(outcome.Undo);
        AssertNoRescue(repo);
        Assert.DoesNotContain("<<<<<<<", File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));

        var commands = outcome.Commands.Select(c => c.Command).ToList();
        Assert.Contains("git merge --abort", commands);
        Assert.Contains("git stash pop --index", commands);
    }

    [Fact]
    public void A_pop_conflict_keeps_the_merge_rolls_the_pop_back_and_keeps_the_entry()
    {
        using var repo = Touch();
        repo.Write("app.txt", "mine\nline 2\n");
        repo.Write("notes.md", "note\n");
        var op = new StashMergeOperation(repo.Git);

        var outcome = op.Run(new StashMergeRequest("feature/touch"), op.Check(new StashMergeRequest("feature/touch")));

        Assert.Equal(StashMergeKind.PopConflict, outcome.Kind);
        Assert.Equal(new[] { "app.txt" }, outcome.ConflictFiles);
        Assert.Equal(repo.Run("rev-parse", "feature/touch"), repo.Head);
        Assert.True(repo.IsClean);
        Assert.Equal("touched\nline 2\n", File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));
        Assert.False(File.Exists(Path.Combine(repo.Dir, "notes.md")));
        Assert.Equal(StashLabel, repo.Run("stash", "list", "--format=%gs"));
        AssertNoRescue(repo);

        var undo = Assert.IsType<UndoRecord>(outcome.Undo);
        Assert.Equal(UndoKind.Merge, undo.Kind);

        // The entry is still there for Z: a plain pop, which conflicts again on the merged file, and changes nothing.
        var pop = new PopOperation(repo.Git);
        var popCheck = pop.Check(new PopRequest(null));
        Assert.True(popCheck.CanRun, popCheck.Refusal);
        var popped = pop.Run(new PopRequest(null), popCheck);
        Assert.Equal(PopKind.Conflict, popped.Kind);
        Assert.True(repo.IsClean);
        Assert.Equal(StashLabel, repo.Run("stash", "list", "--format=%gs"));
    }

    [Fact]
    public void A_clean_tree_is_refused_because_the_plain_merge_does_it()
    {
        using var repo = FastForward();

        var check = new StashMergeOperation(repo.Git).Check(new StashMergeRequest("feature/ff"));

        Assert.False(check.CanRun);
        Assert.Contains("nothing to stash", check.Refusal);
    }

    [Fact]
    public void A_detached_head_is_refused()
    {
        using var repo = FastForward();
        repo.Run("switch", "-q", "--detach");
        repo.Write("README.md", "edited\n");

        var check = new StashMergeOperation(repo.Git).Check(new StashMergeRequest("feature/ff"));

        Assert.Equal(ChangeRefusals.Detached, check.Refusal);
    }

    [Fact]
    public void A_branch_already_in_head_is_refused_as_up_to_date()
    {
        using var repo = FastForward();
        repo.Run("branch", "same");
        repo.Write("README.md", "edited\n");

        var check = new StashMergeOperation(repo.Git).Check(new StashMergeRequest("same"));

        Assert.Equal(BranchRefusals.UpToDate("same", "main"), check.Refusal);
    }

    [Fact]
    public void A_nested_repository_is_refused_and_nothing_is_stashed()
    {
        using var repo = FastForward();
        repo.Git.Run("init", "-q", "nested");

        var check = new StashMergeOperation(repo.Git).Check(new StashMergeRequest("feature/ff"));

        Assert.Equal(ChangeRefusals.NestedRepoStash("nested/"), check.Refusal);
        Assert.Equal("", repo.Run("stash", "list"));
    }

    [Fact]
    public void A_saved_rescue_copy_refuses_the_next_stash_merge()
    {
        using var repo = FastForward();
        repo.Write("README.md", "edited\n");
        repo.Run("update-ref", StashMergeOperation.RescueRef, repo.Head);

        var check = new StashMergeOperation(repo.Git).Check(new StashMergeRequest("feature/ff"));

        Assert.Equal(
            ChangeRefusals.RescueSaved("your state from before an earlier stash-merge-pop", StashMergeOperation.RescueRef),
            check.Refusal);
    }

    [Fact]
    public void A_missing_branch_is_refused()
    {
        using var repo = FastForward();
        repo.Write("README.md", "edited\n");

        var check = new StashMergeOperation(repo.Git).Check(new StashMergeRequest("nope"));

        Assert.Equal(BranchRefusals.NoBranch("nope"), check.Refusal);
    }

    [Fact]
    public void A_head_that_moved_after_the_check_changes_nothing()
    {
        using var repo = FastForward();
        repo.Write("README.md", "edited\n");
        var op = new StashMergeOperation(repo.Git);
        var check = op.Check(new StashMergeRequest("feature/ff"));
        Assert.True(check.CanRun, check.Refusal);
        repo.Write("other.txt", "moved\n");
        repo.Run("add", "other.txt");
        repo.Run("commit", "-q", "-m", "moved");
        var moved = repo.Head;
        var status = repo.Status();

        var outcome = op.Run(new StashMergeRequest("feature/ff"), check);

        Assert.Equal(StashMergeKind.Failed, outcome.Kind);
        Assert.Equal(moved, repo.Head);
        Assert.Equal(status, repo.Status());
        Assert.Equal("", repo.Run("stash", "list"));
        AssertNoRescue(repo);
    }

    [Fact]
    public void MergeCheck_reports_Dirty_only_for_uncommitted_changes()
    {
        using var repo = FastForward();
        var clean = new MergeOperation(repo.Git).Check(new MergeRequest("feature/ff"));
        Assert.True(clean.CanMerge);
        Assert.False(clean.Dirty);

        repo.Write("README.md", "edited\n");
        var dirty = new MergeOperation(repo.Git).Check(new MergeRequest("feature/ff"));
        Assert.False(dirty.CanMerge);
        Assert.True(dirty.Dirty);
        Assert.Equal("working tree has uncommitted changes", dirty.Refusal);

        repo.Run("switch", "-q", "--detach");
        var detached = new MergeOperation(repo.Git).Check(new MergeRequest("feature/ff"));
        Assert.False(detached.Dirty);
    }

    /// <summary>The subject git gives the entry the temporary stash makes on main (see the stash message in StashMergeOperation).</summary>
    private const string StashLabel = "On main: graftui: before merging feature/touch into main";

    private static void AssertNoRescue(TempRepo repo) =>
        Assert.Equal("", repo.Run("for-each-ref", "refs/graftui/rescue"));

    /// <summary>main has Add app, Add readme and Add a. Every scenario below starts from it.</summary>
    private static TempRepo Base()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\nline 2\n", "Add app");
        repo.Commit("README.md", "readme\n", "Add readme");
        repo.Commit("a.txt", "a\n", "Add a");
        return repo;
    }

    /// <summary>feature/ff is one commit ahead of main, so merging it fast-forwards.</summary>
    private static TempRepo FastForward()
    {
        var repo = Base();
        repo.Run("switch", "-q", "-c", "feature/ff");
        repo.Commit("ff.txt", "ff\n", "Add ff file");
        repo.Run("switch", "-q", "main");
        return repo;
    }

    /// <summary>feature/merge adds b.txt and main adds c.txt: a merge commit.</summary>
    private static TempRepo Diverged()
    {
        var repo = Base();
        repo.Run("switch", "-q", "-c", "feature/merge");
        repo.Commit("b.txt", "b\n", "Add b");
        repo.Run("switch", "-q", "main");
        repo.Commit("c.txt", "c\n", "Add c");
        return repo;
    }

    /// <summary>feature/conflict and main both change line 1 of app.txt.</summary>
    private static TempRepo Conflict()
    {
        var repo = Base();
        repo.Run("switch", "-q", "-c", "feature/conflict");
        repo.Commit("app.txt", "feature\nline 2\n", "Change line 1 on feature");
        repo.Run("switch", "-q", "main");
        repo.Commit("app.txt", "main\nline 2\n", "Change line 1 on main");
        return repo;
    }

    /// <summary>feature/touch changes line 1 of app.txt and main has not moved, so the merge fast-forwards and a pop over app.txt conflicts.</summary>
    private static TempRepo Touch()
    {
        var repo = Base();
        repo.Run("switch", "-q", "-c", "feature/touch");
        repo.Commit("app.txt", "touched\nline 2\n", "Touch line 1");
        repo.Run("switch", "-q", "main");
        return repo;
    }
}
