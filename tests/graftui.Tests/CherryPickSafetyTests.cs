using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// CherryPickOperation against throwaway repos: the refusals (word for word), the picks oldest first, a conflict pause, the resume
/// actions on a paused pick, an empty pick that is skipped and reported, and the one undo step.
/// Picks() feature rows: 0 Add login screen, 1 Fix null check in auth, 2 Add remember-me option (HEAD on main).
/// PickConflict() feature rows: 0 Add login screen, 1 Fix null check in auth, which conflicts with main (HEAD on main).
/// </summary>
public class CherryPickSafetyTests
{
    private static CherryPickCheck CheckPick(TempRepo repo, params string[] commits) =>
        new CherryPickOperation(repo.Git).Check(new CherryPickRequest(commits));

    /// <summary>Check, then Run when the check allows it. A refused check fails the test: use CheckPick for refusals.</summary>
    private static SequenceOutcome Pick(TempRepo repo, params string[] commits)
    {
        var op = new CherryPickOperation(repo.Git);
        var request = new CherryPickRequest(commits);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static SequenceOutcome Resume(TempRepo repo, ResumeRequest request)
    {
        var op = new ResumeSequenceOperation(repo.Git);
        return op.Run(request, op.Check(request));
    }

    private static bool StateFileExists(TempRepo repo) => File.Exists(Path.Combine(repo.Dir, ".git", SequenceStateFile.FileName));

    private static bool PickHeadExists(TempRepo repo) => File.Exists(Path.Combine(repo.Dir, ".git", "CHERRY_PICK_HEAD"));

    [Fact]
    public void One_commit_is_copied_and_the_original_stays()
    {
        using var repo = SequenceRepos.Picks();
        var oldTip = repo.Head;
        var original = repo.Run("rev-parse", "feature~1");
        var featureBefore = repo.Run("rev-parse", "feature");

        var outcome = Pick(repo, original);

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal("Fix null check in auth", repo.Run("log", "-1", "--format=%s"));
        Assert.Equal(oldTip, repo.Run("rev-parse", "HEAD^"));
        Assert.Equal(featureBefore, repo.Run("rev-parse", "feature"));
        Assert.Equal(original, outcome.Moves[0].Old);
        Assert.Equal(repo.Head, outcome.Moves[0].New);
        Assert.False(StateFileExists(repo));
    }

    [Fact]
    public void Several_commits_go_oldest_first_whatever_the_request_order()
    {
        using var repo = SequenceRepos.Picks();
        var login = repo.Run("rev-parse", "feature~2");
        var remember = repo.Run("rev-parse", "feature");

        var outcome = Pick(repo, remember, login);

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal(new[] { "Add login screen", "Add remember-me option" },
            repo.Run("log", "--reverse", "-2", "--format=%s").Split('\n'));
        Assert.Contains(outcome.Commands, c => c.Command == $"git cherry-pick {login} {remember}");
    }

    [Fact]
    public void Check_refuses_an_already_applied_commit_naming_the_twin()
    {
        using var repo = SequenceRepos.Picks();
        var selected = repo.Run("rev-parse", "feature~1");
        repo.Run("cherry-pick", selected);
        var twin = repo.Run("rev-parse", "HEAD");

        var check = CheckPick(repo, selected);

        Assert.False(check.CanRun);
        Assert.Equal($"{selected[..7]} is already on main (same change as {twin[..7]}): nothing to pick", check.Refusal);
    }

    [Fact]
    public void Check_refuses_commits_already_on_the_current_branch()
    {
        using var repo = SequenceRepos.Picks();
        var onMain = repo.Run("rev-parse", "HEAD~2");

        var check = CheckPick(repo, onMain);

        Assert.Equal("these commits are already on main", check.Refusal);
    }

    [Fact]
    public void Check_refuses_a_dirty_tree_naming_the_file_and_changes_nothing()
    {
        using var repo = SequenceRepos.Picks();
        repo.Write("auth.ts", "changed\n");
        var head = repo.Head;

        var check = CheckPick(repo, repo.Run("rev-parse", "feature~1"));

        Assert.False(check.CanRun);
        Assert.Equal("you have uncommitted changes to auth.ts: commit or stash them before picking", check.Refusal);
        Assert.Equal(head, repo.Head);
    }

    [Fact]
    public void Check_refuses_detached_HEAD()
    {
        using var repo = SequenceRepos.Picks();
        repo.Run("checkout", "-q", "--detach");

        var check = CheckPick(repo, repo.Run("rev-parse", "feature~1"));

        Assert.Equal(ChangeRefusals.Detached, check.Refusal);
    }

    [Fact]
    public void Check_refuses_during_a_merge()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        repo.Git.Run("merge", "--no-edit", "feature");

        var check = CheckPick(repo, repo.Run("rev-parse", "feature"));

        Assert.Equal("a merge is in progress: finish or abort it first", check.Refusal);
    }

    [Fact]
    public void Check_refuses_a_merge_commit()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Run("branch", "other");
        repo.Run("merge", "--no-edit", "feature");
        var merge = repo.Run("rev-parse", "HEAD");
        repo.Run("switch", "-q", "other");

        var check = CheckPick(repo, merge);

        Assert.Equal($"{merge[..7]} is a merge commit: graftui won't pick it", check.Refusal);
    }

    [Fact]
    public void Check_refuses_when_nothing_is_selected()
    {
        using var repo = SequenceRepos.Picks();

        var check = CheckPick(repo);

        Assert.Equal("select a commit to pick first", check.Refusal);
    }

    [Fact]
    public void Check_refuses_a_commit_that_does_not_exist()
    {
        using var repo = SequenceRepos.Picks();
        const string missing = "0123456789abcdef0123456789abcdef01234567";

        var check = CheckPick(repo, missing);

        Assert.Equal($"there is no commit {missing}", check.Refusal);
    }

    [Fact]
    public void A_conflict_pauses_after_the_first_pick_landed()
    {
        using var repo = SequenceRepos.PickConflict();
        var oldTip = repo.Head;
        var login = repo.Run("rev-parse", "feature~1");
        var fix = repo.Run("rev-parse", "feature");

        var outcome = Pick(repo, login, fix);

        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        Assert.NotNull(outcome.Stop);
        Assert.Equal(fix, outcome.Stop.Commit);
        Assert.Equal(2, outcome.Stop.Step);
        Assert.Equal(2, outcome.Stop.Total);
        Assert.Equal(oldTip, repo.Run("rev-parse", "HEAD^"));
        Assert.True(StateFileExists(repo));
    }

    [Fact]
    public void Continue_after_resolving_finishes_with_one_undo_step()
    {
        using var repo = SequenceRepos.PickConflict();
        var oldTip = repo.Head;
        var paused = Pick(repo, repo.Run("rev-parse", "feature~1"), repo.Run("rev-parse", "feature"));
        Assert.Equal(SequenceOutcomeKind.Paused, paused.Kind);
        repo.Write("auth.ts", "resolved\n");

        var outcome = Resume(repo, new ResumeRequest(ResumeAction.Continue));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal(new[] { "auth.ts" }, outcome.Resolved);
        Assert.NotNull(outcome.Undo);
        Assert.Equal("cherry-pick of 2 commits", outcome.Undo.What);
        Assert.False(StateFileExists(repo));
        Assert.False(PickHeadExists(repo));

        var undo = new UndoOperation(repo.Git);
        var undoCheck = undo.Check(new UndoRequest(null));
        Assert.True(undoCheck.CanRun, undoCheck.Refusal);
        var undone = undo.Run(new UndoRequest(undoCheck.StepRef), undoCheck);

        Assert.Equal(UndoOutcomeKind.Undone, undone.Kind);
        Assert.Equal(oldTip, repo.Head);
    }

    [Fact]
    public void Skip_keeps_the_rest()
    {
        using var repo = SequenceRepos.PickConflict();
        var oldTip = repo.Head;
        var fix = repo.Run("rev-parse", "feature");
        var paused = Pick(repo, repo.Run("rev-parse", "feature~1"), fix);
        Assert.Equal(SequenceOutcomeKind.Paused, paused.Kind);

        var outcome = Resume(repo, new ResumeRequest(ResumeAction.Skip));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal(new[] { fix }, outcome.Skipped);
        Assert.Equal(new[] { "Add login screen" }, repo.Run("log", oldTip + "..HEAD", "--format=%s").Split('\n'));
        Assert.Contains(outcome.Moves, m => m.Old == fix && m.New is null);
        Assert.False(StateFileExists(repo));
        Assert.False(PickHeadExists(repo));
    }

    [Fact]
    public void Abort_removes_every_copy()
    {
        using var repo = SequenceRepos.PickConflict();
        var oldTip = repo.Head;
        var paused = Pick(repo, repo.Run("rev-parse", "feature~1"), repo.Run("rev-parse", "feature"));
        Assert.Equal(SequenceOutcomeKind.Paused, paused.Kind);

        var outcome = Resume(repo, new ResumeRequest(ResumeAction.Abort));

        Assert.Equal(SequenceOutcomeKind.Aborted, outcome.Kind);
        Assert.Equal("cherry-pick aborted", outcome.Message);
        Assert.Equal(oldTip, repo.Head);
        Assert.Equal("refs/heads/main", repo.Run("symbolic-ref", "-q", "HEAD"));
        Assert.False(PickHeadExists(repo));
        Assert.False(StateFileExists(repo));
        Assert.True(repo.IsClean);
    }

    [Fact]
    public void A_single_pick_conflict_pause_keeps_the_old_tip_in_the_state_file()
    {
        using var repo = SequenceRepos.PickConflict();
        var oldTip = repo.Head;
        var fix = repo.Run("rev-parse", "feature");
        var paused = Pick(repo, fix);
        Assert.Equal(SequenceOutcomeKind.Paused, paused.Kind);
        var gitDir = Path.Combine(repo.Dir, ".git");
        Assert.False(Directory.Exists(Path.Combine(gitDir, "sequencer")));

        var stop = new SequenceReader(repo.Git).ReadPaused();

        Assert.NotNull(stop);
        Assert.True(stop.Ours);
        Assert.Equal(fix, stop.Commit);
        var state = SequenceStateFile.Load(gitDir);
        Assert.NotNull(state);
        Assert.Equal(oldTip, state.OldTip);

        var outcome = Resume(repo, new ResumeRequest(ResumeAction.Abort));

        Assert.Equal(SequenceOutcomeKind.Aborted, outcome.Kind);
        Assert.Equal(oldTip, repo.Head);
    }

    [Fact]
    public void An_empty_pick_the_check_missed_is_skipped_and_reported()
    {
        // The two changes differ (main's also adds z.txt), so the patch check does not see them as the same. Once picked, the
        // change to a.txt is already in the tree and the pick comes out empty.
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "1\n", "Add a");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("a.txt", "2\n", "Change a on feature");
        var pick = repo.Run("rev-parse", "HEAD");
        repo.Run("switch", "-q", "main");
        repo.Write("a.txt", "2\n");
        repo.Write("z.txt", "z\n");
        repo.Run("add", "-A");
        repo.Run("commit", "-q", "-m", "Change a and add z on main");
        var oldTip = repo.Head;

        var outcome = Pick(repo, pick);

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal(new[] { pick }, outcome.AlreadyApplied);
        Assert.Equal(oldTip, repo.Head);
        Assert.Null(outcome.Undo);
        Assert.False(PickHeadExists(repo));
        Assert.False(StateFileExists(repo));
    }

    [Fact]
    public void Undo_removes_all_copies_in_one_step()
    {
        using var repo = SequenceRepos.Picks();
        var oldTip = repo.Head;
        var outcome = Pick(repo, repo.Run("rev-parse", "feature~2"), repo.Run("rev-parse", "feature"));
        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);

        var undo = new UndoOperation(repo.Git);
        var undoCheck = undo.Check(new UndoRequest(null));
        Assert.True(undoCheck.CanRun, undoCheck.Refusal);
        var undone = undo.Run(new UndoRequest(undoCheck.StepRef), undoCheck);

        Assert.Equal(UndoOutcomeKind.Undone, undone.Kind);
        Assert.Contains("cherry-pick of 2 commits", undone.Caption);
        Assert.Equal(oldTip, repo.Head);
    }
}
