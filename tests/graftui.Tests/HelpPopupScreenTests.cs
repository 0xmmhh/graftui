using System.Drawing;
using graftui.Git;
using graftui.Model;
using graftui.Operations;
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

    /// <summary>The app on Feature4 with the todo list open, as i opens it after a check that passed. The window is not laid out yet.</summary>
    private AppController OpenTodo(TempRepo repo)
    {
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        app.BuildWindow();
        var check = app.RebaseOp.Check(new RebaseRequest("main"));
        Assert.True(check.CanRun, check.Refusal);
        app.BranchesTab.AfterRebaseCheck(check, Layouts.Diverged(), interactive: true);
        return app;
    }

    [Fact]
    public void A_question_mark_typed_in_the_commit_subject_types_it_and_opens_no_popup()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(Key.Tab);
        app.ChangesTab.OpenForm(false, app.CommitOp.CheckRepo(amend: false), "", "", false, false);
        Assert.Equal(ChangesMode.Input, app.ChangesTab.Mode);

        var question = new Key('?');
        _ui.Keyboard.RaiseKeyDownEvent(question);

        Assert.False(app.HelpOpen);
        Assert.False(question.Handled);
        // In a running app the focused field takes an unhandled key. The test window never runs, so the field gets it here.
        app.SubjectField.NewKeyDownEvent(question);
        Assert.Equal("?", app.SubjectField.Text);
    }

    [Fact]
    public void A_question_mark_typed_in_the_branch_name_box_types_it_and_opens_no_popup()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        app.BranchesTab.OpenNameForm(false, app.Snapshot.CurrentBranch, app.Snapshot.HeadHash ?? "", Layouts.Diverged());
        Assert.Equal(BranchesMode.Input, app.BranchesTab.Mode);

        var question = new Key('?');
        _ui.Keyboard.RaiseKeyDownEvent(question);

        Assert.False(app.HelpOpen);
        Assert.False(question.Handled);
        // In a running app the focused field takes an unhandled key. The test window never runs, so the field gets it here.
        app.SubjectField.NewKeyDownEvent(question);
        Assert.Equal("?", app.SubjectField.Text);
    }

    [Fact]
    public void A_preview_asked_for_while_the_popup_is_open_does_not_draw_over_it()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(new Key('?'));

        // Moving the selection asks for another branch's preview: its loading caption would replace the popup's title and box.
        app.BranchesTab.MoveSelection(+1);

        Assert.True(app.HelpOpen);
        Assert.Equal("Keys — Branches", app.Art.Title);
    }

    [Fact]
    public void The_popup_closes_when_the_window_shrinks_and_the_art_no_longer_fits()
    {
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo);
        _ui.Keyboard.RaiseKeyDownEvent(new Key('?'));
        Assert.True(app.HelpOpen);

        _ui.Driver!.SetScreenSize(100, 12);
        app.Art.SuperView!.Layout(new Size(100, 12));

        Assert.False(app.ArtFits());
        Assert.False(app.HelpOpen);
        Assert.Equal("Preview", app.Art.Title);
    }

    [Fact]
    public void The_todo_art_is_not_hidden_under_the_popup_and_is_hidden_again_when_the_popup_closes()
    {
        using var repo = SequenceRepos.Feature4();
        _ui.Driver!.SetScreenSize(80, 40);
        var app = OpenTodo(repo);
        var window = app.Art.SuperView!;
        window.Layout(new Size(80, 40));
        _ui.Keyboard.RaiseKeyDownEvent(new Key('?'));
        Assert.True(app.HelpOpen);

        // The pane is now too short for the todo rows beside the art, but the popup is drawn over the art: it stays, and the art stays shown.
        _ui.Driver.SetScreenSize(80, 22);
        window.Layout(new Size(80, 22));
        Assert.True(app.ArtFits());
        Assert.True(app.HelpOpen);
        Assert.False(app.Art.HideArt);

        // Closing the popup asks for the art again, and the short pane hides it.
        _ui.Keyboard.RaiseKeyDownEvent(new Key('?'));
        Assert.False(app.HelpOpen);
        Assert.True(app.Art.HideArt);
    }
}
