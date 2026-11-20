using System.Drawing;
using graftui.Git;
using graftui.Ui.App;
using graftui.Ui.Text;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Time;
using Terminal.Gui.ViewBase;

namespace graftui.Tests;

/// <summary>
/// The new look's frame on the app's own views, without a terminal: the tab row, the panes' rows, the Commands dock and the old look
/// unchanged. The headless driver keeps no screen text, so these check frames and properties; the drawing is the tester's job.
/// They share a headless Terminal.Gui session, so they run one at a time.
/// </summary>
[Collection("TerminalGui")]
public sealed class ScreenFrameScreenTests : IDisposable
{
    private readonly IApplication _ui;

    public ScreenFrameScreenTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose() => _ui.Dispose();

    private AppController Open(TempRepo repo, int cols, int rows, bool newUi)
    {
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        _ui.Driver!.SetScreenSize(cols, rows);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo", newUi);
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }

    // Lays the window out again at the current screen size, as the run loop does after a key or a resize.
    private void Relayout(AppController app, int cols, int rows)
    {
        app.Art.SuperView!.Layout(new Size(cols, rows));
        _ui.LayoutAndDraw();
    }

    private static void AssertFrame(View view, int x, int y, int width, int height)
    {
        Assert.Equal(x, view.Frame.X);
        Assert.Equal(y, view.Frame.Y);
        Assert.Equal(width, view.Frame.Width);
        Assert.Equal(height, view.Frame.Height);
    }

    [Fact]
    public void The_new_look_lays_out_the_frame_at_120x40()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo, 120, 40, newUi: true);

        AssertFrame(app.Status, 0, 0, 120, 1);
        AssertFrame(app.Tabs!, 0, 1, 120, 1);
        AssertFrame(app.BranchList, 0, 2, 31, 32);
        AssertFrame(app.Art, 31, 2, 89, 32);
        AssertFrame(app.Log, 0, 34, 120, 5);
        Assert.Equal(39, app.Keys.Frame.Y);
    }

    [Fact]
    public void The_dock_is_one_line_at_80x24()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo, 80, 24, newUi: true);

        AssertFrame(app.Log, 0, 22, 80, 1);
        Assert.Equal(20, app.BranchList.Frame.Height);
    }

    [Fact]
    public void L_opens_the_list_down_to_the_docks_last_row_and_Esc_puts_the_dock_back()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo, 120, 40, newUi: true);

        _ui.Keyboard.RaiseKeyDownEvent(Key.L);
        Relayout(app, 120, 40);

        Assert.True(app.Log.Open);
        AssertFrame(app.Log, 0, 27, 120, 12);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Esc);
        Relayout(app, 120, 40);

        Assert.False(app.Log.Open);
        AssertFrame(app.Log, 0, 34, 120, 5);
    }

    [Fact]
    public void Tab_moves_the_tab_row_and_the_changes_pane_takes_the_panes_place()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo, 120, 40, newUi: true);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Tab);
        Relayout(app, 120, 40);

        Assert.True(app.Tabs!.ChangesActive);
        AssertFrame(app.ChangesList, 0, 2, 31, 32);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Tab);

        Assert.False(app.Tabs!.ChangesActive);
    }

    [Fact]
    public void The_new_look_squares_the_panes_and_drops_the_list_titles()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo, 120, 40, newUi: true);

        Assert.True(app.BranchList.SquareCorners);
        Assert.True(app.ChangesList.SquareCorners);
        Assert.True(app.Art.SquareCorners);
        Assert.True(app.Log.SquareCorners);
        Assert.Null(app.BranchList.TitleSegs);
        Assert.Null(app.ChangesList.TitleSegs);
    }

    [Fact]
    public void The_old_look_is_unchanged_at_120x40()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo, 120, 40, newUi: false);
        var layout = app.CurrentLayout();

        Assert.Null(app.Tabs);
        Assert.False(layout.Framed);
        // The old numbers: the panes from row 1 for 37 rows, the strip on row 38, the key bar on the last row.
        AssertFrame(app.BranchList, 0, 1, layout.BranchWidth, 37);
        AssertFrame(app.Log, 0, 38, 120, 1);
        Assert.False(app.BranchList.SquareCorners);
        Assert.False(app.ChangesList.SquareCorners);
        Assert.False(app.Art.SquareCorners);
        Assert.False(app.Log.SquareCorners);
        Assert.NotNull(app.BranchList.TitleSegs);
    }

    [Fact]
    public void Too_small_hides_the_tab_row_the_panes_and_the_dock()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo, 60, 20, newUi: true);

        Assert.Equal(0, app.Tabs!.Frame.Height);
        Assert.Equal(0, app.BranchList.Frame.Width);
        Assert.Equal(0, app.Art.Frame.Width);
        Assert.Equal(0, app.Log.Frame.Height);
    }

    [Fact]
    public void A_resize_moves_the_dock_to_the_strip()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo, 120, 40, newUi: true);

        _ui.Driver!.SetScreenSize(100, 24);
        Relayout(app, 100, 24);

        AssertFrame(app.Log, 0, 22, 100, 1);
    }
}
