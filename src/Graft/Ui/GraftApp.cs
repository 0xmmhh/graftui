using System.Diagnostics;
using Graft.Git;
using Graft.Ui.Views;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Graft.Ui;

/// <summary>
/// Controller: owns the state (snapshot, selection, running merge) and wires the views to git.
/// Git reads and the merge run on worker threads; every change to the views happens on the UI thread
/// (through <see cref="Ui"/>).
/// </summary>
public sealed class GraftApp
{
    private static readonly Style Warn = new(Ink.Warn), WarnB = new(Ink.Warn, true);
    private static readonly Style Muted = new(Ink.Dim), Bad = new(Ink.Bad), BadB = new(Ink.Bad, true);
    private static readonly Style Fg = new(Ink.Fg);

    private readonly IApplication _app;
    private readonly GitClient _git;
    private readonly RepoReader _reader;
    private readonly Merger _merger;
    private readonly string _repoDisplay;
    private readonly bool _sandboxMode;

    private readonly Dictionary<(string Head, string Tip, bool Clean), GraphLayout> _cache = new();

    private RepoSnapshot _snapshot;
    private int _selected;
    private int _snapshotSeq;
    private bool _busy;
    private string? _result;
    private CancellationTokenSource? _previewCts;
    private int _previewSeq;

    private MergeAnimation? _anim;
    private Stopwatch? _clock;
    private (MergeOutcome Outcome, RepoSnapshot? After, CommitInfo? MergeCommit)? _pending;

    private StatusBarView _status = null!;
    private BranchesView _branches = null!;
    private ArtPaneView _art = null!;
    private LogView _log = null!;
    private KeyBarView _keys = null!;
    private TooSmallView _tooSmall = null!;
    private Window? _window;

    private volatile bool _quitting;
    private volatile bool _outcomeShown;

    public GraftApp(IApplication app, GitClient git, RepoSnapshot initial, string repoDisplay, bool sandboxMode)
    {
        _app = app;
        _git = git;
        _reader = new RepoReader(git);
        _merger = new Merger(git);
        _snapshot = initial;
        _repoDisplay = repoDisplay;
        _sandboxMode = sandboxMode;
        _selected = FirstNonCurrent(initial);
    }

    /// <summary>The merge worker. Null until a merge has been started. Its result is the outcome, or null when nothing merged.</summary>
    public Task<MergeOutcome?>? MergeTask { get; private set; }

    /// <summary>True once the outcome has been drawn in the UI (so the exit path need not print it again).</summary>
    public bool OutcomeShown => _outcomeShown;

    /// <summary>Stops a merge that has not started yet. Called when the UI is shutting down.</summary>
    public void MarkQuitting() => _quitting = true;

    public Window BuildWindow()
    {
        ScreenLayout Layout() => ScreenLayout.Compute(_app.Screen.Width, _app.Screen.Height);

        var window = new Window { BorderStyle = LineStyle.None };
        window.SetScheme(Palette.RootScheme);
        _window = window;

        _status = new StatusBarView { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
        _branches = new BranchesView
        {
            X = 0,
            Y = 1,
            Width = Dim.Func(_ => Layout().TooSmall ? 0 : Layout().BranchWidth),
            Height = Dim.Func(_ => Layout().TooSmall ? 0 : Layout().TopHeight),
        };
        _art = new ArtPaneView
        {
            X = Pos.Func(_ => Layout().BranchWidth),
            Y = 1,
            Width = Dim.Func(_ => Layout().TooSmall ? 0 : Layout().Cols - Layout().BranchWidth),
            Height = Dim.Func(_ => Layout().TooSmall ? 0 : Layout().TopHeight),
        };
        _log = new LogView
        {
            X = 0,
            Y = Pos.Func(_ => 1 + Layout().TopHeight),
            Width = Dim.Fill(),
            Height = Dim.Func(_ => Layout().TooSmall ? 0 : Layout().LogHeight),
        };
        _keys = new KeyBarView { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1, SandboxMode = _sandboxMode };
        _tooSmall = new TooSmallView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Func(_ => Layout().TooSmall ? Math.Max(0, Layout().Rows - 2) : 0),
        };

        _status.RepoDisplay = _repoDisplay;
        _status.Snapshot = _snapshot;
        _branches.Branches = _snapshot.Branches;
        _branches.Counts = _snapshot.Counts;
        _branches.Selected = _selected;
        _keys.SandboxMode = _sandboxMode;
        _tooSmall.Cols = _app.Screen.Width;
        _tooSmall.Rows = _app.Screen.Height;
        ShowInitialState();

        window.Add(_status, _branches, _art, _log, _keys, _tooSmall);

        _app.ScreenChanged += (_, _) =>
        {
            _tooSmall.Cols = _app.Screen.Width;
            _tooSmall.Rows = _app.Screen.Height;
            window.SetNeedsLayout();
            _tooSmall.SetNeedsDraw();
            _art.SetNeedsDraw();
            _branches.SetNeedsDraw();
            _log.SetNeedsDraw();
        };
        _app.Keyboard.KeyDown += OnKeyDown;

        RequestPreview();
        ReloadCountsAsync();
        return window;
    }

    // ---------- keys ----------

    private void OnKeyDown(object? sender, Key e)
    {
        if (e == Key.CursorUp)
        {
            e.Handled = true;
            MoveSelection(-1);
        }
        else if (e == Key.CursorDown)
        {
            e.Handled = true;
            MoveSelection(+1);
        }
        else if (e == Key.M)
        {
            e.Handled = true;
            StartMerge();
        }
    }

    private void MoveSelection(int delta)
    {
        if (_busy || _snapshot.Branches.Count == 0)
            return;

        var n = _snapshot.Branches.Count;
        _selected = ((_selected + delta) % n + n) % n;
        _result = null;
        _branches.Selected = _selected;
        _branches.SetNeedsDraw();
        RequestPreview();
    }

    private void StartMerge()
    {
        var layout = ScreenLayout.Compute(_app.Screen.Width, _app.Screen.Height);
        if (_busy || layout.TooSmall || _snapshot.Branches.Count == 0)
            return;

        var snap = _snapshot;
        var b = snap.Branches[_selected];
        _busy = true;
        _previewCts?.Cancel();
        _keys.Busy = true;
        _keys.SetNeedsDraw();
        _snapshotSeq++;

        _cache.TryGetValue(CacheKey(snap, b), out var cached);

        MergeTask = Task.Run<MergeOutcome?>(() =>
        {
            try
            {
                var check = _merger.Check(b.Name);
                if (!check.CanMerge)
                {
                    Ui(() => ShowRefused(check, b.Name, cached));
                    return null;
                }

                var layoutNow = cached ?? TryLoadLayout(snap, b)
                    ?? GraphLayout.Minimal(snap.CurrentBranch, snap.CurrentBranch.Length == 0, b.Name, snap.HeadHash ?? "", b.Hash);
                if (_quitting)
                    return null;

                Ui(() => BeginAnimation(check, layoutNow));

                var o = _merger.Merge(b.Name, check.CurrentBranch);
                var after = TryLoadSnapshot();
                var mergeCommit = o.Kind == MergeKind.MergeCommit && o.NewHead is { } head ? TryGetCommit(head) : null;
                Ui(() => _pending = (o, after, mergeCommit));
                return o;
            }
            catch (Exception ex)
            {
                Ui(() =>
                {
                    ShowGitError(ex);
                    _busy = false;
                    _keys.Busy = false;
                    _keys.SetNeedsDraw();
                });
                return null;
            }
        });
    }

    // ---------- preview ----------

    private void RequestPreview()
    {
        if (_snapshot.Branches.Count == 0)
            return;

        var snap = _snapshot;
        var b = snap.Branches[_selected];
        var key = CacheKey(snap, b);
        if (_cache.TryGetValue(key, out var hit))
        {
            if (!_busy && _result is null)
                ShowPreview(hit);
            return;
        }

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;
        var seq = ++_previewSeq;
        if (!_busy && _result is null)
            ShowLoading(b);

        Task.Run(() =>
        {
            try
            {
                var layout = _reader.LoadLayout(snap, b, ct);
                Ui(() =>
                {
                    if (seq != _previewSeq)
                        return;
                    _cache[key] = layout;
                    if (!_busy && _result is null)
                        ShowPreview(layout);
                });
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer selection
            }
            catch (Exception ex)
            {
                Ui(() =>
                {
                    if (seq == _previewSeq)
                        ShowGitError(ex);
                });
            }
        });
    }

    private void ShowInitialState()
    {
        if (_snapshot.Branches.Count == 0)
        {
            _art.Title = "Preview";
            _art.FallbackCaption = new Caption("no branches", Muted);
            _art.Commits = [];
        }
    }

    private void ShowLoading(Branch b)
    {
        _art.Title = "Preview";
        _art.Direction = _ => DirectionLine.Loading(b.Name);
        _art.Scene = null;
        _art.FallbackCaption = new Caption("Loading…", Muted);
        _art.Commits = [];
        _art.SetNeedsDraw();
    }

    private void ShowPreview(GraphLayout layout)
    {
        var clean = _snapshot.Clean;
        _art.Title = "Preview";
        _art.Direction = cols => DirectionLine.Browse(layout, cols);
        _art.Commits = CommitLists.Idle(layout);
        _art.FallbackCaption = null;
        _art.Scene = geo =>
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
                caption = new Caption($"{layout.Current} is the current branch", Muted);
            else if (layout.Kind == GraphKind.Unrelated)
                caption = new Caption($"{layout.Selected} and {layout.Current} have no common history", Muted);
            return new SceneFrame(grid, caption);
        };
        _art.SetNeedsDraw();
    }

    // ---------- merge result and animation ----------

    private void ShowRefused(MergeCheck check, string selName, GraphLayout? layout)
    {
        LogCommands(check.Commands);
        LogMessage("Refused: " + check.Refusal, WarnB);

        var detached = check.CurrentBranch.Length == 0;
        _art.Title = "Refused";
        _art.Direction = cols => DirectionLine.Merge(selName, check.CurrentBranch, detached, cols);
        _art.Commits = layout is null ? [] : CommitLists.Idle(layout);
        _art.FallbackCaption = null;
        var reason = check.Refusal ?? "";
        _art.Scene = geo =>
        {
            var frame = layout is null
                ? new SceneFrame(new ArtGrid(geo.Width, geo.Height), null)
                : MergeScenes.Preview(geo, layout);
            MergeScenes.RefusedOverlay(frame.Grid, reason);
            return new SceneFrame(frame.Grid, new Caption("Refused: " + reason, WarnB));
        };

        _result = selName;
        _busy = false;
        _keys.Busy = false;
        _keys.SetNeedsDraw();
        _art.SetNeedsDraw();
    }

    private void BeginAnimation(MergeCheck check, GraphLayout layout)
    {
        LogCommands(check.Commands);
        var anim = new MergeAnimation(layout);
        var clock = Stopwatch.StartNew();
        _anim = anim;
        _clock = clock;
        _pending = null; // an outcome from an earlier merge must never be applied to this one
        var detached = layout.Detached;

        _art.Title = anim.Title(0);
        _art.Direction = cols => DirectionLine.Merge(layout.Selected, layout.Current, detached, cols);
        _art.Commits = CommitLists.Idle(layout);
        _art.FallbackCaption = null;
        _art.Scene = geo => anim.Render(geo, clock.ElapsedMilliseconds);
        _art.SetNeedsDraw();

        _app.AddTimeout(TimeSpan.FromMilliseconds(50), Tick);
    }

    private bool Tick()
    {
        if (_anim is null || _clock is null)
            return false;

        var e = _clock.ElapsedMilliseconds;
        if (_pending is { } p && _anim.Outcome is null)
            _anim.SetOutcome(p.Outcome, e);

        _art.Title = _anim.Title(e);
        _art.SetNeedsDraw();

        if (_anim.IsDone(e))
        {
            FinishAnimation();
            return false;
        }
        return true;
    }

    private void FinishAnimation()
    {
        var anim = _anim!;
        var pending = _pending!.Value;
        var outcome = pending.Outcome;
        var final = _clock!.ElapsedMilliseconds;
        _outcomeShown = true;

        // Freeze the scene at its final time so the art stops moving.
        _art.Scene = geo => anim.Render(geo, final);
        _art.Title = anim.Title(final);
        _art.Commits = CommitLists.After(anim.Layout, outcome, pending.MergeCommit);

        var mergeIndex = LogCommands(outcome.Commands);
        if (outcome.ConflictFiles.Count > 0)
            LogMessage("Conflicting files: " + string.Join(", ", outcome.ConflictFiles), Bad);
        if (outcome.Kind == MergeKind.Failed)
            LogMessage("Merge failed: " + outcome.Message, Bad);
        if (mergeIndex >= 0)
            _log.SetAnchor(mergeIndex);

        if (pending.After is not null)
            ApplySnapshot(pending.After);

        _result = anim.Layout.Selected;
        _busy = false;
        _keys.Busy = false;
        _keys.SetNeedsDraw();
        _art.SetNeedsDraw();
        _anim = null;
        _clock = null;
        _pending = null;
    }

    // ---------- snapshot, status and log ----------

    private void ApplySnapshot(RepoSnapshot s)
    {
        var selName = _snapshot.Branches.Count > 0 ? _snapshot.Branches[_selected].Name : null;
        _snapshot = s;
        _snapshotSeq++;
        _cache.Clear();

        // Keep the selection on the same branch name; if that branch is gone, keep the index (clamped).
        var kept = -1;
        for (var i = 0; selName is not null && i < s.Branches.Count; i++)
            if (s.Branches[i].Name == selName)
            {
                kept = i;
                break;
            }
        _selected = kept >= 0 ? kept : Math.Clamp(_selected, 0, Math.Max(0, s.Branches.Count - 1));

        _status.Snapshot = s;
        _status.ErrorText = null;
        _status.SetNeedsDraw();

        _branches.Branches = s.Branches;
        _branches.Counts = s.Counts;
        _branches.Selected = _selected;
        _branches.SetNeedsDraw();
    }

    private void ShowGitError(Exception ex)
    {
        _status.ErrorText = ex.Message;
        _status.SetNeedsDraw();
        LogMessage("git error: " + ex.Message, Bad);
    }

    /// <summary>Logs each command, its exit code, and the output of `git merge` commands. Returns the log index of the first `git merge` line, or -1.</summary>
    private int LogCommands(IEnumerable<GitResult> results)
    {
        var mergeIndex = -1;
        foreach (var r in results)
        {
            var isMerge = r.Command.StartsWith("git merge", StringComparison.Ordinal);
            if (mergeIndex < 0 && isMerge)
                mergeIndex = _log.Count;
            _log.Add(new LogLine(LogKind.Command, r.Command, r.ExitCode));
            if (!isMerge)
                continue;

            foreach (var line in (r.StdOut + "\n" + r.StdErr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _log.Add(new LogLine(LogKind.Output, line));
        }
        _art.SetNeedsDraw();
        return mergeIndex;
    }

    private void LogMessage(string text, Style style)
    {
        _log.Add(new LogLine(LogKind.Message, text, 0, style));
        _log.SetNeedsDraw();
    }

    private void ReloadCountsAsync()
    {
        var seq = _snapshotSeq;
        Task.Run(() =>
        {
            try
            {
                var s = _reader.LoadSnapshot(withCounts: true, CancellationToken.None);
                Ui(() =>
                {
                    // Dropped when a merge or another snapshot has changed things since this read started.
                    if (!_busy && seq == _snapshotSeq)
                        ApplySnapshot(s);
                });
            }
            catch (Exception ex)
            {
                Ui(() => ShowGitError(ex));
            }
        });
    }

    // ---------- helpers ----------

    /// <summary>Runs an action on the UI thread. Ignored when the UI has already stopped.</summary>
    private void Ui(Action action)
    {
        try
        {
            _app.Invoke(action);
        }
        catch
        {
            // The UI is gone (Esc during a merge); the merge result is reported after the terminal is restored.
        }
    }

    private GraphLayout? TryLoadLayout(RepoSnapshot snap, Branch b)
    {
        try
        {
            return _reader.LoadLayout(snap, b, CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }

    private RepoSnapshot? TryLoadSnapshot()
    {
        try
        {
            return _reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }

    private CommitInfo? TryGetCommit(string hash)
    {
        try
        {
            return _git.GetCommit(hash);
        }
        catch
        {
            return null;
        }
    }

    private static (string Head, string Tip, bool Clean) CacheKey(RepoSnapshot snap, Branch b) =>
        (snap.HeadHash ?? "", b.Hash, snap.Clean);

    private static int FirstNonCurrent(RepoSnapshot s)
    {
        for (var i = 0; i < s.Branches.Count; i++)
            if (!s.Branches[i].IsCurrent)
                return i;
        return 0;
    }
}
