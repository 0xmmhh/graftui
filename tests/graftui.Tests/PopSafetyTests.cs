using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>Pop (Z) against throwaway repos: a clean pop restores the entry, and a pop that git cannot apply leaves the state as it was.</summary>
public class PopSafetyTests
{
    private const string StashMergeMessage = "graftui: before merging x into main";

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

    [Fact]
    public void A_pop_is_refused_while_an_intent_to_add_file_is_present_and_changes_nothing()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "one\n");
        repo.Run("stash", "push", "-q");
        var seen = repo.Run("rev-parse", "stash@{0}");
        repo.Write("new.txt", "new\n");
        repo.Run("add", "-N", "new.txt");
        var before = repo.Status();

        var op = new PopOperation(repo.Git);
        var request = new PopRequest(seen);
        var check = op.Check(request);
        var outcome = op.Run(request, check);

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.IntentToAddStash("new.txt"), check.Refusal);
        Assert.Equal(PopKind.Refused, outcome.Kind);
        Assert.Equal(before, repo.Status());
        Assert.Equal(seen, repo.Run("rev-parse", "stash@{0}"));
    }

    [Fact]
    public void A_pop_is_refused_while_a_rescue_copy_is_kept_and_changes_nothing()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "one\n");
        repo.Run("stash", "push", "-q");
        var seen = repo.Run("rev-parse", "stash@{0}");
        repo.Write("app.txt", "two\n");
        repo.Run("update-ref", "refs/graftui/rescue/pop", "HEAD");
        var before = repo.Status();

        var op = new PopOperation(repo.Git);
        var request = new PopRequest(seen);
        var check = op.Check(request);
        var outcome = op.Run(request, check);

        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.RescueSaved("your state from before an earlier pop", "refs/graftui/rescue/pop"), check.Refusal);
        Assert.Equal(PopKind.Refused, outcome.Kind);
        Assert.Equal(before, repo.Status());
        Assert.Equal(seen, repo.Run("rev-parse", "stash@{0}"));
        Assert.Equal(repo.Head, repo.Run("rev-parse", "refs/graftui/rescue/pop"));
    }

    [Fact]
    public void A_copy_that_appears_after_the_check_is_never_overwritten()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "one\n");
        repo.Run("stash", "push", "-q");
        var seen = repo.Run("rev-parse", "stash@{0}");
        repo.Commit("other.txt", "o\n", "Add other");
        var op = new PopOperation(repo.Git);
        var request = new PopRequest(seen);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);

        // Another process saves its copy between the check and the run.
        var earlier = repo.Run("rev-parse", "HEAD~1");
        repo.Run("update-ref", "refs/graftui/rescue/pop", earlier);
        var before = repo.Status();
        var outcome = op.Run(request, check);

        Assert.Equal(PopKind.Failed, outcome.Kind);
        Assert.Contains("could not save a safety copy", outcome.Message);
        Assert.Equal(earlier, repo.Run("rev-parse", "refs/graftui/rescue/pop"));
        Assert.Equal(seen, repo.Run("rev-parse", "stash@{0}"));
        Assert.Equal(before, repo.Status());
    }

    [Fact]
    public void A_stash_merge_entry_whose_index_pop_conflicts_in_the_tree_is_rolled_back_and_the_entry_is_kept()
    {
        // HEAD's B equals the stash's index B, so git skips the index step. The tree merge of A to C, against B, then conflicts.
        using var repo = TempRepo.Create();
        repo.Commit("f.txt", "A\n", "Add f");
        repo.Write("f.txt", "B\n");
        repo.Run("add", "f.txt");
        repo.Write("f.txt", "C\n");
        repo.Run("stash", "push", "-m", StashMergeMessage);
        repo.Commit("f.txt", "B\n", "Move f on main");
        var stashed = repo.Run("rev-parse", "stash@{0}");
        var statusBefore = repo.Status();
        var headBefore = repo.Head;

        var outcome = Pop(repo, stashed);

        Assert.Equal(PopKind.Conflict, outcome.Kind);
        Assert.Equal(new[] { "f.txt" }, outcome.ConflictFiles);
        Assert.Equal(statusBefore, repo.Status());
        Assert.Equal(headBefore, repo.Head);
        Assert.Equal("B\n", File.ReadAllText(Path.Combine(repo.Dir, "f.txt")));
        Assert.DoesNotContain("<<<<<<<", File.ReadAllText(Path.Combine(repo.Dir, "f.txt")));
        Assert.Equal("", repo.Run("diff", "--name-only", "--diff-filter=U"));
        Assert.Equal(stashed, repo.Run("rev-parse", "stash@{0}"));
        Assert.Contains(StashMergeMessage, repo.Run("stash", "list"));
        Assert.Equal("", RescueRefs(repo));
        Assert.Null(outcome.Undo);

        // The failed `pop --index` ran, and the plain pop did not run after it.
        Assert.Contains(outcome.Commands, c => c.Args.SequenceEqual(["stash", "pop", "--index"]) && c.ExitCode != 0);
        Assert.DoesNotContain(outcome.Commands, c => c.Args.SequenceEqual(["stash", "pop"]));
    }

    [Fact]
    public void Up_front_refusal_is_decided_from_the_repo_and_not_from_git_text()
    {
        // RefusedUpFront never reads the text git printed, so an untouched repo counts as refused up front, with or without any stderr.
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "one\n");
        repo.Run("stash", "push", "-q");
        var top = repo.Run("rev-parse", "stash@{0}");
        var log = new List<GitResult>();
        var gitDir = RepoState.GitDir(repo.Git, log)!;
        var state = new WorkState(repo.Git, gitDir);
        var before = state.Take(log)!;
        var op = new PopOperation(repo.Git);

        Assert.True(op.RefusedUpFront(state, before, top, log));

        // The stash entry is not the one that was popped: not a refusal, so the state is restored instead.
        Assert.False(op.RefusedUpFront(state, before, repo.Head, log));

        // The files changed since the snapshot: not a refusal.
        repo.Write("app.txt", "two\n");
        Assert.False(op.RefusedUpFront(state, before, top, log));
    }

    /// <summary>The first line git printed on stderr for the command with these arguments.</summary>
    private static string FirstStderrLine(PopOutcome outcome, string[] args)
    {
        var result = outcome.Commands.First(c => c.Args.SequenceEqual(args));
        return result.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
    }

    [Fact]
    public void A_stash_merge_entry_that_an_unstaged_edit_overlaps_is_refused_with_the_git_line_and_changes_nothing()
    {
        // f.txt is staged as B over A, and the stash keeps C in the tree. The user then edits f.txt again, unstaged. git will not
        // let either pop overwrite that edit. The staged part differs from the working part, so Z refuses and names it.
        using var repo = TempRepo.Create();
        repo.Commit("f.txt", "A\n", "Add f");
        repo.Write("f.txt", "B\n");
        repo.Run("add", "f.txt");
        repo.Write("f.txt", "C\n");
        repo.Run("stash", "push", "-m", StashMergeMessage);
        var stashed = repo.Run("rev-parse", "stash@{0}");
        repo.Write("f.txt", "D\n");
        var statusBefore = repo.Status();
        var headBefore = repo.Head;

        var outcome = Pop(repo, stashed);

        Assert.Equal(PopKind.Refused, outcome.Kind);
        Assert.Contains(FirstStderrLine(outcome, ["stash", "pop", "--index"]), outcome.Message);
        Assert.Contains("your staged f.txt can't be put back", outcome.Message);
        Assert.Equal(statusBefore, repo.Status());
        Assert.Equal(headBefore, repo.Head);
        Assert.Equal("D\n", File.ReadAllText(Path.Combine(repo.Dir, "f.txt")));
        Assert.Equal(stashed, repo.Run("rev-parse", "stash@{0}"));
        Assert.Contains(StashMergeMessage, repo.Run("stash", "list"));
        Assert.Equal("", RescueRefs(repo));
        Assert.Null(outcome.Undo);
    }

    [Fact]
    public void A_stash_merge_entry_with_nothing_staged_at_risk_is_still_refused_when_the_plain_pop_also_refuses()
    {
        // f.txt is staged as B and kept as B, so no staged content can be lost and the plain pop runs. Git refuses that pop too,
        // because the user's unstaged edit overlaps the entry. The refusal comes back as Refused, and nothing changes.
        using var repo = TempRepo.Create();
        repo.Commit("f.txt", "A\n", "Add f");
        repo.Write("f.txt", "B\n");
        repo.Run("add", "f.txt");
        repo.Run("stash", "push", "-m", StashMergeMessage);
        var stashed = repo.Run("rev-parse", "stash@{0}");
        repo.Write("f.txt", "D\n");
        var statusBefore = repo.Status();
        var headBefore = repo.Head;

        var outcome = Pop(repo, stashed);

        Assert.Equal(PopKind.Refused, outcome.Kind);
        Assert.Contains("nothing changed, the stash entry is kept", outcome.Message);
        Assert.Contains(FirstStderrLine(outcome, ["stash", "pop"]), outcome.Message);
        Assert.Contains(outcome.Commands, c => c.Args.SequenceEqual(["stash", "pop"]) && c.ExitCode != 0);
        Assert.Equal(statusBefore, repo.Status());
        Assert.Equal(headBefore, repo.Head);
        Assert.Equal("D\n", File.ReadAllText(Path.Combine(repo.Dir, "f.txt")));
        Assert.Equal(stashed, repo.Run("rev-parse", "stash@{0}"));
        Assert.Equal("", RescueRefs(repo));
    }
}
