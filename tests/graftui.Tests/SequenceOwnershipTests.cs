using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// Which sequence graftui started. A state file can outlive its sequence (the user finishes a paused rebase with plain git), so a sequence
/// git starts later must not be shown as graftui's, and must be refused by resume and abort. Each case runs in a throwaway repo.
/// </summary>
public class SequenceOwnershipTests
{
    private static string GitDir(TempRepo repo) => Path.Combine(repo.Dir, ".git");

    private static string StatePath(TempRepo repo) => Path.Combine(GitDir(repo), SequenceStateFile.FileName);

    /// <summary>Starts the conflicting rebase of feature onto main with graftui, and asserts that it paused.</summary>
    private static void PauseOnConflict(TempRepo repo)
    {
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(request, check).Kind);
    }

    [Fact]
    public void A_rebase_git_starts_by_hand_after_a_graftui_pause_was_aborted_is_not_ours_and_cannot_be_resumed_or_aborted()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var before = repo.Head;
        PauseOnConflict(repo);
        repo.Run("rebase", "--abort");
        Assert.Equal(before, repo.Head);
        // A new commit moves the tip, so the folder git keeps no longer matches the state graftui saved.
        repo.Commit("app.txt", "feature again\n", "Change line 1 again");

        var byHand = repo.Git.Run("rebase", "main");

        Assert.False(byHand.Success);
        Assert.True(SequenceGit.RebaseInProgress(GitDir(repo)));
        var stop = new SequenceReader(repo.Git).ReadPaused();
        Assert.NotNull(stop);
        Assert.False(stop.Ours);

        var resume = new ResumeSequenceOperation(repo.Git);
        var request = new ResumeRequest(ResumeAction.Continue);
        var check = resume.Check(request);
        Assert.False(check.CanRun);
        Assert.Equal(ChangeRefusals.InProgress("rebase"), check.Refusal);
        var refused = resume.Run(request, check);
        Assert.Equal(SequenceOutcomeKind.Refused, refused.Kind);

        var abort = resume.Check(new ResumeRequest(ResumeAction.Abort));
        Assert.False(abort.CanRun);
        Assert.Equal(ChangeRefusals.InProgress("rebase"), abort.Refusal);
        Assert.True(SequenceGit.RebaseInProgress(GitDir(repo)));
    }

    [Fact]
    public void Graftui_removes_a_stale_state_when_its_own_rebase_is_checked_so_the_next_rebase_by_hand_is_not_ours()
    {
        using var repo = SequenceRepos.FeatureConflict();
        PauseOnConflict(repo);
        repo.Run("rebase", "--abort");
        // Same branch and same tip as the aborted pause: only the stale state tells them apart.
        Assert.True(File.Exists(StatePath(repo)));

        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));

        Assert.True(check.CanRun, check.Refusal);
        Assert.False(File.Exists(StatePath(repo)));
        Assert.False(repo.Git.Run("rebase", "main").Success);
        var stop = new SequenceReader(repo.Git).ReadPaused();
        Assert.NotNull(stop);
        Assert.False(stop.Ours);
    }

    [Fact]
    public void A_pause_whose_git_folder_names_another_branch_is_not_ours()
    {
        using var repo = SequenceRepos.FeatureConflict();
        PauseOnConflict(repo);
        File.WriteAllText(Path.Combine(GitDir(repo), "rebase-merge", "head-name"), "refs/heads/main\n");

        var stop = new SequenceReader(repo.Git).ReadPaused();

        Assert.NotNull(stop);
        Assert.False(stop.Ours);
        Assert.False(new ResumeSequenceOperation(repo.Git).Check(new ResumeRequest(ResumeAction.Abort)).CanRun);
    }

    [Fact]
    public void A_pause_whose_git_folder_started_from_another_tip_is_not_ours()
    {
        using var repo = SequenceRepos.FeatureConflict();
        PauseOnConflict(repo);
        File.WriteAllText(Path.Combine(GitDir(repo), "rebase-merge", "orig-head"), new string('0', 40) + "\n");

        var stop = new SequenceReader(repo.Git).ReadPaused();

        Assert.NotNull(stop);
        Assert.False(stop.Ours);
    }

    [Fact]
    public void A_cherry_pick_git_holds_that_graftui_did_not_pick_is_not_ours()
    {
        using var repo = SequenceRepos.PickConflict();
        var fix = repo.Run("rev-parse", "feature");
        var check = new CherryPickOperation(repo.Git).Check(new CherryPickRequest([fix]));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, new CherryPickOperation(repo.Git).Run(new CherryPickRequest([fix]), check).Kind);
        // CHERRY_PICK_HEAD names the branch's own tip, which is not one of the picks graftui started.
        File.WriteAllText(Path.Combine(GitDir(repo), "CHERRY_PICK_HEAD"), repo.Head + "\n");

        var stop = new SequenceReader(repo.Git).ReadPaused();

        Assert.NotNull(stop);
        Assert.False(stop.Ours);
    }

    [Fact]
    public void A_state_file_is_never_ours_when_git_holds_no_sequence()
    {
        using var repo = SequenceRepos.FeatureConflict();
        PauseOnConflict(repo);

        Assert.Null(SequenceOwnership.Owned(GitDir(repo), null));
        Assert.True(File.Exists(StatePath(repo)));
    }

    [Fact]
    public void An_empty_git_dir_is_no_repo_so_nothing_is_in_progress_there()
    {
        Assert.False(SequenceGit.RebaseInProgress(""));
        Assert.False(SequenceGit.PickInProgress(""));
        Assert.Null(SequenceOwnership.Owned("", "rebase"));
    }

    /// <summary>The commits of the feature branch that main does not have, oldest first.</summary>
    private static string[] FeatureCommits(TempRepo repo) =>
        repo.Run("rev-list", "--reverse", "main..feature").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Starts the picks of the given commits onto the current branch, and asserts that the first one paused.</summary>
    private static void PausePick(TempRepo repo, params string[] commits)
    {
        var op = new CherryPickOperation(repo.Git);
        var request = new CherryPickRequest(commits);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(request, check).Kind);
    }

    /// <summary>Resolves the conflicted file and commits it by hand, the way a user finishes a pick without graftui.</summary>
    private static void ResolveByPlainCommit(TempRepo repo)
    {
        repo.Write("auth.ts", "auth resolved\n");
        repo.Run("add", "auth.ts");
        repo.Run("commit", "-q", "-m", "Fix null check in auth");
    }

    [Fact]
    public void A_multi_commit_pick_finished_by_a_plain_commit_is_still_a_cherry_pick_in_progress_and_ours()
    {
        using var repo = SequenceRepos.PickConflictThenMore();
        PausePick(repo, FeatureCommits(repo));
        ResolveByPlainCommit(repo);

        // CHERRY_PICK_HEAD is gone, but git keeps the sequencer folder for the picks that are left.
        Assert.False(File.Exists(Path.Combine(GitDir(repo), "CHERRY_PICK_HEAD")));
        Assert.True(Directory.Exists(Path.Combine(GitDir(repo), "sequencer")));
        Assert.Equal("cherry-pick", RepoState.InProgress(repo.Git.Run, GitDir(repo)));
        Assert.NotNull(SequenceOwnership.Owned(GitDir(repo), "cherry-pick"));

        // Between two picks there is no stop to show, so the sequence is in progress but unreadable.
        var read = new SequenceReader(repo.Git).ReadSequence();
        Assert.Null(read.Stop);
        Assert.Null(read.OldTip);
        Assert.Equal("cherry-pick", read.Unreadable);
    }

    [Fact]
    public void While_that_sequencer_is_left_a_rebase_or_a_pick_is_refused_and_the_state_file_stays()
    {
        using var repo = SequenceRepos.PickConflictThenMore();
        PausePick(repo, FeatureCommits(repo));
        ResolveByPlainCommit(repo);
        var pick = repo.Run("rev-parse", "feature~1");

        var rebase = new RebaseOperation(repo.Git).Check(new RebaseRequest("feature"));
        Assert.False(rebase.CanRun);
        Assert.Equal(ChangeRefusals.InProgress("cherry-pick"), rebase.Refusal);

        var cherry = new CherryPickOperation(repo.Git).Check(new CherryPickRequest([pick]));
        Assert.False(cherry.CanRun);
        Assert.Equal(ChangeRefusals.InProgress("cherry-pick"), cherry.Refusal);

        var resume = new ResumeSequenceOperation(repo.Git).Check(new ResumeRequest(ResumeAction.Continue));
        Assert.False(resume.CanRun);
        Assert.Equal(ChangeRefusals.InProgress("cherry-pick"), resume.Refusal);

        Assert.True(File.Exists(StatePath(repo)));
        Assert.True(Directory.Exists(Path.Combine(GitDir(repo), "sequencer")));
    }

    [Fact]
    public void A_sequencer_that_started_from_another_tip_is_not_ours_and_the_next_check_keeps_its_state()
    {
        using var repo = SequenceRepos.PickConflictThenMore();
        PausePick(repo, FeatureCommits(repo));
        ResolveByPlainCommit(repo);
        File.WriteAllText(Path.Combine(GitDir(repo), "sequencer", "head"), new string('1', 40) + "\n");

        Assert.Null(SequenceOwnership.Owned(GitDir(repo), "cherry-pick"));
        var read = new SequenceReader(repo.Git).ReadSequence();
        Assert.NotNull(read.Stop);
        Assert.False(read.Stop.Ours);

        // Not graftui's, so the check refuses it as a sequence in progress and leaves the state file alone.
        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("feature"));
        Assert.Equal(ChangeRefusals.InProgress("cherry-pick"), check.Refusal);
        Assert.True(File.Exists(StatePath(repo)));
    }

    [Fact]
    public void A_held_pick_is_not_ours_when_the_sequencer_started_from_another_tip()
    {
        using var repo = SequenceRepos.PickConflictThenMore();
        PausePick(repo, FeatureCommits(repo));
        Assert.True(File.Exists(Path.Combine(GitDir(repo), "CHERRY_PICK_HEAD")));
        File.WriteAllText(Path.Combine(GitDir(repo), "sequencer", "head"), new string('1', 40) + "\n");

        Assert.Null(SequenceOwnership.Owned(GitDir(repo), "cherry-pick"));
    }

    [Fact]
    public void A_rebase_stays_owned_across_an_edit_stop_and_an_amend()
    {
        using var repo = SequenceRepos.Feature4();
        var op = new RebaseOperation(repo.Git);
        var rows = op.Check(new RebaseRequest("main")).Rows;
        var plan = rows.Select((row, i) => i == 1 ? row with { Action = RebaseAction.Edit } : row).ToList();
        var request = new RebaseRequest("main", plan);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(request, check);
        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        Assert.Equal(PauseKind.Edit, outcome.Stop!.Pause);

        // The amend moves HEAD, but git's rebase folder still starts from the old tip, which is what ownership reads.
        repo.Write("f2.txt", "2 amended\n");
        repo.Run("add", "f2.txt");
        repo.Run("commit", "-q", "--amend", "--no-edit");

        Assert.NotNull(SequenceOwnership.Owned(GitDir(repo), "rebase"));
        Assert.True(new SequenceReader(repo.Git).ReadPaused()!.Ours);
    }

    [Fact]
    public void A_rebase_stays_owned_after_a_skip_onto_the_next_conflict()
    {
        using var repo = SequenceRepos.SkipStopsOnNextConflict();
        PauseOnConflict(repo);
        var resume = new ResumeSequenceOperation(repo.Git);
        var request = new ResumeRequest(ResumeAction.Skip);
        var check = resume.Check(request);
        Assert.True(check.CanRun, check.Refusal);

        var outcome = resume.Run(request, check);

        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        Assert.NotNull(SequenceOwnership.Owned(GitDir(repo), "rebase"));
    }

    [Fact]
    public void A_cherry_pick_stays_owned_after_a_skip_onto_the_next_conflict()
    {
        using var repo = SequenceRepos.SkipStopsOnNextConflict();
        repo.Run("switch", "-q", "main");
        PausePick(repo, FeatureCommits(repo));
        var resume = new ResumeSequenceOperation(repo.Git);
        var request = new ResumeRequest(ResumeAction.Skip);
        var check = resume.Check(request);
        Assert.True(check.CanRun, check.Refusal);

        var outcome = resume.Run(request, check);

        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        Assert.NotNull(SequenceOwnership.Owned(GitDir(repo), "cherry-pick"));
    }

    [Fact]
    public void A_leftover_state_then_a_plain_rebase_to_done_leaves_no_state_file()
    {
        using var repo = SequenceRepos.FeatureConflict();
        PauseOnConflict(repo);
        repo.Run("rebase", "--abort");
        Assert.True(File.Exists(StatePath(repo)));
        repo.Run("switch", "-q", "-c", "clean", repo.Run("rev-list", "--max-parents=0", "HEAD"));
        repo.Commit("b.txt", "b\n", "Add b");

        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(request, check);

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.False(File.Exists(StatePath(repo)));
        Assert.False(SequenceGit.RebaseInProgress(GitDir(repo)));
    }

    [Fact]
    public void A_leftover_pick_state_then_a_plain_pick_to_done_leaves_no_state_file()
    {
        using var repo = SequenceRepos.PickConflict();
        PausePick(repo, repo.Run("rev-parse", "feature"));
        repo.Run("cherry-pick", "--abort");
        Assert.True(File.Exists(StatePath(repo)));
        var login = repo.Run("rev-parse", "feature~1");

        var op = new CherryPickOperation(repo.Git);
        var request = new CherryPickRequest([login]);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(request, check);

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.False(File.Exists(StatePath(repo)));
    }
}
