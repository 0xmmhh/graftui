using graftui.Git;
using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// The marks of the branch list (the new look): a forecast of `m` for every other branch, run in the background through the same single
/// forecast slot as the preview's forecast. The selected branch's own forecast comes from the preview and goes first; the queue holds
/// the others, nearest the selection first, at most <see cref="ListForecastMax"/>.
/// Marks belong to one snapshot key (see <see cref="LaneGraphKey"/>). A new key shows "·" on every row at once; the marks of the last
/// <see cref="MarkKeysKept"/> keys are kept, so an undo back to one of them shows its marks without a new read. A mark of "·" (Unknown)
/// does not count as done: the branch is forecast again for the same key on the next snapshot.
/// Limit: the key is made of the branch names and tips, not of the merge configuration. A change to merge.ff or
/// branch.&lt;current&gt;.mergeOptions with the same tips is seen by the marks only after the next tip change. The preview forecasts the
/// selected branch again each time it is shown after a snapshot change, so the selected branch's detail is never stale.
/// UI thread only, except the worker's read.
/// </summary>
internal sealed partial class BranchesTab
{
    /// <summary>The most branches forecast for one snapshot. The rest keep "·" until they are selected.</summary>
    internal const int ListForecastMax = 100;

    /// <summary>How many snapshot keys keep their marks, the current one included.</summary>
    internal const int MarkKeysKept = 4;

    // The branches still to forecast for the snapshot key _listKey, in order. ClearCache empties it.
    private readonly Queue<Branch> _listQueue = new();

    // The snapshot key the queue was built for. Null after ClearCache, so the next EnsureListForecasts builds the queue again.
    private string? _listKey;

    // The mark of each branch by name, for the snapshot key _marksKey. One dictionary for the life of the tab (the list draws from it), changed in place.
    private readonly Dictionary<string, BranchMark> _marks = new();
    private string? _marksKey;

    // The marks of the other recent keys, oldest first. Each is a copy, taken when its key was left.
    private readonly List<(string Key, Dictionary<string, BranchMark> Marks)> _savedMarks = new();

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
    /// already. A new key switches the marks first. Nothing is queued with the new look off, for a detached HEAD, with no commits, with no
    /// branches, or during a pause. Then the slot is pumped. UI thread only.
    /// </summary>
    internal void EnsureListForecasts()
    {
        if (!_newUi)
            return;

        var snap = _app.Snapshot;
        var key = LaneGraphKey(snap);
        if (key != _marksKey)
            SwitchMarksTo(key);

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
                if (!IsMarked(b.Name))
                    _listQueue.Enqueue(b);
            }
        }
        PumpListForecasts();
    }

    /// <summary>
    /// The snapshot on screen is about to be replaced: a write has finished and its snapshot is being read. Every row shows "·" until the
    /// new snapshot is applied. The marks of the key left are kept, so a snapshot with that key gets them back at once. A forecast still
    /// running is cancelled and its result is dropped (the generation moves on), so no mark of the old key can land in the blank table.
    /// The new look only. UI thread only.
    /// </summary>
    internal void MarksStale()
    {
        if (!_newUi)
            return;

        if (_marksKey is not null)
            RememberMarks(_marksKey, new Dictionary<string, BranchMark>(_marks));
        _marks.Clear();
        _marksKey = null;

        _listQueue.Clear();
        _listKey = null;
        _forecastNext = null;
        Cancellation.CancelAndDispose(ref _forecastCts);
        _forecastGeneration++;
        _app.BranchList.SetNeedsDraw();
    }

    /// <summary>
    /// Makes the marks of <paramref name="key"/> the ones on screen. The marks of the key left are kept as a copy. A key seen recently
    /// gets its marks back at once; any other key starts with none, so every row shows "·" until its mark arrives. Redraws the list.
    /// </summary>
    private void SwitchMarksTo(string key)
    {
        if (_marksKey is not null)
            RememberMarks(_marksKey, new Dictionary<string, BranchMark>(_marks));

        _marks.Clear();
        var index = IndexOfSaved(key);
        if (index >= 0)
        {
            foreach (var pair in _savedMarks[index].Marks)
                _marks[pair.Key] = pair.Value;
            _savedMarks.RemoveAt(index);
        }

        _marksKey = key;
        _app.BranchList.SetNeedsDraw();
    }

    // Keeps a copy of a key's marks. The newest key is last; the oldest is dropped when more than MarkKeysKept - 1 are kept
    // (the current key is the other one).
    private void RememberMarks(string key, Dictionary<string, BranchMark> marks)
    {
        var index = IndexOfSaved(key);
        if (index >= 0)
            _savedMarks.RemoveAt(index);
        _savedMarks.Add((key, marks));
        while (_savedMarks.Count > MarkKeysKept - 1)
            _savedMarks.RemoveAt(0);
    }

    private int IndexOfSaved(string key)
    {
        for (var i = 0; i < _savedMarks.Count; i++)
        {
            if (_savedMarks[i].Key == key)
                return i;
        }
        return -1;
    }

    /// <summary>True when the branch has a real mark for this key. "·" (Unknown) is not a mark: the branch is forecast again.</summary>
    private bool IsMarked(string name) =>
        _marks.TryGetValue(name, out var mark) && mark != BranchMark.Unknown;

    // A detached HEAD refuses every merge, a pause owns the art, and without commits or branches there is nothing to forecast.
    private bool CanForecastList(RepoSnapshot snap) =>
        snap.HeadHash is not null && snap.CurrentBranch.Length > 0 && snap.Branches.Count > 0 && !_app.IsPaused;

    /// <summary>
    /// Starts the next queued branch when the forecast slot is free: nothing runs and no selected layout waits. A branch that already has
    /// a real mark is skipped. UI thread only.
    /// </summary>
    private void PumpListForecasts()
    {
        if (!_newUi || _forecastRunning || _forecastNext is not null || _app.IsPaused)
            return;

        while (_listQueue.Count > 0)
        {
            var b = _listQueue.Dequeue();
            if (IsMarked(b.Name))
                continue;

            StartListForecast(b);
            return;
        }
    }

    /// <summary>
    /// Runs one list job on the worker: the forecast layout (one merge-base read), then the forecast. When the layout cannot be read, the
    /// job reports a minimal stand-in with an Unavailable forecast, and nothing is cached for it: only the mark is set, to "·". Either way
    /// ForecastFinished releases the slot.
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
                // Not logged: a list mark is not a git command the Commands pane shows.
                var stub = GraphLayout.Minimal(snap.CurrentBranch, snap.CurrentBranch.Length == 0, b.Name, snap.HeadHash ?? "", b.Hash);
                _app.Ui(() => ForecastFinished(stub, MergeForecast.Unavailable([]), generation, false));
                return;
            }
            _app.Ui(() => ForecastFinished(layout, f, generation, true));
        }, CancellationToken.None);
    }
}
