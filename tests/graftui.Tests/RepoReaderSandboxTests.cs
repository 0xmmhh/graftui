using graftui.Git;
using graftui.Model;

namespace graftui.Tests;

/// <summary>What the screen reads from the sandbox: branch counts and preview layouts.</summary>
public sealed class RepoReaderSandboxTests : IClassFixture<ReadOnlySandbox>
{
    private readonly RepoReader _reader;

    public RepoReaderSandboxTests(ReadOnlySandbox sandbox)
    {
        _reader = sandbox.Reader;
    }

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
    public void The_selected_branch_lists_every_commit_its_ahead_count_counts_newest_first_with_dates()
    {
        var snap = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var branch = snap.Branches.Single(b => b.Name == "feature/ff-to-merge");

        var layout = _reader.LoadLayout(snap, branch, CancellationToken.None);

        Assert.Equal(
            new[] { "Merge branch 'side-for-ffm' into feature/ff-to-merge", "Add side file", "Add ffm file" },
            layout.SelectedNew.Select(c => c.Subject));
        Assert.Empty(layout.CurrentNew);
        Assert.All(layout.SelectedNew, c => Assert.Equal(new DateTime(2025, 1, 1), c.Date!.Value.UtcDateTime.Date));
    }

    [Fact]
    public void The_current_branch_lists_the_commits_it_has_that_the_selected_branch_lacks()
    {
        var snap = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var branch = snap.Branches.Single(b => b.Name == "feature/picks");

        var layout = _reader.LoadLayout(snap, branch, CancellationToken.None);

        Assert.Equal(3, layout.SelectedNew.Count);
        Assert.Equal(new[] { "Change line 1 on main" }, layout.CurrentNew.Select(c => c.Subject));
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
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Run("branch", "old", "main~1");
        var reader = new RepoReader(repo.Git);
        var snap = reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var old = snap.Branches.Single(b => b.Name == "old");

        var layout = reader.LoadLayout(snap, old, CancellationToken.None);

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

    [Theory]
    [InlineData("feature/conflict", ForecastKind.Conflict)]
    [InlineData("feature/fast-forward", ForecastKind.FastForward)]
    [InlineData("feature/ff-to-merge", ForecastKind.FastForward)]
    [InlineData("feature/merge-commit", ForecastKind.Clean)]
    [InlineData("feature/picks", ForecastKind.Clean)]
    public void The_forecast_layout_gives_the_same_kind_and_forecast_as_the_full_layout(string branch, ForecastKind expected)
    {
        var snap = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var sel = snap.Branches.Single(b => b.Name == branch);
        var forecaster = new MergeForecaster(_git);

        var full = _reader.LoadLayout(snap, sel, CancellationToken.None);
        var light = _reader.LoadForecastLayout(snap, sel, CancellationToken.None);

        Assert.Equal(full.Kind, light.Kind);
        Assert.Equal(full.MergeBase, light.MergeBase);
        Assert.Equal(full.CurrentTip, light.CurrentTip);
        Assert.Equal(full.SelectedTip, light.SelectedTip);
        Assert.Equal(expected, forecaster.Forecast(light).Kind);
        Assert.Equal(expected, forecaster.Forecast(full).Kind);
        Assert.Equal("", _git.RunRead(CancellationToken.None, "status", "--porcelain").StdOut.Trim());
    }

    [Fact]
    public void The_forecast_layout_of_the_current_branch_is_the_same_commit_with_no_lists()
    {
        var snap = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var main = snap.Branches.Single(b => b.Name == "main");

        var layout = _reader.LoadForecastLayout(snap, main, CancellationToken.None);

        Assert.Equal(GraphKind.Same, layout.Kind);
        Assert.Equal(snap.HeadHash, layout.MergeBase);
        Assert.Empty(layout.SelectedOwn);
        Assert.Empty(layout.SelectedNew);
    }

    [Fact]
    public void A_branch_behind_main_forecasts_as_up_to_date()
    {
        _git.Run("branch", "old", "main~1");
        var snap = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var old = snap.Branches.Single(b => b.Name == "old");

        var layout = _reader.LoadForecastLayout(snap, old, CancellationToken.None);

        Assert.Equal(GraphKind.UpToDate, layout.Kind);
    }

    [Fact]
    public void A_branch_with_no_common_history_forecasts_as_unrelated_with_no_merge_base()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Main commit");
        repo.Run("switch", "-q", "--orphan", "lone");
        repo.Commit("lone.txt", "x\n", "Lone commit");
        repo.Run("switch", "-q", "main");
        var reader = new RepoReader(repo.Git);
        var snap = reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var lone = snap.Branches.Single(b => b.Name == "lone");

        var layout = reader.LoadForecastLayout(snap, lone, CancellationToken.None);

        Assert.Equal(GraphKind.Unrelated, layout.Kind);
        Assert.Null(layout.MergeBase);
    }
}
