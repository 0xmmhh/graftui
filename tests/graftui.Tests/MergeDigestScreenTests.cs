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
/// or drop it, g's replay of the merge on the lane graph, and the help popup over it. The app has no terminal session here and the UI queue
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

    // g plays the merge on the lane graph with no git run; Space ends it and the card comes back at once.
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
        Assert.Null(app.Art.Scene);
        Assert.NotNull(app.Art.Sheet);
        Assert.Equal("Merging feature → main", app.Art.Title);

        // Row 0 is a spinner, a space and the step; the step depends on the clock, so only the shape is checked.
        var running = RowText(app.Art.Sheet!(85, 30), 0);
        Assert.Contains(running[0], "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏");
        Assert.Equal(' ', running[1]);
        Assert.Equal(logged, app.Log.Position);
        Assert.Equal(head, repo.Head);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Space);

        Assert.False(app.Digest.Replaying);
        Assert.NotNull(app.Art.Sheet);
        Assert.Equal("Merged feature → main", app.Art.Title);
        Assert.Equal(logged, app.Log.Position);
    }

    // The play runs to its total, holds the card for the hold, then ends. From the total on the title is the card's.
    [Fact]
    public void The_play_ends_on_the_card_after_its_hold()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        Merge(app, repo, snapshot, "feature");
        app.Digest.Replay();

        Assert.True(app.Digest.Advance(2599));
        Assert.Equal("Merging feature → main", app.Art.Title);

        Assert.True(app.Digest.Advance(2600));
        Assert.True(app.Digest.Replaying);
        Assert.Equal("Merged feature → main", app.Art.Title);

        Assert.True(app.Digest.Advance(2600 + MergeDigest.ReplayHoldMs - 1));
        Assert.False(app.Digest.Advance(2600 + MergeDigest.ReplayHoldMs));

        Assert.False(app.Digest.Replaying);
        Assert.Equal("Merged feature → main", app.Art.Title);
        Assert.Equal("feature", app.BranchList.MergedName);
        Assert.True(app.Keys.CanPlay);
        Assert.EndsWith("g play · u undo", RowText(app.Art.Sheet!(85, 30), 0));
    }

    // During the hold the play is still on (the last frame is the card): Space, Esc and g each end it at once, and g does not start it again.
    [Fact]
    public void Space_during_the_hold_ends_the_play()
    {
        HoldEndsOn(Key.Space);
    }

    [Fact]
    public void Esc_during_the_hold_ends_the_play()
    {
        HoldEndsOn(Key.Esc);
    }

    [Fact]
    public void G_during_the_hold_ends_the_play_and_does_not_restart_it()
    {
        HoldEndsOn(Key.G);
    }

    // The play has reached its total and is holding; the key ends it, and the card is drawn again.
    private void HoldEndsOn(Key key)
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        Merge(app, repo, snapshot, "feature");
        app.Digest.Replay();
        Assert.True(app.Digest.Advance(2600));
        Assert.True(app.Digest.Replaying);

        _ui.Keyboard.RaiseKeyDownEvent(key);

        Assert.False(app.Digest.Replaying);
        Assert.Equal("Merged feature → main", app.Art.Title);
        Assert.NotNull(app.Art.Sheet);
    }

    // A conflict plays too: row 0 is in the red ink, and the play ends on the stopped card.
    [Fact]
    public void A_conflict_plays_in_red_and_ends_on_the_stopped_card()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        Merge(app, repo, snapshot, "feature");

        app.Digest.Replay();

        Assert.True(app.Digest.Replaying);
        Assert.Equal(Ink.Bad, app.Art.Sheet!(85, 30).Lines[0].Segs[0].Style.Ink);

        Assert.False(app.Digest.Advance(2800 + MergeDigest.ReplayHoldMs));
        Assert.False(app.Digest.Replaying);
        Assert.Equal("Tried feature → main", app.Art.Title);
        Assert.True(repo.IsClean);
    }

    // Merging again finds the branch up to date: the card has no play, the key bar has no g, and g does nothing.
    [Fact]
    public void Up_to_date_has_no_play()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        Merge(app, repo, snapshot, "feature");

        Merge(app, repo, ReadSnapshot(repo), "feature");

        Assert.Equal(DigestKind.UpToDate, app.Digest.Card!.Kind);
        Assert.False(app.Keys.CanPlay);
        _ui.Keyboard.RaiseKeyDownEvent(Key.G);
        Assert.False(app.Digest.Replaying);
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

        // The card's sheet and title leave the art with the card: nothing of the card is drawn while the undo runs.
        Assert.Null(app.Digest.Card);
        Assert.Null(app.Art.Sheet);
        Assert.Equal("Undo", app.Art.Title);
        Assert.NotNull(app.ChangesTask);
        await app.ChangesTask!;
        Assert.Equal(before, repo.Head);
    }

    // m reads the lane graph on screen only when it is read for this snapshot. When it is not (the read is still running, or the
    // snapshot changed), the merge's worker reads it before the merge runs, so the card's graph does not depend on the timing.
    [Fact]
    public void The_merge_reads_the_graph_when_none_is_on_screen()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);

        var graph = app.BranchesTab.MergeGraph(snapshot, null);

        Assert.True(graph.Lanes.Count > 0);
    }

    // The graph on screen is the merge's graph when it is there.
    [Fact]
    public void The_merge_keeps_the_graph_on_screen()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        var shown = new RepoReader(repo.Git).LoadLaneGraph(snapshot, CancellationToken.None);

        Assert.Same(shown, app.BranchesTab.MergeGraph(snapshot, shown));
    }

    // A conflict card draws its lane graph, and g's play draws the graph too (the stop's trail and ✕ are on it).
    [Fact]
    public void A_conflict_card_draws_the_lane_graph_and_plays_on_it()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot);
        Merge(app, repo, snapshot, "feature");

        Assert.True(GraphCells(app.Art.Sheet!(85, 30)) > 0);
        app.Digest.Replay();
        Assert.True(app.Digest.Replaying);
        Assert.True(GraphCells(app.Art.Sheet!(85, 30)) > 0);
    }

    // The number of graph cells that are drawn (not blank) in a sheet's grid.
    private static int GraphCells(PreviewSheet sheet)
    {
        var count = 0;
        for (var y = 0; y < sheet.Graph.Height; y++)
        {
            for (var x = 0; x < sheet.Graph.Width; x++)
            {
                if (sheet.Graph[x, y].Ch != ' ')
                    count++;
            }
        }

        return count;
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

    // Autoplay (the default): a clean merge plays by itself as its card comes up, with no g. The title is the play's, as after g.
    [Fact]
    public void Autoplay_plays_a_clean_merge_without_g()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var app = MergeWithAutoplay(repo, "feature", autoPlay: true);

        AssertPlaying(app);
        Assert.Equal("Merging feature → main", app.Art.Title);
        Assert.Equal(DigestKind.Merged, app.Digest.Card!.Kind);
    }

    // A fast-forward plays by itself too.
    [Fact]
    public void Autoplay_plays_a_fast_forward()
    {
        using var repo = TempRepo.FastForwardable();
        var app = MergeWithAutoplay(repo, "feature", autoPlay: true);

        Assert.Equal(DigestKind.FastForwarded, app.Digest.Card!.Kind);
        AssertPlaying(app);
    }

    // A conflict plays by itself too, in red, and ends on the stopped card with the repo back to clean.
    [Fact]
    public void Autoplay_plays_a_conflict_in_red()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        var app = MergeWithAutoplay(repo, "feature", autoPlay: true);

        AssertPlaying(app);
        Assert.Equal(Ink.Bad, app.Art.Sheet!(85, 30).Lines[0].Segs[0].Style.Ink);
        Assert.True(repo.IsClean);
    }

    // Up to date has nothing to play, so the card stays and nothing starts by itself.
    [Fact]
    public void Autoplay_does_not_play_up_to_date()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var app = MergeWithAutoplay(repo, "feature", autoPlay: true);

        Merge(app, repo, ReadSnapshot(repo), "feature");

        Assert.Equal(DigestKind.UpToDate, app.Digest.Card!.Kind);
        AssertCardOnly(app);
    }

    // A failed merge has no play, so nothing starts by itself.
    [Fact]
    public void Autoplay_does_not_play_a_failed_merge()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot, autoPlay: true);
        var start = StartOf(repo, snapshot, "feature");
        var outcome = new MergeOutcome(MergeKind.Failed, "feature", "main", null, [], "boom", []);
        var verdict = new MergeVerdict(true, true, snapshot.HeadHash, false, true, []);
        app.Busy = true;

        app.Digest.ShowOutcome(start, outcome, verdict, snapshot, null);

        Assert.Equal(DigestKind.Failed, app.Digest.Card!.Kind);
        AssertCardOnly(app);
    }

    // A refusal is never played, with autoplay on or off.
    [Fact]
    public void Autoplay_does_not_play_a_refusal()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Run("switch", "-q", "--detach");
        var app = MergeWithAutoplay(repo, "feature", autoPlay: true);

        Assert.Equal(DigestKind.Refused, app.Digest.Card!.Kind);
        AssertCardOnly(app);
    }

    // A key during the autoplay ends it, as it ends g's replay: Space shows the card at once.
    [Fact]
    public void Space_during_autoplay_ends_it_and_shows_the_card()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var app = MergeWithAutoplay(repo, "feature", autoPlay: true);
        AssertPlaying(app);

        _ui.Keyboard.RaiseKeyDownEvent(Key.Space);

        AssertCardOnly(app);
        Assert.Equal("Merged feature → main", app.Art.Title);
    }

    // A count read that lands while the autoplay plays (a merge with no snapshot after it reads the counts again) does not cut the play short.
    [Fact]
    public void Autoplay_survives_a_counts_read()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var app = MergeWithAutoplay(repo, "feature", autoPlay: true);
        AssertPlaying(app);

        app.CountsRead(ReadSnapshot(repo), null, app.SnapshotSeq, null);

        AssertPlaying(app);
        Assert.Equal("Merging feature → main", app.Art.Title);
    }

    // u during the autoplay ends the play as any key does, and starts the undo: the card goes and HEAD is back where m found it.
    [Fact]
    public async Task U_during_autoplay_ends_it_and_undoes_the_merge()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var before = repo.Head;
        var app = MergeWithAutoplay(repo, "feature", autoPlay: true);
        AssertPlaying(app);

        _ui.Keyboard.RaiseKeyDownEvent(Key.U);

        AssertCardOnly(app);
        Assert.Null(app.Digest.Card);
        Assert.NotNull(app.ChangesTask);
        await app.ChangesTask!;
        Assert.Equal(before, repo.Head);
    }

    // With autoplay off (--calm) the card stays after a merge, and g is what plays it.
    [Fact]
    public void Calm_leaves_the_card_until_g()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        var app = MergeWithAutoplay(repo, "feature", autoPlay: false);

        AssertCardOnly(app);
        Assert.Equal("Merged feature → main", app.Art.Title);

        _ui.Keyboard.RaiseKeyDownEvent(Key.G);

        AssertPlaying(app);
    }

    // Opens the app with autoplay on or off and runs m on the branch, as the Merge helper does. The snapshot is read after any setup in the test.
    private AppController MergeWithAutoplay(TempRepo repo, string name, bool autoPlay)
    {
        var snapshot = ReadSnapshot(repo);
        var app = OpenApp(repo, snapshot, autoPlay: autoPlay);
        Merge(app, repo, snapshot, name);
        return app;
    }

    // The play is on: Replaying is true.
    private static void AssertPlaying(AppController app)
    {
        Assert.True(app.Digest.Replaying);
    }

    // No play: the card is all there is, and nothing is replaying.
    private static void AssertCardOnly(AppController app)
    {
        Assert.False(app.Digest.Replaying);
    }

    // Runs m the way StartMerge's worker does, step by step, and hands each result to the screen: Check, the running line when the
    // merge can start, Run, the verify, and the card. A refusal is handed over as a refused card.
    internal static void Merge(AppController app, TempRepo repo, RepoSnapshot snapshot, string name)
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

    // The new look is on unless a test says otherwise. Autoplay is off unless a test says otherwise, so the g tests start from the card
    // and press g themselves; the autoplay tests pass autoPlay: true. The screen is 120 × 40, the size the art and the help popup are laid out for.
    private AppController OpenApp(TempRepo repo, RepoSnapshot snapshot, bool newUi = true, bool autoPlay = false)
    {
        _ui.Driver!.SetScreenSize(120, 40);
        var app = new AppController(_ui, repo.Git, repo.Git, snapshot, "repo", newUi, autoPlay);
        app.BuildWindow().Layout(_ui.Screen.Size);
        return app;
    }
}
