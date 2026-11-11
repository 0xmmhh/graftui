using graftui.Git;
using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// The merge forecast in the preview: what `m` would do, read after each preview loads. One worker at a time, and the newest selection
/// goes next. Results are cached by the two tips and the current branch's name (an empty name is a detached HEAD). The cache is
/// cleared with the snapshot. Only the UI thread touches these fields; the worker sees just its own layout and forecaster.
/// </summary>
internal sealed partial class BranchesTab
{
    private readonly Dictionary<(string Current, string CurrentTip, string SelectedTip), MergeForecast> _forecasts = new();

    // A merge-tree process cannot be stopped once it has started, so one runs at a time: holding a key down must not start one per branch.
    private bool _forecastRunning;

    // Bumped by ClearCache. A forecast that started before a clear answers for the old snapshot, so its result is dropped.
    private int _forecastGeneration;

    // The latest layout asked for while a forecast ran. Each request replaces it, so the newest selection is the one computed next.
    private GraphLayout? _forecastNext;

    // The layout whose plain preview the art shows now. Null while the art shows something else (loading, a flash, a question, the pause).
    private GraphLayout? _shown;

    private static (string Current, string CurrentTip, string SelectedTip) ForecastKey(GraphLayout L) =>
        (L.Current, L.CurrentTip, L.SelectedTip);

    /// <summary>The generation a forecast result is checked against. ClearCache bumps it.</summary>
    internal int ForecastGeneration => _forecastGeneration;

    /// <summary>True while a forecast runs or one waits to run next. The tests read it.</summary>
    internal bool ForecastInFlight => _forecastRunning || _forecastNext is not null;

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
        var forecaster = _app.Forecaster;
        var generation = _forecastGeneration;
        Task.Run(() =>
        {
            var f = forecaster.Forecast(L);
            _app.Ui(() => ForecastDone(L, f, generation));
        }, CancellationToken.None);
    }

    /// <summary>
    /// Asks for the forecast of the layout the art shows, after a snapshot change cleared the cache. A result that was still running
    /// was dropped with the old snapshot, so nothing else would ask again. Does nothing when the browse preview does not own the art.
    /// UI thread only.
    /// </summary>
    internal void RequestShownForecast()
    {
        var shown = _shown;
        if (shown is not null && MayDrawPreview)
            RequestForecast(shown);
    }

    /// <summary>Stops the plain preview from being drawn again until the next ShowPreview (the art shows something else now).</summary>
    internal void ForgetShown() => _shown = null;

    /// <summary>
    /// A forecast finished: it is cached (unless the snapshot changed meanwhile), the preview is drawn again when the art shows the same tips,
    /// and the waiting layout (if any) starts. It leaves the status bar alone: an error still shown stays until a preview loads.
    /// UI thread only.
    /// </summary>
    internal void ForecastDone(GraphLayout L, MergeForecast f, int generation)
    {
        _forecastRunning = false;

        // A result from before the last ClearCache is dropped: its snapshot is gone. The waiting layout still runs below.
        if (generation == _forecastGeneration)
        {
            _forecasts[ForecastKey(L)] = f;

            var shown = _shown;
            if (shown is not null && ForecastKey(shown) == ForecastKey(L) && MayDrawPreview)
                DrawPreview(shown);
        }

        var next = _forecastNext;
        _forecastNext = null;
        if (next is not null)
            RequestForecast(next);
    }
}
