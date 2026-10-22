using Graft.Git;
using Graft.Model;

namespace Graft.Tests;

/// <summary>What the screen reads from the sandbox: branch counts and preview layouts.</summary>
public class RepoReaderSandboxTests : IDisposable
{
    private readonly string _dir = TempRepo.NewSandboxDir();
    private readonly GitClient _git;
    private readonly RepoReader _reader;

    public RepoReaderSandboxTests()
    {
        Sandbox.RebuildAt(_dir, TextWriter.Null);
        _git = new GitClient(_dir);
        _reader = new RepoReader(_git);
    }

    public void Dispose() => TempRepo.DeleteSandboxRoot(_dir);

    [Fact]
    public void The_snapshot_has_main_as_current_and_a_clean_tree()
    {
        var snap = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);

        Assert.Equal("main", snap.CurrentBranch);
        Assert.True(snap.Clean);
    }

    [Theory]
    [InlineData("feature/fast-forward", 1, 0)]
    [InlineData("feature/merge-commit", 1, 1)]
    [InlineData("feature/conflict", 1, 1)]
    [InlineData("feature/ff-to-merge", 3, 0)]
    [InlineData("feature/picks", 3, 1)]
    public void Ahead_and_behind_counts_match_each_branch(string branch, int ahead, int behind)
    {
        var snap = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);

        Assert.Equal((ahead, behind), snap.Counts[branch]);
    }

    [Fact]
    public void A_fast_forward_onto_a_merge_commit_previews_as_fast_forward_with_a_merge_tip()
    {
        var snap = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var branch = snap.Branches.Single(b => b.Name == "feature/ff-to-merge");

        var layout = _reader.LoadLayout(snap, branch, CancellationToken.None);

        Assert.Equal(GraphKind.FastForward, layout.Kind);
        Assert.Equal(3, layout.Ahead);
        Assert.Equal(2, layout.SelectedOwn.Count);
        Assert.True(layout.SelectedTipCommit.IsMerge);
    }

    [Fact]
    public void The_picks_branch_previews_as_diverged_with_its_three_commits()
    {
        var snap = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var branch = snap.Branches.Single(b => b.Name == "feature/picks");

        var layout = _reader.LoadLayout(snap, branch, CancellationToken.None);

        Assert.Equal(GraphKind.Diverged, layout.Kind);
        Assert.Equal(
            new[] { "Fix typo in README", "Add logging", "Bump version to 0.3" },
            layout.SelectedOwn.Select(c => c.Subject));
        Assert.Equal(new[] { "Initial commit", "Add app" }, layout.Shared.Select(c => c.Subject));
    }

    [Fact]
    public void Main_previews_as_the_same_commit_as_itself()
    {
        var snap = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var main = snap.Branches.Single(b => b.Name == "main");

        var layout = _reader.LoadLayout(snap, main, CancellationToken.None);

        Assert.Equal(GraphKind.Same, layout.Kind);
    }

    [Fact]
    public void A_branch_behind_main_previews_as_up_to_date()
    {
        _git.Run("branch", "old", "main~1");
        var snap = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var old = snap.Branches.Single(b => b.Name == "old");

        var layout = _reader.LoadLayout(snap, old, CancellationToken.None);

        Assert.Equal(GraphKind.UpToDate, layout.Kind);
    }

    [Fact]
    public void A_branch_with_no_common_history_previews_as_unrelated()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Main commit");
        repo.Run("switch", "-q", "--orphan", "lone");
        repo.Commit("lone.txt", "x\n", "Lone commit");
        repo.Run("switch", "-q", "main");
        var reader = new RepoReader(repo.Git);
        var snap = reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var lone = snap.Branches.Single(b => b.Name == "lone");

        var layout = reader.LoadLayout(snap, lone, CancellationToken.None);

        Assert.Equal(GraphKind.Unrelated, layout.Kind);
    }
}
