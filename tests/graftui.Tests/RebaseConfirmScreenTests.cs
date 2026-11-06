using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The confirm screen on the app's own parts, built without a terminal: a check that passes opens it, other keys are ignored, and Esc
/// gives the keys back to the Branches tab. They share a headless Terminal.Gui session, so they run one at a time.
/// </summary>
[Collection("TerminalGui")]
public sealed class RebaseConfirmScreenTests : IDisposable
{
    private readonly IApplication _ui;

    public RebaseConfirmScreenTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose() => _ui.Dispose();

    /// <summary>The app on a repo whose current branch can be rebased onto main, and the check that passed.</summary>
    private (AppController App, RebaseCheck Check) Open(TempRepo repo)
    {
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        app.BuildWindow();
        var check = app.RebaseOp.Check(new RebaseRequest("main"));
        Assert.True(check.CanRun, check.Refusal);
        app.BranchesTab.AfterRebaseCheck(check, Layouts.Diverged());
        return (app, check);
    }

    [Fact]
    public void A_check_that_passes_opens_the_confirm_screen()
    {
        using var repo = SequenceRepos.Feature4();
        var (app, check) = Open(repo);

        Assert.Equal(BranchesMode.Confirm, app.BranchesTab.Mode);
        Assert.Equal(KeyBarMode.Confirm, app.Keys.Mode);
        Assert.Equal(RebaseText.ConfirmTitle, app.Art.Title);
        Assert.Equal(4, check.Rows.Count);
    }

    [Fact]
    public void Other_keys_are_ignored_and_the_confirm_screen_stays_open()
    {
        using var repo = SequenceRepos.Feature4();
        var (app, _) = Open(repo);

        var key = Key.X;
        app.BranchesTab.HandleConfirmKey(key);

        Assert.True(key.Handled);
        Assert.Equal(BranchesMode.Confirm, app.BranchesTab.Mode);
    }

    [Fact]
    public void Esc_gives_the_keys_back_to_the_branches_tab()
    {
        using var repo = SequenceRepos.Feature4();
        var (app, _) = Open(repo);

        app.BranchesTab.HandleConfirmKey(Key.Esc);

        Assert.Equal(BranchesMode.Browse, app.BranchesTab.Mode);
        Assert.Equal(KeyBarMode.Branches, app.Keys.Mode);
    }

    [Fact]
    public void The_app_sends_every_key_but_v_to_the_confirm_screen_so_Tab_does_not_leave_it()
    {
        using var repo = SequenceRepos.Feature4();
        var (app, _) = Open(repo);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Tab);
        Assert.Equal(BranchesMode.Confirm, app.BranchesTab.Mode);
        Assert.Equal(AppController.TabKind.Branches, app.ActiveTab);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Esc);
        Assert.Equal(BranchesMode.Browse, app.BranchesTab.Mode);
    }

    [Fact]
    public void r_on_the_current_branch_is_refused_at_once_and_opens_no_confirm_screen()
    {
        using var repo = SequenceRepos.Feature4();
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        app.BuildWindow();
        app.BranchesTab.SelectName("feature");

        var logged = app.Log.Count;

        _ui.Keyboard.RaiseKeyDownEvent(Key.R);

        Assert.Equal(BranchesMode.Browse, app.BranchesTab.Mode);
        Assert.False(app.Busy);
        Assert.Equal("Refused", app.Art.Title);
        Assert.True(app.Log.Count > logged);
    }
}
