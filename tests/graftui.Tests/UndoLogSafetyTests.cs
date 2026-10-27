using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>The undo history in refs/graftui/undo/, against throwaway repos only.</summary>
public class UndoLogSafetyTests
{
    private const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
    private const string MissingObject = "1111111111111111111111111111111111111111";

    private static UndoRecord CommitStep(string before, string after) =>
        new(UndoKind.Commit, "commit", [new RefChange("refs/heads/main", before, after)], Branch: "main", Head: after);

    private static List<string> UndoRefs(TempRepo repo) =>
        repo.Git.Run("for-each-ref", "--format=%(refname)", UndoLog.Prefix).StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static void AssertSameRecord(UndoRecord expected, UndoRecord? actual) =>
        Assert.Equal(UndoJson.Message(expected), UndoJson.Message(actual!));

    [Fact]
    public void Push_then_top_reads_the_step_back_and_leaves_no_message_file()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        var head = repo.Head;
        var step = CommitStep("", head);
        var log = new List<GitResult>();

        Assert.Null(new UndoLog(repo.Git).Push(step, log));

        var top = new UndoLog(repo.Git).Top(log);
        Assert.NotNull(top);
        Assert.Equal(UndoLog.Prefix + "000001", top.Ref);
        Assert.Equal(repo.Run("rev-parse", top.Ref), top.Commit);
        AssertSameRecord(step, top.Step);
        Assert.Equal(new[] { UndoLog.Prefix + "000001" }, UndoRefs(repo));
        Assert.False(File.Exists(Path.Combine(repo.Dir, ".git", UndoLog.MessageFile)));
        Assert.Contains(log, c => c.Command.StartsWith("git commit-tree --no-gpg-sign", StringComparison.Ordinal) && c.Success);
    }

    [Fact]
    public void A_new_instance_reads_the_same_top()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        var first = CommitStep("", repo.Head);
        var second = CommitStep(repo.Head, repo.Head);
        new UndoLog(repo.Git).Push(first, []);
        new UndoLog(repo.Git).Push(second, []);

        var top = new UndoLog(new GitClient(repo.Dir)).Top([]);

        Assert.NotNull(top);
        Assert.Equal(UndoLog.Prefix + "000002", top.Ref);
        AssertSameRecord(second, top.Step);
    }

    [Fact]
    public void Drop_removes_the_top_step_and_then_the_next_one()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        var log = new List<GitResult>();
        var undo = new UndoLog(repo.Git);
        undo.Push(CommitStep("", repo.Head), log);
        undo.Push(CommitStep(repo.Head, repo.Head), log);

        var top = undo.Top(log)!;
        Assert.True(undo.Drop(top, log));
        Assert.Equal(new[] { UndoLog.Prefix + "000001" }, UndoRefs(repo));
        Assert.Equal(UndoLog.Prefix + "000001", undo.Top(log)!.Ref);

        Assert.True(undo.Drop(undo.Top(log)!, log));
        Assert.Null(undo.Top(log));
        Assert.Empty(UndoRefs(repo));
    }

    [Fact]
    public void Drop_refuses_a_step_that_has_moved_since_it_was_read()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        var undo = new UndoLog(repo.Git);
        undo.Push(CommitStep("", repo.Head), []);
        var top = undo.Top([])!;
        undo.Push(CommitStep(repo.Head, repo.Head), []);
        repo.Run("update-ref", top.Ref, repo.Head);

        Assert.False(new UndoLog(repo.Git).Drop(top with { Commit = MissingObject }, []));
        Assert.Equal(2, UndoRefs(repo).Count);
    }

    [Fact]
    public void The_history_keeps_the_newest_50_steps()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        var undo = new UndoLog(repo.Git);
        var head = repo.Head;

        for (var i = 0; i < UndoLog.Max + 1; i++)
            Assert.Null(undo.Push(CommitStep(head, head), []));

        var refs = UndoRefs(repo);
        Assert.Equal(UndoLog.Max, refs.Count);
        Assert.DoesNotContain(UndoLog.Prefix + "000001", refs);
        Assert.Equal(UndoLog.Prefix + "000051", refs[^1]);
        Assert.Equal(UndoLog.Prefix + "000002", refs[0]);
    }

    [Fact]
    public void Empty_tree_is_used_for_a_plain_step()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        var log = new List<GitResult>();
        new UndoLog(repo.Git).Push(CommitStep("", repo.Head), log);

        var top = new UndoLog(repo.Git).Top(log)!;
        Assert.Equal(EmptyTree, repo.Run("rev-parse", top.Commit + "^{tree}"));
        Assert.Equal(EmptyTree, log.First(c => c.Command == "git mktree").StdOut.Trim());
    }

    [Fact]
    public void The_step_keeps_the_commits_it_needs_as_parents_without_duplicates_or_empties()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        var before = repo.Head;
        repo.Commit("app.txt", "b\n", "Change app");
        var after = repo.Head;
        var step = new UndoRecord(UndoKind.Merge, "merge of feature/x", [new RefChange("refs/heads/main", before, after)],
            Branch: "main", Head: after, StashCommit: after);
        var log = new List<GitResult>();

        Assert.Null(new UndoLog(repo.Git).Push(step, log));

        var top = new UndoLog(repo.Git).Top(log)!;
        var parents = repo.Run("rev-list", "--parents", "-n", "1", top.Commit).Split(' ').Skip(1).ToList();
        Assert.Equal(new[] { before, after }, parents);
    }

    [Fact]
    public void An_unstage_step_saves_its_index_entries_in_a_tree()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "A\n", "Add app");
        var blob = repo.Run("rev-parse", "HEAD:app.txt");
        var step = new UndoRecord(UndoKind.Unstage, "unstage of app.txt", [],
            IndexBefore: [new IndexEntry("app.txt", "100644", blob), new IndexEntry("gone.txt", "", "")],
            IndexAfter: [new IndexEntry("app.txt", "", "")]);
        var log = new List<GitResult>();

        Assert.Null(new UndoLog(repo.Git).Push(step, log));

        var top = new UndoLog(repo.Git).Top(log)!;
        Assert.Equal($"100644 blob {blob}\tapp.txt", repo.Run("ls-tree", "-r", top.Commit + "^{tree}"));
        Assert.Contains(log, c => c.Command.StartsWith("GIT_INDEX_FILE=.git/", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(repo.Dir, ".git", WorkState.TempIndex)));
        AssertSameRecord(step, top.Step);
    }

    [Fact]
    public void A_step_that_git_cannot_write_is_not_saved()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        var log = new List<GitResult>();

        var problem = new UndoLog(repo.Git).Push(CommitStep(MissingObject, repo.Head), log);

        Assert.NotNull(problem);
        Assert.Empty(UndoRefs(repo));
        Assert.Contains(log, c => c.Command.StartsWith("git commit-tree", StringComparison.Ordinal) && !c.Success);
        Assert.False(File.Exists(Path.Combine(repo.Dir, ".git", UndoLog.MessageFile)));
    }

    [Fact]
    public void A_step_whose_message_cannot_be_read_comes_back_with_a_null_step()
    {
        using var repo = TempRepo.Create();
        var junk = repo.Run("commit-tree", "--no-gpg-sign", EmptyTree, "-m", "not a graftui step");
        repo.Run("update-ref", UndoLog.Prefix + "000001", junk, "");

        var top = new UndoLog(repo.Git).Top([]);

        Assert.NotNull(top);
        Assert.Equal(UndoLog.Prefix + "000001", top.Ref);
        Assert.Equal(junk, top.Commit);
        Assert.Null(top.Step);
    }
}
