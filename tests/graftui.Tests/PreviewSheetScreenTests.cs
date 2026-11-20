using graftui.Git;
using graftui.Model;
using graftui.Ui.App;
using graftui.Ui.Scenes;
using Terminal.Gui.App;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The Branches preview on screen: the sheet (the lane graph and the incoming commits) when the graph is read, the plain preview
/// otherwise, and the sheet's place among the other screens. The app is built without a terminal session, as the other screen tests
/// are: the graph's read result is handed to the tab directly, since the UI queue never runs here.
/// </summary>
[Collection("TerminalGui")]
public sealed class PreviewSheetScreenTests : IDisposable
{
    private readonly IApplication _ui;

    public PreviewSheetScreenTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose() => _ui.Dispose();

    // 1. Without a graph the old preview is drawn: no sheet, the scene and the title of today's preview.
    [Fact]
    public void Without_a_graph_the_old_preview_is_drawn()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        app.BranchesTab.SelectName("feature");

        app.BranchesTab.DrawPreview(LayoutOf(repo, snapshot, "feature"));

        Assert.Null(app.Art.Sheet);
        Assert.NotNull(app.Art.Scene);
        Assert.Equal("Preview", app.Art.Title);
    }

    // 6. Another screen clears the sheet, and a sheet clears the other screen's parts (the scene and the commit list).
    [Fact]
    public void Another_screen_clears_the_sheet_and_a_sheet_clears_the_old_preview()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        app.BranchesTab.SelectName("feature");
        app.BranchesTab.DrawPreview(LayoutOf(repo, snapshot, "feature"));
        Assert.NotNull(app.Art.Scene);
        Assert.NotNull(app.Art.Panel);

        app.Art.ShowSheet(EmptySheet);
        Assert.NotNull(app.Art.Sheet);
        Assert.Null(app.Art.Scene);
        Assert.Null(app.Art.Panel);

        app.Art.Scene = geo => new SceneFrame(new ArtGrid(geo.Width, geo.Height), null);
        Assert.Null(app.Art.Sheet);

        app.Art.ShowSheet(EmptySheet);
        app.Art.ShowScene(null);
        Assert.Null(app.Art.Sheet);
    }

    // 2. A loaded graph turns the preview into the sheet: the sheet is drawn, the scene is gone, and the title is the sheet's.
    [Fact]
    public void A_loaded_graph_turns_the_preview_into_the_sheet()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        app.BranchesTab.SelectName("feature");
        app.BranchesTab.DrawPreview(LayoutOf(repo, snapshot, "feature"));

        LoadGraph(app, repo, snapshot);

        Assert.NotNull(app.Art.Sheet);
        Assert.Null(app.Art.Scene);
        Assert.Equal("If you press m: feature → main", app.Art.Title);
    }

    // 3. A graph read that is no longer the newest is dropped: the old preview stays.
    [Fact]
    public void A_stale_graph_is_dropped()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        app.BranchesTab.SelectName("feature");
        app.BranchesTab.DrawPreview(LayoutOf(repo, snapshot, "feature"));
        app.BranchesTab.EnsureLaneGraph();
        var graph = new RepoReader(repo.Git).LoadLaneGraph(snapshot, CancellationToken.None);

        app.BranchesTab.LaneGraphLoaded(app.BranchesTab.LaneGraphSeq - 1, BranchesTab.LaneGraphKey(snapshot), graph);

        Assert.Null(app.Art.Sheet);
        Assert.NotNull(app.Art.Scene);
    }

    // 4. A failed read keeps the old preview, and the same snapshot is not read again.
    [Fact]
    public void A_failed_read_keeps_the_old_preview_and_is_not_retried()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        app.BranchesTab.SelectName("feature");
        app.BranchesTab.DrawPreview(LayoutOf(repo, snapshot, "feature"));
        app.BranchesTab.EnsureLaneGraph();
        var seq = app.BranchesTab.LaneGraphSeq;

        app.BranchesTab.LaneGraphLoaded(seq, BranchesTab.LaneGraphKey(snapshot), null);
        app.BranchesTab.RequestPreview();

        Assert.Equal(seq, app.BranchesTab.LaneGraphSeq);
        Assert.Null(app.Art.Sheet);
    }

    // 5. A new snapshot (a commit on main here) reads the graph again.
    [Fact]
    public void A_new_snapshot_reads_the_graph_again()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        app.BranchesTab.EnsureLaneGraph();
        var before = app.BranchesTab.LaneGraphSeq;

        repo.Commit("d.txt", "d\n", "Add d on main");
        app.ApplySnapshot(ReadSnapshot(repo));

        Assert.Equal(before + 1, app.BranchesTab.LaneGraphSeq);
    }

    // 7. The current branch's preview is the "main is checked out" sheet.
    [Fact]
    public void Selecting_the_current_branch_shows_main_is_checked_out()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        app.BranchesTab.SelectName("main");
        LoadGraph(app, repo, snapshot);

        app.BranchesTab.DrawPreview(LayoutOf(repo, snapshot, "main"));

        Assert.NotNull(app.Art.Sheet);
        Assert.Equal("main is checked out", app.Art.Title);
    }

    // 8. A result screen is not replaced by a graph that arrives late.
    [Fact]
    public void A_result_screen_is_not_replaced_by_a_late_graph()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        app.BranchesTab.SelectName("feature");
        app.BranchesTab.DrawPreview(LayoutOf(repo, snapshot, "feature"));
        app.Result = "feature";

        LoadGraph(app, repo, snapshot);

        Assert.Null(app.Art.Sheet);
        Assert.NotNull(app.Art.Scene);
    }

    // Reads the lane graph the way the worker does, and hands it to the tab as the UI thread would receive it.
    private static void LoadGraph(AppController app, TempRepo repo, RepoSnapshot snapshot)
    {
        app.BranchesTab.EnsureLaneGraph();
        var graph = new RepoReader(repo.Git).LoadLaneGraph(snapshot, CancellationToken.None);
        app.BranchesTab.LaneGraphLoaded(app.BranchesTab.LaneGraphSeq, BranchesTab.LaneGraphKey(snapshot), graph);
    }

    // The sheet function used by the tests that only check where the sheet is.
    private static PreviewSheet EmptySheet(int cw, int rows) =>
        new(new List<SheetLine>(), new ArtGrid(cw + 1, 0), PreviewSheets.HeadRows);

    private static RepoSnapshot ReadSnapshot(TempRepo repo) => new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);

    private static GraphLayout LayoutOf(TempRepo repo, RepoSnapshot snapshot, string name) =>
        new RepoReader(repo.Git).LoadLayout(snapshot, snapshot.Branches.Single(b => b.Name == name), CancellationToken.None);

    private AppController OpenApp(TempRepo repo, RepoSnapshot snapshot)
    {
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo");
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }
}
