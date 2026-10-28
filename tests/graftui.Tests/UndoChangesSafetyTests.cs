using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// Undo of discard, stash, pop and the hand-picked unstage, plus several steps in a row, against throwaway repos.
/// Each undo must put back the exact state, and each guard must refuse a state that has changed since the step.
/// </summary>
public class UndoChangesSafetyTests
{
    private static WorkingStatus Read(TempRepo repo) =>
        new RepoReader(repo.Git).LoadChanges(CancellationToken.None).Status;

    private static FileChange Row(TempRepo repo, ChangeSide side, string path) =>
        Read(repo).Rows.Single(r => r.Side == side && r.Path == path);

    private static UndoCheck CheckOnly(TempRepo repo) => new UndoOperation(repo.Git).Check(new UndoRequest(null));

    private static UndoOutcome Undo(TempRepo repo)
    {
        var op = new UndoOperation(repo.Git);
        var check = op.Check(new UndoRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new UndoRequest(check.StepRef), check);
    }

    private static DiscardOutcome Discard(TempRepo repo, FileChange row)
    {
        var op = new DiscardOperation(repo.Git);
        var request = new DiscardRequest(row);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static StashOutcome Stash(TempRepo repo, string? message = null)
    {
        var op = new StashOperation(repo.Git);
        var request = new StashRequest(message);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static PopOutcome Pop(TempRepo repo)
    {
        var op = new PopOperation(repo.Git);
        var request = new PopRequest(null);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static CommitOutcome Commit(TempRepo repo, string subject)
    {
        var op = new CommitOperation(repo.Git);
        var request = new CommitRequest(false, subject, "", null, false);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static StageOutcome Unstage(TempRepo repo, StageAction action, FileChange? file, bool confirmed = false)
    {
        var op = new StageOperation(repo.Git);
        var request = new StageRequest(action, file, confirmed);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static List<string> UndoRefs(TempRepo repo) =>
        repo.Git.Run("for-each-ref", "--format=%(refname)", UndoLog.Prefix).StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Text(TempRepo repo, string path) => File.ReadAllText(Path.Combine(repo.Dir, path));

    // ---- discard ----

    [Fact]
    public void Discard_of_a_partly_staged_file_then_undo_brings_back_the_unstaged_part_and_keeps_the_staged_one()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Write("app.txt", "line A\n");
        repo.Run("add", "app.txt");
        repo.Write("app.txt", "line A\nline B\n");
        Assert.True(Discard(repo, Row(repo, ChangeSide.Unstaged, "app.txt")).Kind == DiscardKind.Discarded);
        Assert.Equal("M  app.txt", repo.Status());

        var outcome = Undo(repo);

        Assert.True(outcome.Kind == UndoOutcomeKind.Undone, outcome.Message);
        Assert.Equal("MM app.txt", repo.Status());
        Assert.Equal("line A\nline B\n", Text(repo, "app.txt"));
        Assert.Equal("line A", repo.Run("show", ":app.txt"));
        Assert.Equal("", repo.Run("stash", "list"));
        Assert.Equal("↶ Brought back your changes to app.txt", outcome.Caption);
        Assert.Empty(UndoRefs(repo));
    }

    [Fact]
    public void Undo_of_a_discard_is_refused_after_an_edit_and_the_stash_entry_is_kept()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Write("app.txt", "line A\n");
        repo.Run("add", "app.txt");
        repo.Write("app.txt", "line A\nline B\n");
        Discard(repo, Row(repo, ChangeSide.Unstaged, "app.txt"));
        repo.Write("app.txt", "edited meanwhile\n");

        var check = CheckOnly(repo);

        Assert.Equal(ChangeRefusals.UndoChanged("app.txt"), check.Refusal);
        Assert.Equal("edited meanwhile\n", Text(repo, "app.txt"));
        Assert.Single(repo.Run("stash", "list").Split('\n'));
        Assert.Single(UndoRefs(repo));
    }

    [Fact]
    public void Undo_of_a_discard_is_refused_when_its_stash_entry_is_gone()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Write("app.txt", "changed\n");
        Discard(repo, Row(repo, ChangeSide.Unstaged, "app.txt"));
        repo.Run("stash", "drop", "-q");

        Assert.Equal(ChangeRefusals.UndoStashGone, CheckOnly(repo).Refusal);
    }

    [Fact]
    public void Discard_of_an_untracked_file_then_undo_brings_the_file_back()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Write("n.txt", "hi\n");
        Discard(repo, Row(repo, ChangeSide.Unstaged, "n.txt"));
        Assert.False(File.Exists(Path.Combine(repo.Dir, "n.txt")));

        var outcome = Undo(repo);

        Assert.True(outcome.Kind == UndoOutcomeKind.Undone, outcome.Message);
        Assert.Equal("hi\n", Text(repo, "n.txt"));
        Assert.Equal("?? n.txt", repo.Status());
        Assert.Equal("", repo.Run("stash", "list"));
    }

    [Fact]
    public void Undo_of_an_untracked_discard_is_refused_when_the_file_was_recreated()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Write("n.txt", "hi\n");
        Discard(repo, Row(repo, ChangeSide.Unstaged, "n.txt"));
        repo.Write("n.txt", "again\n");

        Assert.Equal(ChangeRefusals.UndoChanged("n.txt"), CheckOnly(repo).Refusal);
        Assert.Equal("again\n", Text(repo, "n.txt"));
    }

    [Fact]
    public void Discard_of_a_deleted_file_then_undo_deletes_it_again()
    {
        using var repo = TempRepo.Create();
        repo.Commit("gone.txt", "bye\n", "Add gone");
        File.Delete(Path.Combine(repo.Dir, "gone.txt"));
        Discard(repo, Row(repo, ChangeSide.Unstaged, "gone.txt"));
        Assert.True(File.Exists(Path.Combine(repo.Dir, "gone.txt")));

        var outcome = Undo(repo);

        Assert.True(outcome.Kind == UndoOutcomeKind.Undone, outcome.Message);
        Assert.False(File.Exists(Path.Combine(repo.Dir, "gone.txt")));
        Assert.Equal(" D gone.txt", repo.Status());
        Assert.Equal("", repo.Run("stash", "list"));
    }

    // ---- stash ----

    [Fact]
    public void Stash_then_undo_restores_the_status_byte_for_byte_including_what_is_staged()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Commit("README.md", "readme\n", "Add readme");
        repo.Write("app.txt", "line 1 (staged)\n");
        repo.Run("add", "app.txt");
        repo.Write("README.md", "readme edited\n");
        repo.Write("new.txt", "new\n");
        var statusBefore = repo.Status();
        Assert.Equal(" M README.md\nM  app.txt\n?? new.txt", statusBefore);

        var stashed = Stash(repo, "wip test");
        Assert.Equal(StashKind.Stashed, stashed.Kind);
        Assert.Equal("", repo.Status());

        var outcome = Undo(repo);

        Assert.True(outcome.Kind == UndoOutcomeKind.Undone, outcome.Message);
        Assert.Equal(statusBefore, repo.Status());
        Assert.Equal("line 1 (staged)\n", Text(repo, "app.txt"));
        Assert.Equal("readme edited\n", Text(repo, "README.md"));
        Assert.Equal("", repo.Run("stash", "list"));
        Assert.Equal("↶ Unstashed 3 files", outcome.Caption);
    }

    [Fact]
    public void Undo_of_a_stash_is_refused_after_an_edit_and_nothing_moves()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        Stash(repo);
        repo.Write("later.txt", "later\n");

        Assert.Equal(ChangeRefusals.UndoDirty, CheckOnly(repo).Refusal);
        Assert.Equal("?? later.txt", repo.Status());
        Assert.Single(repo.Run("stash", "list").Split('\n'));
    }

    [Fact]
    public void Undo_of_a_stash_is_refused_when_another_stash_was_made_since()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        Stash(repo);
        repo.Write("app.txt", "line 1 (again)\n");
        repo.Run("stash", "push", "-q");

        Assert.Equal(ChangeRefusals.UndoStashMoved, CheckOnly(repo).Refusal);
        Assert.Equal(2, repo.Run("stash", "list").Split('\n').Length);
    }

    [Fact]
    public void Undo_of_a_stash_is_refused_when_head_moved_since()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        Stash(repo);
        repo.Commit("b.txt", "b\n", "Add b");
        var moved = repo.Head;

        Assert.Equal(ChangeRefusals.UndoHeadMoved("HEAD"), CheckOnly(repo).Refusal);
        Assert.Equal(moved, repo.Head);
    }

    // ---- pop ----

    [Fact]
    public void Pop_then_undo_restores_the_state_from_before_the_pop_and_the_stash_entry_as_it_was()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        repo.Write("new.txt", "new\n");
        Stash(repo, "wip test");
        var stashed = repo.Run("rev-parse", "refs/stash");
        // The state before the pop is not clean: a staged file and an untracked one, which the pop must not touch.
        repo.Write("other.txt", "o\n");
        repo.Run("add", "other.txt");
        repo.Write("u.txt", "u\n");
        var before = repo.Status();

        Assert.Equal(PopKind.Popped, Pop(repo).Kind);
        Assert.NotEqual(before, repo.Status());

        var outcome = Undo(repo);

        Assert.True(outcome.Kind == UndoOutcomeKind.Undone, outcome.Message);
        Assert.Equal(before, repo.Status());
        Assert.Equal(stashed, repo.Run("rev-parse", "stash@{0}"));
        Assert.Equal("stash@{0}: On main: wip test", repo.Run("stash", "list"));
        Assert.Equal("↶ Put the changes back into the stash", outcome.Caption);
        Assert.False(File.Exists(Path.Combine(repo.Dir, "new.txt")));
    }

    [Fact]
    public void Undo_of_a_pop_is_refused_after_an_edit_and_nothing_is_overwritten()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        Stash(repo);
        Pop(repo);
        repo.Write("app.txt", "changed after the pop\n");

        Assert.Equal(ChangeRefusals.UndoStateChanged, CheckOnly(repo).Refusal);
        Assert.Equal("changed after the pop\n", Text(repo, "app.txt"));
        Assert.Equal("", repo.Run("stash", "list"));
    }

    [Fact]
    public void Undo_of_a_pop_is_refused_when_the_stash_has_changed_since()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        Stash(repo);
        Pop(repo);
        repo.Write("app.txt", "line 1 (again)\n");
        repo.Run("stash", "push", "-q");

        Assert.Equal(ChangeRefusals.UndoStashMoved, CheckOnly(repo).Refusal);
        Assert.Single(repo.Run("stash", "list").Split('\n'));
    }

    [Fact]
    public void Undo_of_a_pop_is_refused_when_head_moved_since()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        Stash(repo);
        Pop(repo);
        repo.Run("add", "app.txt");
        repo.Run("commit", "-q", "-m", "Change app");
        var moved = repo.Head;

        Assert.Equal(ChangeRefusals.UndoHeadMoved("HEAD"), CheckOnly(repo).Refusal);
        Assert.Equal(moved, repo.Head);
    }

    // ---- hand-picked unstage ----

    [Fact]
    public void Hand_picked_unstage_then_undo_restores_the_staged_version_and_leaves_the_work_tree_alone()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "A\n");
        repo.Run("add", "app.txt");
        repo.Write("app.txt", "B\n");
        Unstage(repo, StageAction.Unstage, Row(repo, ChangeSide.Staged, "app.txt"), confirmed: true);
        Assert.Equal(" M app.txt", repo.Status());

        var outcome = Undo(repo);

        Assert.True(outcome.Kind == UndoOutcomeKind.Undone, outcome.Message);
        Assert.Equal("A", repo.Run("show", ":app.txt"));
        Assert.Equal("B\n", Text(repo, "app.txt"));
        Assert.Equal("MM app.txt", repo.Status());
        Assert.Equal("↶ Restored the hand-picked staged changes to app.txt", outcome.Caption);
    }

    [Fact]
    public void Undo_of_a_hand_picked_unstage_is_refused_when_the_file_was_staged_again()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "A\n");
        repo.Run("add", "app.txt");
        repo.Write("app.txt", "B\n");
        Unstage(repo, StageAction.Unstage, Row(repo, ChangeSide.Staged, "app.txt"), confirmed: true);
        repo.Run("add", "app.txt");

        Assert.Equal(ChangeRefusals.UndoChanged("app.txt"), CheckOnly(repo).Refusal);
        Assert.Equal("B", repo.Run("show", ":app.txt"));
    }

    [Fact]
    public void Hand_picked_unstage_of_a_new_file_then_undo_puts_the_index_entry_back()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("new.txt", "A\n");
        repo.Run("add", "new.txt");
        File.Delete(Path.Combine(repo.Dir, "new.txt"));
        Assert.Equal("AD new.txt", repo.Status());
        var staged = repo.Run("rev-parse", ":new.txt");
        Unstage(repo, StageAction.Unstage, Row(repo, ChangeSide.Staged, "new.txt"), confirmed: true);
        Assert.Equal("", repo.Status());

        var outcome = Undo(repo);

        Assert.True(outcome.Kind == UndoOutcomeKind.Undone, outcome.Message);
        Assert.Equal("AD new.txt", repo.Status());
        Assert.Equal(staged, repo.Run("rev-parse", ":new.txt"));
    }

    [Fact]
    public void Unstage_all_with_two_hand_picked_files_then_undo_restores_both()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Commit("b.txt", "b\n", "Add b");
        foreach (var file in new[] { "a.txt", "b.txt" })
        {
            repo.Write(file, "A\n");
            repo.Run("add", file);
            repo.Write(file, "B\n");
        }
        Unstage(repo, StageAction.UnstageAll, null, confirmed: true);
        Assert.Equal(" M a.txt\n M b.txt", repo.Status());

        var outcome = Undo(repo);

        Assert.True(outcome.Kind == UndoOutcomeKind.Undone, outcome.Message);
        Assert.Equal("MM a.txt\nMM b.txt", repo.Status());
        Assert.Equal("A", repo.Run("show", ":a.txt"));
        Assert.Equal("A", repo.Run("show", ":b.txt"));
        Assert.Equal("↶ Restored the hand-picked staged changes to 2 files", outcome.Caption);
    }

    [Fact]
    public void An_unstage_record_with_no_index_entries_is_refused_as_unreadable_and_never_runs_an_empty_pathspec()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        var step = new UndoRecord(UndoKind.Unstage, "unstage of 1 file", [], IndexBefore: [], IndexAfter: []);
        new UndoLog(repo.Git).Push(step, []);

        Assert.Equal(ChangeRefusals.UndoUnreadable(UndoRefs(repo)[0]), CheckOnly(repo).Refusal);
        Assert.Equal("", repo.Status());
    }

    // ---- several steps ----

    [Fact]
    public void Commit_discard_and_stash_undo_newest_first_and_then_nothing_is_left()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 0\n", "Add app");
        repo.Commit("README.md", "readme\n", "Add readme");

        repo.Write("app.txt", "line 1\n");
        repo.Run("add", "app.txt");
        Assert.Equal(CommitKind.Committed, Commit(repo, "Change line 1").Kind);

        repo.Write("README.md", "readme edited\n");
        Discard(repo, Row(repo, ChangeSide.Unstaged, "README.md"));

        repo.Write("app.txt", "line 2\n");
        Stash(repo);
        Assert.Equal("", repo.Status());
        Assert.Equal(3, UndoRefs(repo).Count);

        var first = Undo(repo);
        Assert.Equal("↶ Unstashed 1 file", first.Caption);
        Assert.Equal("line 2\n", Text(repo, "app.txt"));

        var second = Undo(repo);
        Assert.Equal("↶ Brought back your changes to README.md", second.Caption);
        Assert.Equal("readme edited\n", Text(repo, "README.md"));

        var third = Undo(repo);
        Assert.StartsWith("↶ Undid the commit: main is back at", third.Caption);
        Assert.Equal("Add readme", repo.Run("log", "-1", "--format=%s"));

        var fourth = CheckOnly(repo);
        Assert.True(fourth.NothingToUndo);
        Assert.Equal("Nothing to undo", fourth.Refusal);
        Assert.Empty(UndoRefs(repo));
        Assert.Equal("", repo.Run("for-each-ref", "refs/graftui/"));
    }

    [Fact]
    public void A_failed_undo_of_a_pop_keeps_the_state_after_the_pop_under_a_ref_and_refuses_until_it_is_dealt_with()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        Stash(repo);
        Pop(repo);
        var step = new UndoLog(repo.Git).Top([])!.Step!;
        var afterPop = Text(repo, "app.txt");

        // A lock on the branch makes the reset that the undo needs fail after the guard has passed. The guard only reads.
        var branchLock = Path.Combine(repo.Dir, ".git", "refs", "heads", "main.lock");
        var op = new UndoOperation(repo.Git);
        var check = op.Check(new UndoRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        File.WriteAllText(branchLock, "");
        UndoOutcome outcome;
        try
        {
            outcome = op.Run(new UndoRequest(check.StepRef), check);
        }
        finally
        {
            File.Delete(branchLock);
        }

        Assert.Equal(UndoOutcomeKind.Failed, outcome.Kind);
        Assert.Contains("refs/graftui/rescue/undo-pop", outcome.Message);
        Assert.Contains("git restore --source=" + step.StateAfter, outcome.Message);
        Assert.Equal(step.StateAfter, repo.Run("rev-parse", "refs/graftui/rescue/undo-pop"));
        Assert.Equal(afterPop, repo.Run("show", step.StateAfter + ":app.txt") + "\n");
        Assert.Equal(2, UndoRefs(repo).Count);

        // While the copy is kept, nothing is undone, and the refusal says how to deal with it.
        Assert.Equal(ChangeRefusals.RescueSaved("the state after a pop, from an undo of it that failed", "refs/graftui/rescue/undo-pop"),
            CheckOnly(repo).Refusal);

        // Restoring from the copy, as the message says, and deleting it, lets the undo go through.
        repo.Run("restore", "--source=" + step.StateAfter, "--worktree", "--", ".");
        repo.Run("update-ref", "-d", "refs/graftui/rescue/undo-pop");
        Assert.Equal(afterPop, Text(repo, "app.txt"));
        Assert.Equal(UndoOutcomeKind.Undone, Undo(repo).Kind);
        Assert.Single(UndoRefs(repo));
        Assert.Equal(UndoRefs(repo), repo.Run("for-each-ref", "--format=%(refname)", "refs/graftui/").Split('\n').ToList());
        Assert.Equal("line 1\n", Text(repo, "app.txt"));
        Assert.Equal("", repo.Status());
        Assert.Single(repo.Run("stash", "list").Split('\n'));
    }
}
