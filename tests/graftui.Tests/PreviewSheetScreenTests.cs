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
