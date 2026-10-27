using graftui.Git;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>Take, Save, Load and RestoreTo against throwaway repos. Nothing here touches the user's repo or the sandbox.</summary>
public class WorkStateSafetyTests
{
    private static WorkState StateOf(TempRepo repo) => new(repo.Git, Path.Combine(repo.Dir, ".git"));

    /// <summary>app.txt staged then edited (MM), b.txt deleted in the work tree, staged.txt staged, u.txt untracked.</summary>
    private static TempRepo MixedRepo()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Commit("b.txt", "b\n", "Add b");
        repo.Commit("c.txt", "c\n", "Add c");
        repo.Write("app.txt", "staged\n");
        repo.Run("add", "app.txt");
        repo.Write("app.txt", "staged then edited\n");
        File.Delete(Path.Combine(repo.Dir, "b.txt"));
        repo.Write("staged.txt", "new\n");
        repo.Run("add", "staged.txt");
        repo.Write("u.txt", "untracked\n");
        return repo;
    }

    [Fact]
    public void Take_describes_the_work_tree_and_leaves_the_real_index_and_status_alone()
    {
        using var repo = MixedRepo();
        var statusBefore = repo.Status();
        var indexTreeBefore = repo.Run("write-tree");

        var snap = StateOf(repo).Take([]);

        Assert.NotNull(snap);
        Assert.Equal(indexTreeBefore, snap.IndexTree);
        Assert.Equal(statusBefore, repo.Status());
        Assert.Equal(indexTreeBefore, repo.Run("write-tree"));
        var all = repo.Run("ls-tree", "-r", "--name-only", snap.AllTree).Split('\n');
        Assert.Contains("u.txt", all);
        Assert.Contains("staged.txt", all);
        Assert.Contains("app.txt", all);
        Assert.DoesNotContain("b.txt", all);
        Assert.False(File.Exists(Path.Combine(repo.Dir, ".git", WorkState.TempIndex)));
    }

    [Fact]
    public void Take_leaves_ignored_files_out()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        repo.Write(".gitignore", "*.log\n");
        repo.Run("add", ".gitignore");
        repo.Run("commit", "-q", "-m", "Ignore logs");
        repo.Write("build.log", "noise\n");

        var snap = StateOf(repo).Take([]);

        Assert.NotNull(snap);
        var all = repo.Run("ls-tree", "-r", "--name-only", snap.AllTree).Split('\n');
        Assert.DoesNotContain("build.log", all);
    }

    [Fact]
    public void Save_then_Load_gives_the_same_snapshot_with_HEAD_as_its_parent()
    {
        using var repo = MixedRepo();
        var state = StateOf(repo);
        var snap = state.Take([])!;
        var head = repo.Head;
        var log = new List<GitResult>();

        var commit = state.Save(snap, head, log);

        Assert.NotNull(commit);
        Assert.Equal(snap, state.Load(commit, log));
        Assert.Equal(head, repo.Run("rev-parse", commit + "^1"));
        Assert.Equal(head, repo.Run("rev-parse", "HEAD"));
    }

    [Fact]
    public void RestoreTo_puts_back_the_exact_snapshot_after_random_edits()
    {
        using var repo = MixedRepo();
        var state = StateOf(repo);
        var log = new List<GitResult>();
        var snap = state.Take(log)!;
        var statusBefore = repo.Status();
        var appBefore = File.ReadAllText(Path.Combine(repo.Dir, "app.txt"));
        var commit = state.Save(snap, repo.Head, log)!;

        // Random edits: overwrite a tracked file, delete the staged file, restore the deleted one, drop a staged change,
        // and create two untracked files.
        repo.Write("app.txt", "something else entirely\n");
        repo.Write("b.txt", "back, but not the same\n");
        repo.Run("rm", "-q", "--cached", "c.txt");
        File.Delete(Path.Combine(repo.Dir, "staged.txt"));
        repo.Write("x.txt", "x\n");
        repo.Write("y.txt", "y\n");
        Assert.NotEqual(statusBefore, repo.Status());

        var problem = state.RestoreTo(snap, commit, new HashSet<string> { "x.txt", "y.txt" }, log);

        Assert.Null(problem);
        Assert.Equal(snap, state.Take(log));
        Assert.Equal(statusBefore, repo.Status());
        Assert.Equal(appBefore, File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));
        Assert.False(File.Exists(Path.Combine(repo.Dir, "b.txt")));
        Assert.False(File.Exists(Path.Combine(repo.Dir, "x.txt")));
        Assert.False(File.Exists(Path.Combine(repo.Dir, "y.txt")));
        Assert.False(File.Exists(Path.Combine(repo.Dir, ".git", WorkState.PathsFile)));
    }

    [Fact]
    public void RestoreTo_never_removes_an_untracked_file_the_snapshot_has()
    {
        using var repo = MixedRepo();
        var state = StateOf(repo);
        var log = new List<GitResult>();
        var snap = state.Take(log)!;
        var commit = state.Save(snap, repo.Head, log)!;
        File.Delete(Path.Combine(repo.Dir, "u.txt"));

        // u.txt is in the snapshot, so it is not removable even when the caller lists it.
        var problem = state.RestoreTo(snap, commit, new HashSet<string> { "u.txt" }, log);

        Assert.Null(problem);
        Assert.Equal("untracked\n", File.ReadAllText(Path.Combine(repo.Dir, "u.txt")));
    }

    [Fact]
    public void RunWithIndex_logs_the_index_it_used_and_does_not_touch_the_real_index()
    {
        using var repo = MixedRepo();
        var tempIndex = Path.Combine(repo.Dir, ".git", "scratch.idx");
        var realBefore = repo.Run("ls-files", "-s");

        var empty = repo.Git.RunWithIndex(tempIndex, "ls-files", "-s");

        Assert.True(empty.Success);
        Assert.Equal("", empty.StdOut.Trim());
        Assert.StartsWith("GIT_INDEX_FILE=.git/scratch.idx git ls-files", empty.Command, StringComparison.Ordinal);
        Assert.Equal(realBefore, repo.Run("ls-files", "-s"));
    }

    [Fact]
    public void Take_is_null_while_the_index_has_unmerged_entries()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var merge = repo.Git.Run("merge", "--no-edit", "feature");
        Assert.False(merge.Success);
        try
        {
            Assert.Null(StateOf(repo).Take([]));
        }
        finally
        {
            repo.Git.Run("merge", "--abort");
        }
    }
}
