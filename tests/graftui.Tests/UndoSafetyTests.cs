using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// Undo of commit, amend and merge against throwaway repos, and the refusals that guard them. Undo of the Changes-tab steps
/// (discard, stash, pop, unstage) is in <see cref="UndoChangesSafetyTests"/>.
/// </summary>
public class UndoSafetyTests
{
    /// <summary>The refusal of the step on top, with the drop hint that a guard refusal carries (see ChangeRefusals.WithUndoHint).</summary>
    private static string Hinted(TempRepo repo, string refusal) =>
        ChangeRefusals.WithUndoHint(refusal, new UndoLog(repo.Git).Top([])!.Ref);

    private static UndoCheck CheckOnly(TempRepo repo) => new UndoOperation(repo.Git).Check(new UndoRequest(null));

    private static UndoOutcome Undo(TempRepo repo)
    {
        var op = new UndoOperation(repo.Git);
        var check = op.Check(new UndoRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new UndoRequest(check.StepRef), check);
    }

    private static CommitOutcome Commit(TempRepo repo, string subject, bool amend = false)
    {
        var op = new CommitOperation(repo.Git);
        var request = new CommitRequest(amend, subject, "", null, false);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static MergeOutcome Merge(TempRepo repo, string branch)
    {
        var op = new MergeOperation(repo.Git);
        var check = op.Check(new MergeRequest(branch));
        Assert.True(check.CanMerge, check.Refusal);
        return op.Run(new MergeRequest(branch), check);
    }

    private static List<string> UndoRefs(TempRepo repo) =>
        repo.Git.Run("for-each-ref", "--format=%(refname)", UndoLog.Prefix).StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    [Fact]
    public void Commit_then_undo_puts_the_branch_back_and_stages_the_same_files()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        var before = repo.Head;
        repo.Write("app.txt", "line 1\nline 2\n");
        repo.Write("new.txt", "n\n");
        repo.Run("add", "app.txt", "new.txt");
        var committed = Commit(repo, "Change app");
        Assert.Equal(CommitKind.Committed, committed.Kind);

        var outcome = Undo(repo);

        Assert.Equal(UndoOutcomeKind.Undone, outcome.Kind);
        Assert.Equal(before, repo.Head);
        Assert.Equal("M  app.txt\nA  new.txt", repo.Status());
        Assert.Equal($"↶ Undid the commit: main is back at {before[..7]} and its changes are staged again", outcome.Caption);
        Assert.Empty(UndoRefs(repo));
    }

    [Fact]
    public void Amend_then_undo_puts_the_previous_commit_back_and_keeps_the_amended_files_staged()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Commit("b.txt", "b\n", "Add b");
        var before = repo.Head;
        repo.Write("c.txt", "c\n");
        repo.Run("add", "c.txt");
        Assert.Equal(CommitKind.Amended, Commit(repo, "Add b and c", amend: true).Kind);
        var amended = repo.Head;
        Assert.NotEqual(before, amended);

        var outcome = Undo(repo);

        Assert.True(outcome.Kind == UndoOutcomeKind.Undone, outcome.Message);
        Assert.Equal(before, repo.Head);
        Assert.Equal("A  c.txt", repo.Status());
        Assert.Equal($"↶ Undid the amend: main is back at {before[..7]}", outcome.Caption);
    }

    [Fact]
    public void Initial_commit_then_undo_leaves_the_branch_unborn_with_the_files_staged()
    {
        using var repo = TempRepo.Create();
        repo.Write("app.txt", "a\n");
        repo.Run("add", "app.txt");
        Assert.Equal(CommitKind.Committed, Commit(repo, "Add app").Kind);

        var outcome = Undo(repo);

        Assert.True(outcome.Kind == UndoOutcomeKind.Undone, outcome.Message);
        Assert.False(repo.Git.Run("rev-parse", "-q", "--verify", "HEAD").Success);
        Assert.Equal("A  app.txt", repo.Status());
        Assert.Contains("has no commits again", outcome.Caption);
    }

    [Fact]
    public void Merge_commit_then_undo_puts_the_branch_back_with_a_clean_tree()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var before = repo.Head;
        Assert.Equal(MergeKind.MergeCommit, Merge(repo, "feature").Kind);
        Assert.NotEqual(before, repo.Head);

        var outcome = Undo(repo);

        Assert.True(outcome.Kind == UndoOutcomeKind.Undone, outcome.Message);
        Assert.Equal(before, repo.Head);
        Assert.True(repo.IsClean);
        Assert.False(repo.Merging);
        Assert.Equal($"↶ Undid the merge of feature: main is back at {before[..7]}", outcome.Caption);
    }

    [Fact]
    public void A_fast_forward_merge_is_undone_to_the_old_tip()
    {
        using var repo = TempRepo.FastForwardable();
        var before = repo.Head;
        Assert.Equal(MergeKind.FastForward, Merge(repo, "feature").Kind);

        Assert.Equal(UndoOutcomeKind.Undone, Undo(repo).Kind);

        Assert.Equal(before, repo.Head);
        Assert.True(repo.IsClean);
    }

    [Fact]
    public void A_dirty_tree_refuses_the_merge_undo_and_nothing_moves()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        Merge(repo, "feature");
        var merged = repo.Head;
        repo.Write("app.txt", "dirty\n");

        var check = CheckOnly(repo);

        Assert.Equal(Hinted(repo, ChangeRefusals.UndoDirty), check.Refusal);
        Assert.Equal(merged, repo.Head);
        Assert.Equal(" M app.txt", repo.Status());
        Assert.Single(UndoRefs(repo));
    }

    [Fact]
    public void Undo_is_refused_on_another_branch_and_on_a_detached_head()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 2\n");
        repo.Run("add", "app.txt");
        Commit(repo, "Change app");
        var committed = repo.Head;

        repo.Run("switch", "-q", "-c", "other");
        Assert.Equal(Hinted(repo, ChangeRefusals.UndoWrongBranch("main")), CheckOnly(repo).Refusal);

        repo.Run("switch", "-q", "--detach");
        Assert.Equal(Hinted(repo, ChangeRefusals.UndoWrongBranch("main")), CheckOnly(repo).Refusal);
        Assert.Equal(committed, repo.Head);
        Assert.Single(UndoRefs(repo));
    }

    [Fact]
    public void A_commit_made_after_the_step_refuses_the_undo_and_nothing_moves()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 2\n");
        repo.Run("add", "app.txt");
        Commit(repo, "Change app");
        repo.Commit("b.txt", "b\n", "Add b");
        var newer = repo.Head;

        var check = CheckOnly(repo);

        Assert.Equal(Hinted(repo, ChangeRefusals.UndoHeadMoved("main")), check.Refusal);
        Assert.Equal(newer, repo.Head);
        Assert.Single(UndoRefs(repo));
    }

    [Fact]
    public void A_merge_in_progress_refuses_the_undo()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Write("d.txt", "d\n");
        repo.Run("add", "d.txt");
        Assert.Equal(CommitKind.Committed, Commit(repo, "Add d").Kind);
        Assert.True(repo.Git.Run("merge", "--no-ff", "--no-commit", "feature").Success);
        Assert.True(repo.Merging);

        Assert.Equal(ChangeRefusals.InProgress("merge"), CheckOnly(repo).Refusal);
        Assert.True(repo.Merging);
        Assert.Single(UndoRefs(repo));
    }

    [Fact]
    public void Conflicted_files_refuse_the_undo_even_without_a_merge()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "stash-side\n");
        repo.Run("stash", "push", "-q");
        repo.Write("app.txt", "head-side\n");
        repo.Run("add", "app.txt");
        Commit(repo, "Head side");
        Assert.False(repo.Git.Run("stash", "pop").Success);

        Assert.Equal(ChangeRefusals.Conflicts, CheckOnly(repo).Refusal);
        Assert.False(repo.Merging);
    }

    [Fact]
    public void With_no_steps_there_is_nothing_to_undo()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");

        var check = CheckOnly(repo);

        Assert.True(check.NothingToUndo);
        Assert.Equal(ChangeRefusals.NothingToUndo, check.Refusal);
        Assert.Null(check.Step);
    }

    [Fact]
    public void A_step_whose_message_cannot_be_read_is_refused_as_unreadable_and_not_run()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        var junk = repo.Run("commit-tree", "--no-gpg-sign", "4b825dc642cb6eb9a060e54bf8d69288fbee4904", "-m", "not a step");
        repo.Run("update-ref", UndoLog.Prefix + "000001", junk, "");

        var check = CheckOnly(repo);

        Assert.Equal(ChangeRefusals.UndoUnreadable(UndoLog.Prefix + "000001"), check.Refusal);
        Assert.False(check.NothingToUndo);
        Assert.Equal(UndoLog.Prefix + "000001", check.StepRef);
        Assert.Equal(junk, repo.Run("rev-parse", UndoLog.Prefix + "000001"));
    }

    [Fact]
    public void A_record_missing_a_field_its_kind_needs_is_refused_as_unreadable()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        var head = repo.Head;
        new UndoLog(repo.Git).Push(new UndoRecord(UndoKind.Commit, "commit", [new RefChange("refs/heads/main", "", head)],
            Branch: null, Head: head), []);

        var check = CheckOnly(repo);

        Assert.Equal(ChangeRefusals.UndoUnreadable(UndoRefs(repo)[0]), check.Refusal);
        Assert.Equal(head, repo.Head);
    }

    [Fact]
    public void A_fresh_client_reads_the_step_and_undoes_it()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        var before = repo.Head;
        repo.Write("app.txt", "line 2\n");
        repo.Run("add", "app.txt");
        Commit(repo, "Change app");

        var op = new UndoOperation(new GitClient(repo.Dir));
        var check = op.Check(new UndoRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(new UndoRequest(check.StepRef), check);

        Assert.Equal(UndoOutcomeKind.Undone, outcome.Kind);
        Assert.Equal(before, repo.Head);
        Assert.Empty(UndoRefs(repo));
    }

    [Fact]
    public void A_step_that_moved_between_check_and_run_is_refused_and_nothing_is_undone()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        var before = repo.Head;
        repo.Write("app.txt", "line 2\n");
        repo.Run("add", "app.txt");
        Commit(repo, "Change app");
        var committed = repo.Head;
        var op = new UndoOperation(repo.Git);
        var check = op.Check(new UndoRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        // The step's ref now points at another commit, as if another step had been written in between.
        repo.Run("update-ref", check.StepRef!, before);

        var outcome = op.Run(new UndoRequest(check.StepRef), check);

        Assert.Equal(UndoOutcomeKind.Failed, outcome.Kind);
        Assert.Equal("the undo history changed since you pressed u; nothing was undone", outcome.Message);
        Assert.Equal(committed, repo.Head);
    }
}
