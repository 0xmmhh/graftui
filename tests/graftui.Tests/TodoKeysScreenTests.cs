using System.Drawing;
using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The todo list on the app's own parts, built without a terminal: i opens it, the keys edit the plan and do not leave it, Esc closes it, and
/// Enter either shows why the plan may not start or runs the rebase through the same change as r. They share a headless Terminal.Gui session.
/// </summary>
[Collection("TerminalGui")]
public sealed class TodoKeysScreenTests : IDisposable
{
    private readonly IApplication _ui;

    public TodoKeysScreenTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose() => _ui.Dispose();

    /// <summary>The app on Feature4 with the todo list open, as i opens it after a check that passed.</summary>
    private AppController OpenTodo(TempRepo repo)
    {
        var app = OpenPlain(repo);
        var check = app.RebaseOp.Check(new RebaseRequest("main"));
        Assert.True(check.CanRun, check.Refusal);
        app.BranchesTab.AfterRebaseCheck(check, Layouts.Diverged(), interactive: true);
        return app;
    }

    private AppController OpenPlain(TempRepo repo)
    {
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        app.BuildWindow();
        return app;
    }

    private static string Caption(AppController app) => app.Art.FallbackCaption!.Text;

    [Fact]
    public void The_todo_list_lists_the_plan_under_the_art_and_takes_the_todo_keys()
    {
        using var repo = SequenceRepos.Feature4();

        var app = OpenTodo(repo);

        Assert.Equal(BranchesMode.Todo, app.BranchesTab.Mode);
        Assert.Equal(KeyBarMode.Todo, app.Keys.Mode);
        Assert.Equal(RebaseText.TodoTitle, app.Art.Title);
        Assert.Equal(RebaseText.TodoHint, Caption(app));
        var lines = app.Art.BelowFor!(60, 9).Select(line => string.Concat(line.Select(s => s.Text))).ToList();
        Assert.Equal(4, lines.Count);
        Assert.StartsWith("▸ pick", lines[0], StringComparison.Ordinal);
        Assert.Contains("Add login form", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_does_not_leave_the_todo_list()
    {
        using var repo = SequenceRepos.Feature4();
        var app = OpenTodo(repo);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Tab);

        Assert.Equal(AppController.TabKind.Branches, app.ActiveTab);
        Assert.Equal(BranchesMode.Todo, app.BranchesTab.Mode);
    }

    [Fact]
    public void Down_moves_the_cursor_shift_up_moves_the_row_and_d_drops_it()
    {
        using var repo = SequenceRepos.Feature4();
        var app = OpenTodo(repo);

        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorDown);
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorUp.WithShift);
        _ui.Keyboard.RaiseKeyDownEvent(Key.D);

        var plan = app.BranchesTab.TodoPlan!;
        Assert.Equal("Fix typo in form", plan.Rows[0].Subject);
        Assert.Equal(RebaseAction.Drop, plan.Rows[0].Action);
        Assert.Equal(0, plan.Cursor);
    }

    [Fact]
    public void Ctrl_and_alt_with_a_letter_leave_the_row_action_alone()
    {
        using var repo = SequenceRepos.Feature4();
        var app = OpenTodo(repo);

        _ui.Keyboard.RaiseKeyDownEvent(Key.D.WithCtrl);
        _ui.Keyboard.RaiseKeyDownEvent(Key.S.WithAlt);

        Assert.Equal(RebaseAction.Pick, app.BranchesTab.TodoPlan!.Rows[0].Action);
        Assert.Equal(BranchesMode.Todo, app.BranchesTab.Mode);
    }

    [Fact]
    public void Left_and_right_move_the_cursor_like_up_and_down_and_wrap_and_do_not_move_a_row()
    {
        using var repo = SequenceRepos.Feature4();
        var app = OpenTodo(repo);
        var plan = app.BranchesTab.TodoPlan!;
        var order = string.Join("|", plan.Rows.Select(r => r.Subject));

        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorLeft);
        Assert.Equal(3, plan.Cursor);
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorRight);
        Assert.Equal(0, plan.Cursor);
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorRight);
        Assert.Equal(1, plan.Cursor);
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorLeft);
        Assert.Equal(0, plan.Cursor);

        // Shift and Alt with the left and right arrows do nothing: the rows keep their order and the cursor stays.
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorRight.WithShift);
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorLeft.WithShift);
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorRight.WithAlt);
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorLeft.WithAlt);

        Assert.Equal(order, string.Join("|", app.BranchesTab.TodoPlan!.Rows.Select(r => r.Subject)));
        Assert.Equal(0, app.BranchesTab.TodoPlan!.Cursor);
        Assert.Equal(BranchesMode.Todo, app.BranchesTab.Mode);
    }

    [Fact]
    public void The_todo_art_is_hidden_on_a_short_pane_and_drawn_again_when_the_pane_grows()
    {
        using var repo = SequenceRepos.Feature4();
        _ui.Driver!.SetScreenSize(80, 40);
        var app = OpenTodo(repo);
        var window = app.Art.SuperView!;
        window.Layout(new Size(80, 40));
        Assert.False(app.Art.HideArt);

        _ui.Driver.SetScreenSize(80, 22);
        window.Layout(new Size(80, 22));
        Assert.True(app.Art.HideArt);

        _ui.Driver.SetScreenSize(80, 40);
        window.Layout(new Size(80, 40));
        Assert.False(app.Art.HideArt);
        Assert.Equal(ArtPaneView.ListNeedsWholePane(app.Art.Viewport.Height, compact: true), app.Art.HideArt);
    }

    [Fact]
    public void Esc_closes_the_todo_list_and_gives_the_keys_back()
    {
        using var repo = SequenceRepos.Feature4();
        var app = OpenTodo(repo);

        var key = Key.Esc;
        app.BranchesTab.HandleTodoKey(key);

        Assert.True(key.Handled);
        Assert.Equal(BranchesMode.Browse, app.BranchesTab.Mode);
        Assert.Equal(KeyBarMode.Branches, app.Keys.Mode);
        Assert.Null(app.BranchesTab.TodoPlan);
    }

    [Fact]
    public void Enter_on_a_plan_that_starts_with_a_squash_keeps_the_list_open_with_the_reason_and_runs_nothing()
    {
        using var repo = SequenceRepos.Feature4();
        var head = repo.Head;
        var app = OpenTodo(repo);

        _ui.Keyboard.RaiseKeyDownEvent(Key.S);
        _ui.Keyboard.RaiseKeyDownEvent(Key.Enter);

        Assert.Equal(BranchesMode.Todo, app.BranchesTab.Mode);
        Assert.Equal(RebaseText.FirstSquash, Caption(app));
        Assert.False(app.Busy);
        Assert.Equal(head, repo.Head);
    }

    [Fact]
    public async Task Enter_on_a_plan_that_may_start_runs_it_and_the_commits_are_the_plans()
    {
        using var repo = SequenceRepos.Feature4();
        var app = OpenTodo(repo);

        // Drop the last commit, then start.
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorDown);
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorDown);
        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorDown);
        _ui.Keyboard.RaiseKeyDownEvent(Key.D);
        _ui.Keyboard.RaiseKeyDownEvent(Key.Enter);

        Assert.Equal(BranchesMode.Browse, app.BranchesTab.Mode);
        Assert.Equal(KeyBarMode.Branches, app.Keys.Mode);
        Assert.NotNull(app.ChangesTask);
        await app.ChangesTask!;
        Assert.Equal("Add login form|Fix typo in form|Add password check",
            repo.Run("log", "--reverse", "--format=%s", "main..feature").Replace('\n', '|'));
    }

    [Fact]
    public void i_on_the_current_branch_is_refused_with_the_approved_text()
    {
        using var repo = SequenceRepos.Feature4();
        var app = OpenPlain(repo);
        app.BranchesTab.SelectName("feature");

        _ui.Keyboard.RaiseKeyDownEvent(Key.I);

        Assert.Equal(BranchesMode.Browse, app.BranchesTab.Mode);
        var frame = app.Art.Scene!(ArtGeometry.For(60, 9)!);
        Assert.Equal("Refused: select another branch to rebase feature onto; editing the last commits of feature comes later", frame.Caption!.Text);
    }

    [Fact]
    public void The_list_takes_the_row_keys_but_the_branch_keys_wait_until_it_closes()
    {
        using var repo = SequenceRepos.Feature4();
        var app = OpenTodo(repo);

        _ui.Keyboard.RaiseKeyDownEvent(Key.M);

        Assert.Equal(BranchesMode.Todo, app.BranchesTab.Mode);
        Assert.False(app.Busy);
    }

    /// <summary>Starts the plan's rebase with git, and returns the app opened on the paused repo (as a restart would show it).</summary>
    private AppController OpenPaused(TempRepo repo, IReadOnlyList<RebaseRow> plan)
    {
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main", plan);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(request, check).Kind);

        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }

    [Fact]
    public async Task An_edit_pause_started_from_the_todo_list_is_amended_and_continued_from_the_pause_screen()
    {
        using var repo = SequenceRepos.Feature4();
        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));
        var plan = check.Rows.Select((r, i) => r with { Action = i == 0 ? RebaseAction.Edit : RebaseAction.Pick }).ToList();
        var app = OpenPaused(repo, plan);
        Assert.True(app.IsPaused);

        repo.Write("extra.txt", "extra\n");
        repo.Run("add", "extra.txt");
        app.BranchesTab.HandlePauseKey(Key.Enter);
        await app.ChangesTask!;

        Assert.Equal("Add login form|Fix typo in form|Add password check|WIP debug logging",
            repo.Run("log", "--reverse", "--format=%s", "main..feature").Replace('\n', '|'));
        Assert.Contains("extra.txt", repo.Run("show", "--name-only", "--format=", "feature~3"));
    }

    [Fact]
    public async Task A_reword_pause_is_continued_with_its_message_kept_when_enter_is_pressed()
    {
        using var repo = SequenceRepos.Feature4();
        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));
        var plan = check.Rows.Select((r, i) => r with { Action = i == 0 ? RebaseAction.Reword : RebaseAction.Pick }).ToList();
        var app = OpenPaused(repo, plan);
        Assert.Equal(PauseKind.Reword, app.Snapshot.Paused!.Pause);

        app.BranchesTab.HandlePauseKey(Key.Enter);
        await app.ChangesTask!;

        Assert.Equal("Add login form|Fix typo in form|Add password check|WIP debug logging",
            repo.Run("log", "--reverse", "--format=%s", "main..feature").Replace('\n', '|'));
        Assert.Equal("Add login form", repo.Run("log", "-1", "--format=%B", "feature~3").Trim());
    }
}
