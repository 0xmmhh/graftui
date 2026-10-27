using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>Pop (Z) against throwaway repos: a clean pop restores the entry, and a pop that git cannot apply leaves the state as it was.</summary>
public class PopSafetyTests
{
    private static PopOutcome Pop(TempRepo repo, string? expectedTop = null)
    {
        var op = new PopOperation(repo.Git);
        var request = new PopRequest(expectedTop);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static PopCheck CheckOnly(TempRepo repo, string? expectedTop = null) =>
        new PopOperation(repo.Git).Check(new PopRequest(expectedTop));

    private static IReadOnlyList<string> Lines(TempRepo repo) =>
        repo.Status().Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToList();

    private static string RescueRefs(TempRepo repo) => repo.Git.Run("for-each-ref", "refs/graftui/rescue/").StdOut.Trim();

    [Fact]
    public void A_clean_pop_restores_the_files_and_empties_the_stash_without_restoring_the_staging()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Commit("README.md", "readme\n", "Add readme");
        repo.Write("app.txt", "changed\n");
        repo.Run("add", "app.txt");
        repo.Write("README.md", "readme, edited\n");
        repo.Write("new.txt", "new\n");
        var stash = new StashOperation(repo.Git);
        var request = new StashRequest("x");
        var stashCheck = stash.Check(request);
        var stashed = stash.Run(request, stashCheck);
        Assert.Equal(StashKind.Stashed, stashed.Kind);
        var stashCommit = repo.Run("rev-parse", "stash@{0}");

        var outcome = Pop(repo, stashCommit);

        Assert.Equal(PopKind.Popped, outcome.Kind);
        Assert.Equal(new[] { "?? new.txt", " M README.md", " M app.txt" }.Order(StringComparer.Ordinal), Lines(repo));
        Assert.Equal("changed\n", File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));
        Assert.Equal("", repo.Run("stash", "list"));
        Assert.Equal("", RescueRefs(repo));
        Assert.NotNull(outcome.Undo);
        Assert.Equal("On main: x", outcome.TopMessage);

        var step = new UndoLog(repo.Git).Top([])!.Step!;
        Assert.Equal(UndoKind.Pop, step.Kind);
        Assert.Equal(stashCommit, step.StashCommit);
        Assert.Equal("On main: x", step.StashMessage);
        Assert.Equal("", step.StashBelow);
        Assert.NotNull(step.StateBefore);
        Assert.NotNull(step.StateAfter);
    }

    /// <summary>
    /// A stash on app.txt that carries an untracked file, then a commit that changes app.txt again. Before the pop the tree has
    /// a staged-and-modified other.txt, an untracked mine.txt and a deleted gone.txt.
    /// </summary>
    private static TempRepo ConflictingPop(out string stashCommit)
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Commit("other.txt", "o\n", "Add other");
        repo.Commit("gone.txt", "g\n", "Add gone");
        repo.Write("app.txt", "stash-side\n");
        repo.Write("fromstash.txt", "from the stash\n");
        repo.Run("stash", "push", "-u", "-q");
        stashCommit = repo.Run("rev-parse", "stash@{0}");
        repo.Write("app.txt", "head-side\n");
        repo.Run("commit", "-q", "-am", "head moves");

        repo.Write("other.txt", "o2\n");
        repo.Run("add", "other.txt");
        repo.Write("other.txt", "o3\n");
        repo.Write("mine.txt", "mine\n");
        File.Delete(Path.Combine(repo.Dir, "gone.txt"));
        return repo;
    }

    [Fact]
    public void A_conflicting_pop_is_rolled_back_exactly_and_the_stash_entry_is_kept()
    {
        using var repo = ConflictingPop(out var stashCommit);
        var statusBefore = repo.Status();
        var headBefore = repo.Head;
        var stashListBefore = repo.Run("stash", "list");
        Assert.Equal(new[] { " D gone.txt", "?? mine.txt", "MM other.txt" }.Order(StringComparer.Ordinal), Lines(repo));

        var outcome = Pop(repo, stashCommit);

        Assert.Equal(PopKind.Conflict, outcome.Kind);
        Assert.Equal(new[] { "app.txt" }, outcome.ConflictFiles);
        Assert.Equal(statusBefore, repo.Status());
        Assert.Equal(headBefore, repo.Head);
        Assert.Equal("head-side\n", File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));
        Assert.DoesNotContain("<<<<<<<", File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));
        Assert.Equal("", repo.Run("diff", "--name-only", "--diff-filter=U"));
        Assert.Equal(stashCommit, repo.Run("rev-parse", "stash@{0}"));
        Assert.Equal(stashListBefore, repo.Run("stash", "list"));
        Assert.False(File.Exists(Path.Combine(repo.Dir, "fromstash.txt")));
        Assert.Equal("", RescueRefs(repo));
        Assert.Null(outcome.Undo);
    }

    [Fact]
    public void A_pop_that_git_refuses_up_front_changes_nothing()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "stash-side\n");
        repo.Run("stash", "push", "-q");
        var stashCommit = repo.Run("rev-parse", "stash@{0}");
        // A local edit to the same file: git will not merge over it.
        repo.Write("app.txt", "local\n");
        var statusBefore = repo.Status();
        var headBefore = repo.Head;

        var outcome = Pop(repo, stashCommit);

        Assert.Equal(PopKind.Refused, outcome.Kind);
        Assert.Contains("the stash entry is kept", outcome.Message);
        Assert.Equal(statusBefore, repo.Status());
        Assert.Equal("local\n", File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));
        Assert.Equal(headBefore, repo.Head);
        Assert.Equal(stashCommit, repo.Run("rev-parse", "stash@{0}"));
        Assert.Single(repo.Run("stash", "list").Split('\n'));
        Assert.Equal("", RescueRefs(repo));
    }

    [Fact]
    public void A_stash_that_moved_since_the_user_saw_it_is_refused()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "one\n");
        repo.Run("stash", "push", "-q");
        var seen = repo.Run("rev-parse", "stash@{0}");
        repo.Write("app.txt", "two\n");
        repo.Run("stash", "push", "-q");

        var check = CheckOnly(repo, seen);

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.StashMoved, check.Refusal);
        Assert.Equal(2, repo.Run("stash", "list").Split('\n').Length);
    }

    [Fact]
    public void An_empty_stash_is_refused()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");

        var check = CheckOnly(repo);

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.StashEmpty, check.Refusal);
    }
}
