using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace graftui.Ui.App;

/// <summary>Which keys the Changes tab is taking: browsing the list, typing a message, or answering a question.</summary>
internal enum ChangesMode { Browse, Input, Ask }

/// <summary>
/// The Changes tab: the status list, the diff of the selected file, and a refresh every two seconds while it is shown.
/// Reads run on worker threads and are applied on the UI thread, with a sequence number so a stale answer is dropped.
/// </summary>
internal sealed partial class ChangesTab
{
    private static readonly Style Dim = new(Ink.Dim), Warn = new(Ink.Warn), WarnB = new(Ink.Warn, true);
    private static readonly Style Ok = new(Ink.Ok), OkB = new(Ink.Ok, true), BadB = new(Ink.Bad, true);
    private static readonly Style Fg = new(Ink.Fg), FgB = new(Ink.Fg, true), CurB = new(Ink.Cur, true);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly AppController _app;
    private int _refreshSeq;
    private int _diffSeq;
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _diffCts;
    private bool _polling;
    private FileDiff? _diff;
    private Caption? _flash;

    // Where the selection goes after a successful change (set when the change lands, used by the next refresh).
    private Func<IReadOnlyList<FileChange>, int>? _chooseAfter;

    // The message box: the check it opened on, what is typed is in the fields, and the last error to show.
    private CommitCheck? _formCheck;
    private bool _formAmend;
    private bool _formPushedConfirmed;
    private bool _formBlankAfterSubject = true;
    private string? _formError;

    // The amend confirmations: the check they are about, and which question is on screen.
    private CommitCheck? _askCheck;
    private readonly PushedAmendQuestions _questions = new();

    // The last change's final frame. It stays on the art until the next Up, Down or Tab, as the mock keeps its result.
    private IAreasAnimation? _result;
    private Func<int, IReadOnlyList<Seg>>? _resultDirection;

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
        CancelAndDispose(ref _refreshCts);
        CancelAndDispose(ref _diffCts);
        _flash = null;
        _result = null;
        if (Mode != ChangesMode.Browse)
            CloseForm();
        if (_yesNo is not null)
        {
            _yesNo = null;
            ResetAskKeys();
        }
        Mode = ChangesMode.Browse;
    }

    /// <summary>
    /// Browse-mode keys: Up and Down move, Space stages or unstages the selected row, a stages everything, U (shift) unstages
    /// everything, c and A (shift) commit and amend, x discards, z stashes, Z (shift) pops and u undoes. Every other key falls
    /// through, so Esc still quits. Ignored while git is busy.
    /// </summary>
    internal void HandleKey(Key e)
    {
        if (_app.Busy)
            return;

        ClearFlash();
        if (e == Key.CursorUp)
        {
            e.Handled = true;
            _result = null;
            Move(-1);
        }
        else if (e == Key.CursorDown)
        {
            e.Handled = true;
            _result = null;
            Move(+1);
        }
        else if (e == Key.Space)
        {
            e.Handled = true;
            StageSelected();
        }
        else if (e == Key.A)
        {
            e.Handled = true;
            StageAll();
        }
        else if (e == Key.U.WithShift)
        {
            e.Handled = true;
            UnstageAll();
        }
        else if (e == Key.C)
        {
            e.Handled = true;
            BeginCommitInput(amend: false);
        }
        else if (e == Key.A.WithShift)
        {
            e.Handled = true;
            BeginCommitInput(amend: true);
        }
        else if (e == Key.X)
        {
            e.Handled = true;
            AskDiscard();
        }
        else if (e == Key.Z)
        {
            e.Handled = true;
            BeginStashInput();
        }
        else if (e == Key.Z.WithShift)
        {
            e.Handled = true;
            StartPop();
        }
        else if (e == Key.U)
        {
            e.Handled = true;
            StartUndo();
        }
    }
    /// <summary>True when the whole art, and so the message box's fields, fits inside the art pane at this size.</summary>
    private bool ArtFits() =>
        ArtPaneView.ArtArea(_app.Art.Viewport.Width, _app.Art.Viewport.Height) is { } area
        && area.Y + area.H <= _app.Art.Viewport.Height;

    private static string Short(string? hash) => string.IsNullOrEmpty(hash) ? "" : hash[..Math.Min(7, hash.Length)];

    /// <summary>The short hash of HEAD, or "no commits" on an unborn branch.</summary>
    private static string HeadText(string? hash) => string.IsNullOrEmpty(hash) ? "no commits" : Short(hash);

    private IReadOnlyList<IReadOnlyList<Seg>> Summary() =>
        Snapshot is null ? [] : ChangesText.Summary(Snapshot.Status, Snapshot.Head, Snapshot.Stash);

    private void ClearFlash()
    {
        if (_flash is null)
            return;
        _flash = null;
        _app.Art.SetNeedsDraw();
    }

    /// <summary>Reads the status on a worker. Its answer is applied on the UI thread, unless a newer read has started.</summary>
    internal void RequestRefresh()
    {
        CancelAndDispose(ref _refreshCts);
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
            && Snapshot.Head.Branch == snap.Head.Branch
            && Snapshot.Stash.TopHash == snap.Stash.TopHash
            && Snapshot.Stash.Count == snap.Stash.Count;
        if (unchanged)
        {
            // The same rows and head, but the undo state may have moved (a merge saved a step): keep the newer snapshot.
            Snapshot = snap;
            SyncKeyBar();
            RequestDiff();
            return;
        }

        // Keep the selection on the same row (side and path) when it still exists; otherwise keep the index, clamped.
        var keep = SelectedRow;
        var rows = snap.Status.Rows;
        var index = keep is null ? -1 : IndexOfRow(rows, keep.Side, keep.Path);
        if (_chooseAfter is { } choose)
            index = choose(rows);
        _chooseAfter = null;
        Snapshot = snap;
        Selected = index >= 0 ? index : Math.Clamp(Selected, 0, Math.Max(0, rows.Count - 1));
        SyncKeyBar();

        _app.ChangesList.Status = snap.Status;
        _app.ChangesList.Stash = snap.Stash;
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
        SyncKeyBar();
        RequestDiff();
    }

    private void Refuse(IReadOnlyList<GitResult> commands, string reason)
    {
        _app.LogCommands(commands);
        foreach (var line in RefusalText.LogLines(reason))
            _app.LogMessage(line, WarnB);
        _flash = new Caption(RefusalText.Caption(reason), Warn);
        EndBusy();
        RequestRefresh();
    }

    /// <summary>
    /// After a change: its final frame stays on the art, the selection moves as the operation says (on success), and the
    /// status and branch snapshot are reloaded.
    /// </summary>
    private void AfterChange(bool success, Func<IReadOnlyList<FileChange>, int>? chooseAfter, IAreasAnimation? anim,
        Func<int, IReadOnlyList<Seg>>? direction)
    {
        if (success)
            _chooseAfter = chooseAfter;
        _result = anim;
        _resultDirection = direction;
        RequestRefresh();
        _app.ReloadCountsAsync();
    }

    private void FailUnexpectedly(Exception ex, string verb)
    {
        _app.ShowGitError(ex);
        if (_app.ChangesAnimator.Running)
        {
            var failed = new AreasResult(false, Before(), new Caption($"✕ {verb} failed: {ex.Message}", BadB), "Failed", ex.Message);
            _app.ChangesAnimator.SetPending(failed, [], $"{verb} failed: {ex.Message}", () => AfterChange(false, null, null, null));
            return;
        }
        // The error is on screen now (the status bar), so the exit path need not print this change's line again.
        _app.MarkChangeOutcomeShown();
        EndBusy();
        RequestRefresh();
    }

    private AreasModel Before() =>
        Snapshot is null ? new AreasModel([], [], new HeadLine("", null, [], false))
            : new AreasModel(Snapshot.Status.Unstaged, Snapshot.Status.Staged, Snapshot.Head, Snapshot.Stash);

    private void BeginBusy()
    {
        _app.Busy = true;
        _app.Keys.Busy = true;
        _app.Keys.SetNeedsDraw();
        _flash = null;
        _result = null;
        // Any read still in flight is stale now: its answer must not overwrite what the change shows.
        _refreshSeq++;
        CancelAndDispose(ref _refreshCts);
        _diffSeq++;
        CancelAndDispose(ref _diffCts);
    }

    private void EndBusy()
    {
        _app.Busy = false;
        _app.Keys.Busy = false;
        _app.Keys.SetNeedsDraw();
        _app.Art.SetNeedsDraw();
    }

    private void Flash(string text, Style style)
    {
        _flash = new Caption(text, style);
        _app.Art.SetNeedsDraw();
    }

    /// <summary>
    /// The gate every change goes through: Check, then the refusal test, then the quit gate, then the animation starts, then Run.
    /// A refusal goes to <paramref name="refused"/>; a refusal or a quit returns null and runs nothing after that point.
    /// </summary>
    internal static TOut? RunGated<TReq, TCheck, TOut>(IGitOperation<TReq, TCheck, TOut> op, TReq req, Func<TCheck, string?> refusal,
        Func<bool> isQuitting, Action<TCheck> refused, Action<TCheck> starting) where TOut : class
    {
        var check = op.Check(req);
        if (refusal(check) is not null)
        {
            refused(check);
            return null;
        }
        if (isQuitting())
            return null;
        starting(check);
        return op.Run(req, check);
    }

    /// <summary>
    /// Reads the diff of the selected row. The old diff stays on screen until the new one arrives, unless the selection moved to
    /// another file; then the art shows "Loading…".
    /// </summary>
    private void RequestDiff()
    {
        CancelAndDispose(ref _diffCts);
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
        if (_result is { } done && snap is not null)
        {
            art.Title = done.Result!.Title;
            art.Direction = _resultDirection ?? (_ => []);
            art.Below = ChangesText.Summary(snap.Status, snap.Head, snap.Stash);
            art.Panel = null;
            art.FallbackCaption = null;
            art.Scene = geo => done.Final(geo);
            art.SetNeedsDraw();
            return;
        }

        if (snap is null)
        {
            art.Direction = _ => [];
            art.Below = null;
            art.Panel = null;
            art.Scene = null;
            art.FallbackCaption = new Caption("Loading…", Dim);
            art.SetNeedsDraw();
            return;
        }

        var head = snap.Head;
        art.Direction = cols => ChangesText.BrowseDirection(row, head, cols);
        art.Below = ChangesText.Summary(snap.Status, head, snap.Stash);
        art.Panel = null;

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

    /// <summary>
    /// Cancels a read still in flight and disposes its source. The worker keeps its token, which stays valid after disposal;
    /// it only reads IsCancellationRequested.
    /// </summary>
    private static void CancelAndDispose(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private static int IndexOfRow(IReadOnlyList<FileChange> rows, ChangeSide side, string path)
    {
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Side == side && rows[i].Path == path)
                return i;
        return -1;
    }

    /// <summary>The Changes key bar: which keys act now, from the last status read and the selected row.</summary>
    private void SyncKeyBar()
    {
        var snap = Snapshot;
        var state = snap is null ? ChangesKeyState.Empty : new ChangesKeyState(
            RowCount: Rows.Count,
            SelectedSide: SelectedRow?.Side,
            HasUnstaged: snap.Status.Unstaged.Count > 0,
            HasStaged: snap.Status.Staged.Count > 0,
            HasHead: snap.Head.HeadHash is not null,
            HasStash: snap.Stash.Count > 0,
            HasUndo: snap.HasUndo);
        if (_app.Keys.ChangesState == state)
            return;
        _app.Keys.ChangesState = state;
        _app.Keys.SetNeedsDraw();
    }
}
