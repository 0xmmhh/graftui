using Graft.Git;
using Graft.Model;
using Graft.Operations;
using Graft.Ui.Text;
using Graft.Ui.Views;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Graft.Ui.App;

/// <summary>
/// Controller: owns the views and the shared state (snapshot, result, busy flag) and wires them to git.
/// The branch list lives in <see cref="BranchesTab"/>, the merge's screens in <see cref="Animator"/>, the quit check in <see cref="QuitGate"/>.
/// Git reads and the merge run on worker threads; every change to the views happens on the UI thread
/// (through <see cref="Ui"/>).
/// </summary>
public sealed class AppController
{
    private static readonly Style Bad = new(Ink.Bad);

    private readonly string _repoDisplay;
    private readonly bool _sandboxMode;
    private int _wantedBranchWidth;
    private volatile bool _outcomeShown;

    private TooSmallView _tooSmall = null!;
    private Window? _window;

    public AppController(IApplication app, GitClient git, RepoSnapshot initial, string repoDisplay, bool sandboxMode)
    {
        App = app;
        Git = git;
        Reader = new RepoReader(git);
        MergeOp = new MergeOperation(git);
        Snapshot = initial;
        _wantedBranchWidth = BranchesView.WantedWidth(initial.Branches, initial.Counts);
        _repoDisplay = repoDisplay;
        _sandboxMode = sandboxMode;
        BranchesTab = new BranchesTab(this);
        Animator = new Animator(this);
    }

    /// <summary>The merge worker. Null until a merge has been started. Its result is the outcome, or null when nothing merged.</summary>
    public Task<MergeOutcome?>? MergeTask { get; internal set; }

    /// <summary>True once the outcome has been drawn in the UI (so the exit path need not print it again).</summary>
    public bool OutcomeShown => _outcomeShown;

    /// <summary>Stops a merge that has not started yet. Called when the UI is shutting down.</summary>
    public void MarkQuitting() => Gate.MarkQuitting();

    internal IApplication App { get; }
    internal GitClient Git { get; }
    internal RepoReader Reader { get; }
    internal MergeOperation MergeOp { get; }
    internal QuitGate Gate { get; } = new();

    internal RepoSnapshot Snapshot { get; private set; }
    internal int SnapshotSeq { get; set; }
    internal bool Busy { get; set; }
    internal string? Result { get; set; }

    internal StatusBarView Status { get; private set; } = null!;
    internal BranchesView BranchList { get; private set; } = null!;
    internal ArtPaneView Art { get; private set; } = null!;
    internal LogView Log { get; private set; } = null!;
    internal KeyBarView Keys { get; private set; } = null!;

    internal BranchesTab BranchesTab { get; }
    internal Animator Animator { get; }

    internal ScreenLayout CurrentLayout() => ScreenLayout.Compute(App.Screen.Width, App.Screen.Height, _wantedBranchWidth);

    internal void MarkOutcomeShown() => _outcomeShown = true;

    public Window BuildWindow()
    {
        ScreenLayout Layout() => ScreenLayout.Compute(App.Screen.Width, App.Screen.Height, _wantedBranchWidth);

        var window = new Window { BorderStyle = LineStyle.None };
        window.SetScheme(Palette.RootScheme);
        _window = window;

        Status = new StatusBarView { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
        BranchList = new BranchesView
        {
            X = 0,
            Y = 1,
            Width = Dim.Func(_ => Layout().TooSmall ? 0 : Layout().BranchWidth),
            Height = Dim.Func(_ => Layout().TooSmall ? 0 : Layout().TopHeight),
        };
        Art = new ArtPaneView
        {
            X = Pos.Func(_ => Layout().BranchWidth),
            Y = 1,
            Width = Dim.Func(_ => Layout().TooSmall ? 0 : Layout().Cols - Layout().BranchWidth),
            Height = Dim.Func(_ => Layout().TooSmall ? 0 : Layout().TopHeight),
        };
        Log = new LogView
        {
            X = 0,
            Y = Pos.Func(_ => 1 + Layout().TopHeight),
            Width = Dim.Fill(),
            Height = Dim.Func(_ => Layout().TooSmall ? 0 : Layout().LogHeight),
        };
        Keys = new KeyBarView { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1, SandboxMode = _sandboxMode };
        _tooSmall = new TooSmallView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Func(_ => Layout().TooSmall ? Math.Max(0, Layout().Rows - 2) : 0),
        };

        Status.RepoDisplay = _repoDisplay;
        Status.Snapshot = Snapshot;
        BranchList.Branches = Snapshot.Branches;
        BranchList.Counts = Snapshot.Counts;
        BranchList.Selected = BranchesTab.Selected;
        Keys.SandboxMode = _sandboxMode;
        _tooSmall.Cols = App.Screen.Width;
        _tooSmall.Rows = App.Screen.Height;
        BranchesTab.ShowInitialState();

        window.Add(Status, BranchList, Art, Log, Keys, _tooSmall);

        App.ScreenChanged += (_, _) =>
        {
            _tooSmall.Cols = App.Screen.Width;
            _tooSmall.Rows = App.Screen.Height;
            window.SetNeedsLayout();
            _tooSmall.SetNeedsDraw();
            Art.SetNeedsDraw();
            BranchList.SetNeedsDraw();
            Log.SetNeedsDraw();
        };
        App.Keyboard.KeyDown += OnKeyDown;

        BranchesTab.RequestPreview();
        ReloadCountsAsync();
        return window;
    }

    // ---------- keys ----------

    private void OnKeyDown(object? sender, Key e)
    {
        if (e == Key.CursorUp)
        {
            e.Handled = true;
            BranchesTab.MoveSelection(-1);
        }
        else if (e == Key.CursorDown)
        {
            e.Handled = true;
            BranchesTab.MoveSelection(+1);
        }
        else if (e == Key.M)
        {
            e.Handled = true;
            BranchesTab.StartMerge();
        }
    }

    // ---------- snapshot, status and log ----------

    internal void ApplySnapshot(RepoSnapshot s)
    {
        var selName = BranchesTab.SelectedName();
        Snapshot = s;
        SnapshotSeq++;
        BranchesTab.ClearCache();
        _wantedBranchWidth = BranchesView.WantedWidth(s.Branches, s.Counts);
        _window?.SetNeedsLayout();
        BranchesTab.KeepSelection(selName, s);

        Status.Snapshot = s;
        Status.ErrorText = null;
        Status.SetNeedsDraw();

        BranchList.Branches = s.Branches;
        BranchList.Counts = s.Counts;
        BranchList.Selected = BranchesTab.Selected;
        BranchList.SetNeedsDraw();
    }

    internal void ShowGitError(Exception ex)
    {
        Status.ErrorText = ex.Message;
        Status.SetNeedsDraw();
        LogMessage("git error: " + ex.Message, Bad);
    }

    /// <summary>Logs each command, its exit code, and the output of `git merge` commands. Returns the log position of the first `git merge` line, or -1.</summary>
    internal int LogCommands(IEnumerable<GitResult> results)
    {
        var mergeIndex = -1;
        foreach (var r in results)
        {
            var isMerge = r.Command.StartsWith("git merge", StringComparison.Ordinal);
            if (mergeIndex < 0 && isMerge)
                mergeIndex = Log.Position;
            Log.Add(new LogLine(LogKind.Command, r.Command, r.ExitCode));
            if (!isMerge)
                continue;

            foreach (var line in (r.StdOut + "\n" + r.StdErr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Log.Add(new LogLine(LogKind.Output, line));
        }
        Art.SetNeedsDraw();
        return mergeIndex;
    }

    internal void LogMessage(string text, Style style)
    {
        Log.Add(new LogLine(LogKind.Message, text, 0, style));
        Log.SetNeedsDraw();
    }

    internal void ReloadCountsAsync()
    {
        var seq = SnapshotSeq;
        Task.Run(() =>
        {
            try
            {
                var s = Reader.LoadSnapshot(withCounts: true, CancellationToken.None);
                Ui(() =>
                {
                    // Dropped when a merge or another snapshot has changed things since this read started.
                    if (!Busy && seq == SnapshotSeq)
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
    internal void Ui(Action action)
    {
        try
        {
            App.Invoke(action);
        }
        catch
        {
            // The UI is gone (Esc during a merge); the merge result is reported after the terminal is restored.
        }
    }

    internal GraphLayout? TryLoadLayout(RepoSnapshot snap, Branch b)
    {
        try
        {
            return Reader.LoadLayout(snap, b, CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }

    internal RepoSnapshot? TryLoadSnapshot()
    {
        try
        {
            return Reader.LoadSnapshot(withCounts: true, CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }

    internal CommitInfo? TryGetCommit(string hash)
    {
        try
        {
            return Git.GetCommit(hash);
        }
        catch
        {
            return null;
        }
    }
}
