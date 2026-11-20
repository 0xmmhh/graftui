using graftui.Git;
using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// The marks of the branch list (the new look): a forecast of `m` for every other branch, run in the background through the same single
/// forecast slot as the preview's forecast. The selected branch's own forecast comes from the preview and goes first; the queue holds
/// the others, nearest the selection first, at most <see cref="ListForecastMax"/>. The marks belong to one snapshot key (see
/// <see cref="LaneGraphKey"/>): a snapshot with the same tips keeps them, a moved tip clears them. UI thread only, except the worker's read.
/// </summary>
internal sealed partial class BranchesTab
{
    /// <summary>The most branches forecast for one snapshot. The rest keep "·" until they are selected.</summary>
    internal const int ListForecastMax = 100;

    // The branches still to forecast for the snapshot key _listKey, in order. ClearCache empties it.
    private readonly Queue<Branch> _listQueue = new();

    // The snapshot key the queue was built for. Null after ClearCache, so the next EnsureListForecasts builds the queue again.
    private string? _listKey;

    // The mark of each branch by name, for the snapshot key _marksKey. Cleared when the key changes; kept when only the counts changed.
    private readonly Dictionary<string, BranchMark> _marks = new();
    private string? _marksKey;

    // The branch the running forecast is for, or null when none runs. The tests read it.
    private string? _forecastName;

    /// <summary>The mark of each branch by name. The list draws from this dictionary, so it is changed in place, never replaced.</summary>
    internal IReadOnlyDictionary<string, BranchMark> Marks => _marks;

    /// <summary>The branch the running forecast is for, or null. The tests read it.</summary>
    internal string? ForecastRunningName => _forecastName;

    /// <summary>The names still queued, in the order they will run. The tests read it.</summary>
    internal IReadOnlyList<string> ListQueueNames
    {
        get
        {
            var names = new List<string>();
            foreach (var b in _listQueue)
                names.Add(b.Name);
            return names;
        }
    }

    /// <summary>
    /// Builds the queue for the snapshot on screen (branches whose mark is already known are left out), unless it is built for this key
    /// already. A new key clears the marks first. Nothing is queued with the new look off, for a detached HEAD, with no commits, with no
    /// branches, or during a pause. Then the slot is pumped. UI thread only.
    /// </summary>
    internal void EnsureListForecasts()
    {
        if (!_newUi)
            return;

        var snap = _app.Snapshot;
        var key = LaneGraphKey(snap);
        if (key != _marksKey)
        {
            _marks.Clear();
            _marksKey = key;
        }

        if (key == _listKey)
        {
            PumpListForecasts();
            return;
        }

        _listQueue.Clear();
        _listKey = key;
        if (CanForecastList(snap))
        {
            foreach (var b in BranchMarks.Order(snap.Branches, _selected, ListForecastMax))
            {
                if (!_marks.ContainsKey(b.Name))
                    _listQueue.Enqueue(b);
            }
        }
        PumpListForecasts();
    }

    // A detached HEAD refuses every merge, a pause owns the art, and without commits or branches there is nothing to forecast.
    private bool CanForecastList(RepoSnapshot snap) =>
        snap.HeadHash is not null && snap.CurrentBranch.Length > 0 && snap.Branches.Count > 0 && !_app.IsPaused;

    /// <summary>
    /// Starts the next queued branch when the forecast slot is free: nothing runs and no selected layout waits. A branch whose mark is
    /// already known is skipped. UI thread only.
    /// </summary>
    private void PumpListForecasts()
    {
        if (!_newUi || _forecastRunning || _forecastNext is not null || _app.IsPaused)
            return;

        while (_listQueue.Count > 0)
        {
            var b = _listQueue.Dequeue();
            if (_marks.ContainsKey(b.Name))
                continue;

            StartListForecast(b);
            return;
        }
    }

    /// <summary>
    /// Runs one list job on the worker: the forecast layout (one merge-base read), then the forecast. Any failure gives the layout of a
    /// minimal stand-in and an Unavailable forecast, so the mark reads "·" and the slot is always released by ForecastDone.
    /// </summary>
    private void StartListForecast(Branch b)
    {
        _forecastRunning = true;
        _forecastName = b.Name;
        var snap = _app.Snapshot;
        var reader = _app.Reader;
        var forecaster = _app.Forecaster;
        var generation = _forecastGeneration;

        // No forecast is running now, so the previous source is finished: it is disposed before the new one replaces it.
        Cancellation.CancelAndDispose(ref _forecastCts);
        _forecastCts = new CancellationTokenSource();
        var ct = _forecastCts.Token;
        Task.Run(() =>
        {
            GraphLayout layout;
            MergeForecast f;
            try
            {
                layout = reader.LoadForecastLayout(snap, b, ct);
                f = forecaster.Forecast(layout, ct);
            }
            catch (Exception)
            {
                // Not logged: a list mark is not a git command the Commands pane shows. The mark stays "·".
                layout = GraphLayout.Minimal(snap.CurrentBranch, snap.CurrentBranch.Length == 0, b.Name, snap.HeadHash ?? "", b.Hash);
                f = MergeForecast.Unavailable([]);
            }
            _app.Ui(() => ForecastDone(layout, f, generation));
        }, CancellationToken.None);
    }
}
