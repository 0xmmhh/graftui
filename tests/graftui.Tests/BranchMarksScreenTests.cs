using graftui.Git;
using graftui.Model;
using graftui.Ui.App;
using graftui.Ui.Text;
using Terminal.Gui.App;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The branch marks on screen: which forecasts are queued and in what order, when a result sets a mark, and when the marks are kept or
/// cleared. The app is built without a terminal session, as the other screen tests are, and a forecast's result is handed to the tab
/// directly (the UI queue never runs here). The repo is the sandbox in a temp folder.
/// </summary>
[Collection("TerminalGui")]
public sealed class BranchMarksScreenTests : IDisposable
{
    private readonly IApplication _ui;
    private readonly string _dir = TempRepo.NewSandboxDir();
    private readonly GitClient _git;
    private readonly RepoReader _reader;

    public BranchMarksScreenTests()
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
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose()
    {
        _ui.Dispose();
        TempRepo.DeleteSandboxRoot(_dir);
    }

    // 1. The new look is off: no branch is queued, no forecast runs, a finished forecast sets no mark, and the list keeps its star.
    [Fact]
    public void With_the_new_look_off_nothing_is_queued_and_no_mark_is_set()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: false);

        Assert.False(app.BranchList.NewLook);
        Assert.Empty(app.BranchesTab.ListQueueNames);
        Assert.Null(app.BranchesTab.ForecastRunningName);

        MarkBranch(app, snapshot, "feature/conflict");

        Assert.Empty(app.BranchesTab.Marks);
    }

    // 2. The first preview queues the other branches: the selected one runs first, then the rest in order from the selection.
    [Fact]
    public void Every_other_branch_is_queued_and_the_selected_one_runs_first()
    {
        var app = OpenApp(ReadSnapshot(), newUi: true);

        Assert.True(app.BranchList.NewLook);
        Assert.Equal("feature/conflict", app.BranchesTab.ForecastRunningName);
        Assert.Equal(
            new[] { "feature/fast-forward", "feature/ff-to-merge", "feature/merge-commit", "feature/picks" },
            app.BranchesTab.ListQueueNames);
    }

    // 3. A finished forecast sets the mark of its branch.
    [Theory]
    [InlineData("feature/conflict", BranchMark.Conflict)]
    [InlineData("feature/fast-forward", BranchMark.FastForward)]
    [InlineData("feature/merge-commit", BranchMark.Merge)]
    public void A_finished_forecast_sets_the_mark(string name, BranchMark expected)
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);

        MarkBranch(app, snapshot, name);

        Assert.Equal(expected, app.BranchesTab.Marks[name]);
    }

    // 4. A result from before the last ClearCache is dropped: no mark is set for it.
    [Fact]
    public void A_stale_forecast_sets_no_mark()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);
        var layout = _reader.LoadForecastLayout(snapshot, BranchNamed(snapshot, "feature/conflict"), CancellationToken.None);
        var forecast = new MergeForecaster(_git).Forecast(layout);

        app.BranchesTab.ForecastDone(layout, forecast, app.BranchesTab.ForecastGeneration - 1);

        Assert.False(app.BranchesTab.Marks.ContainsKey("feature/conflict"));
    }

    // 5. A snapshot with the same tips (a counts reload) keeps the marks, and the marked branch is not queued again.
    [Fact]
    public void The_same_tips_keep_the_marks_and_the_marked_branch_is_not_queued_again()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);
        MarkBranch(app, snapshot, "feature/conflict");

        app.ApplySnapshot(ReadSnapshot());

        Assert.Equal(BranchMark.Conflict, app.BranchesTab.Marks["feature/conflict"]);
        Assert.DoesNotContain("feature/conflict", app.BranchesTab.ListQueueNames);
    }

    // 6. A moved tip is another snapshot key: the marks are cleared, and the branches are queued again.
    [Fact]
    public void A_moved_tip_clears_the_marks_and_queues_the_branches_again()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);
        MarkBranch(app, snapshot, "feature/conflict");

        _git.Run("branch", "-f", "feature/picks", "main");
        app.ApplySnapshot(ReadSnapshot());

        Assert.Empty(app.BranchesTab.Marks);
        Assert.Contains("feature/picks", app.BranchesTab.ListQueueNames);
    }

    // 7. The selection goes before the queue: a layout that the preview asks for while a list job runs is the next forecast.
    [Fact]
    public void The_selected_branch_goes_before_the_queue()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);
        Assert.Equal("feature/conflict", app.BranchesTab.ForecastRunningName);

        app.BranchesTab.SelectName("feature/merge-commit");
        app.BranchesTab.DrawPreview(_reader.LoadLayout(snapshot, BranchNamed(snapshot, "feature/merge-commit"), CancellationToken.None));
        MarkBranch(app, snapshot, "feature/conflict");

        Assert.Equal("feature/merge-commit", app.BranchesTab.ForecastRunningName);
    }

    // 8. A detached HEAD queues nothing: no branch can be merged into it.
    [Fact]
    public void A_detached_head_queues_nothing()
    {
        _git.Run("switch", "--detach", "HEAD");
        var snapshot = ReadSnapshot();

        var app = OpenApp(snapshot, newUi: true);

        Assert.Empty(app.BranchesTab.ListQueueNames);
        Assert.Null(app.BranchesTab.ForecastRunningName);
    }

    private RepoSnapshot ReadSnapshot() => _reader.LoadSnapshot(withCounts: true, CancellationToken.None);

    private static Branch BranchNamed(RepoSnapshot snapshot, string name) => snapshot.Branches.Single(b => b.Name == name);

    // Forecasts the branch the way the worker does, and hands the result to the tab as the UI thread would receive it.
    private void MarkBranch(AppController app, RepoSnapshot snapshot, string name)
    {
        var layout = _reader.LoadForecastLayout(snapshot, BranchNamed(snapshot, name), CancellationToken.None);
        var forecast = new MergeForecaster(_git).Forecast(layout);
        app.BranchesTab.ForecastDone(layout, forecast, app.BranchesTab.ForecastGeneration);
    }

    private AppController OpenApp(RepoSnapshot snapshot, bool newUi)
    {
        var app = new AppController(_ui, _git, _git, snapshot, "sandbox", newUi);
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }
}
