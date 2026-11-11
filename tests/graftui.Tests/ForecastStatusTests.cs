using graftui.Git;
using graftui.Model;
using graftui.Ui.App;
using Terminal.Gui.App;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The forecast's effect on the preview and the status bar. The app is built without a terminal session, as the other screen tests are,
/// and the forecast's result is handed to the tab directly (a result posted through the app would wait for a loop that never runs here).
/// </summary>
[Collection("TerminalGui")]
public sealed class ForecastStatusTests : IDisposable
{
    private readonly IApplication _ui;

    public ForecastStatusTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose() => _ui.Dispose();

    [Fact]
    public void An_error_on_the_status_bar_survives_a_forecast_that_finishes_after_it()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = OpenApp(repo, snapshot);
        var layout = FeatureLayout(repo, snapshot);
        var forecast = new MergeForecaster(repo.Git).Forecast(layout);

        // The plain preview of the layout is on the art, so the forecast's result redraws it.
        app.BranchesTab.DrawPreview(layout);
        var panelBefore = app.Art.Panel;
        app.Status.ErrorText = "git error: boom";

        app.BranchesTab.ForecastDone(layout, forecast, app.BranchesTab.ForecastGeneration);

        Assert.NotSame(panelBefore, app.Art.Panel);
        Assert.Equal("git error: boom", app.Status.ErrorText);
    }

    [Fact]
    public void A_snapshot_change_asks_for_the_shown_forecast_again_after_its_result_was_cached()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = OpenApp(repo, snapshot);
        var layout = FeatureLayout(repo, snapshot);
        var forecast = new MergeForecaster(repo.Git).Forecast(layout);
        app.BranchesTab.DrawPreview(layout);
        app.BranchesTab.ForecastDone(layout, forecast, app.BranchesTab.ForecastGeneration);
        Assert.False(app.BranchesTab.ForecastInFlight);

        app.ApplySnapshot(snapshot);

        Assert.True(app.BranchesTab.ForecastInFlight);
    }

    [Fact]
    public void A_result_dropped_by_a_snapshot_change_is_followed_by_a_new_forecast_of_the_shown_layout()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = OpenApp(repo, snapshot);
        var layout = FeatureLayout(repo, snapshot);
        var forecast = new MergeForecaster(repo.Git).Forecast(layout);

        // The forecast starts with the preview; the snapshot changes before its result arrives, so the result is dropped.
        app.BranchesTab.DrawPreview(layout);
        var stale = app.BranchesTab.ForecastGeneration;
        app.ApplySnapshot(snapshot);
        app.BranchesTab.ForecastDone(layout, forecast, stale);

        Assert.True(app.BranchesTab.ForecastInFlight);
    }

    // The refresh moves a tip the preview on the art still shows: no forecast of that old layout starts. The preview is read again.
    [Theory]
    [InlineData("feature")]
    [InlineData("main")]
    public void A_refresh_that_moves_a_tip_reads_the_preview_again_instead_of_forecasting_the_old_layout(string moved)
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = OpenApp(repo, snapshot);
        var layout = FeatureLayout(repo, snapshot);
        var forecast = new MergeForecaster(repo.Git).Forecast(layout);
        app.BranchesTab.DrawPreview(layout);
        app.BranchesTab.ForecastDone(layout, forecast, app.BranchesTab.ForecastGeneration);
        Assert.False(app.BranchesTab.ForecastInFlight);

        repo.Run("switch", "-q", moved);
        repo.Commit("moved.txt", "moved\n", "Move " + moved);
        repo.Run("switch", "-q", "main");
        var refreshed = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);

        app.ApplySnapshot(refreshed);

        Assert.False(app.BranchesTab.ForecastInFlight);
        Assert.Null(app.BranchesTab.Shown);
    }

    private AppController OpenApp(TempRepo repo, RepoSnapshot snapshot)
    {
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }

    private static GraphLayout FeatureLayout(TempRepo repo, RepoSnapshot snapshot) =>
        new RepoReader(repo.Git).LoadLayout(snapshot, snapshot.Branches.Single(b => b.Name == "feature"), CancellationToken.None);
}
