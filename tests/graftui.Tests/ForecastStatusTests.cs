using graftui.Git;
using graftui.Model;
using graftui.Ui.App;
using Terminal.Gui.App;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// A forecast that finishes after a git error was shown must leave that error on the status bar: only a preview that loads clears it.
/// The app is built without a terminal session, as the other screen tests are, and the forecast's result is handed to the tab directly.
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
        var reader = new RepoReader(repo.Git);
        var snapshot = reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        app.BuildWindow().Layout(_ui.Screen.Size);
        var layout = reader.LoadLayout(snapshot, snapshot.Branches.Single(b => b.Name == "feature"), CancellationToken.None);
        var forecast = new MergeForecaster(repo.Git).Forecast(layout);
        app.Status.ErrorText = "git error: boom";

        app.BranchesTab.ForecastDone(layout, forecast, app.BranchesTab.ForecastGeneration);

        Assert.Equal("git error: boom", app.Status.ErrorText);
    }
}
