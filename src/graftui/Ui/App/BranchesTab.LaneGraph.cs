using System.Text;
using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// The lane graph of the Branches preview: read on a worker for the snapshot on screen, once per snapshot key. The key is the current
/// branch, HEAD and every branch tip, so a graph is never drawn for history the screen no longer shows. A read that fails is not
/// retried until the key changes, and the preview falls back to the plain art meanwhile. UI thread only, except the worker's read.
/// </summary>
internal sealed partial class BranchesTab
{
    // The snapshot key the graph below (or the running read) belongs to.
    private string? _laneKey;

    // Null while the read runs, and after a failed read.
    private LaneGraph? _laneGraph;

    // Stops the running read when the snapshot changes before it finishes.
    private CancellationTokenSource? _laneCts;

    // The number of the newest read. A read that finishes with an older number is dropped.
    private int _laneSeq;

    /// <summary>The key of a snapshot: its current branch, its HEAD and every branch name and tip in list order.</summary>
    internal static string LaneGraphKey(RepoSnapshot s)
    {
        var key = new StringBuilder();
        key.Append(s.CurrentBranch).Append('\n');
        key.Append(s.HeadHash ?? "").Append('\n');
        foreach (var b in s.Branches)
            key.Append(b.Name).Append(' ').Append(b.Hash).Append('\n');
        return key.ToString();
    }

    /// <summary>The number of the newest lane-graph read. The tests read it.</summary>
    internal int LaneGraphSeq => _laneSeq;

    /// <summary>
    /// Starts reading the lane graph of the snapshot on screen, unless that key is already read, being read, or failed. UI thread only.
    /// </summary>
    internal void EnsureLaneGraph()
    {
        // The new look is off: no graph is read, and no worker is started.
        if (!_newUi)
            return;

        var snap = _app.Snapshot;
        var key = LaneGraphKey(snap);
        if (key == _laneKey)
            return;

        Cancellation.CancelAndDispose(ref _laneCts);
        _laneCts = new CancellationTokenSource();
        var ct = _laneCts.Token;
        _laneKey = key;
        _laneGraph = null;
        var seq = ++_laneSeq;

        // The sheet on screen still holds the old graph: draw the preview again now, so that graph is not left up while the new one is read.
        RedrawShownPreview();

        Task.Run(() =>
        {
            try
            {
                var graph = _app.Reader.LoadLaneGraph(snap, ct);
                _app.Ui(() => LaneGraphLoaded(seq, key, graph));
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer snapshot
            }
            catch (Exception)
            {
                // Not logged: the read is not a git command the Commands pane shows. The plain preview stays.
                _app.Ui(() => LaneGraphLoaded(seq, key, null));
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// A lane-graph read finished (a null graph means it failed). A read that is no longer the newest is dropped. Otherwise the shown
    /// preview is drawn again: as the sheet when the graph is in, else as the old preview. UI thread only.
    /// </summary>
    internal void LaneGraphLoaded(int seq, string key, LaneGraph? graph)
    {
        if (seq != _laneSeq || key != _laneKey)
            return;

        _laneGraph = graph;
        RedrawShownPreview();
    }

    /// <summary>
    /// Draws the shown browse preview again, so that it matches the lane graph now held: the sheet when the graph is in, else the old
    /// preview. Only when the browse preview owns the art and the shown layout still matches the snapshot on screen; a result, a question
    /// or a form is never drawn over. UI thread only.
    /// </summary>
    private void RedrawShownPreview()
    {
        if (_shown is null || !MayDrawPreview)
            return;

        var shown = _shown;
        if (ShowsSnapshot(shown, _app.Snapshot))
            DrawPreview(shown);
    }

    /// <summary>The graph of the snapshot on screen, or null when it is not read yet, failed, or has no lanes.</summary>
    private LaneGraph? LaneGraphForSnapshot()
    {
        if (_laneKey != LaneGraphKey(_app.Snapshot) || _laneGraph is null)
            return null;
        if (_laneGraph.Lanes.Count == 0)
            return null;
        return _laneGraph;
    }
}
