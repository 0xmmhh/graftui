using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;
using Terminal.Gui.App;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// The detail block under the branch list, on screen: which branch it follows, when its files are read and cancelled, when it is kept or
/// blanked, and that the new look off reads nothing. The app is built without a terminal session; a preview or a files read is handed to
/// the tab directly, as the UI thread would receive it (the UI queue never runs here). The repo is the sandbox in a temp folder.
/// </summary>
[Collection("TerminalGui")]
public sealed class BranchDetailScreenTests : IDisposable
{
    private readonly IApplication _ui;
    private readonly string _dir = TempRepo.NewSandboxDir();
    private readonly GitClient _git;
    private readonly RepoReader _reader;

    public BranchDetailScreenTests()
    {
        try
        {
            Sandbox.RebuildAt(_dir, TextWriter.Null);
        }
        catch
        {
            // Dispose never runs when the constructor throws, so clean up the temp root here.
            TempRepo.DeleteSandboxRoot(_dir);
            throw;
        }
        _git = new GitClient(_dir);
        _reader = new RepoReader(_git);
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose()
    {
        _ui.Dispose();
        TempRepo.DeleteSandboxRoot(_dir);
    }

    // 1. The new look off: no block, and no files are read.
    [Fact]
    public void With_the_new_look_off_there_is_no_detail_and_no_files_read()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: false);

        app.BranchesTab.SelectName("feature/picks");
        PreviewLoaded(app, snapshot, "feature/picks");

        Assert.Null(app.BranchList.Detail);
        Assert.Equal(0, app.BranchesTab.IncomingFilesSeq);
    }

    // 2. The block follows the selection: it reads the files of the selected branch's new commits, and shows them once read.
    [Fact]
    public void The_detail_follows_the_selection_and_shows_the_files_once_read()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);

        app.BranchesTab.SelectName("feature/picks");
        PreviewLoaded(app, snapshot, "feature/picks");

        var detail = app.BranchList.Detail;
        Assert.NotNull(detail);
        Assert.Equal("feature/picks", detail.Name);
        Assert.Equal(IncomingFilesState.Reading, detail.FilesState);
        Assert.Equal(1, app.BranchesTab.IncomingFilesSeq);

        var (mergeBase, tip) = FilesKey(snapshot, "feature/picks");
        app.BranchesTab.FilesLoaded(1, (mergeBase, tip), _reader.LoadIncomingFiles(mergeBase, tip, CancellationToken.None));

        var read = app.BranchList.Detail;
        Assert.NotNull(read);
        Assert.Equal(IncomingFilesState.Read, read.FilesState);
        Assert.Equal(3, read.Files?.Count);
    }

    // 3. An answer from a read that was replaced is dropped: the block still reads.
    [Fact]
    public void A_stale_files_read_is_dropped()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);
        app.BranchesTab.SelectName("feature/picks");
        PreviewLoaded(app, snapshot, "feature/picks");
        var (mergeBase, tip) = FilesKey(snapshot, "feature/picks");

        app.BranchesTab.FilesLoaded(0, (mergeBase, tip), _reader.LoadIncomingFiles(mergeBase, tip, CancellationToken.None));

        Assert.Equal(IncomingFilesState.Reading, app.BranchList.Detail?.FilesState);
    }

    // 4. The current branch needs no read: its block shows its own tip and the commits under it.
    [Fact]
    public void The_current_branch_reads_no_files()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);
        var before = app.BranchesTab.IncomingFilesSeq;

        app.BranchesTab.SelectName("main");
        PreviewLoaded(app, snapshot, "main");

        var detail = app.BranchList.Detail;
        Assert.NotNull(detail);
        Assert.True(detail.IsCurrent);
        Assert.NotNull(detail.Layout);
        Assert.Equal(IncomingFilesState.NotNeeded, detail.FilesState);
        Assert.Equal(before, app.BranchesTab.IncomingFilesSeq);
    }

    // 5. Moving the selection stops the files read of the branch left: its answer would be for a selection that has gone.
    [Fact]
    public void Moving_the_selection_cancels_the_files_read_of_the_branch_left()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);
        app.BranchesTab.SelectName("feature/picks");
        PreviewLoaded(app, snapshot, "feature/picks");
        var running = app.BranchesTab.IncomingFilesSeq;

        app.BranchesTab.SelectName("feature/merge-commit");

        Assert.True(app.BranchesTab.IncomingFilesSeq > running);
        Assert.Equal("feature/merge-commit", app.BranchList.Detail?.Name);
        Assert.Equal(IncomingFilesState.NotNeeded, app.BranchList.Detail?.FilesState);
    }

    // 6. A failed read (a null answer) leaves the summary only.
    [Fact]
    public void A_failed_files_read_gives_the_summary_only()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);
        app.BranchesTab.SelectName("feature/picks");
        PreviewLoaded(app, snapshot, "feature/picks");
        var (mergeBase, tip) = FilesKey(snapshot, "feature/picks");

        app.BranchesTab.FilesLoaded(app.BranchesTab.IncomingFilesSeq, (mergeBase, tip), null);

        Assert.Equal(IncomingFilesState.Failed, app.BranchList.Detail?.FilesState);
        Assert.Null(app.BranchList.Detail?.Files);
    }

    // 7. An operation blanks the block at once (no row shows what it may change), and the same snapshot brings it back with the files it had.
    [Fact]
    public void Starting_an_operation_blanks_the_detail_and_ending_it_brings_it_back()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);
        app.BranchesTab.SelectName("feature/picks");
        PreviewLoaded(app, snapshot, "feature/picks");
        var (mergeBase, tip) = FilesKey(snapshot, "feature/picks");
        app.BranchesTab.FilesLoaded(app.BranchesTab.IncomingFilesSeq, (mergeBase, tip), _reader.LoadIncomingFiles(mergeBase, tip, CancellationToken.None));

        app.BranchesTab.StartWork();

        var blank = app.BranchList.Detail;
        Assert.NotNull(blank);
        Assert.Equal("", blank.Name);
        Assert.Null(blank.Layout);
        Assert.Null(blank.Files);

        app.BranchesTab.EndWork();

        var back = app.BranchList.Detail;
        Assert.NotNull(back);
        Assert.Equal("feature/picks", back.Name);
        Assert.Equal(IncomingFilesState.Read, back.FilesState);
        Assert.Equal(3, back.Files?.Count);
    }

    // 8. A new snapshot with the same tips keeps the block's layout, and reads the files again (the files cache is per snapshot).
    [Fact]
    public void A_new_snapshot_with_the_same_tips_reads_the_files_again()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);
        app.BranchesTab.SelectName("feature/picks");
        PreviewLoaded(app, snapshot, "feature/picks");
        var before = app.BranchesTab.IncomingFilesSeq;

        app.ApplySnapshot(ReadSnapshot());

        Assert.NotNull(app.BranchList.Detail?.Layout);
        Assert.Equal(IncomingFilesState.Reading, app.BranchList.Detail?.FilesState);
        Assert.True(app.BranchesTab.IncomingFilesSeq > before);
    }

    // 9. A moved tip is another snapshot: the block has no layout until the preview of the new tip loads, so it says "reading…".
    [Fact]
    public void A_moved_tip_leaves_the_block_waiting_for_its_layout()
    {
        var snapshot = ReadSnapshot();
        var app = OpenApp(snapshot, newUi: true);
        app.BranchesTab.SelectName("feature/picks");
        PreviewLoaded(app, snapshot, "feature/picks");

        _git.Run("branch", "-f", "feature/picks", "main");
        app.ApplySnapshot(ReadSnapshot());

        Assert.Equal("feature/picks", app.BranchList.Detail?.Name);
        Assert.Null(app.BranchList.Detail?.Layout);
    }

    // 10. A detached HEAD: every block says so, and nothing is read.
    [Fact]
    public void A_detached_head_gives_the_detached_block_and_reads_nothing()
    {
        _git.Run("switch", "--detach", "HEAD");
        var snapshot = ReadSnapshot();

        var app = OpenApp(snapshot, newUi: true);
        app.BranchesTab.SelectName("feature/picks");

        Assert.True(app.BranchList.Detail?.Detached == true);
        Assert.Equal(0, app.BranchesTab.IncomingFilesSeq);
    }

    // 11. A pause owns the screen: the block is cleared.
    [Fact]
    public void A_pause_has_no_detail()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main", null);
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        op.Run(request, check);
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);

        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo", newUi: true);
        app.BuildWindow().Layout(_ui.Screen.Size);

        Assert.True(app.IsPaused);
        Assert.Null(app.BranchList.Detail);
    }

    private RepoSnapshot ReadSnapshot() => _reader.LoadSnapshot(withCounts: true, CancellationToken.None);

    private static Branch BranchNamed(RepoSnapshot snapshot, string name) => snapshot.Branches.Single(b => b.Name == name);

    // The preview of a branch as the worker would hand it over: its layout, on the tab's key for the latest request.
    private void PreviewLoaded(AppController app, RepoSnapshot snapshot, string name)
    {
        var branch = BranchNamed(snapshot, name);
        var layout = _reader.LoadLayout(snapshot, branch, CancellationToken.None);
        app.BranchesTab.PreviewLoaded(app.BranchesTab.PreviewSeq, BranchesTab.CacheKey(snapshot, branch), layout);
    }

    // The merge base and tip the files of a branch are read between.
    private (string MergeBase, string Tip) FilesKey(RepoSnapshot snapshot, string name)
    {
        var layout = _reader.LoadLayout(snapshot, BranchNamed(snapshot, name), CancellationToken.None);
        Assert.NotNull(layout.MergeBase);
        return (layout.MergeBase, layout.SelectedTip);
    }

    private AppController OpenApp(RepoSnapshot snapshot, bool newUi)
    {
        var app = new AppController(_ui, _git, _git, snapshot, "sandbox", newUi);
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }
}
