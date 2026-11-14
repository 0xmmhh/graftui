using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>graftui's temporary files in the git dir get a new name each time, so two graftui windows never share one.</summary>
public class GitDirTempTests
{
    [Fact]
    public void A_name_is_the_stem_the_process_id_and_8_hex_digits()
    {
        var name = GitDirTemp.Name(GitDirTemp.Message);

        Assert.StartsWith($"graftui-msg-{Environment.ProcessId}-", name, StringComparison.Ordinal);
        Assert.True(GitDirTemp.IsNameOf(name, GitDirTemp.Message));
        Assert.False(GitDirTemp.IsNameOf(name, GitDirTemp.UndoMessage));
        Assert.False(GitDirTemp.IsNameOf("graftui-msg", GitDirTemp.Message));
    }

    [Fact]
    public void Each_path_is_new_and_its_file_is_created_empty()
    {
        using var repo = TempRepo.Create();
        var gitDir = Path.Combine(repo.Dir, ".git");

        var first = GitDirTemp.NewPath(gitDir, GitDirTemp.Index);
        var second = GitDirTemp.NewPath(gitDir, GitDirTemp.Index);

        Assert.NotEqual(first, second);
        Assert.Equal(gitDir, Path.GetDirectoryName(first));
        Assert.Equal(0, new FileInfo(first).Length);
        Assert.Equal(0, new FileInfo(second).Length);
    }

    [Fact]
    public void A_missing_folder_is_an_io_error()
    {
        var missing = Path.Combine(Path.GetTempPath(), "graftui-no-such-dir-" + Guid.NewGuid().ToString("N"));

        Assert.ThrowsAny<IOException>(() => GitDirTemp.NewPath(missing, GitDirTemp.Message));
    }

    [Fact]
    public void A_commit_leaves_another_windows_message_file_alone_and_removes_its_own()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Base");
        // Another graftui (or one that crashed) has its own message file in the git dir.
        var other = Path.Combine(repo.Dir, ".git", "graftui-msg-1-deadbeef");
        File.WriteAllText(other, "someone else's message\n");
        repo.Write("b.txt", "b\n");
        repo.Run("add", "b.txt");

        var op = new CommitOperation(repo.Git);
        var request = new CommitRequest(false, "Second", "", null, false);
        var outcome = op.Run(request, op.Check(request));

        Assert.Equal(CommitKind.Committed, outcome.Kind);
        Assert.Equal("Second", repo.Run("log", "-1", "--format=%s"));
        Assert.Equal("someone else's message\n", File.ReadAllText(other));
        Assert.Equal(["graftui-msg-1-deadbeef"], repo.TempFiles(GitDirTemp.Message));
    }
}
