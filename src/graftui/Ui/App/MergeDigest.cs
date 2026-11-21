using System.Diagnostics;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>What a merge started with: the pre-merge layout and lane graph, the forecast on screen, and HEAD when m was pressed.</summary>
internal sealed record MergeStart(GraphLayout Layout, LaneGraph Graph, MergeForecast? Forecast, string? Before);

/// <summary>
/// The new look's merge screens (--new-ui, calm mode): the running line, the card after git answers, the refusal card, and g's replay of
/// the merge on the lane graph. No animation plays by itself. UI thread only. The old look never calls it.
/// </summary>
internal sealed class MergeDigest
{
    /// <summary>How long the replay holds its last frame (the card) before the card stays on screen as it is.</summary>
    internal const long ReplayHoldMs = 1200;

    private static readonly Style WarnB = new(Ink.Warn, true);
    private static readonly Style Bad = new(Ink.Bad);

    private readonly AppController _app;

    // The card on screen and what it was made from. Null when no card is shown.
    private Shown? _shown;

    // The play of the merge, while g plays it, and its clock.
    private LaneMergeAnimation? _play;
    private Stopwatch? _clock;

    // The number of the newest replay timer. A timer whose number is older has been stopped and does nothing.
    private int _replaySeq;

    // The card's outcome is null for a refusal (nothing ran, so there is nothing to replay).
    private sealed record Shown(MergeCard Card, MergeStart Start, MergeOutcome? Outcome, CommitInfo? MergeCommit);

    internal MergeDigest(AppController app)
    {
        _app = app;
    }

    /// <summary>The card on screen, or null.</summary>
    internal MergeCard? Card => _shown?.Card;

    /// <summary>True while g plays the replay.</summary>
    internal bool Replaying => _play is not null;

    /// <summary>The merge is running: its running line, and the commands it has run so far.</summary>
    internal void ShowRunning(MergeCheck check, MergeStart start)
    {
        _app.LogCommands(check.Commands);
        var layout = start.Layout;
        var into = layout.Detached ? "HEAD" : layout.Current;
        _app.BranchesTab.ForgetShown();
        _app.Art.Title = "Merging " + layout.Selected + " → " + into;
        _app.Art.ShowSheet((w, h) => PreviewSheets.BuildRunning(new PreviewSheetInput(layout, start.Forecast, start.Graph, true), w, h,
            Glyphs.Current));
        _app.Art.SetNeedsDraw();
    }

    /// <summary>The merge's result: the log lines of the commands, the verify reads, and the card with the snapshot read after the merge.</summary>
    internal void ShowOutcome(MergeStart start, MergeOutcome o, MergeVerdict verdict, RepoSnapshot? after, CommitInfo? mergeCommit)
    {
        // The same order as Animator.FinishAnimation: its lines, then the snapshot, then the result.
        _app.MarkOutcomeShown();
        var mergeIndex = _app.LogCommands(o.Commands);
        if (o.ConflictFiles.Count > 0)
            _app.LogMessage("Conflicting files: " + string.Join(", ", o.ConflictFiles), Bad);
        if (o.Kind == MergeKind.Failed)
            _app.LogMessage("Merge failed: " + o.Message, Bad);
        if (o.Undo is null && (o.Kind == MergeKind.MergeCommit || o.Kind == MergeKind.FastForward))
            _app.LogMessage(o.Message, WarnB);
        _app.LogCommands(verdict.Commands);
        if (!verdict.Verified)
            _app.LogMessage("Not verified: check git status", WarnB);
        if (mergeIndex >= 0)
            _app.Log.SetAnchor(mergeIndex);

        // Still busy, so the snapshot's own preview does not paint over the card.
        if (after is not null)
            _app.ApplySnapshot(after);

        var card = DigestText.FromOutcome(o, verdict.Verified, start.Before, start.Layout.Ahead);
        _shown = new Shown(card, start, o, mergeCommit);
        Show();

        _app.Result = start.Layout.Selected;
        _app.Busy = false;
        _app.Keys.Busy = false;
        _app.Keys.SetNeedsDraw();

        // The merge ran but there is no snapshot after it: the counts are read again.
        if (after is null)
            _app.ReloadCountsAsync();
    }

    /// <summary>A merge that git refused before it ran: nothing changed, so the card says so and nothing can play.</summary>
    internal void ShowRefused(MergeCheck check, MergeStart start)
    {
        _app.LogCommands(check.Commands);
        var reason = check.Refusal ?? "";
        foreach (var line in RefusalText.LogLines(reason))
            _app.LogMessage(line, WarnB);

        var selected = start.Layout.Selected;
        _shown = new Shown(DigestText.Refused(selected, check.CurrentBranch, reason), start, null, null);
        Show();

        _app.Result = selected;
        _app.Busy = false;
        _app.Keys.Busy = false;
        _app.Keys.SetNeedsDraw();
        _app.ReloadCountsAsync();
    }

    /// <summary>
    /// g: plays the merge on the lane graph, from the pre-merge graph to the card's own. The outcome is known from the start, so no git runs.
    /// A card that has no play (up to date, failed, refused) does nothing.
    /// </summary>
    internal void Replay()
    {
        var shown = _shown;
        if (shown is null || shown.Outcome is null || _app.Busy || Replaying || !LaneMergeAnimation.CanPlay(shown.Card))
            return;

        var input = new DigestSheetInput(shown.Start.Layout, shown.Start.Graph, shown.Card);
        _play = new LaneMergeAnimation(input);
        _clock = Stopwatch.StartNew();
        _app.Art.Title = ReplayText.Title(shown.Card);
        _app.Art.ShowSheet(PlaySheet);
        _app.Art.SetNeedsDraw();

        var seq = ++_replaySeq;
        _app.App.AddTimeout(TimeSpan.FromMilliseconds(50), () => ReplayTick(seq));
    }

    /// <summary>Ends the replay and shows the card again.</summary>
    internal void StopReplay()
    {
        if (!Replaying)
            return;

        _replaySeq++;
        _play = null;
        _clock = null;
        Show();
    }

    /// <summary>The card is gone: a replay stops, the card is forgotten, and the list and the key bar lose their merge marks.</summary>
    internal void Dismiss()
    {
        if (_shown is null && _play is null)
            return;

        _replaySeq++;
        _play = null;
        _clock = null;
        _shown = null;
        _app.BranchList.MergedName = null;
        _app.Keys.CanPlay = false;
        _app.BranchList.SetNeedsDraw();
        _app.Keys.SetNeedsDraw();
    }

    // One step of the replay, every 50 ms, from the stopwatch. Returns false when the timer should stop: it was superseded, or the replay has ended.
    private bool ReplayTick(int seq)
    {
        if (seq != _replaySeq || _play is null || _clock is null)
            return false;

        return Advance(_clock.ElapsedMilliseconds);
    }

    /// <summary>
    /// One step of the replay at elapsedMs since g. It runs to the play's Total, holds the card for ReplayHoldMs, then ends (the card stays).
    /// Returns false when the replay has ended. ReplayTick calls it; the tests step it.
    /// </summary>
    internal bool Advance(long elapsedMs)
    {
        var play = _play;
        if (play is null)
            return false;

        if (elapsedMs >= play.Total + ReplayHoldMs)
        {
            StopReplay();
            return false;
        }

        // From the end on the sheet is the card, so its title is too.
        var card = play.Input.Card;
        _app.Art.Title = elapsedMs >= play.Total ? DigestText.Title(card) : ReplayText.Title(card);
        _app.Art.SetNeedsDraw();
        return true;
    }

    // The play's sheet at the clock's time. After the replay has ended (only a tab change can leave the old sheet up) it is the card, or nothing.
    private PreviewSheet PlaySheet(int w, int h)
    {
        var play = _play;
        if (play is not null && _clock is not null)
            return play.Sheet(_clock.ElapsedMilliseconds, w, h, Glyphs.Current);

        var shown = _shown;
        if (shown is not null)
            return PreviewSheets.BuildDigest(new DigestSheetInput(shown.Start.Layout, shown.Start.Graph, shown.Card), w, h, Glyphs.Current);
        return new PreviewSheet(new List<SheetLine>(), new ArtGrid(w + 1, 0), PreviewSheets.HeadRows);
    }

    // The card's sheet, drawn from the pre-merge layout and graph with the card's ghost. The list and the key bar follow the card.
    private void Show()
    {
        var shown = _shown;
        if (shown is null)
            return;

        var card = shown.Card;
        var start = shown.Start;
        _app.BranchesTab.ForgetShown();
        _app.Art.Title = DigestText.Title(card);
        _app.Art.ShowSheet((w, h) => PreviewSheets.BuildDigest(new DigestSheetInput(start.Layout, start.Graph, card), w, h, Glyphs.Current));
        _app.BranchList.MergedName = MergedNameOf(card);
        _app.Keys.CanPlay = card.CanPlay;
        _app.Art.SetNeedsDraw();
        _app.BranchList.SetNeedsDraw();
        _app.Keys.SetNeedsDraw();
    }

    // The row that says "merged": the branch that was merged or fast-forwarded. A stop or a refusal marks no row.
    private static string? MergedNameOf(MergeCard card)
    {
        if (card.Kind == DigestKind.Merged || card.Kind == DigestKind.FastForwarded)
            return card.Branch;
        return null;
    }
}
