using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The preview after a branch change (switch, rebase, interactive rebase) finishes. The change's result owns the art, so the preview must
/// not draw "Loading…" over it, and the art must never be left on "Loading…" after the result. The workers of a change deliver through the
/// app's UI queue, which never runs headless, so each test hands the change's finish to the tab directly, as the worker would.
/// </summary>
[Collection("TerminalGui")]
public sealed class PreviewAfterChangeTests : IDisposable
{
    private readonly IApplication _ui;

    public PreviewAfterChangeTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose() => _ui.Dispose();

    // B1: select a branch, `s`, and the cursor stays where it is. The switch's finish runs with the art showing the preview.
    [Fact]
    public void A_switch_that_finishes_leaves_the_result_on_the_art_not_Loading()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        app.BranchesTab.SelectName("feature");
        app.BranchesTab.DrawPreview(LayoutOf(repo, snapshot, "feature"));

        repo.Run("switch", "-q", "feature");
        var after = ReadSnapshot(repo);
        app.BranchesTab.EndBranchChange(SwitchFinish("feature", after));
        // The counts read that follows applies the same snapshot again.
        app.ApplySnapshot(after);

        Assert.NotNull(app.Result);
        Assert.False(ShowsLoading(app));
    }

    // B2 and B3: a plain rebase (`r`) or an interactive one (`i`) finishes with the preview on the art.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rebase_that_finishes_leaves_the_result_on_the_art_not_Loading(bool interactive)
    {
        using var repo = SequenceRepos.Feature4();
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        var before = LayoutOf(repo, snapshot, "feature");
        app.BranchesTab.DrawPreview(before);

        var check = app.RebaseOp.Check(new RebaseRequest("main"));
        Assert.True(check.CanRun, check.Refusal);
        var outcome = app.RebaseOp.Run(new RebaseRequest("main", ExpectedHead: check.Head, Plan: check.Rows), check);
        Assert.Equal(SequenceOutcomeKind.Done, outcome.Kind);
        var after = ReadSnapshot(repo);
        var finish = BranchesTab.RebaseFinish(outcome, after, check.Rows, before, EngineOf(repo), interactive);

        app.BranchesTab.EndBranchChange(finish);
        app.ApplySnapshot(after);

        Assert.NotNull(app.Result);
        Assert.False(ShowsLoading(app));
    }

    // The hand-back: a key that gives the art back to the browse preview asks for the preview of the current snapshot, and that load draws.
    [Fact]
    public void A_key_that_hands_the_art_back_after_a_change_draws_the_preview_of_the_current_snapshot()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        app.BranchesTab.SelectName("feature");
        app.BranchesTab.DrawPreview(LayoutOf(repo, snapshot, "feature"));

        repo.Run("switch", "-q", "feature");
        var after = ReadSnapshot(repo);
        app.BranchesTab.EndBranchChange(SwitchFinish("feature", after));

        _ui.Keyboard.RaiseKeyDownEvent(Key.CursorDown);

        Assert.Null(app.Result);
        var name = app.BranchesTab.SelectedName();
        var branch = after.Branches.Single(b => b.Name == name);
        var layout = LayoutOf(repo, after, name!);
        app.BranchesTab.PreviewLoaded(app.BranchesTab.PreviewSeq, BranchesTab.CacheKey(after, branch), layout);
        Assert.Same(layout, app.BranchesTab.Shown);
    }

    private static bool ShowsLoading(AppController app) => app.Art.Scene is null && app.Art.FallbackCaption?.Text == "Loading…";

    private static RepoSnapshot ReadSnapshot(TempRepo repo) => new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);

    private static GraphLayout LayoutOf(TempRepo repo, RepoSnapshot snapshot, string name) =>
        new RepoReader(repo.Git).LoadLayout(snapshot, snapshot.Branches.Single(b => b.Name == name), CancellationToken.None);

    /// <summary>The engine the app gives a rebase's finish, reading the repo the test made.</summary>
    private static BranchesTab.RebaseEngine EngineOf(TempRepo repo)
    {
        var reader = new RepoReader(repo.Git);
        return new BranchesTab.RebaseEngine(repo.Git,
            () => reader.LoadSnapshot(withCounts: true, CancellationToken.None),
            (snap, b) => reader.LoadLayout(snap, b, CancellationToken.None));
    }

    /// <summary>The finish a switch gives on success: the result caption and the branch to select.</summary>
    private static BranchesTab.BranchFinish SwitchFinish(string name, RepoSnapshot after) =>
        new(new BranchResult(true, "Switched", new Caption($"✔ Switched to {name}", new Style(Ink.Ok, true)), null), [], [],
            $"graftui: switched to {name}", after, name);

    private AppController OpenApp(TempRepo repo, RepoSnapshot snapshot)
    {
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }
}
