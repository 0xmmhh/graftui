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
}
