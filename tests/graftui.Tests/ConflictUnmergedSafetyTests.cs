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
    public void A_keep_answer_for_a_pause_that_has_moved_on_is_refused_and_nothing_is_staged()
    {
        using var repo = SequenceRepos.BinaryConflict();
        PauseRebase(repo);
        var keep = new[] { new KeptFile("logo.bin", ConflictKind.Binary, TreeSide.Ours) };
        var otherCommit = repo.Run("rev-parse", "main");

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, otherCommit, keep));

        Assert.False(check.CanRun);
        Assert.Equal(SequenceRefusals.PauseChanged, check.Refusal);
        Assert.Contains("logo.bin", repo.Run("ls-files", "-u"));
        Assert.True(RebaseInProgress(repo));
    }

    [Fact]
    public void A_rename_rename_conflict_asks_about_all_three_paths_and_keeping_them_removes_the_old_name()
    {
        using var repo = SequenceRepos.RenameRename();
        var stop = PauseRebase(repo).Stop!;

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.False(check.CanRun);
        var paths = check.Keep!.Select(k => k.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" }, paths);
        Assert.Equal(ConflictKind.DeletedBoth, check.Keep!.Single(k => k.Path == "a.txt").Kind);

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit, check.Keep));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal("", repo.Run("ls-files", "a.txt"));
        Assert.True(repo.IsClean);
        Assert.False(RebaseInProgress(repo));
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
        var kept = check.Keep!.Single();
        Assert.Equal(("logo.bin", ConflictKind.Binary, TreeSide.Edited), (kept.Path, kept.Kind, kept.Side));
        Assert.Equal(SequenceOutcomeKind.Refused, RunResume(repo, request).Kind);
        Assert.True(RebaseInProgress(repo));
        Assert.Contains("logo.bin", repo.Run("ls-files", "-u"));
    }

    [Fact]
    public void A_yes_for_one_edited_copy_does_not_cover_a_second_edit_made_before_the_answer()
    {
        using var repo = SequenceRepos.BinaryConflict();
        var stop = PauseRebase(repo).Stop!;
        var logo = Path.Combine(repo.Dir, "logo.bin");
        File.WriteAllBytes(logo, [0x65, 0x64, 0x69, 0x74, 0, 1]);
        var asked = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit)).Keep!;
        Assert.Equal(TreeSide.Edited, asked.Single().Side);
        File.WriteAllBytes(logo, [0x65, 0x64, 0x69, 0x74, 0, 2]);

        var request = new ResumeRequest(ResumeAction.Continue, null, stop.Commit, asked);
        var check = CheckResume(repo, request);

        Assert.False(check.CanRun);
        Assert.NotEqual(asked.Single().Version, check.Keep!.Single().Version);
        Assert.Equal(SequenceOutcomeKind.Refused, RunResume(repo, request).Kind);
        Assert.True(RebaseInProgress(repo));
        Assert.Contains("logo.bin", repo.Run("ls-files", "-u"));
    }

    [Fact]
    public void Markers_in_a_file_that_diffs_as_binary_are_refused_before_any_question()
    {
        using var repo = SequenceRepos.BinaryConflict();
        var stop = PauseRebase(repo).Stop!;
        File.WriteAllText(Path.Combine(repo.Dir, "logo.bin"), "<<<<<<< HEAD\nmain\n=======\nfeature\n>>>>>>> feature\n");

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.False(check.CanRun);
        Assert.Equal("logo.bin still has conflict markers: resolve them before continuing", check.Refusal);
        Assert.Null(check.Keep);
        Assert.True(RebaseInProgress(repo));
    }

    [Fact]
    public void An_answer_for_a_file_resolved_by_hand_after_the_question_is_refused_then_a_fresh_enter_continues()
    {
        using var repo = SequenceRepos.MergeBinaryLock();
        var stop = PauseRebase(repo).Stop!;
        var asked = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit)).Keep!;
        Assert.Equal(TreeSide.Ours, asked.Single().Side);
        File.WriteAllText(Path.Combine(repo.Dir, asked.Single().Path), "merged by hand\n");

        var stale = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit, asked));
        Assert.False(stale.CanRun);
        Assert.Equal(SequenceRefusals.KeepChanged, stale.Refusal);
        Assert.True(RebaseInProgress(repo));

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));
        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal("merged by hand", repo.Run("show", "feature:" + asked.Single().Path).Trim());
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

    [Fact]
    public void A_text_file_that_is_missing_is_not_resolved_and_asks_to_keep_it_deleted()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var stop = PauseRebase(repo).Stop!;
        File.Delete(Path.Combine(repo.Dir, "app.txt"));

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.False(check.CanRun);
        Assert.Equal(new[] { new KeptFile("app.txt", ConflictKind.Text, TreeSide.Deleted) }, check.Keep);
        Assert.Equal("app.txt has no conflict markers. The working tree has it deleted. Keep it deleted and continue?",
            RebaseText.KeepText(stop, check.Keep!));

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit, check.Keep));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal("", repo.Run("ls-tree", "feature", "--", "app.txt"));
        Assert.Contains(outcome.Commands, c => c.Args.Contains("rm"));
    }

    [Fact]
    public void A_text_file_with_no_markers_that_holds_one_side_asks_which_side_and_keeping_it_continues()
    {
        // merge=binary leaves app.lock unmerged with no markers, holding main's bytes: resolved by the marker rule, but not by the bytes.
        using var repo = SequenceRepos.MergeBinaryLock();
        var stop = PauseRebase(repo).Stop!;
        Assert.True(stop.Files.Single().Resolved);

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.False(check.CanRun);
        Assert.Equal(new[] { new KeptFile("app.lock", ConflictKind.Text, TreeSide.Ours) }, check.Keep);
        Assert.Equal("app.lock has no conflict markers. The working tree holds main's version. Keep it and continue?",
            RebaseText.KeepText(stop, check.Keep!));

        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit, check.Keep));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal(repo.Run("rev-parse", "main"), repo.Run("rev-parse", "feature"));
        Assert.True(repo.IsClean);
    }

    [Fact]
    public void A_hand_merged_text_that_matches_neither_side_continues_without_a_question()
    {
        using var repo = SequenceRepos.FeatureConflict();
        PauseRebase(repo);
        repo.Write("app.txt", "merged by hand\n");

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue));

        Assert.True(check.CanRun, check.Refusal);
        Assert.Null(check.Keep);
        Assert.Equal(SequenceOutcomeKind.Done, RunResume(repo, new ResumeRequest(ResumeAction.Continue)).Kind);
    }

    [Fact]
    public void A_symlink_conflict_asks_which_link_to_keep_and_keeping_the_one_the_tree_holds_continues()
    {
        using var repo = SequenceRepos.SymlinkConflict();
        var stop = PauseRebase(repo).Stop!;
        Assert.Equal(ConflictKind.Binary, stop.Files.Single().Kind);

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.Equal(new[] { new KeptFile("link", ConflictKind.Binary, TreeSide.Ours) }, check.Keep);
        var outcome = RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit, check.Keep));

        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        Assert.Equal(repo.Run("rev-parse", "main"), repo.Run("rev-parse", "feature"));
        Assert.True(repo.IsClean);
    }

    [Fact]
    public void A_link_edited_to_a_missing_target_is_asked_about_as_edited_and_is_not_a_git_error()
    {
        using var repo = SequenceRepos.SymlinkConflict();
        var stop = PauseRebase(repo).Stop!;
        var link = Path.Combine(repo.Dir, "link");
        File.Delete(link);
        File.CreateSymbolicLink(link, "missing.txt");

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.False(check.CanRun);
        Assert.Equal("link has no conflict markers: say which version to keep before continuing", check.Refusal);
        var kept = check.Keep!.Single();
        Assert.Equal(("link", ConflictKind.Binary, TreeSide.Edited, "missing.txt"), (kept.Path, kept.Kind, kept.Side, kept.Version));
        Assert.True(RebaseInProgress(repo));
    }

    [Fact]
    public void A_submodule_conflict_is_refused_with_a_line_to_stage_it_in_a_shell()
    {
        using var repo = SequenceRepos.SubmoduleConflict();
        var stop = PauseRebase(repo).Stop!;
        Assert.Equal(ConflictKind.Submodule, stop.Files.Single().Kind);

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.False(check.CanRun);
        Assert.Equal("sub is a submodule conflict: stage it in a shell, then press Enter", check.Refusal);
        Assert.Null(check.Keep);
        Assert.Equal(SequenceOutcomeKind.Refused, RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit)).Kind);
        Assert.True(RebaseInProgress(repo));
    }

    [Fact]
    public void A_conflicted_path_that_is_now_a_folder_is_refused_and_nothing_is_staged()
    {
        using var repo = SequenceRepos.ModifyDelete();
        var stop = PauseRebase(repo).Stop!;
        // The work tree holds feature's file here; the user puts a folder in its place.
        File.Delete(Path.Combine(repo.Dir, "gone.txt"));
        Directory.CreateDirectory(Path.Combine(repo.Dir, "gone.txt"));
        File.WriteAllText(Path.Combine(repo.Dir, "gone.txt", "inner.txt"), "inner\n");

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.False(check.CanRun);
        Assert.Equal("gone.txt is a folder in the work tree: resolve it in a shell (git add or git rm), then press Enter", check.Refusal);
        Assert.Null(check.Keep);
        Assert.Equal(SequenceOutcomeKind.Refused, RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit)).Kind);
        Assert.True(RebaseInProgress(repo));
        Assert.Contains("gone.txt", repo.Run("ls-files", "-u"));
    }

    [Fact]
    public void A_file_added_only_by_the_replayed_side_is_asked_about_as_added_on_theirs()
    {
        using var repo = SequenceRepos.FileVersusFolder();
        var stop = PauseRebase(repo).Stop!;
        var file = stop.Files.Single();
        Assert.Equal(ConflictKind.AddedTheirs, file.Kind);
        Assert.False(file.Resolved);

        var check = CheckResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit));

        Assert.Equal(new[] { new KeptFile(file.Path, ConflictKind.AddedTheirs, TreeSide.Theirs) }, check.Keep);
        Assert.Equal(SequenceOutcomeKind.Done, RunResume(repo, new ResumeRequest(ResumeAction.Continue, null, stop.Commit, check.Keep)).Kind);
        Assert.False(RebaseInProgress(repo));
    }
}
