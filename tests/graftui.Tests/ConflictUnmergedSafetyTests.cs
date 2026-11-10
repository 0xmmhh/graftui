using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// A paused rebase or cherry-pick whose conflicted files have no markers (a binary file, or a file one side deleted). The question about
/// them, the answer that the engine checks, and what each answer does in git. Throwaway repos only.
/// </summary>
public class ConflictUnmergedSafetyTests
{
    private static SequenceOutcome PauseRebase(TempRepo repo)
    {
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
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
        return op.Run(request, op.Check(request));
    }

    private static bool RebaseInProgress(TempRepo repo) => SequenceGit.RebaseInProgress(Path.Combine(repo.Dir, ".git"));

    private static bool PickInProgress(TempRepo repo) => File.Exists(Path.Combine(repo.Dir, ".git", "CHERRY_PICK_HEAD"));

    [Fact]
    public void A_binary_conflict_refuses_continue_and_asks_which_version_to_keep()
    {
        using var repo = SequenceRepos.BinaryConflict();
        var stop = PauseRebase(repo).Stop!;

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.False(check.CanRun);
        Assert.Equal("logo.bin has no conflict markers: say which version to keep before continuing", check.Refusal);
        Assert.Equal(new[] { new KeptFile("logo.bin", ConflictKind.Binary, TreeSide.Ours) }, check.Keep);
        Assert.True(RebaseInProgress(repo));
    }

    [Fact]
    public void Keeping_the_tree_version_of_a_binary_file_continues_and_git_drops_the_empty_commit()
    {
        using var repo = SequenceRepos.BinaryConflict();
        var stop = PauseRebase(repo).Stop!;
        var keep = new[] { new KeptFile("logo.bin", ConflictKind.Binary, TreeSide.Ours) };

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit, keep));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal(repo.Run("rev-parse", "main"), repo.Run("rev-parse", "feature"));
        Assert.True(repo.IsClean);
        Assert.False(RebaseInProgress(repo));
    }

    [Fact]
    public void A_version_copied_in_before_the_answer_is_the_one_the_question_names_and_keeps()
    {
        using var repo = SequenceRepos.BinaryConflict();
        var stop = PauseRebase(repo).Stop!;
        var featureBlob = repo.Run("rev-parse", "feature:logo.bin");
        Assert.True(repo.Git.Run("checkout", "--theirs", "--", "logo.bin").Success);

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));
        Assert.Equal(new[] { new KeptFile("logo.bin", ConflictKind.Binary, TreeSide.Theirs) }, check.Keep);

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit, check.Keep));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal("Feature logo", repo.Run("log", "-1", "--format=%s", "feature"));
        Assert.Equal(featureBlob, repo.Run("rev-parse", "feature:logo.bin"));
    }

    [Fact]
    public void A_yes_given_before_the_file_changed_is_refused_and_the_pause_stays()
    {
        using var repo = SequenceRepos.BinaryConflict();
        var stop = PauseRebase(repo).Stop!;
        var asked = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit)).Keep;
        File.WriteAllBytes(Path.Combine(repo.Dir, "logo.bin"), [0x65, 0x64, 0x69, 0x74, 0, 9]);

        var request = new ResumeRequest(ResumeAction.Continue, null, stop.Commit, asked);
        var check = CheckResume(repo, request);

        Assert.False(check.CanRun);
        Assert.Equal("logo.bin has no conflict markers: say which version to keep before continuing", check.Refusal);
        Assert.Equal(new[] { new KeptFile("logo.bin", ConflictKind.Binary, TreeSide.Edited) }, check.Keep);
        Assert.Equal(SequenceOutcomeKind.Refused, RunResume(repo, request).Kind);
        Assert.True(RebaseInProgress(repo));
        Assert.Contains("logo.bin", repo.Run("ls-files", "-u"));
    }

    [Fact]
    public void A_deleted_on_main_file_is_kept_as_feature_edited_it()
    {
        using var repo = SequenceRepos.ModifyDelete();
        var stop = PauseRebase(repo).Stop!;
        Assert.Equal(ConflictKind.DeletedOurs, stop.Files.Single().Kind);

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));
        Assert.Equal(new[] { new KeptFile("gone.txt", ConflictKind.DeletedOurs, TreeSide.Theirs) }, check.Keep);

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit, check.Keep));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal("feature\n", File.ReadAllText(Path.Combine(repo.Dir, "gone.txt")));
        Assert.Equal("Edit gone", repo.Run("log", "-1", "--format=%s", "feature"));
        Assert.True(repo.IsClean);
    }

    [Fact]
    public void A_file_deleted_on_feature_keeps_main_s_version_and_a_file_removed_by_hand_is_removed_with_git_rm()
    {
        using var repo = SequenceRepos.DeleteModify();
        var stop = PauseRebase(repo).Stop!;
        Assert.Equal(ConflictKind.DeletedTheirs, stop.Files.Single().Kind);

        var first = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));
        Assert.Equal(new[] { new KeptFile("gone.txt", ConflictKind.DeletedTheirs, TreeSide.Ours) }, first.Keep);

        File.Delete(Path.Combine(repo.Dir, "gone.txt"));
        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));
        Assert.Equal(new[] { new KeptFile("gone.txt", ConflictKind.DeletedTheirs, TreeSide.Deleted) }, check.Keep);

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit, check.Keep));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal("", repo.Run("ls-tree", "feature", "--", "gone.txt"));
        Assert.Contains(outcome.Commands, c => c.Args.Contains("rm"));
    }

    [Fact]
    public void A_separator_line_that_is_not_a_marker_no_longer_blocks_continue()
    {
        using var repo = SequenceRepos.FeatureConflict();
        PauseRebase(repo);
        repo.Write("app.txt", "top\n=======\nbottom\n");

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Contains("=======", repo.Run("show", "feature:app.txt"));
    }

    [Fact]
    public void Files_with_markers_are_refused_before_any_question_about_the_others()
    {
        using var repo = SequenceRepos.BinaryAndTextConflict();
        var stop = PauseRebase(repo).Stop!;

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.Equal("app.txt still has conflict markers: resolve them before continuing", check.Refusal);
        Assert.Null(check.Keep);

        repo.Write("app.txt", "resolved\n");
        var next = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.Equal(new[] { new KeptFile("logo.bin", ConflictKind.Binary, TreeSide.Ours) }, next.Keep);
    }

    [Fact]
    public void A_file_staged_outside_graftui_is_not_asked_about_and_the_rebase_continues()
    {
        using var repo = SequenceRepos.BinaryConflict();
        PauseRebase(repo);
        repo.Run("add", "logo.bin");

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue));
        Assert.True(check.CanRun, check.Refusal);

        Assert.Equal(SequenceOutcomeKind.Done, RunResume(repo, new ResumeRequest(ResumeAction.Continue)).Kind);
        Assert.False(RebaseInProgress(repo));
    }

    [Fact]
    public void A_picked_binary_conflict_asks_for_main_s_version_and_names_the_branch()
    {
        using var repo = SequenceRepos.PickBinary();
        var feature = repo.Run("rev-parse", "feature");
        var pick = new CherryPickOperation(repo.Git);
        var request = new CherryPickRequest([feature]);
        var pickCheck = pick.Check(request);
        Assert.True(pickCheck.CanRun, pickCheck.Refusal);
        var paused = pick.Run(request, pickCheck);
        Assert.Equal(SequenceOutcomeKind.Paused, paused.Kind);
        var stop = paused.Stop!;

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.Equal(new[] { new KeptFile("logo.bin", ConflictKind.Binary, TreeSide.Ours) }, check.Keep);
        Assert.Equal("logo.bin has no conflict markers. The working tree holds main's version. Keep it and continue?",
            RebaseText.KeepText(stop, check.Keep!));

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit, check.Keep));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.False(PickInProgress(repo));
        Assert.True(repo.IsClean);
    }
}
