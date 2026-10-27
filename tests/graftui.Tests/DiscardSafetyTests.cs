using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>Discard (x) against throwaway repos: the change is saved as a stash entry and the index is never changed.</summary>
public class DiscardSafetyTests
{
    private static WorkingStatus Read(TempRepo repo) =>
        new RepoReader(repo.Git).LoadChanges(CancellationToken.None).Status;

    private static FileChange Row(TempRepo repo, ChangeSide side, string path) =>
        Read(repo).Rows.Single(r => r.Side == side && r.Path == path);

    private static DiscardOutcome Discard(TempRepo repo, FileChange row)
    {
        var op = new DiscardOperation(repo.Git);
        var request = new DiscardRequest(row);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static DiscardCheck CheckOnly(TempRepo repo, FileChange row) =>
        new DiscardOperation(repo.Git).Check(new DiscardRequest(row));

    /// <summary>The raw blob or file, with its trailing newline (TempRepo.Run trims).</summary>
    private static string Show(TempRepo repo, string rev) => repo.Git.Run("show", rev).StdOut;

    /// <summary>
    /// A conflicted index with no MERGE_HEAD: a stash pop that git could not apply. The stash is kept and app.txt is unmerged.
    /// </summary>
    private static TempRepo PopConflict()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "stash-side\n");
        repo.Run("stash", "push", "-q");
        repo.Write("app.txt", "head-side\n");
        repo.Run("commit", "-qam", "head moves");
        Assert.False(repo.Git.Run("stash", "pop").Success);
        return repo;
    }

    /// <summary>app.txt with "line A" staged and "line B" added on top, so the status is MM.</summary>
    private static TempRepo PartlyStaged()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Write("app.txt", "line A\n");
        repo.Run("add", "app.txt");
        repo.Write("app.txt", "line A\nline B\n");
        return repo;
    }

    [Fact]
    public void Partly_staged_discard_keeps_the_staged_part_and_saves_only_the_unstaged_hunk()
    {
        using var repo = PartlyStaged();
        Assert.Equal("MM app.txt", repo.Status());
        var treeBefore = repo.Run("write-tree");

        var outcome = Discard(repo, Row(repo, ChangeSide.Unstaged, "app.txt"));

        Assert.Equal(DiscardKind.Discarded, outcome.Kind);
        Assert.Equal("M  app.txt", repo.Status());
        Assert.Equal("line A\n", File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));
        Assert.Equal("line A\n", Show(repo, ":app.txt"));
        Assert.Equal(treeBefore, repo.Run("write-tree"));
        Assert.Equal("stash@{0}: graftui: discarded app.txt", repo.Run("stash", "list"));

        var patch = repo.Run("stash", "show", "-p", "stash@{0}");
        Assert.Contains("+line B", patch);
        Assert.DoesNotContain("+line A", patch);
        Assert.Contains(outcome.Commands, c => c.Command.Contains("ls-files", StringComparison.Ordinal) && c.Success);
    }

    [Fact]
    public void The_discard_stash_applies_back_by_hand_to_the_unstaged_change_only()
    {
        using var repo = PartlyStaged();
        Discard(repo, Row(repo, ChangeSide.Unstaged, "app.txt"));

        repo.Run("stash", "apply", "-q", "stash@{0}");

        Assert.Equal("line A\nline B\n", File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));
        Assert.Equal("MM app.txt", repo.Status());
        Assert.Equal("line A\n", Show(repo, ":app.txt"));
    }

    [Fact]
    public void Partly_staged_discard_records_an_undo_step_that_names_the_stash_and_the_index_entry()
    {
        using var repo = PartlyStaged();
        var indexBefore = repo.Run("ls-files", "-s", "--", "app.txt");

        var outcome = Discard(repo, Row(repo, ChangeSide.Unstaged, "app.txt"));

        Assert.NotNull(outcome.Undo);
        Assert.Equal(UndoKind.Discard, outcome.Undo.Kind);
        var top = new UndoLog(repo.Git).Top([])!;
        Assert.NotNull(top.Step);
        Assert.Equal(UndoKind.Discard, top.Step.Kind);
        Assert.Equal("app.txt", top.Step.Path);
        Assert.Equal(repo.Run("rev-parse", "stash@{0}"), top.Step.StashCommit);
        Assert.Equal(top.Step.StashCommit, top.Step.Backup);
        Assert.False(top.Step.Untracked);
        Assert.Equal("", top.Step.StashBelow);
        Assert.Equal(repo.Head, top.Step.Head);
        var entry = Assert.Single(top.Step.IndexBefore!);
        Assert.Equal(indexBefore.Split(' ')[0], entry.Mode);
        Assert.Equal(indexBefore.Split(' ')[1], entry.Blob);
    }

    [Fact]
    public void Untracked_discard_removes_the_file_and_the_stash_holds_its_content_in_the_third_parent()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Write("n.txt", "content\n");

        var outcome = Discard(repo, Row(repo, ChangeSide.Unstaged, "n.txt"));

        Assert.Equal(DiscardKind.Discarded, outcome.Kind);
        Assert.False(File.Exists(Path.Combine(repo.Dir, "n.txt")));
        Assert.True(repo.IsClean);
        Assert.Equal("content\n", Show(repo, "stash@{0}^3:n.txt"));
        Assert.Equal("stash@{0}: graftui: discarded n.txt", repo.Run("stash", "list"));
        Assert.True(outcome.Undo!.Untracked);
        Assert.Equal(repo.Run("rev-parse", "stash@{0}^3"), outcome.Undo.Backup);
    }

    [Fact]
    public void A_tracked_file_deleted_in_the_work_tree_comes_back_from_the_index()
    {
        using var repo = TempRepo.Create();
        repo.Commit("gone.txt", "bye\n", "Add gone");
        File.Delete(Path.Combine(repo.Dir, "gone.txt"));
        Assert.Equal(" D gone.txt", repo.Status());

        var outcome = Discard(repo, Row(repo, ChangeSide.Unstaged, "gone.txt"));

        Assert.Equal(DiscardKind.Discarded, outcome.Kind);
        Assert.Equal("bye\n", File.ReadAllText(Path.Combine(repo.Dir, "gone.txt")));
        Assert.True(repo.IsClean);
        Assert.Single(repo.Run("stash", "list").Split('\n'));
    }

    [Fact]
    public void Discard_on_a_detached_head_is_allowed()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Run("checkout", "-q", "--detach");
        repo.Write("n.txt", "content\n");

        var outcome = Discard(repo, Row(repo, ChangeSide.Unstaged, "n.txt"));

        Assert.Equal(DiscardKind.Discarded, outcome.Kind);
        Assert.False(File.Exists(Path.Combine(repo.Dir, "n.txt")));
    }

    [Fact]
    public void A_staged_only_row_is_refused_as_having_no_unstaged_changes()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Write("app.txt", "staged\n");
        repo.Run("add", "app.txt");

        var check = CheckOnly(repo, new FileChange("app.txt", null, ChangeSide.Staged, ChangeKind.Modified));

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.NoUnstaged("app.txt"), check.Refusal);
        Assert.Equal("M  app.txt", repo.Status());
    }

    [Fact]
    public void A_conflicted_file_is_refused()
    {
        using var repo = PopConflict();
        var row = Row(repo, ChangeSide.Unstaged, "app.txt");
        Assert.Equal(ChangeKind.Conflicted, row.Kind);

        var check = CheckOnly(repo, row);

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.ConflictFile("app.txt"), check.Refusal);
        Assert.Single(repo.Run("stash", "list").Split('\n'));
    }

    [Fact]
    public void A_discard_during_a_merge_is_refused()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        Assert.False(repo.Git.Run("merge", "--no-edit", "feature").Success);
        try
        {
            var check = CheckOnly(repo, Row(repo, ChangeSide.Unstaged, "app.txt"));

            Assert.False(check.CanRun);
            Assert.Equal(ChangeRefusals.InProgress("merge"), check.Refusal);
            Assert.True(repo.Merging);
        }
        finally
        {
            repo.Git.Run("merge", "--abort");
        }
    }

    [Fact]
    public void An_intent_to_add_file_is_refused()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Write("new.txt", "hi\n");
        repo.Run("add", "-N", "new.txt");
        var row = Row(repo, ChangeSide.Unstaged, "new.txt");
        Assert.Equal(ChangeKind.Added, row.Kind);

        var check = CheckOnly(repo, row);

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.IntentToAdd("new.txt"), check.Refusal);
        Assert.Equal(" A new.txt", repo.Status());
    }

    [Fact]
    public void An_unborn_branch_is_refused_with_no_commit_yet()
    {
        using var repo = TempRepo.Create();
        repo.Write("new.txt", "hi\n");

        var check = CheckOnly(repo, new FileChange("new.txt", null, ChangeSide.Unstaged, ChangeKind.Untracked));

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.NoCommitYet, check.Refusal);
        Assert.True(File.Exists(Path.Combine(repo.Dir, "new.txt")));
    }

    [Fact]
    public void A_dirty_submodule_is_refused()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        var sub = TempRepo.Create();
        sub.Commit("s.txt", "s\n", "Add s");
        repo.Git.Run("-c", "protocol.file.allow=always", "submodule", "add", sub.Dir, "sub");
        repo.Run("commit", "-q", "-m", "Add sub");
        // A new commit inside the submodule makes its row a modified submodule.
        new GitClient(Path.Combine(repo.Dir, "sub")).Run("-c", "user.name=t", "-c", "user.email=t@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-q", "--allow-empty", "-m", "moved");
        var row = Row(repo, ChangeSide.Unstaged, "sub");
        Assert.True(row.IsSubmodule);

        var check = CheckOnly(repo, row);

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.Submodule("sub"), check.Refusal);
        Assert.Contains(" M sub", repo.Status());
        sub.Dispose();
    }

    [Fact]
    public void A_nested_repository_is_refused()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Git.Run("init", "-q", "nested");

        var check = CheckOnly(repo, Row(repo, ChangeSide.Unstaged, "nested/"));

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.NestedRepoDiscard("nested/"), check.Refusal);
        Assert.True(Directory.Exists(Path.Combine(repo.Dir, "nested", ".git")));
    }

    [Fact]
    public void A_row_reverted_between_check_and_run_is_refused_and_leaves_no_stash_entry()
    {
        using var repo = PartlyStaged();
        var op = new DiscardOperation(repo.Git);
        var request = new DiscardRequest(Row(repo, ChangeSide.Unstaged, "app.txt"));
        var check = op.Check(request);
        Assert.True(check.CanRun);
        repo.Write("app.txt", "line A\n");
        Assert.Equal("M  app.txt", repo.Status());

        var outcome = op.Run(request, check);

        Assert.Equal(DiscardKind.Failed, outcome.Kind);
        Assert.Equal(ChangeRefusals.NoUnstaged("app.txt"), outcome.Message);
        Assert.Equal("", repo.Run("stash", "list"));
        Assert.Null(outcome.Undo);
        Assert.Equal("line A\n", File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));
    }
}
