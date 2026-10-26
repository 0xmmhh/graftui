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

    private static StageOutcome Act(TempRepo repo, StageAction action, FileChange? file)
    {
        var op = new StageOperation(repo.Git);
        var request = new StageRequest(action, file);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static StageCheck CheckOnly(TempRepo repo, StageAction action, FileChange? file) =>
        new StageOperation(repo.Git).Check(new StageRequest(action, file));

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
        Assert.Contains(outcome.Commands, c => c.Command == "git restore --staged -- new name.txt old.txt");
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

        var outcome = Act(repo, StageAction.Unstage, Find(repo, ChangeSide.Staged, "a.txt"));

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
        Assert.Contains(single.Commands, c => c.Command == "git restore --staged -- -new file.txt");
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
}
