using graftui.Git;
using graftui.Model;

namespace graftui.Tests;

/// <summary>
/// The files a branch's new commits change, read with git diff --numstat: the sandbox (read only) and throwaway repos. Each case leaves the
/// work tree clean and HEAD where it was.
/// </summary>
public sealed class IncomingFilesReaderTests : IClassFixture<ReadOnlySandbox>
{
    private readonly ReadOnlySandbox _sandbox;

    public IncomingFilesReaderTests(ReadOnlySandbox sandbox)
    {
        _sandbox = sandbox;
    }

    [Fact]
    public void The_sandbox_picks_read_as_three_files_in_git_order()
    {
        var reader = _sandbox.Reader;
        var snap = reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var picks = snap.Branches.Single(b => b.Name == "feature/picks");
        var layout = reader.LoadLayout(snap, picks, CancellationToken.None);
        Assert.NotNull(layout.MergeBase);

        var files = reader.LoadIncomingFiles(layout.MergeBase, picks.Hash, CancellationToken.None);

        Assert.NotNull(files);
        Assert.Equal(3, files.Count);
        Assert.Equal(new IncomingFile("README.md", null, 1, 1), files.Files[0]);
        Assert.Equal(new IncomingFile("VERSION", null, 1, 0), files.Files[1]);
        Assert.Equal(new IncomingFile("log.txt", null, 1, 0), files.Files[2]);
    }

    [Fact]
    public void A_binary_file_has_no_counts_and_the_tree_stays_clean()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Main commit");
        var head = repo.Head;
        repo.Run("switch", "-q", "-c", "feature");
        File.WriteAllBytes(Path.Combine(repo.Dir, "logo.bin"), [0, 1, 2, 3]);
        repo.Run("add", "logo.bin");
        repo.Run("commit", "-q", "-m", "Binary file");
        var (mergeBase, tip) = BaseAndTip(repo, "feature");
        var reader = new RepoReader(repo.Git);

        var files = reader.LoadIncomingFiles(mergeBase, tip, CancellationToken.None);

        Assert.NotNull(files);
        Assert.Equal(new IncomingFile("logo.bin", null, null, null), Assert.Single(files.Files));
        Assert.True(repo.IsClean);
        Assert.Equal(head, repo.Run("rev-parse", "main"));
    }

    [Fact]
    public void A_rename_is_one_file_with_the_new_path_and_the_old_path()
    {
        using var repo = TempRepo.Create();
        repo.Commit("old.txt", "same text\n", "Main commit");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Run("mv", "old.txt", "new.txt");
        repo.Run("commit", "-q", "-m", "Rename");
        var (mergeBase, tip) = BaseAndTip(repo, "feature");
        var reader = new RepoReader(repo.Git);

        var files = reader.LoadIncomingFiles(mergeBase, tip, CancellationToken.None);

        Assert.NotNull(files);
        Assert.Equal(new IncomingFile("new.txt", "old.txt", 0, 0), Assert.Single(files.Files));
    }

    [Fact]
    public void Paths_come_from_the_top_level_even_with_diff_relative_and_a_subfolder_reader()
    {
        using var repo = TempRepo.Create();
        Directory.CreateDirectory(Path.Combine(repo.Dir, "sub"));
        repo.Commit("sub/x.txt", "x\n", "Main commit");
        repo.Run("config", "diff.relative", "true");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("sub/x.txt", "x\ny\n", "Edit in the folder");
        var (mergeBase, tip) = BaseAndTip(repo, "feature");
        var fromSubfolder = new RepoReader(new GitClient(Path.Combine(repo.Dir, "sub")));

        var files = fromSubfolder.LoadIncomingFiles(mergeBase, tip, CancellationToken.None);

        Assert.NotNull(files);
        Assert.Equal(new IncomingFile("sub/x.txt", null, 1, 0), Assert.Single(files.Files));
    }

    [Fact]
    public void A_commit_with_no_changes_gives_no_files()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Main commit");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Run("commit", "-q", "--allow-empty", "-m", "Empty");
        var (mergeBase, tip) = BaseAndTip(repo, "feature");
        var reader = new RepoReader(repo.Git);

        var files = reader.LoadIncomingFiles(mergeBase, tip, CancellationToken.None);

        Assert.NotNull(files);
        Assert.Equal(0, files.Count);
        Assert.Empty(files.Files);
    }

    [Fact]
    public void A_cancelled_read_throws_and_starts_no_work()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Main commit");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("b.txt", "b\n", "Feature commit");
        var (mergeBase, tip) = BaseAndTip(repo, "feature");
        var reader = new RepoReader(repo.Git);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => reader.LoadIncomingFiles(mergeBase, tip, cts.Token));
    }

    [Fact]
    public void Git_older_than_2_28_gives_the_summary_only()
    {
        Assert.False(RepoReader.SupportsNumstatRead(null));
        Assert.False(RepoReader.SupportsNumstatRead(new Version(2, 27)));
        Assert.True(RepoReader.SupportsNumstatRead(new Version(2, 28)));
        Assert.True(RepoReader.SupportsNumstatRead(new Version(2, 56)));
    }

    // The merge base of main and the branch, and the branch tip.
    private static (string MergeBase, string Tip) BaseAndTip(TempRepo repo, string branch) =>
        (repo.Run("merge-base", "main", branch), repo.Run("rev-parse", branch));
}
