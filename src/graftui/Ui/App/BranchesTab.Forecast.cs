using graftui.Git;
using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// The merge forecast in the preview: what `m` would do, read after each preview loads. One worker at a time, and the newest selection
/// goes next. Results are cached by the two tips, the current branch's name (an empty name is a detached HEAD) and the selected name:
/// m merges by name, so two names on one tip can get different answers. The cache is cleared with the snapshot. Only the UI thread
/// touches these fields; the worker sees just its own layout and forecaster.
/// </summary>
internal sealed partial class BranchesTab
{
    private readonly Dictionary<(string Current, string CurrentTip, string Selected, string SelectedTip), MergeForecast> _forecasts = new();

    // A merge-tree process cannot be stopped once it has started, so one runs at a time: holding a key down must not start one per branch.
    private bool _forecastRunning;

    // Bumped by ClearCache. A forecast that started before a clear answers for the old snapshot, so its result is dropped.
    private int _forecastGeneration;

    // The latest layout asked for while a forecast ran. Each request replaces it, so the newest selection is the one computed next.
    private GraphLayout? _forecastNext;

    // Stops the running forecast's git reads when ClearCache drops its snapshot. Made per forecast; only one runs at a time.
    private CancellationTokenSource? _forecastCts;

    // The layout whose plain preview the art shows now. Null while the art shows something else (loading, a flash, a question, the pause).
    private GraphLayout? _shown;

    private static (string Current, string CurrentTip, string Selected, string SelectedTip) ForecastKey(GraphLayout L) =>
        (L.Current, L.CurrentTip, L.Selected, L.SelectedTip);

    /// <summary>The generation a forecast result is checked against. ClearCache bumps it.</summary>
    internal int ForecastGeneration => _forecastGeneration;

    /// <summary>True while a forecast runs or one waits to run next. The tests read it.</summary>
    internal bool ForecastInFlight => _forecastRunning || _forecastNext is not null;

    /// <summary>The layout whose plain preview the art shows, or null. The tests read it.</summary>
    internal GraphLayout? Shown => _shown;

    /// <summary>The forecast of a layout if it is known, else null.</summary>
    private MergeForecast? CachedForecast(GraphLayout L) =>
        _forecasts.TryGetValue(ForecastKey(L), out var f) ? f : null;

    /// <summary>
    /// Starts the forecast of a layout, unless it is cached or there is nothing to forecast: a detached HEAD, the same commit, or
    /// unrelated histories (MergeForecaster answers Unavailable for those). While another forecast runs, the layout waits as the next one.
    /// UI thread only.
    /// </summary>
    private void RequestForecast(GraphLayout L)
    {
        if (L.Detached || L.Kind == GraphKind.Same || L.Kind == GraphKind.Unrelated || CachedForecast(L) is not null)
            return;

        if (_forecastRunning)
        {
            _forecastNext = L;
            return;
        }

        _forecastRunning = true;
        _forecastName = L.Selected;
        var forecaster = _app.Forecaster;
        var generation = _forecastGeneration;

        // No forecast is running now, so the previous source is finished: it is disposed before the new one replaces it.
        Cancellation.CancelAndDispose(ref _forecastCts);
        _forecastCts = new CancellationTokenSource();
        var ct = _forecastCts.Token;
        Task.Run(() =>
        {
            var f = forecaster.Forecast(L, ct);
            _app.Ui(() => ForecastDone(L, f, generation));
        }, CancellationToken.None);
    }

    /// <summary>
    /// Called after a snapshot change cleared the cache. A preview load still running reads the old snapshot: its sequence number moves
    /// on, so its result is dropped and it cannot draw or forecast old tips. Then, when the browse preview owns the art, the shown layout
    /// is forecast again if its tips still hold; if it is gone or its tips moved, the preview is read again (the forecast follows that).
    /// A result that was still running was dropped with the old snapshot, so nothing else would ask again. UI thread only.
    /// </summary>
    internal void SnapshotChanged()
    {
        // The graph of the new snapshot is read even while a result or a question is on screen: git log is read-only and takes no lock.
        EnsureLaneGraph();

        // The marks of the other branches are queued for the new snapshot (the new look only; a snapshot with the same tips keeps them).
        EnsureListForecasts();

        Cancellation.CancelAndDispose(ref _previewCts);
        _previewSeq++;

        if (!MayDrawPreview)
            return;

        var shown = _shown;
        if (shown is null || !ShowsSnapshot(shown, _app.Snapshot))
        {
            RequestPreview();
            return;
        }
        RequestForecast(shown);
    }

    /// <summary>
    /// True when the layout still has the snapshot's current branch and tip, and the selected branch and tip the selection now points at.
    /// A snapshot with no branches never matches.
    /// </summary>
    private bool ShowsSnapshot(GraphLayout shown, RepoSnapshot snap)
    {
        if (snap.Branches.Count == 0 || _selected >= snap.Branches.Count)
            return false;

        var selected = snap.Branches[_selected];
        return shown.Current == snap.CurrentBranch
            && shown.CurrentTip == (snap.HeadHash ?? "")
            && shown.Selected == selected.Name
            && shown.SelectedTip == selected.Hash;
    }

    /// <summary>Stops the plain preview from being drawn again until the next ShowPreview (the art shows something else now).</summary>
    internal void ForgetShown() => _shown = null;

    /// <summary>
    /// A forecast finished: it is cached (unless the snapshot changed meanwhile), the preview is drawn again when the art shows the same tips,
    /// and the waiting layout (if any) starts. It leaves the status bar alone: an error still shown stays until a preview loads.
    /// UI thread only.
    /// </summary>
    internal void ForecastDone(GraphLayout L, MergeForecast f, int generation) => ForecastFinished(L, f, generation, true);

    /// <summary>
    /// The shared end of a forecast. With <paramref name="cache"/> false (a list job whose layout could not be read) the result is not
    /// cached and the preview is not drawn again: only the branch's mark is set, and selecting the branch forecasts it properly.
    /// </summary>
    private void ForecastFinished(GraphLayout L, MergeForecast f, int generation, bool cache)
    {
        _forecastRunning = false;
        _forecastName = null;

        // A result from before the last ClearCache is dropped: its snapshot is gone. The waiting layout still runs below.
        if (generation == _forecastGeneration)
        {
            if (cache)
                _forecasts[ForecastKey(L)] = f;

            // The branch's mark (the new look only). The selected branch's own result comes here too, from the preview. No key set means
            // the list is blank (MarksStale), so no mark is written until the next snapshot is applied.
            if (_newUi && _marksKey is not null)
            {
                _marks[L.Selected] = BranchMarks.Of(L, f);
                _app.BranchList.SetNeedsDraw();
            }

            var shown = _shown;
            if (cache && shown is not null && ForecastKey(shown) == ForecastKey(L) && MayDrawPreview)
                DrawPreview(shown);
        }

        // The selected branch goes before the queue; the queue then fills the slot if nothing took it.
        var next = _forecastNext;
        _forecastNext = null;
        if (next is not null)
            RequestForecast(next);
        PumpListForecasts();
    }
}
