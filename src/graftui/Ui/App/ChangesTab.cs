using graftui.Git;
using graftui.Model;
using graftui.Operations;
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
    private static readonly Style Dim = new(Ink.Dim), Warn = new(Ink.Warn), WarnB = new(Ink.Warn, true);
    private static readonly Style Ok = new(Ink.Ok), OkB = new(Ink.Ok, true), BadB = new(Ink.Bad, true);
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

    // The last change's final frame. It stays on the art until the next Up, Down or Tab, as the mock keeps its result.
    private ChangesAnimation? _result;
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
        _refreshCts?.Cancel();
        _diffCts?.Cancel();
        _flash = null;
        _result = null;
    }

    /// <summary>
    /// Browse-mode keys: Up and Down move, Space stages or unstages the selected row, a stages everything and U (shift) unstages
    /// everything. Every other key falls through, so Esc still quits. Ignored while git is busy.
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
    }

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
        var index = keep is null ? -1 : IndexOfRow(rows, keep.Side, keep.Path);
        if (_chooseAfter is { } choose)
            index = choose(rows);
        _chooseAfter = null;
        Snapshot = snap;
        Selected = index >= 0 ? index : Math.Clamp(Selected, 0, Math.Max(0, rows.Count - 1));
        SyncKeyBar();

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
        SyncKeyBar();
        RequestDiff();
    }

    // ---------- stage, unstage ----------

    private void StageSelected()
    {
        var row = SelectedRow;
        var snap = Snapshot;
        if (row is null || snap is null)
        {
            Flash("Nothing to stage", Warn);
            return;
        }

        if (row.Side == ChangeSide.Unstaged)
        {
            // The selection stays at the same position among the unstaged rows, or moves to the file's staged row.
            var position = Selected - snap.Status.Staged.Count;
            StartStage(new StageRequest(StageAction.Stage, row), "Stage", all: false, row.Path,
                rows => AfterStage(rows, row.Path, position));
        }
        else
        {
            var position = Selected;
            StartStage(new StageRequest(StageAction.Unstage, row), "Unstage", all: false, row.Path,
                rows => AfterUnstage(rows, row.Path, position));
        }
    }

    private void StageAll() =>
        StartStage(new StageRequest(StageAction.StageAll, null), "Stage", all: true, path: null, _ => 0);

    private void UnstageAll() =>
        StartStage(new StageRequest(StageAction.UnstageAll, null), "Unstage", all: true, path: null, _ => 0);

    private static int AfterStage(IReadOnlyList<FileChange> rows, string path, int position)
    {
        var staged = rows.Count(r => r.Side == ChangeSide.Staged);
        var unstagedCount = rows.Count - staged;
        if (unstagedCount > 0)
            return staged + Math.Clamp(position, 0, unstagedCount - 1);
        return Math.Max(0, IndexOfRow(rows, ChangeSide.Staged, path));
    }

    private static int AfterUnstage(IReadOnlyList<FileChange> rows, string path, int position)
    {
        var staged = rows.Count(r => r.Side == ChangeSide.Staged);
        if (staged > 0)
            return Math.Clamp(position, 0, staged - 1);
        return Math.Max(0, IndexOfRow(rows, ChangeSide.Unstaged, path));
    }

    /// <summary>
    /// One stage or unstage on a worker: Check, the quit gate, the animation starting, Run, then the result for the animation.
    /// Only one change runs at a time (Busy). Refusals and failures are reported; nothing is rolled back (StageOperation has no rollback).
    /// </summary>
    private void StartStage(StageRequest request, string verb, bool all, string? path, Func<IReadOnlyList<FileChange>, int> chooseAfter)
    {
        var snap = Snapshot;
        if (snap is null)
            return;

        var before = new AreasModel(snap.Status.Unstaged, snap.Status.Staged, snap.Head);
        var staging = request.Action is StageAction.Stage or StageAction.StageAll;
        var areaOp = staging ? AreasOp.Stage : AreasOp.Unstage;
        var below = ChangesText.Summary(snap.Status, snap.Head);
        var stageOp = _app.StageOp;
        var animator = _app.ChangesAnimator;
        BeginBusy();

        // Set on the UI thread when the animation starts, read there again when it ends.
        ChangesAnimation? anim = null;
        Func<int, IReadOnlyList<Seg>>? direction = null;
        _app.ChangesTask = Task.Run(() =>
        {
            try
            {
                var o = RunGated(stageOp, request, c => c.Refusal, () => _app.Gate.IsQuitting,
                    check => _app.Ui(() => Refuse(check.Commands, check.Refusal ?? "")),
                    check => _app.Ui(() =>
                    {
                        _app.LogCommands(check.Commands);
                        var what = ChangesText.What(check.Moving, all);
                        var target = staging ? "staging area" : "working tree";
                        anim = new ChangesAnimation(areaOp, before, check.Moving, $"{(staging ? "Staging" : "Unstaging")} {what}…");
                        direction = cols => ChangesText.OpDirection(verb.ToLowerInvariant(), what, [new Seg(target, new Style(Ink.Dim))], cols);
                        animator.Begin(anim, direction, below);
                    }));
                if (o is null)
                    return;

                var moved = ChangesText.What(o.Moved, all);
                var after = o.Success && o.After is { } s ? new AreasModel(s.Unstaged, s.Staged, snap.Head) : before;
                var result = o.Success
                    ? new AreasResult(true, after,
                        new Caption(staging
                            ? $"✔ Staged {moved}: it goes into the next commit"
                            : $"✔ Unstaged {moved}: the edit is still in your working tree", OkB),
                        staging ? "Staged" : "Unstaged")
                    : new AreasResult(false, before, new Caption($"✕ {verb} failed: {o.Message}", BadB), "Failed", o.Message);
                var failureLog = o.Success ? null : $"{verb} failed: {o.Message}";
                _app.Ui(() => animator.SetPending(result, o.Commands, failureLog,
                    () => AfterChange(o.Success, chooseAfter, anim, direction)));
            }
            catch (Exception ex)
            {
                _app.Ui(() => FailUnexpectedly(ex, verb));
            }
        });
    }

    private void Refuse(IReadOnlyList<GitResult> commands, string reason)
    {
        _app.LogCommands(commands);
        _app.LogMessage("Refused: " + reason, WarnB);
        _flash = new Caption("Refused: " + reason, Warn);
        EndBusy();
        RequestRefresh();
    }

    /// <summary>
    /// After a change: its final frame stays on the art, the selection moves as the operation says (on success), and the
    /// status and branch snapshot are reloaded.
    /// </summary>
    private void AfterChange(bool success, Func<IReadOnlyList<FileChange>, int> chooseAfter, ChangesAnimation? anim,
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
            _app.ChangesAnimator.SetPending(failed, [], $"{verb} failed: {ex.Message}", () => AfterChange(false, _ => 0, null, null));
            return;
        }
        EndBusy();
        RequestRefresh();
    }

    private AreasModel Before() =>
        Snapshot is null ? new AreasModel([], [], new HeadLine("", null, [], false))
            : new AreasModel(Snapshot.Status.Unstaged, Snapshot.Status.Staged, Snapshot.Head);

    private void BeginBusy()
    {
        _app.Busy = true;
        _app.Keys.Busy = true;
        _app.Keys.SetNeedsDraw();
        _flash = null;
        _result = null;
        // Any read still in flight is stale now: its answer must not overwrite what the change shows.
        _refreshSeq++;
        _refreshCts?.Cancel();
        _diffSeq++;
        _diffCts?.Cancel();
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
        if (_result is { } done && snap is not null)
        {
            art.Title = done.Result!.Title;
            art.Direction = _resultDirection ?? (_ => []);
            art.Below = ChangesText.Summary(snap.Status, snap.Head);
            art.Commits = [];
            art.FallbackCaption = null;
            art.Scene = geo => done.Final(geo);
            art.SetNeedsDraw();
            return;
        }

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

    private static int IndexOfRow(IReadOnlyList<FileChange> rows, ChangeSide side, string path)
    {
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Side == side && rows[i].Path == path)
                return i;
        return -1;
    }

    /// <summary>The key bar's Space label follows the selected row: unstage for a staged row, stage otherwise.</summary>
    private void SyncKeyBar()
    {
        var label = SelectedRow?.Side == ChangeSide.Staged ? "unstage" : "stage";
        if (_app.Keys.SpaceLabel == label)
            return;
        _app.Keys.SpaceLabel = label;
        _app.Keys.SetNeedsDraw();
    }
}
