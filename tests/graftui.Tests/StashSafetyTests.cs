using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>Stash (z) against throwaway repos: every change goes into one stash entry, and the tree is clean afterwards.</summary>
public class StashSafetyTests
{
    private static StashOutcome Stash(TempRepo repo, string? message)
    {
        var op = new StashOperation(repo.Git);
        var request = new StashRequest(message);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static StashCheck CheckOnly(TempRepo repo, string? message = null) =>
        new StashOperation(repo.Git).Check(new StashRequest(message));

    /// <summary>app.txt staged, README.md edited but not staged, new.txt untracked: three changed paths.</summary>
    private static TempRepo MixedTree()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Commit("README.md", "readme\n", "Add readme");
        repo.Write("app.txt", "changed\n");
        repo.Run("add", "app.txt");
        repo.Write("README.md", "readme, edited\n");
        repo.Write("new.txt", "new\n");
        return repo;
    }

    [Fact]
    public void Stash_takes_staged_unstaged_and_untracked_changes_and_leaves_the_tree_clean()
    {
        using var repo = MixedTree();
        var headBefore = repo.Head;

        var outcome = Stash(repo, "x");

        Assert.Equal(StashKind.Stashed, outcome.Kind);
        Assert.True(repo.IsClean);
        Assert.Equal("stash@{0}: On main: x", repo.Run("stash", "list"));
        Assert.Equal(headBefore, repo.Head);
        Assert.Equal(3, outcome.Moved.Count);
        Assert.Equal("On main: x", outcome.Label);
        Assert.Equal("line 1\n", File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));
        Assert.Equal("readme\n", File.ReadAllText(Path.Combine(repo.Dir, "README.md")));
        Assert.False(File.Exists(Path.Combine(repo.Dir, "new.txt")));
    }

    [Fact]
    public void A_blank_message_gives_git_default_message_and_records_an_undo_step()
    {
        using var repo = MixedTree();
        var headShort = repo.Run("log", "-1", "--format=%h %s");

        var outcome = Stash(repo, "   ");

        Assert.Equal(StashKind.Stashed, outcome.Kind);
        Assert.Equal($"stash@{{0}}: WIP on main: {headShort}", repo.Run("stash", "list"));
        Assert.NotNull(outcome.Undo);
        Assert.Equal(UndoKind.Stash, outcome.Undo.Kind);
        var top = new UndoLog(repo.Git).Top([])!;
        Assert.Equal(repo.Run("rev-parse", "stash@{0}"), top.Step!.StashCommit);
        Assert.Equal("", top.Step.StashBelow);
        Assert.Equal(repo.Head, top.Step.Head);
    }

    [Fact]
    public void Stashing_a_second_time_keeps_the_older_entry_below_the_new_one()
    {
        using var repo = MixedTree();
        Stash(repo, "first");
        repo.Write("app.txt", "again\n");

        var outcome = Stash(repo, "second");

        Assert.Equal(StashKind.Stashed, outcome.Kind);
        var list = repo.Run("stash", "list").Split('\n');
        Assert.Equal("stash@{0}: On main: second", list[0]);
        Assert.Equal("stash@{1}: On main: first", list[1]);
        Assert.Equal(repo.Run("rev-parse", "stash@{1}"), new UndoLog(repo.Git).Top([])!.Step!.StashBelow);
    }

    [Fact]
    public void A_clean_tree_is_refused()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");

        var check = CheckOnly(repo);

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.NothingToStash, check.Refusal);
        Assert.Equal("", repo.Run("stash", "list"));
    }

    [Fact]
    public void A_repo_without_commits_is_refused()
    {
        using var repo = TempRepo.Create();
        repo.Write("new.txt", "hi\n");

        var check = CheckOnly(repo);

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.NoCommitYet, check.Refusal);
    }

    [Fact]
    public void A_conflicted_tree_is_refused()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        Assert.False(repo.Git.Run("merge", "--no-edit", "feature").Success);
        try
        {
            var check = CheckOnly(repo);

            Assert.False(check.CanRun);
            Assert.Equal(ChangeRefusals.InProgress("merge"), check.Refusal);
        }
        finally
        {
            repo.Git.Run("merge", "--abort");
        }
    }

    [Fact]
    public void A_nested_repository_is_refused_and_nothing_is_stashed()
    {
        using var repo = MixedTree();
        repo.Git.Run("init", "-q", "nested");
        var before = repo.Status();

        var check = CheckOnly(repo);

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.NestedRepoStash("nested/"), check.Refusal);
        Assert.Equal(before, repo.Status());
        Assert.Equal("", repo.Run("stash", "list"));
    }

    [Fact]
    public void A_dirty_submodule_is_refused_and_nothing_is_stashed()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        var sub = TempRepo.Create();
        sub.Commit("s.txt", "s\n", "Add s");
        repo.Git.Run("-c", "protocol.file.allow=always", "submodule", "add", sub.Dir, "sub");
        repo.Run("commit", "-q", "-m", "Add sub");
        new GitClient(Path.Combine(repo.Dir, "sub")).Run("-c", "user.name=t", "-c", "user.email=t@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-q", "--allow-empty", "-m", "moved");
        var before = repo.Status();

        try
        {
            var check = CheckOnly(repo);

            Assert.False(check.CanRun);
            Assert.Equal(ChangeRefusals.Submodule("sub"), check.Refusal);
            Assert.Equal(before, repo.Status());
            Assert.Equal("", repo.Run("stash", "list"));
        }
        finally
        {
            sub.Dispose();
        }
    }
}
