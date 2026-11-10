using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// Where a paused rebase stops, in throwaway repos. A squash group split from its target by a drop still pauses as a squash on its target
/// (never as a conflict), a stop graftui cannot place in the plan is unreadable rather than shown as step 0, and an unreadable
/// stop still lets the user abort the rebase. A held cherry-pick that is not one of the picks is unreadable too.
/// Feature4 is main + four feature commits (rows 0..3 = Add login form, Fix typo in form, Add password check, WIP debug logging).
/// TypoLogVersion is the user's own picks (rows 0..2 = Fix typo in README, Add logging, Bump version to 0.3).
/// PickConflict is the cherry-pick fixture (feature picks: 0 Add login screen, 1 Fix null check in auth, which conflicts with main).
/// </summary>
public class RebaseStopPlacementTests
{
    /// <summary>The rows of the default plan (every row picked), as the rebase screen would list them.</summary>
    private static IReadOnlyList<RebaseRow> RowsOf(TempRepo repo) =>
        new RebaseOperation(repo.Git).Check(new RebaseRequest("main")).Rows;

    /// <summary>The rows in plan order, each with the action given for its index in the default plan.</summary>
    private static List<RebaseRow> Plan(IReadOnlyList<RebaseRow> rows, params (int Index, RebaseAction Action)[] steps) =>
        steps.Select(s => rows[s.Index] with { Action = s.Action }).ToList();

    /// <summary>Check, then Run, for a plan that the check must allow.</summary>
    private static SequenceOutcome Run(TempRepo repo, IReadOnlyList<RebaseRow> plan)
    {
        var op = new RebaseOperation(repo.Git);
        var check = op.Check(new RebaseRequest("main", plan));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new RebaseRequest("main", plan), check);
    }

    /// <summary>Continues a squash pause with the given message, as the pause screen does.</summary>
    private static SequenceOutcome Continue(TempRepo repo, SequenceStop stop, string message)
    {
        var op = new ResumeSequenceOperation(repo.Git);
        var request = new ResumeRequest(ResumeAction.Message, message, stop.Commit);
        return op.Run(request, op.Check(request));
    }

    /// <summary>The subjects of the rebased commits (main..feature), oldest first, joined with '|'.</summary>
    private static string Subjects(TempRepo repo) => repo.Run("log", "--reverse", "--format=%s", "main..feature").Replace('\n', '|');

    [Fact]
    public void A_drop_between_a_squash_and_its_target_pauses_on_the_squash_and_finishes_with_the_message()
    {
        using var repo = SequenceRepos.Feature4();
        var rows = RowsOf(repo);

        var paused = Run(repo, Plan(rows, (0, RebaseAction.Pick), (1, RebaseAction.Drop), (2, RebaseAction.Squash), (3, RebaseAction.Pick)));

        Assert.Equal(SequenceOutcomeKind.Paused, paused.Kind);
        Assert.Equal(PauseKind.Squash, paused.Stop!.Pause);
        Assert.Equal(rows[0].Hash, paused.Stop.Commit);
        Assert.Equal(1, paused.Stop.Step);
        Assert.Equal(4, paused.Stop.Total);
        Assert.Equal(new[] { rows[2].Hash }, paused.Stop.Folded);
        Assert.Equal("Add login form\n\nAdd password check", paused.Stop.Message);

        var done = Continue(repo, paused.Stop, paused.Stop.Message);

        Assert.Equal(SequenceOutcomeKind.Done, done.Kind);
        Assert.Equal("Add login form|WIP debug logging", Subjects(repo));
        Assert.True(repo.IsClean);
    }

    [Fact]
    public void A_fixup_only_group_split_by_a_drop_writes_no_break_so_the_rebase_finishes_without_a_pause()
    {
        using var repo = SequenceRepos.Feature4();
        var rows = RowsOf(repo);

        var done = Run(repo, Plan(rows, (0, RebaseAction.Pick), (1, RebaseAction.Drop), (2, RebaseAction.Fixup), (3, RebaseAction.Pick)));

        Assert.Equal(SequenceOutcomeKind.Done, done.Kind);
        Assert.Equal("Add login form|WIP debug logging", Subjects(repo));
        Assert.True(repo.IsClean);
    }

    [Fact]
    public void Two_squash_groups_split_by_a_drop_each_pause_on_their_own_squash()
    {
        using var repo = SequenceRepos.Feature4();
        var rows = RowsOf(repo);

        var first = Run(repo, Plan(rows, (0, RebaseAction.Pick), (1, RebaseAction.Squash), (2, RebaseAction.Drop), (3, RebaseAction.Squash)));

        Assert.Equal(SequenceOutcomeKind.Paused, first.Kind);
        Assert.Equal(PauseKind.Squash, first.Stop!.Pause);
        Assert.Equal(rows[0].Hash, first.Stop.Commit);
        Assert.Equal(new[] { rows[1].Hash }, first.Stop.Folded);

        var second = Continue(repo, first.Stop, "Add login form");

        Assert.Equal(SequenceOutcomeKind.Paused, second.Kind);
        Assert.Equal(PauseKind.Squash, second.Stop!.Pause);
        Assert.Equal(rows[0].Hash, second.Stop.Commit);
        Assert.Equal(1, second.Stop.Step);
        Assert.Equal(new[] { rows[3].Hash }, second.Stop.Folded);
        Assert.Equal("Add login form\n\nWIP debug logging", second.Stop.Message);

        var done = Continue(repo, second.Stop, "Add login form");

        Assert.Equal(SequenceOutcomeKind.Done, done.Kind);
        Assert.Equal("Add login form", Subjects(repo));
    }

    [Fact]
    public void The_users_picks_reordered_with_a_drop_and_a_squash_pause_on_the_squash_not_a_conflict()
    {
        using var repo = SequenceRepos.TypoLogVersion();
        var rows = RowsOf(repo);

        // Add logging first, the typo fix dropped, the version bump folded into Add logging.
        var paused = Run(repo, Plan(rows, (1, RebaseAction.Pick), (0, RebaseAction.Drop), (2, RebaseAction.Squash)));

        Assert.Equal(SequenceOutcomeKind.Paused, paused.Kind);
        Assert.Equal(PauseKind.Squash, paused.Stop!.Pause);
        Assert.Equal(rows[1].Hash, paused.Stop.Commit);
        Assert.Equal(1, paused.Stop.Step);
        Assert.Equal(3, paused.Stop.Total);
        Assert.Equal("Add logging\n\nBump version to 0.3", paused.Stop.Message);
    }

    [Fact]
    public void A_break_with_no_fold_group_before_it_is_unreadable_and_never_a_conflict()
    {
        using var repo = SequenceRepos.Feature4();
        var rows = RowsOf(repo);
        var state = new SequenceState(1, SequenceKind.Rebase, "feature", "main", repo.Head, rows, [], [], [], [], [], []);
        // git stops at a break that no fold group leads to: graftui cannot say what it waits for.
        SequenceRepos.RebaseByHand(repo, "main", "pick " + rows[0].Hash, "break", "pick " + rows[1].Hash, "pick " + rows[2].Hash, "pick " + rows[3].Hash);
        var gitDir = Path.Combine(repo.Dir, ".git");

        var stop = SequenceGit.ReadRebaseStop(repo.Git, gitDir, state, []);

        Assert.Null(stop);
    }

    [Fact]
    public void An_unreadable_break_fails_as_unreadable_and_the_user_can_still_abort_the_rebase()
    {
        using var repo = SequenceRepos.Feature4();
        var rows = RowsOf(repo);
        var before = repo.Head;
        var gitDir = Path.Combine(repo.Dir, ".git");
        var state = new SequenceState(1, SequenceKind.Rebase, "feature", "main", before, rows, [], [], [], [], [], []);
        var started = SequenceRepos.RebaseByHand(repo, "main", "pick " + rows[0].Hash, "break", "pick " + rows[1].Hash, "pick " + rows[2].Hash, "pick " + rows[3].Hash);
        Assert.True(started.Success, started.StdErr);
        Assert.Null(SequenceStateFile.Save(gitDir, state));

        var outcome = SequenceGit.Settle(repo.Git, gitDir, state, started, []);

        Assert.Equal(SequenceOutcomeKind.Failed, outcome.Kind);
        Assert.Contains("could not read the paused rebase", outcome.Message);
        Assert.True(SequenceGit.RebaseInProgress(gitDir));
        Assert.True(File.Exists(Path.Combine(gitDir, SequenceStateFile.FileName)));

        var op = new ResumeSequenceOperation(repo.Git);
        var request = new ResumeRequest(ResumeAction.Abort);
        var aborted = op.Run(request, op.Check(request));

        Assert.Equal(SequenceOutcomeKind.Aborted, aborted.Kind);
        Assert.Equal(before, repo.Head);
        Assert.False(SequenceGit.RebaseInProgress(gitDir));
        Assert.False(File.Exists(Path.Combine(gitDir, SequenceStateFile.FileName)));
    }

    [Fact]
    public void A_held_cherry_pick_that_is_not_one_of_the_picks_is_unreadable()
    {
        using var repo = SequenceRepos.PickConflict();
        var login = repo.Run("rev-parse", "feature~1");
        var fix = repo.Run("rev-parse", "feature");
        // git holds the conflicting fix, but graftui's picks are only the login commit: the held commit has no place in the sequence.
        var state = new SequenceState(1, SequenceKind.CherryPick, "main", "main", repo.Head, [], [login], [], [], [], [], []);
        Assert.False(repo.Git.Run("cherry-pick", fix).Success);
        Assert.True(File.Exists(Path.Combine(repo.Dir, ".git", "CHERRY_PICK_HEAD")));

        var stop = SequenceGit.ReadPickStop(repo.Git, state, []);

        Assert.Null(stop);
    }
}
