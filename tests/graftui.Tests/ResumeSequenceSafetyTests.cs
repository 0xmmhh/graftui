using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// ResumeSequenceOperation against a rebase that graftui paused: the refusals of each action (word for word), continue, skip, abort,
/// the reword and squash messages, the edit amend, and a fresh reader that finds the pause again.
/// Feature4 rows: 0 Add login form, 1 Fix typo in form, 2 Add password check, 3 WIP debug logging.
/// </summary>
public class ResumeSequenceSafetyTests
{
    /// <summary>Starts the rebase and asserts that it paused. Returns the pause outcome.</summary>
    private static SequenceOutcome Pause(TempRepo repo, IReadOnlyList<RebaseRow>? plan = null)
    {
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main", plan);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(request, check);
        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        return outcome;
    }

    private static ResumeCheck CheckResume(TempRepo repo, ResumeRequest request) => new ResumeSequenceOperation(repo.Git).Check(request);

    private static SequenceOutcome RunResume(TempRepo repo, ResumeRequest request)
    {
        var op = new ResumeSequenceOperation(repo.Git);
        var check = op.Check(request);
        return op.Run(request, check);
    }

    private static List<RebaseRow> PlanOf(RebaseCheck check, params (int Index, RebaseAction Action)[] steps) =>
        steps.Select(s => check.Rows[s.Index] with { Action = s.Action }).ToList();

    private static RebaseCheck DefaultCheck(TempRepo repo) => new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));

    private static bool StateFileExists(TempRepo repo) => File.Exists(Path.Combine(repo.Dir, ".git", SequenceStateFile.FileName));

    [Fact]
    public void Continue_is_refused_while_conflict_markers_remain_and_the_pause_stays()
    {
        using var repo = SequenceRepos.FeatureConflict();
        Pause(repo);

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue));

        Assert.False(check.CanRun);
        Assert.Equal("app.txt still has conflict markers: resolve them before continuing", check.Refusal);
        Assert.NotNull(new SequenceReader(repo.Git).ReadPaused());
    }

    [Fact]
    public void Continue_after_resolving_adds_the_file_and_finishes_with_one_undo_step()
    {
        using var repo = SequenceRepos.FeatureConflict();
        Pause(repo);
        repo.Write("app.txt", "resolved\n");

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal(new[] { "app.txt" }, outcome.Resolved);
        Assert.NotNull(outcome.Undo);
        Assert.False(StateFileExists(repo));
        Assert.Equal("resolved", repo.Run("show", "feature:app.txt"));
        Assert.DoesNotContain("<<<<<<<", repo.Run("show", "feature:app.txt"));
    }

    [Fact]
    public void Skip_drops_the_conflicting_commit_and_finishes()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var conflicting = repo.Run("rev-parse", "feature~1");
        Pause(repo);

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Skip));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal(new[] { conflicting }, outcome.Skipped);
        Assert.DoesNotContain("Add password check", repo.Run("log", "--format=%s", "main..feature").Split('\n'));
        Assert.Contains(outcome.Moves, m => m.Old == conflicting && m.New is null);
        Assert.False(StateFileExists(repo));
    }

    [Fact]
    public void Skip_that_stops_on_the_next_conflict_still_records_the_skip()
    {
        using var repo = SequenceRepos.SkipStopsOnNextConflict();
        var first = repo.Run("rev-parse", "feature~1");
        var second = repo.Run("rev-parse", "feature");
        Pause(repo);

        var skipped = RunResume(repo, new ResumeRequest(ResumeAction.Skip));

        Assert.Equal(SequenceOutcomeKind.Paused, skipped.Kind);
        Assert.Equal(second, skipped.Stop!.Commit);
        Assert.Contains(first, skipped.Skipped);

        var stored = SequenceStateFile.Load(Path.Combine(repo.Dir, ".git"));
        Assert.NotNull(stored);
        Assert.Contains(first, stored.Skipped);

        repo.Write("app.txt", "resolved\n");
        var done = RunResume(repo, new ResumeRequest(ResumeAction.Continue));

        Assert.Equal(SequenceOutcomeKind.Done, done.Kind);
        Assert.Contains(done.Moves, m => m.Old == first && m.New is null);
        Assert.False(StateFileExists(repo));
    }

    [Fact]
    public void Skip_is_refused_at_a_pause_that_is_not_a_conflict()
    {
        using var repo = SequenceRepos.Feature4();
        var check = DefaultCheck(repo);
        Pause(repo, PlanOf(check, (0, RebaseAction.Pick), (1, RebaseAction.Pick), (2, RebaseAction.Edit), (3, RebaseAction.Pick)));

        var refusal = CheckResume(repo, new ResumeRequest(ResumeAction.Skip)).Refusal;

        Assert.Equal("only a conflicted commit can be skipped", refusal);
    }

    [Fact]
    public void Abort_restores_the_old_tip_and_a_clean_tree()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        Pause(repo);

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Abort));

        Assert.Equal(SequenceOutcomeKind.Aborted, outcome.Kind);
        Assert.Equal(before, repo.Head);
        Assert.Equal("refs/heads/feature", repo.Run("symbolic-ref", "-q", "HEAD"));
        Assert.True(repo.IsClean);
        Assert.False(StateFileExists(repo));
        Assert.False(SequenceGit.RebaseInProgress(Path.Combine(repo.Dir, ".git")));
    }

    [Fact]
    public void A_rebase_graftui_did_not_start_is_refused_and_left_alone()
    {
        using var repo = SequenceRepos.Feature4();
        var head = repo.Head;
        var started = SequenceRepos.RebaseByHand(repo, "main", "edit " + repo.Run("rev-parse", "feature~3"));
        Assert.True(started.Success, started.StdErr);

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue));

        Assert.Equal(SequenceOutcomeKind.Refused, outcome.Kind);
        Assert.Equal("a rebase is in progress: finish or abort it first", outcome.Message);
        Assert.True(SequenceGit.RebaseInProgress(Path.Combine(repo.Dir, ".git")));
        Assert.False(StateFileExists(repo));

        repo.Git.Run("rebase", "--abort");
        Assert.Equal(head, repo.Head);
    }

    [Fact]
    public void Nothing_paused_is_refused()
    {
        using var repo = SequenceRepos.Feature4();

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue));

        Assert.Equal("nothing is paused: there is no rebase or cherry-pick to continue", check.Refusal);
    }

    [Fact]
    public void A_message_with_an_empty_subject_is_refused()
    {
        using var repo = SequenceRepos.Feature4();
        var check = DefaultCheck(repo);
        Pause(repo, PlanOf(check, (0, RebaseAction.Reword), (1, RebaseAction.Pick), (2, RebaseAction.Pick), (3, RebaseAction.Pick)));

        var refusal = CheckResume(repo, new ResumeRequest(ResumeAction.Message, "   \n\nbody only")).Refusal;

        Assert.Equal("Write a subject first: one short line saying what the commit does", refusal);
    }

    [Fact]
    public void Edit_continue_with_unstaged_changes_is_refused_and_names_the_file()
    {
        using var repo = SequenceRepos.Feature4();
        var check = DefaultCheck(repo);
        Pause(repo, PlanOf(check, (0, RebaseAction.Pick), (1, RebaseAction.Pick), (2, RebaseAction.Edit), (3, RebaseAction.Pick)));
        repo.Write("app.txt", "changed\n");

        var refusal = CheckResume(repo, new ResumeRequest(ResumeAction.Continue)).Refusal;

        Assert.Equal("app.txt has unstaged changes: stage it first, then Amend & continue", refusal);
    }

    [Fact]
    public void Continue_at_an_edit_stop_keeps_the_staged_change_in_the_commit()
    {
        using var repo = SequenceRepos.Feature4();
        var check = DefaultCheck(repo);
        var target = repo.Run("rev-parse", "feature~1");
        Pause(repo, PlanOf(check, (0, RebaseAction.Pick), (1, RebaseAction.Pick), (2, RebaseAction.Edit), (3, RebaseAction.Pick)));
        repo.Write("extra.txt", "extra\n");
        repo.Run("add", "extra.txt");

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Contains(target, outcome.Amended);
        Assert.Contains("extra.txt", repo.Run("ls-tree", "-r", "--name-only", "feature").Split('\n'));
    }

    [Fact]
    public void Amend_and_continue_at_an_edit_stop_puts_the_staged_file_into_the_commit()
    {
        using var repo = SequenceRepos.Feature4();
        var check = DefaultCheck(repo);
        var target = repo.Run("rev-parse", "feature~1");
        Pause(repo, PlanOf(check, (0, RebaseAction.Pick), (1, RebaseAction.Pick), (2, RebaseAction.Edit), (3, RebaseAction.Pick)));
        repo.Write("extra.txt", "extra\n");
        repo.Run("add", "extra.txt");

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.AmendAndContinue));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Contains(target, outcome.Amended);
        Assert.Contains("Add password check", repo.Run("log", "--format=%s", "main..feature").Split('\n'));
        Assert.Contains("extra.txt", repo.Run("ls-tree", "-r", "--name-only", "feature").Split('\n'));
    }

    [Fact]
    public void Squash_message_is_written_and_the_fold_finishes()
    {
        using var repo = SequenceRepos.Feature4();
        var check = DefaultCheck(repo);
        Pause(repo, PlanOf(check, (0, RebaseAction.Pick), (1, RebaseAction.Pick), (2, RebaseAction.Pick), (3, RebaseAction.Squash)));

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Message, "Add password check"));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal("Add password check", repo.Run("log", "-1", "--format=%s", "feature"));
        Assert.Contains("f4.txt", repo.Run("ls-tree", "-r", "--name-only", "feature").Split('\n'));
        Assert.False(StateFileExists(repo));
    }

    [Fact]
    public void Reword_message_is_written_and_the_commit_is_recorded_as_reworded()
    {
        using var repo = SequenceRepos.Feature4();
        var firstOriginal = repo.Run("rev-parse", "feature~3");
        var check = DefaultCheck(repo);
        Pause(repo, PlanOf(check, (0, RebaseAction.Reword), (1, RebaseAction.Pick), (2, RebaseAction.Pick), (3, RebaseAction.Pick)));

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Message, "Add login form with remember-me"));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Contains(firstOriginal, outcome.Reworded);
        Assert.Equal("Add login form with remember-me", repo.Run("log", "--reverse", "--format=%s", "main..feature").Split('\n')[0]);
        Assert.False(StateFileExists(repo));
    }

    [Fact]
    public void A_fresh_reader_finds_the_same_squash_pause_after_a_restart()
    {
        using var repo = SequenceRepos.Feature4();
        var check = DefaultCheck(repo);
        var target = repo.Run("rev-parse", "feature~1");
        Pause(repo, PlanOf(check, (0, RebaseAction.Pick), (1, RebaseAction.Pick), (2, RebaseAction.Pick), (3, RebaseAction.Squash)));

        var stop = new SequenceReader(new GitClient(repo.Dir)).ReadPaused();

        Assert.NotNull(stop);
        Assert.Equal(PauseKind.Squash, stop.Pause);
        Assert.Equal(target, stop.Commit);
        Assert.True(stop.Ours);
    }
}
