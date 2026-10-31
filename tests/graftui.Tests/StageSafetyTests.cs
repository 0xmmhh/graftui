using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>Stage and unstage against throwaway repos: the index changes as asked and the work tree never does.</summary>
public class StageSafetyTests
{
    private static WorkingStatus Read(TempRepo repo) =>
        new RepoReader(repo.Git).LoadChanges(CancellationToken.None).Status;

    private static FileChange Find(TempRepo repo, ChangeSide side, string path) =>
        Read(repo).Rows.Single(r => r.Side == side && r.Path == path);

    private static StageOutcome Act(TempRepo repo, StageAction action, FileChange? file, bool confirmed = false)
    {
        var op = new StageOperation(repo.Git);
        var request = new StageRequest(action, file, confirmed);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static StageCheck CheckOnly(TempRepo repo, StageAction action, FileChange? file, bool confirmed = false) =>
        new StageOperation(repo.Git).Check(new StageRequest(action, file, confirmed));

    /// <summary>app.txt staged as "A" and then edited to "B": the index differs from HEAD and from the work tree.</summary>
    private static void HandPicked(TempRepo repo, string path)
    {
        repo.Write(path, "A\n");
        repo.Run("add", path);
        repo.Write(path, "B\n");
    }

    private static byte[] Bytes(TempRepo repo, string path) => File.ReadAllBytes(Path.Combine(repo.Dir, path));

    [Fact]
    public void Stage_adds_one_modified_file_and_leaves_the_work_tree_byte_equal()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        var before = Bytes(repo, "app.txt");

        var outcome = Act(repo, StageAction.Stage, Find(repo, ChangeSide.Unstaged, "app.txt"));

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal("app.txt", repo.Run("diff", "--cached", "--name-only"));
        Assert.Equal(before, Bytes(repo, "app.txt"));
        Assert.Contains(outcome.Commands, c => c.Command == "git add -- app.txt" && c.ExitCode == 0);
        Assert.Null(outcome.Undo);
    }

    [Fact]
    public void Stage_of_an_untracked_file_shows_as_added_and_of_a_deleted_file_as_deleted()
    {
        using var repo = TempRepo.Create();
        repo.Commit("gone.txt", "bye\n", "Add gone");
        repo.Write("new.txt", "hi\n");
        File.Delete(Path.Combine(repo.Dir, "gone.txt"));

        Assert.True(Act(repo, StageAction.Stage, Find(repo, ChangeSide.Unstaged, "new.txt")).Success);
        Assert.True(Act(repo, StageAction.Stage, Find(repo, ChangeSide.Unstaged, "gone.txt")).Success);

        Assert.Equal("D  gone.txt\nA  new.txt", repo.Status());
    }

    [Fact]
    public void Unstage_keeps_the_work_tree_edit()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        Act(repo, StageAction.Stage, Find(repo, ChangeSide.Unstaged, "app.txt"));
        var before = Bytes(repo, "app.txt");

        var outcome = Act(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "app.txt"));

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(" M app.txt", repo.Status());
        Assert.Equal(before, Bytes(repo, "app.txt"));
        Assert.Contains(outcome.Commands, c => c.Command == "git restore --staged -- app.txt");
    }

    [Fact]
    public void Unstaging_a_staged_rename_restores_both_paths_and_leaves_the_files_alone()
    {
        using var repo = TempRepo.Create();
        repo.Commit("old.txt", "same content\n", "Add old");
        repo.Run("mv", "old.txt", "new name.txt");
        var renamedBytes = Bytes(repo, "new name.txt");

        var outcome = Act(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "new name.txt"));

        Assert.True(outcome.Success, outcome.Message);
        Assert.Contains(outcome.Commands, c => c.Command == "git restore --staged -- \"new name.txt\" old.txt");
        var rows = Read(repo).Unstaged;
        Assert.Contains(new FileChange("old.txt", null, ChangeSide.Unstaged, ChangeKind.Deleted), rows);
        Assert.Contains(new FileChange("new name.txt", null, ChangeSide.Unstaged, ChangeKind.Untracked), rows);
        Assert.Empty(Read(repo).Staged);
        Assert.Equal(renamedBytes, Bytes(repo, "new name.txt"));
        Assert.False(File.Exists(Path.Combine(repo.Dir, "old.txt")));
    }

    [Fact]
    public void Stage_all_with_a_rename_a_delete_and_an_untracked_file_leaves_no_unstaged_rows()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Commit("b.txt", "b\n", "Add b");
        repo.Run("mv", "a.txt", "renamed.txt");
        File.Delete(Path.Combine(repo.Dir, "b.txt"));
        repo.Write("c.txt", "c\n");

        var outcome = Act(repo, StageAction.StageAll, null);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Empty(Read(repo).Unstaged);
        Assert.Contains(outcome.Commands, c => c.Command == "git add -A");
    }

    [Fact]
    public void Unstage_all_leaves_no_staged_rows_and_the_work_tree_unchanged()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        repo.Write("new.txt", "new\n");
        Act(repo, StageAction.StageAll, null);
        var appBytes = Bytes(repo, "app.txt");
        var newBytes = Bytes(repo, "new.txt");

        var outcome = Act(repo, StageAction.UnstageAll, null);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Empty(Read(repo).Staged);
        Assert.Equal(appBytes, Bytes(repo, "app.txt"));
        Assert.Equal(newBytes, Bytes(repo, "new.txt"));
        Assert.Contains(outcome.Commands, c => c.Command == "git restore --staged -- .");
    }

    [Fact]
    public void Without_a_head_unstage_uses_rm_cached_and_keeps_the_file_on_disk()
    {
        using var repo = TempRepo.Create();
        repo.Write("a.txt", "a\n");
        Act(repo, StageAction.Stage, Find(repo, ChangeSide.Unstaged, "a.txt"));

        var outcome = Act(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "a.txt"));

        Assert.True(outcome.Success, outcome.Message);
        Assert.Contains(outcome.Commands, c => c.Command == "git rm --cached -q -f -- a.txt");
        Assert.True(File.Exists(Path.Combine(repo.Dir, "a.txt")));
        Assert.Equal("?? a.txt", repo.Status());
    }

    [Fact]
    public void Without_a_head_a_partly_staged_file_unstages_and_keeps_its_edited_content()
    {
        using var repo = TempRepo.Create();
        repo.Write("a.txt", "first\n");
        repo.Run("add", "a.txt");
        repo.Write("a.txt", "edited\n");

        var outcome = Act(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "a.txt"), confirmed: true);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal("edited\n", File.ReadAllText(Path.Combine(repo.Dir, "a.txt")));
        Assert.Equal("?? a.txt", repo.Status());
    }

    [Fact]
    public void Without_a_head_unstage_all_uses_rm_cached_recursively()
    {
        using var repo = TempRepo.Create();
        repo.Write("a.txt", "a\n");
        repo.Write("b.txt", "b\n");
        Act(repo, StageAction.StageAll, null);

        var outcome = Act(repo, StageAction.UnstageAll, null);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Contains(outcome.Commands, c => c.Command == "git rm --cached -r -q -f -- .");
        Assert.Empty(Read(repo).Staged);
        Assert.True(File.Exists(Path.Combine(repo.Dir, "a.txt")));
    }

    [Fact]
    public void A_file_named_with_a_glob_stages_only_that_file()
    {
        using var repo = TempRepo.Create();
        repo.Commit("b.txt", "b\n", "Add b");
        repo.Write("b.txt", "b changed\n");
        repo.Write("*.txt", "glob\n");

        var outcome = Act(repo, StageAction.Stage, Find(repo, ChangeSide.Unstaged, "*.txt"));

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal("*.txt", repo.Run("diff", "--cached", "--name-only"));
        Assert.Equal(
            [new FileChange("b.txt", null, ChangeSide.Unstaged, ChangeKind.Modified)],
            Read(repo).Unstaged);
    }

    [Fact]
    public void A_conflicted_file_is_refused_and_stage_all_is_refused_too()
    {
        using var conflicted = TempRepo.Diverged(conflict: true);
        Assert.False(conflicted.Git.Run("merge", "feature").Success);
        try
        {
            var row = Find(conflicted, ChangeSide.Unstaged, "app.txt");

            Assert.Equal(ChangeRefusals.ConflictFile("app.txt"), CheckOnly(conflicted, StageAction.Stage, row).Refusal);
            Assert.Equal(ChangeRefusals.Conflicts, CheckOnly(conflicted, StageAction.StageAll, null).Refusal);
        }
        finally
        {
            conflicted.Git.Run("merge", "--abort");
        }
    }

    [Fact]
    public void A_nested_repo_blocks_stage_all()
    {
        using var repo = TempRepo.Create();
        repo.Write("a.txt", "a\n");
        var nested = Path.Combine(repo.Dir, "nested");
        Directory.CreateDirectory(nested);
        Assert.True(new GitClient(nested).Run("init", "-q").Success);

        var check = CheckOnly(repo, StageAction.StageAll, null);

        Assert.Equal(ChangeRefusals.NestedRepo("nested/"), check.Refusal);
        Assert.Empty(Read(repo).Staged);
    }

    [Fact]
    public void A_file_reverted_after_the_list_was_read_is_refused_as_stale()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        var row = Find(repo, ChangeSide.Unstaged, "app.txt");
        repo.Write("app.txt", "line 1\n");

        var check = CheckOnly(repo, StageAction.Stage, row);

        Assert.Equal(ChangeRefusals.Stale("app.txt"), check.Refusal);
        Assert.Equal("", repo.Run("diff", "--cached", "--name-only"));
    }

    [Fact]
    public void Unstage_all_with_nothing_staged_is_refused()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");

        Assert.Equal(ChangeRefusals.NothingIsStaged, CheckOnly(repo, StageAction.UnstageAll, null).Refusal);
    }

    [Fact]
    public void Stage_unstage_and_unstage_all_handle_names_with_spaces_and_a_leading_dash()
    {
        using var repo = TempRepo.Create();
        repo.Write("-old file.txt", "old\n");
        repo.Run("add", "--", "-old file.txt");
        repo.Run("commit", "-q", "-m", "Add dash file");
        repo.Write("-old file.txt", "changed\n");
        repo.Write("-new file.txt", "new\n");

        Assert.True(Act(repo, StageAction.Stage, Find(repo, ChangeSide.Unstaged, "-old file.txt")).Success);
        Assert.True(Act(repo, StageAction.Stage, Find(repo, ChangeSide.Unstaged, "-new file.txt")).Success);
        Assert.Equal("-new file.txt\n-old file.txt", repo.Run("diff", "--cached", "--name-only"));

        var single = Act(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "-new file.txt"));
        Assert.True(single.Success, single.Message);
        Assert.Contains(single.Commands, c => c.Command == "git restore --staged -- \"-new file.txt\"");
        Assert.Contains(new FileChange("-new file.txt", null, ChangeSide.Unstaged, ChangeKind.Untracked), Read(repo).Unstaged);

        Assert.True(Act(repo, StageAction.UnstageAll, null).Success);
        Assert.Empty(Read(repo).Staged);
        Assert.Equal("changed\n", File.ReadAllText(Path.Combine(repo.Dir, "-old file.txt")));
    }

    [Fact]
    public void Unstage_all_with_head_unstages_names_that_look_like_pathspecs()
    {
        using var repo = TempRepo.Create();
        repo.Commit("keep.txt", "k\n", "Add keep");
        repo.Write("*.txt", "glob\n");
        repo.Write(":x", "magic\n");
        repo.Write("keep.txt", "k changed\n");
        Act(repo, StageAction.StageAll, null);

        var outcome = Act(repo, StageAction.UnstageAll, null);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Empty(Read(repo).Staged);
        Assert.Equal("glob\n", File.ReadAllText(Path.Combine(repo.Dir, "*.txt")));
        Assert.Equal("magic\n", File.ReadAllText(Path.Combine(repo.Dir, ":x")));
    }

    [Fact]
    public void A_hand_picked_unstage_is_refused_until_the_user_confirms_it()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        HandPicked(repo, "app.txt");

        var check = CheckOnly(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "app.txt"));

        Assert.Equal(ChangeRefusals.HandPickedNotConfirmed("app.txt"), check.Refusal);
        Assert.Equal(["app.txt"], check.HandPicked);
        Assert.Equal("app.txt", repo.Run("diff", "--cached", "--name-only"));
        Assert.Equal("MM app.txt", repo.Status());
    }

    [Fact]
    public void Unstage_all_with_hand_picked_files_is_refused_once_and_names_the_first_one()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Commit("b.txt", "b\n", "Add b");
        HandPicked(repo, "a.txt");
        HandPicked(repo, "b.txt");

        var check = CheckOnly(repo, StageAction.UnstageAll, null);

        Assert.Equal(ChangeRefusals.HandPickedNotConfirmed("a.txt"), check.Refusal);
        Assert.Equal(["a.txt", "b.txt"], check.HandPicked);
        Assert.Equal("a.txt\nb.txt", repo.Run("diff", "--cached", "--name-only"));
    }

    [Fact]
    public void A_confirmed_hand_picked_unstage_puts_the_index_back_to_head_and_saves_the_staged_version()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        HandPicked(repo, "app.txt");
        var staged = repo.Run("rev-parse", ":app.txt");
        var worktree = Bytes(repo, "app.txt");

        var outcome = Act(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "app.txt"), confirmed: true);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(repo.Run("rev-parse", "HEAD:app.txt"), repo.Run("rev-parse", ":app.txt"));
        Assert.Equal(" M app.txt", repo.Status());
        Assert.Equal(worktree, Bytes(repo, "app.txt"));
        Assert.NotNull(outcome.Undo);
        Assert.Equal(UndoKind.Unstage, outcome.Undo.Kind);
        Assert.Equal("unstage of app.txt", outcome.Undo.What);
        Assert.Equal([new IndexEntry("app.txt", "100644", staged)], outcome.Undo.IndexBefore);
        Assert.Equal([new IndexEntry("app.txt", "100644", repo.Run("rev-parse", "HEAD:app.txt"))], outcome.Undo.IndexAfter);
        Assert.Equal(UndoKind.Unstage, new UndoLog(repo.Git).Top([])!.Step!.Kind);
    }

    [Fact]
    public void A_confirmed_unstage_all_records_every_staged_path_and_its_absent_ones()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Commit("b.txt", "b\n", "Add b");
        HandPicked(repo, "a.txt");
        HandPicked(repo, "b.txt");
        repo.Write("new.txt", "plain\n");
        Act(repo, StageAction.Stage, Find(repo, ChangeSide.Unstaged, "new.txt"));
        var aStaged = repo.Run("rev-parse", ":a.txt");
        var bStaged = repo.Run("rev-parse", ":b.txt");
        var newStaged = repo.Run("rev-parse", ":new.txt");

        var outcome = Act(repo, StageAction.UnstageAll, null, confirmed: true);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Empty(Read(repo).Staged);
        Assert.Equal("unstage of 3 files", outcome.Undo!.What);
        Assert.Equal(
            [new IndexEntry("a.txt", "100644", aStaged), new IndexEntry("b.txt", "100644", bStaged), new IndexEntry("new.txt", "100644", newStaged)],
            outcome.Undo.IndexBefore);
        // A newly added file has no entry in HEAD, so after the unstage it is absent from the index.
        Assert.Equal(
            [new IndexEntry("a.txt", "100644", repo.Run("rev-parse", "HEAD:a.txt")), new IndexEntry("b.txt", "100644", repo.Run("rev-parse", "HEAD:b.txt")), new IndexEntry("new.txt", "", "")],
            outcome.Undo.IndexAfter);
    }

    [Fact]
    public void A_plain_unstage_needs_no_confirmation_and_saves_no_undo_step()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        Act(repo, StageAction.Stage, Find(repo, ChangeSide.Unstaged, "app.txt"));

        var check = CheckOnly(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "app.txt"));
        var outcome = Act(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "app.txt"));

        Assert.True(check.CanRun, check.Refusal);
        Assert.Empty(check.HandPicked);
        Assert.True(outcome.Success, outcome.Message);
        Assert.Null(outcome.Undo);
        Assert.Null(new UndoLog(repo.Git).Top([]));
    }

    [Fact]
    public void A_staged_new_file_deleted_from_the_work_tree_is_hand_picked_and_unstages_to_nothing()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("new.txt", "n\n");
        repo.Run("add", "new.txt");
        File.Delete(Path.Combine(repo.Dir, "new.txt"));
        Assert.Equal("AD new.txt", repo.Status());

        Assert.Equal(ChangeRefusals.HandPickedNotConfirmed("new.txt"),
            CheckOnly(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "new.txt")).Refusal);

        var outcome = Act(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "new.txt"), confirmed: true);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal("", repo.Status());
        Assert.Equal([new IndexEntry("new.txt", "", "")], outcome.Undo!.IndexAfter);
        Assert.Equal("100644", outcome.Undo.IndexBefore![0].Mode);
    }

    [Fact]
    public void A_row_that_becomes_hand_picked_after_the_check_is_refused_until_it_is_confirmed()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Write("app.txt", "staged\n");
        repo.Run("add", "app.txt");
        var request = new StageRequest(StageAction.Unstage, Find(repo, ChangeSide.Staged, "app.txt"));
        var op = new StageOperation(repo.Git);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        Assert.Empty(check.HandPicked);

        // Edited between the check and the run: the staged version is now hand-picked.
        repo.Write("app.txt", "edited again\n");
        var before = repo.Status();
        var outcome = op.Run(request, check);

        Assert.False(outcome.Success);
        Assert.Equal(ChangeRefusals.HandPickedNotConfirmed("app.txt"), outcome.Message);
        Assert.Null(outcome.Undo);
        Assert.Equal(before, repo.Status());
    }

    [Fact]
    public void A_confirmed_run_saves_the_index_entries_of_what_is_hand_picked_at_run_time()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Write("app.txt", "staged\n");
        repo.Run("add", "app.txt");
        var request = new StageRequest(StageAction.Unstage, Find(repo, ChangeSide.Staged, "app.txt"), HandPickedConfirmed: true);
        var op = new StageOperation(repo.Git);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);

        repo.Write("app.txt", "edited again\n");
        var outcome = op.Run(request, check);

        Assert.True(outcome.Success, outcome.Message);
        Assert.NotNull(outcome.Undo);
        Assert.Equal("app.txt", outcome.Undo.IndexBefore![0].Path);
        Assert.Equal(" M app.txt", repo.Status());
    }
}
