using graftui.Git;
using graftui.Model;

namespace graftui.Tests;

/// <summary>
/// The lane graph read from git. Only throwaway repos and the sandbox copy under the temp folder; never the graftui repo.
/// </summary>
public sealed class LaneGraphReaderTests : IDisposable
{
    private readonly string _dir = TempRepo.NewSandboxDir();
    private readonly GitClient _git;
    private readonly RepoReader _reader;

    public LaneGraphReaderTests()
    {
        try
        {
            Sandbox.RebuildAt(_dir, TextWriter.Null);
        }
        catch
        {
            // Dispose never runs when the constructor throws, so clean up the temp root here.
            TempRepo.DeleteSandboxRoot(_dir);
            throw;
        }

        _git = new GitClient(_dir);
        _reader = new RepoReader(_git);
    }

    public void Dispose() => TempRepo.DeleteSandboxRoot(_dir);

    [Fact]
    public void The_sandbox_graph()
    {
        var snap = _reader.LoadSnapshot(withCounts: false, CancellationToken.None);

        var graph = _reader.LoadLaneGraph(snap, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "main: 34e4a2e@0 8c5ceee@1 be0126e@2",
                "feature/fast-forward: 8f0068a@3",
                "feature/ff-to-merge: 2b99ab2@3 810cc55@5",
                "side-for-ffm: df42801@4",
                "feature/conflict: 30cc9f2@2",
                "feature/merge-commit: acf4dee@2",
                "feature/picks: 089eae5@2 52b7690@3 f6d6964@4",
            },
            graph.Lanes.Select(lane => Describe(lane)).ToArray());
        Assert.Equal(LaneKind.Side, graph.Lanes[3].Kind);
        Assert.Equal(6, graph.Columns);
        Assert.False(graph.BaseMore);
        Assert.False(graph.OthersCut);
        Assert.Empty(graph.Pointers);
    }

    [Fact]
    public void Reading_the_graph_changes_nothing_in_the_repo()
    {
        var headBefore = _git.Run("rev-parse", "HEAD").StdOut.Trim();
        var snap = _reader.LoadSnapshot(withCounts: false, CancellationToken.None);

        _reader.LoadLaneGraph(snap, CancellationToken.None);

        Assert.Equal(headBefore, _git.Run("rev-parse", "HEAD").StdOut.Trim());
        Assert.Equal("", _git.Run("status", "--porcelain").StdOut.Trim());
    }

    [Fact]
    public void On_another_branch_the_base_is_that_branch()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Run("switch", "-q", "feature");
        var reader = new RepoReader(repo.Git);
        var snap = reader.LoadSnapshot(withCounts: false, CancellationToken.None);

        var graph = reader.LoadLaneGraph(snap, CancellationToken.None);

        Assert.Equal("feature", graph.BaseName);
        Assert.True(graph.LaneOf("main") >= 0);
    }

    [Fact]
    public void A_detached_head_uses_the_trunk()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Commit("b.txt", "b\n", "Add b");
        repo.Run("switch", "-q", "--detach", "HEAD~1");
        var reader = new RepoReader(repo.Git);
        var snap = reader.LoadSnapshot(withCounts: false, CancellationToken.None);

        var graph = reader.LoadLaneGraph(snap, CancellationToken.None);

        Assert.Equal("main", graph.BaseName);
    }

    [Fact]
    public void A_base_that_moved_after_HEAD_was_read_uses_the_branch_list_tip()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        var older = repo.Head;
        repo.Commit("b.txt", "b\n", "Add b");
        var newer = repo.Head;
        var reader = new RepoReader(repo.Git);
        var snap = reader.LoadSnapshot(withCounts: false, CancellationToken.None);
        // HEAD as it was read before main moved on, with the branch list read after the move.
        var stale = new RepoSnapshot(snap.CurrentBranch, older, snap.Clean, snap.Branches, snap.Counts);

        var graph = reader.LoadLaneGraph(stale, CancellationToken.None);

        Assert.Equal(newer, graph.Lanes[0].Commits[^1].Hash);
    }

    [Fact]
    public void No_commits_gives_empty()
    {
        using var repo = TempRepo.Create();
        var reader = new RepoReader(repo.Git);
        var snap = reader.LoadSnapshot(withCounts: false, CancellationToken.None);

        var graph = reader.LoadLaneGraph(snap, CancellationToken.None);

        Assert.Same(LaneGraph.Empty, graph);
    }

    [Fact]
    public void A_cancelled_token_throws()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        var reader = new RepoReader(repo.Git);
        var snap = reader.LoadSnapshot(withCounts: false, CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => reader.LoadLaneGraph(snap, cancel.Token));
    }

    [Fact]
    public void GetGraphCommits_reads_parents()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("b.txt", "b\n", "Add b");
        repo.Run("switch", "-q", "main");
        repo.Commit("c.txt", "c\n", "Add c");
        repo.Run("merge", "-q", "--no-edit", "feature");

        var commits = repo.Git.GetGraphCommits([repo.Head], null, 10, firstParent: false, CancellationToken.None);

        Assert.Equal(4, commits.Count);
        var merge = commits[0];
        Assert.Equal(2, merge.Parents.Count);
        Assert.Equal(repo.Run("rev-parse", "HEAD^1"), merge.Parents[0]);
        Assert.Equal(repo.Run("rev-parse", "HEAD^2"), merge.Parents[1]);
        Assert.Empty(commits[commits.Count - 1].Parents);
    }

    // "<lane name>: <short hash>@<column> ..." for one row, oldest commit first.
    private static string Describe(Lane lane) =>
        lane.Name + ": " + string.Join(" ", lane.Commits.Select(c => c.ShortHash + "@" + c.Column));
}
