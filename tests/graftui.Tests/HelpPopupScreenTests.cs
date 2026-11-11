using graftui.Git;
using graftui.Ui.App;
using graftui.Ui.Views;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The ? key on the app's own views, without a terminal: it opens the popup over the current tab, ? and Esc close it, and
/// every other key waits. They share a headless Terminal.Gui session, so they run one at a time.
/// </summary>
[Collection("TerminalGui")]
public sealed class HelpPopupScreenTests : IDisposable
{
    private readonly IApplication _ui;

    public HelpPopupScreenTests()
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
    public void Question_mark_opens_the_help_popup_over_the_branches_tab()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);

        _ui.Keyboard.RaiseKeyDownEvent(new Key('?'));

        Assert.True(app.HelpOpen);
        Assert.Equal("Keys — Branches", app.Art.Title);
        Assert.Equal(KeyBarMode.Branches, app.Keys.Mode);
    }

    [Fact]
    public void A_second_question_mark_closes_the_popup_and_brings_the_preview_back()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(new Key('?'));

        _ui.Keyboard.RaiseKeyDownEvent(new Key('?'));

        Assert.False(app.HelpOpen);
        Assert.Equal("Preview", app.Art.Title);
    }

    [Fact]
    public void Esc_closes_the_popup_instead_of_quitting()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(new Key('?'));

        var esc = Key.Esc;
        _ui.Keyboard.RaiseKeyDownEvent(esc);

        Assert.True(esc.Handled);
        Assert.False(app.HelpOpen);
        Assert.Equal("Preview", app.Art.Title);
    }

    [Fact]
    public void Other_keys_wait_while_the_popup_is_open()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(new Key('?'));
        var selected = app.BranchesTab.Selected;

        var merge = Key.M;
        _ui.Keyboard.RaiseKeyDownEvent(merge);

        Assert.True(merge.Handled);
        Assert.True(app.HelpOpen);
        Assert.False(app.Busy);
        Assert.Equal(selected, app.BranchesTab.Selected);
    }

    [Fact]
    public void Question_mark_waits_while_git_is_busy()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        app.Busy = true;

        var help = new Key('?');
        _ui.Keyboard.RaiseKeyDownEvent(help);

        Assert.True(help.Handled);
        Assert.False(app.HelpOpen);
    }

    [Fact]
    public void The_popup_over_the_changes_tab_names_the_changes_keys()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.Tab);
        Assert.Equal(KeyBarMode.Changes, app.Keys.Mode);

        _ui.Keyboard.RaiseKeyDownEvent(new Key('?'));

        Assert.True(app.HelpOpen);
        Assert.Equal("Keys — Changes", app.Art.Title);
    }
}
