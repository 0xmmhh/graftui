using graftui.Git;
using graftui.Ui.App;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The amend box and the rename box open with old text. The cursor must sit after it with nothing selected, so typing adds to the
/// old text instead of replacing it. They share a headless Terminal.Gui session.
/// </summary>
[Collection("TerminalGui")]
public sealed class FieldCursorEndTests : IDisposable
{
    private readonly IApplication _ui;

    public FieldCursorEndTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose() => _ui.Dispose();

    private AppController Open(TempRepo repo)
    {
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }

    [Fact]
    public void The_amend_box_opens_with_the_cursor_at_the_end_of_the_old_subject_and_nothing_selected()
    {
        using var repo = TempRepo.FastForwardable();
        var app = Open(repo);
        var check = app.CommitOp.CheckRepo(amend: true);
        Assert.True(check.CanRun, check.Refusal);

        app.ChangesTab.OpenForm(amend: true, check, "add the login form", "the body", pushedConfirmed: false,
            blankAfterSubject: true);

        Assert.True(app.SubjectField.HasFocus);
        Assert.Equal("add the login form", app.SubjectField.Text);
        Assert.Equal("add the login form".Length, app.SubjectField.InsertionPoint);
        Assert.Equal(0, app.SubjectField.SelectedLength);
    }

    [Fact]
    public void Typing_in_the_amend_subject_adds_to_the_old_text()
    {
        using var repo = TempRepo.FastForwardable();
        var app = Open(repo);
        var check = app.CommitOp.CheckRepo(amend: true);
        Assert.True(check.CanRun, check.Refusal);
        app.ChangesTab.OpenForm(amend: true, check, "add the login form", "the body", pushedConfirmed: false,
            blankAfterSubject: true);

        app.SubjectField.NewKeyDownEvent(Key.Space);
        app.SubjectField.NewKeyDownEvent(Key.V);
        app.SubjectField.NewKeyDownEvent(Key.D2);

        Assert.Equal("add the login form v2", app.SubjectField.Text);
    }

    [Fact]
    public void The_rename_box_opens_with_the_cursor_at_the_end_of_the_old_name_and_nothing_selected()
    {
        using var repo = TempRepo.FastForwardable();
        var app = Open(repo);

        app.BranchesTab.OpenNameForm(rename: true, old: "feature", tip: Layouts.Sel, layout: Layouts.Diverged());

        Assert.True(app.SubjectField.HasFocus);
        Assert.Equal("feature", app.SubjectField.Text);
        Assert.Equal("feature".Length, app.SubjectField.InsertionPoint);
        Assert.Equal(0, app.SubjectField.SelectedLength);
    }

    [Fact]
    public void Typing_in_the_rename_box_adds_to_the_old_name()
    {
        using var repo = TempRepo.FastForwardable();
        var app = Open(repo);
        app.BranchesTab.OpenNameForm(rename: true, old: "feature", tip: Layouts.Sel, layout: Layouts.Diverged());

        app.SubjectField.NewKeyDownEvent(Key.D2);

        Assert.Equal("feature2", app.SubjectField.Text);
    }
}
