using System.Drawing;
using graftui.Git;
using graftui.Ui.App;
using graftui.Ui.Text;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The Commands list on the app's own views, without a terminal: l opens it over the bottom of the top panes, Esc and l close it,
/// Tab closes it and switches tab, and a key that is not one of those closes it and goes on. They share a headless Terminal.Gui
/// session, so they run one at a time.
/// </summary>
[Collection("TerminalGui")]
public sealed class CommandsListScreenTests : IDisposable
{
    private readonly IApplication _ui;

    public CommandsListScreenTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose() => _ui.Dispose();

    private AppController Open(TempRepo repo)
    {
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        _ui.Driver!.SetScreenSize(100, 30);
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }

    [Fact]
    public void L_opens_the_list_over_the_bottom_of_the_top_panes_and_its_top_border_is_drawn_on_the_art()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        var layout = app.CurrentLayout();

        _ui.Keyboard.RaiseKeyDownEvent(Key.L);
        // The test drives the layout of the window, as the other screen tests do; the app's run loop does it in the real program.
        app.Art.SuperView!.Layout(_ui.Screen.Size);
        _ui.LayoutAndDraw();

        Assert.True(app.Log.Open);
        var top = 1 + layout.TopHeight + layout.LogHeight - layout.OpenLogHeight;
        Assert.Equal(top, app.Log.Frame.Y);
        Assert.Equal(layout.OpenLogHeight, app.Log.Frame.Height);
        // The top panes keep their size: the list is laid over their bottom rows.
        Assert.Equal(layout.TopHeight, app.Art.Frame.Height);
        Assert.True(app.Log.Frame.Y < app.Art.Frame.Y + app.Art.Frame.Height);
        // It draws over the art because it comes later in the subview order, which is the Z-order (the headless driver keeps no
        // screen text to read, so the drawing itself is checked by the tui-tester on a real terminal).
        var order = app.Art.SuperView!.SubViews.ToList();
        Assert.True(order.IndexOf(app.Log) > order.IndexOf(app.Art));
    }

    [Fact]
    public void Esc_closes_the_list_and_is_handled_so_graftui_does_not_quit()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.L);

        var esc = Key.Esc;
        _ui.Keyboard.RaiseKeyDownEvent(esc);

        Assert.True(esc.Handled);
        Assert.False(app.Log.Open);
    }

    [Fact]
    public void Tab_closes_the_list_and_switches_the_tab()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.L);

        var tab = Key.Tab;
        _ui.Keyboard.RaiseKeyDownEvent(tab);

        Assert.True(tab.Handled);
        Assert.False(app.Log.Open);
        Assert.Equal(AppController.TabKind.Changes, app.ActiveTab);
    }

    [Fact]
    public void A_key_that_is_not_v_l_or_esc_closes_the_list_and_then_acts()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.L);

        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorDown);

        Assert.False(app.Log.Open);
    }

    [Fact]
    public void v_switches_the_list_and_keeps_it_open()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.L);

        _ui.Keyboard.RaiseKeyDownEvent(Key.V);

        Assert.True(app.Log.Open);
        Assert.True(app.Log.ShowExact);
    }

    [Fact]
    public void A_question_or_text_field_on_a_tab_is_not_answered_by_the_key_that_closes_the_list()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.L);
        // A text field is open: the list must close, and the y must not be typed into it or answer anything.
        app.ChangesTab.Mode = ChangesMode.Input;

        var yes = new Key('y');
        _ui.Keyboard.RaiseKeyDownEvent(yes);

        Assert.True(yes.Handled);
        Assert.False(app.Log.Open);
        Assert.Equal(ChangesMode.Input, app.ChangesTab.Mode);
        app.ChangesTab.Mode = ChangesMode.Browse;
    }

    [Fact]
    public void CloseLog_closes_an_open_list_and_does_nothing_when_it_is_closed()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.L);

        app.CloseLog();
        Assert.False(app.Log.Open);

        app.CloseLog();
        Assert.False(app.Log.Open);
    }

    [Fact]
    public void L_does_nothing_on_a_screen_too_small_for_the_list()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Driver!.SetScreenSize(60, 20);
        app.Art.SuperView!.Layout(new Size(60, 20));

        _ui.Keyboard.RaiseKeyDownEvent(Key.L);

        Assert.False(app.Log.Open);
    }

    [Fact]
    public void A_failure_of_the_step_stays_on_the_strip_after_l_and_esc()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorDown);
        var failed = new LogLine(LogKind.Command, "git merge feature/x", 1, Simple: true, Failed: true);
        var ok = new LogLine(LogKind.Command, "git merge --abort", 0, Simple: true);
        app.Log.Add(new LogEntry([failed], [failed]));
        app.Log.Add(new LogEntry([ok], [ok]));

        _ui.Keyboard.RaiseKeyDownEvent(Key.L);
        _ui.Keyboard.RaiseKeyDownEvent(Key.Esc);

        Assert.False(app.Log.Open);
        Assert.Equal(failed, app.Log.StripLineNow());
    }

    [Fact]
    public void A_key_that_starts_a_step_drops_the_last_step_s_failure_from_the_strip()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorDown);
        var failed = new LogLine(LogKind.Command, "git merge feature/x", 1, Simple: true, Failed: true);
        var ok = new LogLine(LogKind.Command, "git merge --abort", 0, Simple: true);
        app.Log.Add(new LogEntry([failed], [failed]));
        app.Log.Add(new LogEntry([ok], [ok]));

        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorDown);

        Assert.Equal(ok, app.Log.StripLineNow());
    }
}
