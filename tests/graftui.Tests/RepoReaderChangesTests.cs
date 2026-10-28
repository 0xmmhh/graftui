using graftui.Git;
using graftui.Model;

namespace graftui.Tests;

/// <summary>The Changes tab's reads against throwaway repos. Nothing here writes to the repo under test except through TempRepo.</summary>
public class RepoReaderChangesTests
{
    [Fact]
    public void Renamed_deleted_and_untracked_files_land_in_the_right_groups()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Commit("b.txt", "b\n", "Add b");
        repo.Run("mv", "a.txt", "renamed.txt");
        File.Delete(Path.Combine(repo.Dir, "b.txt"));
        File.WriteAllText(Path.Combine(repo.Dir, "c.txt"), "c\n");

        var snap = new RepoReader(repo.Git).LoadChanges(CancellationToken.None);

        var renamed = Assert.Single(snap.Status.Staged);
        Assert.Equal(new FileChange("renamed.txt", "a.txt", ChangeSide.Staged, ChangeKind.Renamed), renamed);
        Assert.Equal(
            [
                new FileChange("b.txt", null, ChangeSide.Unstaged, ChangeKind.Deleted),
                new FileChange("c.txt", null, ChangeSide.Unstaged, ChangeKind.Untracked),
            ],
            snap.Status.Unstaged);
    }

    [Fact]
    public void Head_line_holds_the_last_four_commits_oldest_first_and_flags_more()
    {
        using var repo = TempRepo.Create();
        for (var i = 1; i <= 5; i++)
            repo.Commit("f.txt", $"{i}\n", $"Commit {i}");

        var head = new RepoReader(repo.Git).LoadChanges(CancellationToken.None).Head;

        Assert.Equal("main", head.Branch);
        Assert.False(head.Detached);
        Assert.True(head.More);
        Assert.Equal(["Commit 2", "Commit 3", "Commit 4", "Commit 5"], head.Commits.Select(c => c.Subject).ToArray());
        Assert.Equal("Commit 5", head.Tip?.Subject);
    }

    [Fact]
    public void The_stash_summary_counts_the_entries_and_names_the_newest()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        Assert.Equal(StashSummary.None, new RepoReader(repo.Git).LoadChanges(CancellationToken.None).Stash);

        repo.Write("a.txt", "one\n");
        repo.Run("stash", "push", "-q", "-m", "first");
        repo.Write("a.txt", "two\n");
        repo.Run("stash", "push", "-q", "-m", "second");

        var stash = new RepoReader(repo.Git).LoadChanges(CancellationToken.None).Stash;

        Assert.Equal(2, stash.Count);
        Assert.Equal(repo.Run("rev-parse", "refs/stash"), stash.TopHash);
        Assert.Equal("On main: second", stash.TopMessage);
    }

    [Fact]
    public void A_repo_with_no_commits_has_an_empty_head_line()
    {
        using var repo = TempRepo.Create();
        File.WriteAllText(Path.Combine(repo.Dir, "a.txt"), "a\n");

        var snap = new RepoReader(repo.Git).LoadChanges(CancellationToken.None);

        Assert.Null(snap.Head.HeadHash);
        Assert.Empty(snap.Head.Commits);
        Assert.Null(snap.Head.Tip);
        Assert.Equal("main", snap.Head.Branch);
        Assert.Equal(new FileChange("a.txt", null, ChangeSide.Unstaged, ChangeKind.Untracked), Assert.Single(snap.Status.Unstaged));
    }

    [Fact]
    public void Untracked_file_diff_is_all_added_lines()
    {
        using var repo = TempRepo.Create();
        repo.Commit("keep.txt", "k\n", "Add keep");
        File.WriteAllText(Path.Combine(repo.Dir, "new.txt"), "first\nsecond\n");
        var row = new FileChange("new.txt", null, ChangeSide.Unstaged, ChangeKind.Untracked);

        var diff = new RepoReader(repo.Git).LoadDiff(row, CancellationToken.None);

        Assert.DoesNotContain(diff.Lines, l => l.Kind == DiffLineKind.Removed);
        Assert.Contains(new DiffLine(DiffLineKind.Added, "+first"), diff.Lines);
        Assert.Contains(new DiffLine(DiffLineKind.Added, "+second"), diff.Lines);
    }

    [Fact]
    public void Staged_rename_diff_names_the_original_file_in_a_note()
    {
        using var repo = TempRepo.Create();
        repo.Commit("old.txt", "same content\n", "Add old");
        repo.Run("mv", "old.txt", "new.txt");
        var row = new FileChange("new.txt", "old.txt", ChangeSide.Staged, ChangeKind.Renamed);

        var diff = new RepoReader(repo.Git).LoadDiff(row, CancellationToken.None);

        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Note && l.Text == "rename from old.txt");
    }

    [Fact]
    public void Unstaged_modification_diff_shows_the_removed_and_added_lines()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        File.WriteAllText(Path.Combine(repo.Dir, "app.txt"), "line 1 (edited)\n");
        var row = new FileChange("app.txt", null, ChangeSide.Unstaged, ChangeKind.Modified);

        var diff = new RepoReader(repo.Git).LoadDiff(row, CancellationToken.None);

        Assert.Contains(new DiffLine(DiffLineKind.Removed, "-line 1"), diff.Lines);
        Assert.Contains(new DiffLine(DiffLineKind.Added, "+line 1 (edited)"), diff.Lines);
    }

    [Fact]
    public void A_file_name_with_glob_characters_is_diffed_literally()
    {
        using var repo = TempRepo.Create();
        repo.Commit("b.txt", "b\n", "Add b");
        File.WriteAllText(Path.Combine(repo.Dir, "b.txt"), "changed\n");
        File.WriteAllText(Path.Combine(repo.Dir, "*.txt"), "glob\n");
        var row = new FileChange("*.txt", null, ChangeSide.Unstaged, ChangeKind.Untracked);

        var diff = new RepoReader(repo.Git).LoadDiff(row, CancellationToken.None);

        Assert.Contains(new DiffLine(DiffLineKind.Added, "+glob"), diff.Lines);
        Assert.DoesNotContain(diff.Lines, l => l.Text == "+changed");
    }

    [Fact]
    public void Top_level_from_a_subfolder_is_the_repo_root()
    {
        using var repo = TempRepo.Create();
        var sub = Path.Combine(repo.Dir, "sub");
        Directory.CreateDirectory(sub);

        var fromSub = new GitClient(sub).GetTopLevel();

        Assert.NotNull(fromSub);
        Assert.Equal(repo.Git.GetTopLevel(), fromSub);
    }

    [Fact]
    public void A_name_with_a_leading_dash_and_spaces_is_diffed_as_a_file()
    {
        using var repo = TempRepo.Create();
        repo.Write("-old file.txt", "old\n");
        repo.Run("add", "--", "-old file.txt");
        repo.Run("commit", "-q", "-m", "Add");
        repo.Write("-old file.txt", "changed\n");
        repo.Write("-new file.txt", "new\n");
        var reader = new RepoReader(repo.Git);

        var modified = reader.LoadDiff(new FileChange("-old file.txt", null, ChangeSide.Unstaged, ChangeKind.Modified), CancellationToken.None);
        var untracked = reader.LoadDiff(new FileChange("-new file.txt", null, ChangeSide.Unstaged, ChangeKind.Untracked), CancellationToken.None);

        Assert.Contains(new DiffLine(DiffLineKind.Added, "+changed"), modified.Lines);
        Assert.Contains(new DiffLine(DiffLineKind.Added, "+new"), untracked.Lines);
    }

    [Fact]
    public void An_empty_untracked_file_says_so()
    {
        using var repo = TempRepo.Create();
        repo.Commit("keep.txt", "k\n", "Add keep");
        File.WriteAllText(Path.Combine(repo.Dir, "empty.txt"), "");

        var diff = new RepoReader(repo.Git).LoadDiff(new FileChange("empty.txt", null, ChangeSide.Unstaged, ChangeKind.Untracked), CancellationToken.None);

        Assert.Equal(new DiffLine(DiffLineKind.Note, "(empty file)"), Assert.Single(diff.Lines));
    }

    [Fact]
    public void A_small_untracked_binary_file_keeps_the_binary_note_and_is_not_called_empty()
    {
        using var repo = TempRepo.Create();
        repo.Commit("keep.txt", "k\n", "Add keep");
        File.WriteAllBytes(Path.Combine(repo.Dir, "logo.png"), [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02, 0x03]);

        var diff = new RepoReader(repo.Git).LoadDiff(new FileChange("logo.png", null, ChangeSide.Unstaged, ChangeKind.Untracked), CancellationToken.None);

        Assert.DoesNotContain(diff.Lines, l => l.Text == "(empty file)");
        var binary = Assert.Single(diff.Lines, l => l.Text.StartsWith("Binary files", StringComparison.Ordinal));
        Assert.Equal(DiffLineKind.Note, binary.Kind);
        Assert.Equal("Binary files /dev/null and b/logo.png differ", binary.Text);
    }

    [Fact]
    public void A_staged_binary_file_keeps_the_binary_note()
    {
        using var repo = TempRepo.Create();
        repo.Commit("keep.txt", "k\n", "Add keep");
        File.WriteAllBytes(Path.Combine(repo.Dir, "logo.png"), [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02, 0x03]);
        repo.Run("add", "--", "logo.png");

        var diff = new RepoReader(repo.Git).LoadDiff(new FileChange("logo.png", null, ChangeSide.Staged, ChangeKind.Added), CancellationToken.None);

        Assert.DoesNotContain(diff.Lines, l => l.Text == "(empty file)");
        var binary = Assert.Single(diff.Lines, l => l.Text.StartsWith("Binary files", StringComparison.Ordinal));
        Assert.Equal(DiffLineKind.Note, binary.Kind);
        Assert.Equal("Binary files /dev/null and b/logo.png differ", binary.Text);
    }

    [Fact]
    public void A_large_untracked_file_is_not_read_for_the_preview()
    {
        using var repo = TempRepo.Create();
        repo.Commit("keep.txt", "k\n", "Add keep");
        File.WriteAllBytes(Path.Combine(repo.Dir, "big.bin"), new byte[1_000_001]);

        var diff = new RepoReader(repo.Git).LoadDiff(new FileChange("big.bin", null, ChangeSide.Unstaged, ChangeKind.Untracked), CancellationToken.None);

        Assert.Equal(new DiffLine(DiffLineKind.Note, "new file, 1000001 bytes: too large to preview"), Assert.Single(diff.Lines));
    }

    [Fact]
    public void A_nested_repo_is_not_previewed()
    {
        using var repo = TempRepo.Create();
        var diff = new RepoReader(repo.Git).LoadDiff(new FileChange("nested/", null, ChangeSide.Unstaged, ChangeKind.Untracked), CancellationToken.None);

        Assert.Equal(new DiffLine(DiffLineKind.Note, "a separate git repository; not previewed"), Assert.Single(diff.Lines));
    }

    [Fact]
    public void Has_undo_is_false_until_an_undo_step_is_saved_and_both_reads_agree()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        var reader = new RepoReader(repo.Git);

        Assert.False(reader.LoadSnapshot(withCounts: false, CancellationToken.None).HasUndo);
        Assert.False(reader.LoadChanges(CancellationToken.None).HasUndo);

        repo.Run("update-ref", "refs/graftui/undo/000001", repo.Head, "");

        Assert.True(repo.Git.HasUndoSteps());
        Assert.True(reader.LoadSnapshot(withCounts: false, CancellationToken.None).HasUndo);
        Assert.True(reader.LoadChanges(CancellationToken.None).HasUndo);
    }

    [Fact]
    public void Has_undo_steps_is_not_logged_as_a_command()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");

        Assert.False(repo.Git.HasUndoSteps());
    }
}
