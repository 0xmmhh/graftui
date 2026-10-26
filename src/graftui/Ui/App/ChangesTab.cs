using graftui.Git;
using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using Terminal.Gui.Input;

namespace graftui.Ui.App;

/// <summary>Which keys the Changes tab is taking: browsing the list, typing a message, or answering a question.</summary>
internal enum ChangesMode { Browse, Input, Ask }

/// <summary>
/// The Changes tab: the status list, the diff of the selected file, and a refresh every two seconds while it is shown.
/// Reads run on worker threads and are applied on the UI thread, with a sequence number so a stale answer is dropped.
/// </summary>
internal sealed class ChangesTab
{
    private static readonly Style Dim = new(Ink.Dim);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly AppController _app;
    private int _refreshSeq;
    private int _diffSeq;
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _diffCts;
    private bool _polling;
    private FileDiff? _diff;
    private Caption? _flash;

    internal ChangesTab(AppController app)
    {
        _app = app;
    }

    internal ChangesMode Mode { get; set; } = ChangesMode.Browse;

    /// <summary>The last status read that was applied. Null until the first one arrives.</summary>
    internal ChangesSnapshot? Snapshot { get; private set; }

    /// <summary>Index into the status rows (staged first). Always 0 when there are no rows.</summary>
    internal int Selected { get; set; }

    private IReadOnlyList<FileChange> Rows => Snapshot?.Status.Rows ?? Array.Empty<FileChange>();

    private FileChange? SelectedRow => Rows.Count > 0 && Selected < Rows.Count ? Rows[Selected] : null;

    /// <summary>Shown when the tab opens: reads the status now and then every two seconds.</summary>
    internal void Activate()
    {
        Mode = ChangesMode.Browse;
        _app.ChangesList.Loading = Snapshot is null;
        ShowBrowse();
        RequestRefresh();
        if (!_polling)
        {
            _polling = true;
            _app.App.AddTimeout(PollInterval, Poll);
        }
    }

    /// <summary>Drops every read still in flight. The poll stops by itself once another tab is shown.</summary>
    internal void Deactivate()
    {
        _refreshSeq++;
        _diffSeq++;
        _refreshCts?.Cancel();
        _diffCts?.Cancel();
        _flash = null;
    }

    /// <summary>Browse-mode keys. Only Up and Down are used so far; every other key falls through (Esc quits).</summary>
    internal void HandleKey(Key e)
    {
        if (_app.Busy)
            return;

        _flash = null;
        if (e == Key.CursorUp)
        {
            e.Handled = true;
            Move(-1);
        }
        else if (e == Key.CursorDown)
        {
            e.Handled = true;
            Move(+1);
        }
    }

    /// <summary>Reads the status on a worker. Its answer is applied on the UI thread, unless a newer read has started.</summary>
    internal void RequestRefresh()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;
        var seq = ++_refreshSeq;
        var reader = _app.RootReader;

        Task.Run(() =>
        {
            try
            {
                var snap = reader.LoadChanges(ct);
                _app.Ui(() => ApplyRefresh(seq, snap));
            }
            catch (OperationCanceledException)
            {
                // superseded or the tab was closed
            }
            catch (Exception ex)
            {
                _app.Ui(() =>
                {
                    if (seq == _refreshSeq)
                        _app.ShowGitError(ex);
                });
            }
        });
    }

    private bool Poll()
    {
        if (_app.ActiveTab != AppController.TabKind.Changes)
        {
            _polling = false;
            return false;
        }
        if (Mode == ChangesMode.Browse && !_app.Busy)
            RequestRefresh();
        return true;
    }

    private void ApplyRefresh(int seq, ChangesSnapshot snap)
    {
        if (seq != _refreshSeq || Mode != ChangesMode.Browse || _app.Busy || _app.ActiveTab != AppController.TabKind.Changes)
            return;

        // Nothing changed in the status or the head: keep the list as it is (no flicker), but re-read the diff in case
        // the selected file was edited again.
        var unchanged = Snapshot is not null
            && Snapshot.RawStatus == snap.RawStatus
            && Snapshot.Head.HeadHash == snap.Head.HeadHash
            && Snapshot.Head.Branch == snap.Head.Branch;
        if (unchanged)
        {
            RequestDiff();
            return;
        }

        // Keep the selection on the same row (side and path) when it still exists; otherwise keep the index, clamped.
        var keep = SelectedRow;
        var rows = snap.Status.Rows;
        var index = keep is null ? -1 : IndexOfRow(rows, keep);
        Snapshot = snap;
        Selected = index >= 0 ? index : Math.Clamp(Selected, 0, Math.Max(0, rows.Count - 1));

        _app.ChangesList.Status = snap.Status;
        _app.ChangesList.Selected = Selected;
        _app.ChangesList.Loading = false;
        _app.ChangesList.SetNeedsDraw();
        _app.Status.ChangedFiles = snap.Status.ChangedPaths;
        _app.Status.SetNeedsDraw();
        RequestDiff();
    }

    private void Move(int delta)
    {
        var n = Rows.Count;
        if (n == 0)
            return;

        Selected = ((Selected + delta) % n + n) % n;
        _app.Result = null;
        _app.ChangesList.Selected = Selected;
        _app.ChangesList.SetNeedsDraw();
        RequestDiff();
    }

    /// <summary>
    /// Reads the diff of the selected row. The old diff stays on screen until the new one arrives, unless the selection moved to
    /// another file; then the art shows "Loading…".
    /// </summary>
    private void RequestDiff()
    {
        _diffCts?.Cancel();
        _diffCts = new CancellationTokenSource();
        var ct = _diffCts.Token;
        var seq = ++_diffSeq;

        var row = SelectedRow;
        if (row is null)
        {
            _diff = null;
            ShowBrowse();
            return;
        }
        if (_diff?.File != row)
            _diff = null;
        ShowBrowse();

        var reader = _app.RootReader;
        Task.Run(() =>
        {
            try
            {
                var diff = reader.LoadDiff(row, ct);
                _app.Ui(() =>
                {
                    if (seq != _diffSeq || Mode != ChangesMode.Browse)
                        return;
                    _diff = diff;
                    ShowBrowse();
                });
            }
            catch (OperationCanceledException)
            {
                // superseded by another selection or refresh
            }
            catch (Exception ex)
            {
                _app.Ui(() =>
                {
                    if (seq == _diffSeq)
                        _app.ShowGitError(ex);
                });
            }
        });
    }

    /// <summary>Draws the browse screen: the selected file's diff, its direction line, the summary and the hint.</summary>
    private void ShowBrowse()
    {
        var art = _app.Art;
        var row = SelectedRow;
        art.Title = row is null ? "Changes" : "Diff";

        var snap = Snapshot;
        if (snap is null)
        {
            art.Direction = _ => [];
            art.Below = null;
            art.Commits = [];
            art.Scene = null;
            art.FallbackCaption = new Caption("Loading…", Dim);
            art.SetNeedsDraw();
            return;
        }

        var head = snap.Head;
        art.Direction = cols => ChangesText.BrowseDirection(row, head, cols);
        art.Below = ChangesText.Summary(snap.Status, head);
        art.Commits = [];

        if (row is not null && _diff?.File != row)
        {
            art.Scene = null;
            art.FallbackCaption = new Caption("Loading…", Dim);
        }
        else
        {
            var diff = row is null ? null : _diff;
            var hint = new Caption(ChangesText.BrowseHint(row), Dim);
            art.FallbackCaption = null;
            // The flash (a refusal or a notice) replaces the hint until the next key.
            art.Scene = geo => new SceneFrame(AreasScenes.Diff(geo, diff).Grid, _flash ?? hint);
        }
        art.SetNeedsDraw();
    }

    private static int IndexOfRow(IReadOnlyList<FileChange> rows, FileChange keep)
    {
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Side == keep.Side && rows[i].Path == keep.Path)
                return i;
        return -1;
    }
}
