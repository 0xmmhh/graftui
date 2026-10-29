using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// Undo of the branch kinds (create, rename, delete) and of stash-merge-pop, against temp repos. Each guard refusal is checked
/// with the drop hint that a guard refusal carries.
/// </summary>
public class UndoBranchesAndStashMergeSafetyTests
{
    [Fact]
    public void Undoing_a_branch_creation_removes_the_branch_and_switches_back_with_the_changes()
    {
        using var repo = Base();
        repo.Write("app.txt", "edited\n");
        var status = repo.Status();
        CreateBranch(repo, "topic");

        var outcome = Undo(repo);

        Assert.Equal(UndoOutcomeKind.Undone, outcome.Kind);
        Assert.Equal("↶ Removed topic and switched back to main", outcome.Caption);
        Assert.Equal("main", repo.Run("symbolic-ref", "--short", "HEAD"));
        Assert.Equal("", repo.Run("branch", "--list", "topic"));
        Assert.Equal(status, repo.Status());
        Assert.Null(new UndoLog(repo.Git).Top([]));
    }

    [Fact]
    public void Undoing_a_branch_creation_is_refused_once_the_new_branch_has_commits()
    {
        using var repo = Base();
        CreateBranch(repo, "topic");
        repo.Commit("b.txt", "b\n", "On topic");

        Assert.Equal(Hinted(repo, ChangeRefusals.UndoHeadMoved("topic")), Refusal(repo));
    }

    [Fact]
    public void Undoing_a_branch_creation_is_refused_when_its_base_moved_away_from_its_commits()
    {
        using var repo = Base();
        CreateBranch(repo, "topic");
        repo.Run("switch", "-q", "main");
        repo.Run("reset", "-q", "--hard", "HEAD~1");

        Assert.Equal(Hinted(repo, BranchRefusals.UndoWouldLose("main", "topic")), Refusal(repo));
        Assert.NotEqual("", repo.Run("branch", "--list", "topic"));
    }

    [Fact]
    public void Undoing_a_rename_brings_the_old_name_back_and_leaves_head_where_it_was()
    {
        using var repo = Base();
        repo.Run("branch", "feature");
        var tip = repo.Run("rev-parse", "feature");
        RenameBranch(repo, "feature", "topic");

        var outcome = Undo(repo);

        Assert.Equal("↶ Renamed topic back to feature", outcome.Caption);
        Assert.Equal(tip, repo.Run("rev-parse", "feature"));
        Assert.Equal("", repo.Run("branch", "--list", "topic"));
        Assert.Equal("main", repo.Run("symbolic-ref", "--short", "HEAD"));
    }

    [Fact]
    public void Undoing_the_rename_of_the_current_branch_moves_head_back_to_the_old_name()
    {
        using var repo = Base();
        CreateBranch(repo, "topic");
        RenameBranch(repo, "topic", "renamed");
        Assert.Equal("renamed", repo.Run("symbolic-ref", "--short", "HEAD"));

        Undo(repo);

        Assert.Equal("topic", repo.Run("symbolic-ref", "--short", "HEAD"));
        Assert.Equal("", repo.Run("branch", "--list", "renamed"));
    }

    [Fact]
    public void Undoing_a_rename_is_refused_when_the_new_name_is_gone_or_the_old_name_is_taken_again()
    {
        using var gone = Base();
        gone.Run("branch", "feature");
        RenameBranch(gone, "feature", "topic");
        gone.Run("branch", "-D", "topic");
        Assert.Equal(Hinted(gone, BranchRefusals.UndoBranchGone("topic")), Refusal(gone));

        using var taken = Base();
        taken.Run("branch", "feature");
        RenameBranch(taken, "feature", "topic");
        taken.Run("branch", "feature", "main");
        Assert.Equal(Hinted(taken, BranchRefusals.UndoBranchExists("feature")), Refusal(taken));
    }

    [Fact]
    public void Undoing_an_unmerged_delete_restores_the_branch_at_the_same_commit()
    {
        using var repo = TempRepo.FastForwardable();
        var tip = repo.Run("rev-parse", "feature");
        Delete(repo, "feature", force: true);
        Assert.Equal("", repo.Run("branch", "--list", "feature"));

        var outcome = Undo(repo);

        Assert.Equal(UndoOutcomeKind.Undone, outcome.Kind);
        Assert.Equal("↶ Restored feature at " + tip[..7], outcome.Caption);
        Assert.Equal(tip, repo.Run("rev-parse", "feature"));
        Assert.Null(new UndoLog(repo.Git).Top([]));
    }

    [Fact]
    public void Undoing_a_delete_is_refused_when_the_name_is_taken_again()
    {
        using var repo = TempRepo.FastForwardable();
        Delete(repo, "feature", force: true);
        repo.Run("branch", "feature", "main");

        Assert.Equal(Hinted(repo, BranchRefusals.UndoBranchExists("feature")), Refusal(repo));
    }

    [Fact]
    public void Create_rename_and_delete_in_a_row_undo_newest_first_and_leave_no_steps()
    {
        using var repo = Base();
        CreateBranch(repo, "topic");
        RenameBranch(repo, "topic", "renamed");
        repo.Run("switch", "-q", "main");
        var deleted = Delete(repo, "renamed");
        Assert.Equal(BranchChangeKind.Done, deleted.Kind);

        Assert.StartsWith("↶ Restored renamed at ", Undo(repo).Caption);
        Assert.Equal("↶ Renamed renamed back to topic", Undo(repo).Caption);
        Assert.Equal("↶ Removed topic", Undo(repo).Caption);

        Assert.Equal("main", repo.Run("for-each-ref", "--format=%(refname:short)", "refs/heads"));
        Assert.Equal("", repo.Run("for-each-ref", UndoLog.Prefix));
        Assert.Null(new UndoLog(repo.Git).Top([]));
    }

    [Fact]
    public void A_branch_step_without_the_field_it_needs_is_refused_as_unreadable()
    {
        using var repo = Base();
        var head = repo.Head;
        new UndoLog(repo.Git).Push(new UndoRecord(UndoKind.CreateBranch, "creation of topic",
            [new RefChange("refs/heads/topic", "", head)], Branch: "topic", Head: head), []);

        var check = new UndoOperation(repo.Git).Check(new UndoRequest(null));

        Assert.Equal(ChangeRefusals.UndoUnreadable(new UndoLog(repo.Git).Top([])!.Ref), check.Refusal);
        Assert.Equal(head, repo.Head);
    }

    [Fact]
    public void A_stash_merge_step_without_its_states_and_a_step_of_unknown_kind_are_refused_and_not_run()
    {
        using var repo = FastForward();
        var head = repo.Head;
        new UndoLog(repo.Git).Push(new UndoRecord(UndoKind.StashMerge, "merge of feature/ff over your changes",
            [new RefChange("refs/heads/main", head, head)], Branch: "main", Head: head, StashCommit: head, StashBelow: ""), []);
        var missingStates = new UndoLog(repo.Git).Top([])!.Ref;
        Assert.Equal(ChangeRefusals.UndoUnreadable(missingStates), Refusal(repo));

        repo.Run("update-ref", "-d", missingStates);
        new UndoLog(repo.Git).Push(new UndoRecord((UndoKind)99, "something new", []), []);
        var unknown = new UndoLog(repo.Git).Top([])!.Ref;
        Assert.Equal(ChangeRefusals.UndoUnreadable(unknown), Refusal(repo));
        Assert.Equal(head, repo.Head);
    }

    [Fact]
    public void Undoing_a_stash_merge_pop_puts_back_the_branch_and_the_changes_exactly()
    {
        using var repo = FastForward();
        repo.Write("a.txt", "a staged\n");
        repo.Run("add", "a.txt");
        repo.Write("README.md", "readme\nunstaged\n");
        repo.Write("notes.md", "note\n");
        var head = repo.Head;
        var state = StateOf(repo);
        var status = repo.Status();
        StashMerge(repo, "feature/ff");
        var merged = repo.Head;
        Assert.NotEqual(head, merged);

        var outcome = Undo(repo);

        Assert.Equal(UndoOutcomeKind.Undone, outcome.Kind);
        Assert.Equal($"↶ Undid the merge of feature/ff: main is back at {head[..7]} with your changes as they were", outcome.Caption);
        Assert.Equal("main", repo.Run("symbolic-ref", "--short", "HEAD"));
        Assert.Equal(head, repo.Head);
        Assert.Equal(state, StateOf(repo));
        Assert.Equal(status, repo.Status());
        Assert.False(repo.Merging);
        Assert.Equal("", repo.Run("stash", "list"));
        Assert.False(File.Exists(Path.Combine(repo.Dir, "ff.txt")));
        Assert.Equal("", repo.Run("for-each-ref", "refs/graftui/rescue"));
        Assert.Null(new UndoLog(repo.Git).Top([]));
    }

    [Fact]
    public void A_later_edit_stops_the_stash_merge_undo_and_nothing_moves()
    {
        using var repo = FastForward();
        repo.Write("notes.md", "note\n");
        StashMerge(repo, "feature/ff");
        var merged = repo.Head;
        repo.Write("notes.md", "changed later\n");

        Assert.Equal(Hinted(repo, BranchRefusals.UndoMergeStateChanged), Refusal(repo));
        Assert.Equal(merged, repo.Head);
        Assert.Equal("changed later\n", File.ReadAllText(Path.Combine(repo.Dir, "notes.md")));
    }

    [Fact]
    public void After_a_pop_conflict_undo_takes_the_merge_back_and_Z_brings_the_changes_back()
    {
        using var repo = Touch();
        repo.Write("app.txt", "mine\nline 2\n");
        repo.Write("notes.md", "note\n");
        var head = repo.Head;
        var stash = StashMerge(repo, "feature/touch");
        Assert.Equal(StashMergeKind.PopConflict, stash.Kind);
        Assert.NotEqual(head, repo.Head);

        var outcome = Undo(repo);

        Assert.Equal(UndoOutcomeKind.Undone, outcome.Kind);
        Assert.Equal(head, repo.Head);
        Assert.True(repo.IsClean);
        Assert.Equal(StashLabel, repo.Run("stash", "list", "--format=%gs"));

        var pop = new PopOperation(repo.Git);
        var popCheck = pop.Check(new PopRequest(null));
        Assert.True(popCheck.CanRun, popCheck.Refusal);
        var popped = pop.Run(new PopRequest(null), popCheck);
        Assert.Equal(PopKind.Popped, popped.Kind);
        Assert.Equal("mine\nline 2\n", File.ReadAllText(Path.Combine(repo.Dir, "app.txt")));
        Assert.Contains(" M app.txt", repo.Status());
        Assert.Contains("?? notes.md", repo.Status());
        Assert.Equal("", repo.Run("stash", "list"));
    }

    /// <summary>The subject git gives the entry the temporary stash makes on main.</summary>
    private const string StashLabel = "On main: graftui: before merging feature/touch into main";

    private static void CreateBranch(TempRepo repo, string name)
    {
        var op = new CreateBranchOperation(repo.Git);
        var check = op.Check(new CreateBranchRequest(name));
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(new CreateBranchRequest(name), check);
        Assert.Equal(BranchChangeKind.Done, outcome.Kind);
    }

    private static void RenameBranch(TempRepo repo, string old, string nu)
    {
        var op = new RenameBranchOperation(repo.Git);
        var request = new RenameBranchRequest(old, nu);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(request, check);
        Assert.Equal(BranchChangeKind.Done, outcome.Kind);
    }

    private static BranchChangeOutcome Delete(TempRepo repo, string name, bool force = false)
    {
        var op = new DeleteBranchOperation(repo.Git);
        var request = new DeleteBranchRequest(name, null, force);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static StashMergeOutcome StashMerge(TempRepo repo, string branch)
    {
        var op = new StashMergeOperation(repo.Git);
        var check = op.Check(new StashMergeRequest(branch));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new StashMergeRequest(branch), check);
    }

    private static UndoOutcome Undo(TempRepo repo)
    {
        var op = new UndoOperation(repo.Git);
        var check = op.Check(new UndoRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new UndoRequest(check.StepRef), check);
    }

    private static string? Refusal(TempRepo repo) => new UndoOperation(repo.Git).Check(new UndoRequest(null)).Refusal;

    /// <summary>The refusal of the step on top, with the drop hint that a guard refusal carries.</summary>
    private static string Hinted(TempRepo repo, string refusal) =>
        ChangeRefusals.WithUndoHint(refusal, new UndoLog(repo.Git).Top([])!.Ref);

    private static WorkSnapshot StateOf(TempRepo repo) =>
        new WorkState(repo.Git, Path.Combine(repo.Dir, ".git")).Take([])!;

    /// <summary>main has Add app, Add readme and Add a.</summary>
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

    /// <summary>feature/touch changes line 1 of app.txt, so the merge fast-forwards and a pop over app.txt conflicts.</summary>
    private static TempRepo Touch()
    {
        var repo = Base();
        repo.Run("switch", "-q", "-c", "feature/touch");
        repo.Commit("app.txt", "touched\nline 2\n", "Touch line 1");
        repo.Run("switch", "-q", "main");
        return repo;
    }
}
