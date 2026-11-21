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
/// The new look's merge card on screen: m leaves a card with what happened, the card's title, sheet and list mark, the keys that keep
/// or drop it, g's replay of the old animation, and the help popup over it. The app has no terminal session here and the UI queue
/// never runs, so the merge's worker is run step by step in the test, and each result is handed to the screen as the UI thread would
/// receive it. TempRepo only: never the sandbox or the graftui repo.
/// </summary>
[Collection("TerminalGui")]
public sealed class MergeDigestScreenTests : IDisposable
{
    private readonly IApplication _ui;

    public MergeDigestScreenTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose() => _ui.Dispose();

    // A merge that is clean and makes a merge commit: the card is Merged, verified, with the undo step and the g and u hints.
    [Fact]
    public void The_outcome_shows_the_card()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);

        Merge(app, repo, snapshot, "feature");

        Assert.NotNull(app.Art.Sheet);
        Assert.Null(app.Art.Scene);
        Assert.Equal("Merged feature → main", app.Art.Title);
        Assert.Equal(DigestKind.Merged, app.Digest.Card!.Kind);
        Assert.True(app.Digest.Card.Verified);
        Assert.Equal("feature", app.BranchList.MergedName);
        Assert.True(app.Keys.CanPlay);
        Assert.Equal("feature", app.Result);
        Assert.False(app.Busy);

        var sheet = app.Art.Sheet!(85, 30);
        Assert.EndsWith("g play · u undo", RowText(sheet, 0));
        Assert.Contains("✔ verified", RowText(sheet, 1));
    }

    // The verify reads are logged after the merge's own lines: four lines, the four read-only commands, in the order they ran.
    [Fact]
    public void The_verify_reads_are_logged_after_the_merge()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        var start = StartOf(repo, snapshot, "feature");
        var request = new MergeRequest("feature");
        var check = app.MergeOp.Check(request);
        app.Digest.ShowRunning(check, start);
        var outcome = app.MergeOp.Run(request, check);
        var verdict = MergeVerifier.Check(repo.Git, outcome, start.Before);
        var logged = app.Log.Position;

        app.Digest.ShowOutcome(start, outcome, verdict, ReadSnapshot(repo), null);

        Assert.Equal(logged + outcome.Commands.Count + 4, app.Log.Position);
        Assert.Equal(4, verdict.Commands.Count);
        Assert.Equal(new[] { "symbolic-ref", "-q", "HEAD" }, verdict.Commands[0].Args);
        Assert.Equal(new[] { "rev-parse", "-q", "--verify", "HEAD" }, verdict.Commands[1].Args);
        Assert.Equal(new[] { "rev-parse", "-q", "--verify", "MERGE_HEAD" }, verdict.Commands[2].Args);
        Assert.Equal(new[] { "status", "--porcelain", "--untracked-files=normal" }, verdict.Commands[3].Args);
    }

    // ↓ moves the selection: the card is gone, the list loses its mark and g is no longer listed.
    [Fact]
    public void Moving_the_selection_dismisses_the_card()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        Merge(app, repo, snapshot, "feature");
        Assert.NotNull(app.Digest.Card);

        app.BranchesTab.MoveSelection(+1);

        Assert.Null(app.Digest.Card);
        Assert.Null(app.BranchList.MergedName);
        Assert.False(app.Keys.CanPlay);
        Assert.Null(app.Result);
    }

    // Tab leaves the Branches tab: the card goes with it.
    [Fact]
    public void Tab_dismisses_the_card()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        Merge(app, repo, snapshot, "feature");

        _ui.Keyboard.RaiseKeyDownEvent(Key.Tab);

        Assert.Null(app.Digest.Card);
        Assert.Null(app.BranchList.MergedName);
        Assert.Null(app.Result);
    }

    // A conflict: the merge is stopped, the card says so, there is no u, and the repo is back on main, clean and not merging.
    [Fact]
    public void A_conflict_card()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);

        Merge(app, repo, snapshot, "feature");

        Assert.Equal("Tried feature → main", app.Art.Title);
        var sheet = app.Art.Sheet!(85, 30);
        Assert.StartsWith("✕ Merge stopped: ", RowText(sheet, 0));
        Assert.EndsWith("g play", RowText(sheet, 0));
        Assert.DoesNotContain("u undo", RowText(sheet, 0));
        Assert.Null(app.BranchList.MergedName);
        Assert.True(repo.IsClean);
        Assert.False(repo.Merging);
    }

    // A detached HEAD is refused before git runs: the card says nothing was changed, and g has nothing to play.
    [Fact]
    public void A_refusal_card()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Run("switch", "-q", "--detach");
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);

        Merge(app, repo, snapshot, "feature");

        Assert.Equal("Tried feature → HEAD", app.Art.Title);
        Assert.False(app.Keys.CanPlay);
        Assert.Null(app.BranchList.MergedName);
        Assert.Equal(DigestKind.Refused, app.Digest.Card!.Kind);
        app.Digest.Replay();
        Assert.False(app.Digest.Replaying);
    }

    // g plays the old animation with no git run; Space ends it and the card comes back at once.
    [Fact]
    public void G_replays_without_git_and_space_brings_the_card_back()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        Merge(app, repo, snapshot, "feature");
        var logged = app.Log.Position;
        var head = repo.Head;

        _ui.Keyboard.RaiseKeyDownEvent(Key.G);

        Assert.True(app.Digest.Replaying);
        Assert.NotNull(app.Art.Scene);
        Assert.Null(app.Art.Sheet);
        Assert.Equal(logged, app.Log.Position);
        Assert.Equal(head, repo.Head);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Space);

        Assert.False(app.Digest.Replaying);
        Assert.NotNull(app.Art.Sheet);
        Assert.Equal("Merged feature → main", app.Art.Title);
        Assert.Equal(logged, app.Log.Position);
    }

    // ? over the card and its close: the card is drawn again, with its title.
    [Fact]
    public void Help_over_the_card_comes_back()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        Merge(app, repo, snapshot, "feature");

        app.OpenHelp();
        Assert.True(app.HelpOpen);
        app.CloseHelp();

        Assert.NotNull(app.Art.Sheet);
        Assert.Equal("Merged feature → main", app.Art.Title);
    }

    // u undoes the merge: the card goes at once, and the undo puts HEAD back where m found it.
    [Fact]
    public async Task Undo_dismisses_the_card()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        var before = repo.Head;
        Merge(app, repo, snapshot, "feature");

        app.BranchesTab.StartUndo();

        Assert.Null(app.Digest.Card);
        Assert.NotNull(app.ChangesTask);
        await app.ChangesTask!;
        Assert.Equal(before, repo.Head);
    }

    // With the new look off, m runs the old animation: no card, no mark, and the bar has no g.
    [Fact]
    public async Task The_old_look_has_no_card()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot, newUi: false);
        app.BranchesTab.SelectName("feature");

        app.BranchesTab.StartMerge();
        await app.MergeTask!;

        Assert.Null(app.Digest.Card);
        Assert.Null(app.BranchList.MergedName);
        var state = new BranchesKeyState(BranchCount: 2, CanMerge: true, HasUndo: true, CanSwitch: true, CanRebase: true);
        Assert.DoesNotContain("g", KeyNames(KeyBarView.BranchesItems(state)));
    }

    // Runs m the way StartMerge's worker does, step by step, and hands each result to the screen: Check, the running line when the
    // merge can start, Run, the verify, and the card. A refusal is handed over as a refused card.
    private static void Merge(AppController app, TempRepo repo, RepoSnapshot snapshot, string name)
    {
        var start = StartOf(repo, snapshot, name);
        app.Busy = true;
        app.Digest.Dismiss();
        var request = new MergeRequest(name);
        var check = app.MergeOp.Check(request);
        if (!check.CanMerge)
        {
            app.Digest.ShowRefused(check, start);
            return;
        }

        app.Digest.ShowRunning(check, start);
        var outcome = app.MergeOp.Run(request, check);
        var verdict = MergeVerifier.Check(repo.Git, outcome, start.Before);
        var after = ReadSnapshot(repo);
        var mergeCommit = outcome.Kind == MergeKind.MergeCommit && outcome.NewHead is not null ? app.TryGetCommit(outcome.NewHead) : null;
        app.Digest.ShowOutcome(start, outcome, verdict, after, mergeCommit);
    }

    // The layout and the lane graph of the branch as the screen read them before m, with HEAD when m was pressed.
    private static MergeStart StartOf(TempRepo repo, RepoSnapshot snapshot, string name)
    {
        var reader = new RepoReader(repo.Git);
        var branch = snapshot.Branches.Single(b => b.Name == name);
        var layout = reader.LoadLayout(snapshot, branch, CancellationToken.None);
        var graph = reader.LoadLaneGraph(snapshot, CancellationToken.None);
        return new MergeStart(layout, graph, null, snapshot.HeadHash);
    }

    // The text of one row of a sheet: its segments joined.
    private static string RowText(PreviewSheet sheet, int row)
    {
        var text = "";
        foreach (var line in sheet.Lines)
        {
            if (line.Row != row)
                continue;
            foreach (var seg in line.Segs)
                text += seg.Text;
        }

        return text;
    }

    private static IEnumerable<string> KeyNames(IEnumerable<(string Key, string Label, int Priority)> items) => items.Select(i => i.Key);

    private static RepoSnapshot ReadSnapshot(TempRepo repo) => new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);

    // The new look is on unless a test says otherwise. The screen is 120 × 40, the size the art and the help popup are laid out for.
    private AppController OpenApp(TempRepo repo, RepoSnapshot snapshot, bool newUi = true)
    {
        _ui.Driver!.SetScreenSize(120, 40);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo", newUi);
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }
}
