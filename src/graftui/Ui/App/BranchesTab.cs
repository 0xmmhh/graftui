using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Ui.App;

/// <summary>
/// The branch list's behaviour: selection, the preview of the selected branch, and starting a merge.
/// </summary>
internal sealed partial class BranchesTab
{
    private static readonly Style Warn = new(Ink.Warn);
    private static readonly Style Muted = new(Ink.Dim);

    private readonly AppController _app;

    // Keyed by the selected branch's name as well as its tip: branches that share a commit have different layouts.
    private readonly Dictionary<(string Current, string Head, string Name, string Tip, bool Clean), GraphLayout> _cache = new();

    private int _selected;
    private CancellationTokenSource? _previewCts;
    private int _previewSeq;

    internal BranchesTab(AppController app)
    {
        _app = app;
        _selected = FirstNonCurrent(app.Snapshot);
    }

    internal int Selected => _selected;

    /// <summary>Name of the selected branch, or null when there are no branches.</summary>
    internal string? SelectedName() => _app.Snapshot.Branches.Count > 0 ? _app.Snapshot.Branches[_selected].Name : null;

    internal void ClearCache() => _cache.Clear();

    /// <summary>Keeps the selection on the same branch name; if that branch is gone, keeps the index (clamped).</summary>
    internal void KeepSelection(string? selName, RepoSnapshot s)
    {
        var kept = -1;
        for (var i = 0; selName is not null && i < s.Branches.Count; i++)
            if (s.Branches[i].Name == selName)
            {
                kept = i;
                break;
            }
        _selected = kept >= 0 ? kept : Math.Clamp(_selected, 0, Math.Max(0, s.Branches.Count - 1));
    }

    internal void MoveSelection(int delta)
    {
        if (_app.Busy || _app.Snapshot.Branches.Count == 0)
            return;

        var n = _app.Snapshot.Branches.Count;
        _selected = ((_selected + delta) % n + n) % n;
        _app.Result = null;
        _app.BranchList.Selected = _selected;
        _app.BranchList.SetNeedsDraw();
        _app.SyncBranchKeys();
        RequestPreview();
    }

    internal void StartMerge()
    {
        var layout = _app.CurrentLayout();
        if (_app.Busy || layout.TooSmall || _app.Snapshot.Branches.Count == 0)
            return;

        var snap = _app.Snapshot;
        var b = snap.Branches[_selected];
        _app.Busy = true;
        _previewCts?.Cancel();
        _app.Keys.Busy = true;
        _app.Keys.SetNeedsDraw();
        _app.SnapshotSeq++;

        _cache.TryGetValue(CacheKey(snap, b), out var cached);

        _app.MergeTask = Task.Run<MergeOutcome?>(() =>
        {
            try
            {
                // The layout is read first, so that Check is the last git work before the merge.
                var loaded = cached ?? _app.TryLoadLayout(snap, b);
                var o = RunGated(_app.MergeOp, new MergeRequest(b.Name), () => _app.Gate.IsQuitting,
                    check => _app.Ui(() => _app.Animator.ShowRefused(check, b.Name, loaded)),
                    check =>
                    {
                        var layoutNow = loaded
                            ?? GraphLayout.Minimal(snap.CurrentBranch, snap.CurrentBranch.Length == 0, b.Name, snap.HeadHash ?? "", b.Hash);
                        _app.Ui(() => _app.Animator.BeginAnimation(check, layoutNow));
                    });
                if (o is null)
                    return null;

                var after = _app.TryLoadSnapshot();
                var mergeCommit = o.Kind == MergeKind.MergeCommit && o.NewHead is { } head ? _app.TryGetCommit(head) : null;
                _app.Ui(() => _app.Animator.SetPending(o, after, mergeCommit));
                return o;
            }
            catch (Exception ex)
            {
                _app.Ui(() =>
                {
                    _app.ShowGitError(ex);
                    _app.Busy = false;
                    _app.Keys.Busy = false;
                    _app.Keys.SetNeedsDraw();
                });
                return null;
            }
        });
    }

    /// <summary>
    /// The merge's order of work: Check, then the quit gate, then starting (the animation), then Run.
    /// Refusals go to <paramref name="refused"/>; a refusal or a quit returns null and runs nothing after that point.
    /// </summary>
    internal static MergeOutcome? RunGated(MergeOperation op, MergeRequest request, Func<bool> isQuitting,
        Action<MergeCheck> refused, Action<MergeCheck> starting)
    {
        var check = op.Check(request);
        if (!check.CanMerge)
        {
            refused(check);
            return null;
        }
        if (isQuitting())
            return null;
        starting(check);
        return op.Run(request, check);
    }

    // ---------- preview ----------

    internal void RequestPreview()
    {
        if (_app.Snapshot.Branches.Count == 0)
            return;

        // Any request still in flight is stale now, even when the answer is cached: its result must not overwrite this one.
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;
        var seq = ++_previewSeq;

        var snap = _app.Snapshot;
        var b = snap.Branches[_selected];
        var key = CacheKey(snap, b);
        if (_cache.TryGetValue(key, out var hit))
        {
            if (!_app.Busy && _app.Result is null)
                ShowPreview(hit);
            return;
        }

        if (!_app.Busy && _app.Result is null)
            ShowLoading(b);

        Task.Run(() =>
        {
            try
            {
                var layout = _app.Reader.LoadLayout(snap, b, ct);
                _app.Ui(() =>
                {
                    if (seq != _previewSeq)
                        return;
                    _cache[key] = layout;
                    if (!_app.Busy && _app.Result is null)
                        ShowPreview(layout);
                });
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer selection
            }
            catch (Exception ex)
            {
                _app.Ui(() =>
                {
                    if (seq == _previewSeq)
                        _app.ShowGitError(ex);
                });
            }
        });
    }

    internal void ShowInitialState()
    {
        if (_app.Snapshot.Branches.Count == 0)
        {
            _app.Art.Title = "Preview";
            _app.Art.FallbackCaption = new Caption("no branches", Muted);
            _app.Art.Commits = [];
        }
    }

    private void ShowLoading(Branch b)
    {
        _app.Art.Title = "Preview";
        _app.Art.Direction = _ => DirectionLine.Loading(b.Name);
        _app.Art.Scene = null;
        _app.Art.FallbackCaption = new Caption("Loading…", Muted);
        _app.Art.Commits = [];
        _app.Art.SetNeedsDraw();
    }

    private void ShowPreview(GraphLayout layout)
    {
        var clean = _app.Snapshot.Clean;
        // A preview that loaded is proof that git answers again, so an earlier preview error no longer applies.
        _app.Status.ErrorText = null;
        _app.Status.SetNeedsDraw();
        _app.Art.Title = "Preview";
        _app.Art.Direction = cols => DirectionLine.Browse(layout, cols);
        _app.Art.Commits = CommitLists.Idle(layout);
        _app.Art.FallbackCaption = null;
        _app.Art.Scene = geo =>
        {
            var frame = MergeScenes.Preview(geo, layout);
            var grid = frame.Grid;
            Caption? caption = null;
            if (layout.Detached)
            {
                MergeScenes.RefusedOverlay(grid, "HEAD is detached");
                caption = new Caption("Switch to a branch first", Warn);
            }
            else if (!clean)
            {
                MergeScenes.RefusedOverlay(grid, "working tree has uncommitted changes");
                caption = new Caption("Commit or stash your changes first", Warn);
            }
            else if (layout.Kind == GraphKind.Same)
            {
                // Same tip hash is not the same branch: another branch can point at the current commit.
                caption = layout.Selected == layout.Current
                    ? new Caption($"{layout.Current} is the current branch", Muted)
                    : new Caption($"{layout.Selected} points at the same commit as {(layout.Detached ? "HEAD" : layout.Current)}: nothing to merge", Muted);
            }
            else if (layout.Kind == GraphKind.Unrelated)
                caption = new Caption($"{layout.Selected} and {layout.Current} have no common history", Muted);
            return new SceneFrame(grid, caption);
        };
        _app.Art.SetNeedsDraw();
    }

    private static (string Current, string Head, string Name, string Tip, bool Clean) CacheKey(RepoSnapshot snap, Branch b) =>
        (snap.CurrentBranch, snap.HeadHash ?? "", b.Name, b.Hash, snap.Clean);

    private static int FirstNonCurrent(RepoSnapshot s)
    {
        for (var i = 0; i < s.Branches.Count; i++)
            if (!s.Branches[i].IsCurrent)
                return i;
        return 0;
    }
}
