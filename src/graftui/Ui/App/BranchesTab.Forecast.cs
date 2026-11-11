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

    /// <summary>The forecast of a layout if it is known, else null.</summary>
    private MergeForecast? CachedForecast(GraphLayout L) =>
        _forecasts.TryGetValue(ForecastKey(L), out var f) ? f : null;

    /// <summary>
    /// Starts the forecast of a layout, unless it is cached or the layout is the same commit (nothing to forecast). While another
    /// forecast runs, the layout waits as the next one. UI thread only.
    /// </summary>
    private void RequestForecast(GraphLayout L)
    {
        if (L.Kind == GraphKind.Same || CachedForecast(L) is not null)
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
            var f = ForecastOrUnavailable(forecaster, L);
            _app.Ui(() => ForecastDone(L, f, generation));
        }, CancellationToken.None);
    }

    // The forecast is only a preview: any failure says nothing rather than an error.
    private static MergeForecast ForecastOrUnavailable(MergeForecaster forecaster, GraphLayout L)
    {
        try
        {
            return forecaster.Forecast(L, CancellationToken.None);
        }
        catch (Exception)
        {
            return MergeForecast.Unavailable([]);
        }
    }

    /// <summary>Stops the plain preview from being drawn again until the next ShowPreview (the art shows something else now).</summary>
    internal void ForgetShown() => _shown = null;

    /// <summary>
    /// A forecast finished: it is cached (unless the snapshot changed meanwhile), the preview is drawn again when the art shows the same tips,
    /// and the waiting layout (if any) starts. UI thread only.
    /// </summary>
    private void ForecastDone(GraphLayout L, MergeForecast f, int generation)
    {
        _forecastRunning = false;

        // A result from before the last ClearCache is dropped: its snapshot is gone. The waiting layout still runs below.
        if (generation == _forecastGeneration)
        {
            _forecasts[ForecastKey(L)] = f;

            var shown = _shown;
            if (shown is not null && ForecastKey(shown) == ForecastKey(L) && MayDrawPreview)
                ShowPreview(shown);
        }

        var next = _forecastNext;
        _forecastNext = null;
        if (next is not null)
            RequestForecast(next);
    }
}
