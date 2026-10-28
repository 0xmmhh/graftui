using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace graftui.Ui.App;

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
    private int _wantedBranchWidth;
    private volatile bool _outcomeShown;

    private TooSmallView _tooSmall = null!;
    private Window? _window;

    public AppController(IApplication app, GitClient git, GitClient rootGit, RepoSnapshot initial, string repoDisplay)
    {
        App = app;
        Git = git;
        Reader = new RepoReader(git);
        RootGit = rootGit;
        RootReader = new RepoReader(rootGit);
        MergeOp = new MergeOperation(git);
        StageOp = new StageOperation(rootGit);
        CommitOp = new CommitOperation(rootGit);
        DiscardOp = new DiscardOperation(rootGit);
        StashOp = new StashOperation(rootGit);
        PopOp = new PopOperation(rootGit);
        UndoOp = new UndoOperation(rootGit);
        Snapshot = initial;
        _wantedBranchWidth = BranchesView.WantedWidth(initial.Branches, initial.Counts);
        _repoDisplay = repoDisplay;
        BranchesTab = new BranchesTab(this);
        ChangesTab = new ChangesTab(this);
        ChangesAnimator = new ChangesAnimator(this);
        Animator = new Animator(this);
    }

    /// <summary>The tab the left pane shows. Branches is the start tab.</summary>
    internal enum TabKind { Branches, Changes }

    /// <summary>The merge worker. Null until a merge has been started. Its result is the outcome, or null when nothing merged.</summary>
    public Task<MergeOutcome?>? MergeTask { get; internal set; }

    /// <summary>
    /// The latest stage, unstage, commit or amend worker. Its result is the line that says what the change did (null when nothing ran).
    /// Program waits for it before exiting and prints the line if the change never showed its result on screen.
    /// </summary>
    public Task<string?>? ChangesTask { get; internal set; }

    private volatile bool _changeOutcomeShown;

    /// <summary>True once a change's result has been drawn in the UI (so the exit path need not print it again).</summary>
    public bool ChangeOutcomeShown => _changeOutcomeShown;

    internal void MarkChangeOutcomeShown() => _changeOutcomeShown = true;

    /// <summary>Called when a new change starts, so an outcome drawn for an earlier change does not count for this one.</summary>
    internal void BeginChangeOutcome() => _changeOutcomeShown = false;

    /// <summary>True once the outcome has been drawn in the UI (so the exit path need not print it again).</summary>
    public bool OutcomeShown => _outcomeShown;

    /// <summary>Stops a merge that has not started yet. Called when the UI is shutting down.</summary>
    public void MarkQuitting() => Gate.MarkQuitting();

    internal IApplication App { get; }
    internal GitClient Git { get; }
    internal RepoReader Reader { get; }
    internal MergeOperation MergeOp { get; }
    internal StageOperation StageOp { get; }
    internal CommitOperation CommitOp { get; }
    internal DiscardOperation DiscardOp { get; }
    internal StashOperation StashOp { get; }
    internal PopOperation PopOp { get; }
    internal UndoOperation UndoOp { get; }
    internal QuitGate Gate { get; } = new();

    /// <summary>The top-level repo. The Changes tab's paths are relative to it, so its reads and writes run here.</summary>
    internal GitClient RootGit { get; }
    internal RepoReader RootReader { get; }
    internal TabKind ActiveTab { get; private set; } = TabKind.Branches;

    internal RepoSnapshot Snapshot { get; private set; }
    internal int SnapshotSeq { get; set; }
    internal bool Busy { get; set; }
    internal string? Result { get; set; }

    internal StatusBarView Status { get; private set; } = null!;
    internal BranchesView BranchList { get; private set; } = null!;
    internal ArtPaneView Art { get; private set; } = null!;
    internal LogView Log { get; private set; } = null!;
    internal KeyBarView Keys { get; private set; } = null!;

    internal ChangesView ChangesList { get; private set; } = null!;

    /// <summary>The message box's subject line and body. They are subviews of the art pane and are shown only while a message is being typed.</summary>
    internal TextField SubjectField { get; private set; } = null!;
    internal BodyTextView BodyField { get; private set; } = null!;

    internal BranchesTab BranchesTab { get; }
    internal ChangesTab ChangesTab { get; }
    internal ChangesAnimator ChangesAnimator { get; }
    internal Animator Animator { get; }

    internal ScreenLayout CurrentLayout() => ScreenLayout.Compute(App.Screen.Width, App.Screen.Height, _wantedBranchWidth);

    internal void MarkOutcomeShown() => _outcomeShown = true;

    public Window BuildWindow()
    {
        var window = new Window { BorderStyle = LineStyle.None };
        window.SetScheme(Palette.RootScheme);
        _window = window;

        Status = new StatusBarView { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
        BranchList = new BranchesView
        {
            X = 0,
            Y = 1,
            Width = Dim.Func(_ => CurrentLayout().TooSmall ? 0 : CurrentLayout().BranchWidth),
            Height = Dim.Func(_ => CurrentLayout().TooSmall ? 0 : CurrentLayout().TopHeight),
        };
        Art = new ArtPaneView
        {
            X = Pos.Func(_ => CurrentLayout().BranchWidth),
            Y = 1,
            Width = Dim.Func(_ => CurrentLayout().TooSmall ? 0 : CurrentLayout().Cols - CurrentLayout().BranchWidth),
            Height = Dim.Func(_ => CurrentLayout().TooSmall ? 0 : CurrentLayout().TopHeight),
            DirectionRule = false,
        };
        Log = new LogView
        {
            X = 0,
            Y = Pos.Func(_ => 1 + CurrentLayout().TopHeight),
            Width = Dim.Fill(),
            Height = Dim.Func(_ => CurrentLayout().TooSmall ? 0 : CurrentLayout().LogHeight),
        };
        ChangesList = new ChangesView
        {
            X = 0,
            Y = 1,
            Width = Dim.Func(_ => CurrentLayout().TooSmall ? 0 : CurrentLayout().BranchWidth),
            Height = Dim.Func(_ => CurrentLayout().TooSmall ? 0 : CurrentLayout().TopHeight),
            Visible = false,
        };
        BuildMessageBox();
        Keys = new KeyBarView { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
        _tooSmall = new TooSmallView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Func(_ => CurrentLayout().TooSmall ? Math.Max(0, CurrentLayout().Rows - 2) : 0),
        };

        Status.RepoDisplay = _repoDisplay;
        Status.Snapshot = Snapshot;
        BranchList.Branches = Snapshot.Branches;
        BranchList.Counts = Snapshot.Counts;
        BranchList.Selected = BranchesTab.Selected;
        _tooSmall.Cols = App.Screen.Width;
        _tooSmall.Rows = App.Screen.Height;
        Art.ScreenCols = App.Screen.Width;
        BranchesTab.ShowInitialState();
        SyncBranchKeys();

        window.Add(Status, BranchList, ChangesList, Art, Log, Keys, _tooSmall);

        App.ScreenChanged += (_, _) =>
        {
            _tooSmall.Cols = App.Screen.Width;
            _tooSmall.Rows = App.Screen.Height;
            Art.ScreenCols = App.Screen.Width;
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

    /// <summary>
    /// The message box: a one-line subject and a body, laid over the art pane's form area. Hidden and not focusable until a
    /// message is typed (the art pane itself is not focusable, so it becomes focusable only while the form is open).
    /// </summary>
    private void BuildMessageBox()
    {
        SubjectField = new TextField
        {
            Visible = false,
            CanFocus = false,
            X = Pos.Func(_ => FieldX()),
            Y = Pos.Func(_ => FieldArea() is { } a ? a.Y + AreasLayout.For(a.W, a.H).SubjectRow : 0),
            Width = Dim.Func(_ => FieldArea() is { } a && !CurrentLayout().TooSmall ? a.W - AreasLayout.FieldX - 7 : 0),
            Height = 1,
        };
        BodyField = new BodyTextView
        {
            Visible = false,
            CanFocus = false,
            X = Pos.Func(_ => FieldX()),
            Y = Pos.Func(_ => FieldArea() is { } a ? a.Y + AreasLayout.For(a.W, a.H).BodyRow : 0),
            Width = Dim.Func(_ => FieldArea() is { } a && !CurrentLayout().TooSmall ? a.W - AreasLayout.FieldX - 7 : 0),
            Height = 2,
            TabKeyAddsTab = false,
            WordWrap = false,
        };
        SubjectField.SetScheme(Palette.InputScheme);
        BodyField.SetScheme(Palette.InputScheme);
        SubjectField.TextChanged += (_, _) => Art.SetNeedsDraw();
        BodyField.ContentsChanged += (_, _) => Art.SetNeedsDraw();
        Art.Add(SubjectField, BodyField);
    }

    /// <summary>The art's area inside the art pane, in the pane's own coordinates; null when no art fits.</summary>
    private (int X, int Y, int W, int H)? FieldArea() =>
        ArtPaneView.ArtArea(Art.Viewport.Width, Art.Viewport.Height);

    private int FieldX() => FieldArea() is { } a ? a.X + AreasLayout.FieldX : 0;

    // ---------- keys ----------

    private void OnKeyDown(object? sender, Key e)
    {
        // The message box and the amend confirmations take every key first, so Esc cancels them rather than quitting.
        if (ActiveTab == TabKind.Changes && ChangesTab.Mode == ChangesMode.Input)
        {
            ChangesTab.HandleInputKey(e);
            return;
        }
        if (ActiveTab == TabKind.Changes && ChangesTab.Mode == ChangesMode.Ask)
        {
            ChangesTab.HandleAskKey(e);
            return;
        }

        // v switches the Commands pane between the simplified and the exact list, also while git is busy (it only changes the display).
        if (e == Key.V)
        {
            e.Handled = true;
            Log.ToggleExact();
            return;
        }

        if (e == Key.Tab)
        {
            e.Handled = true;
            if (!Busy)
                ToggleTab();
            return;
        }

        if (ActiveTab == TabKind.Changes)
        {
            ChangesTab.HandleKey(e);
            return;
        }

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
        else if (e == Key.U)
        {
            e.Handled = true;
            BranchesTab.StartUndo();
        }
    }

    /// <summary>Switches the left pane between Branches and Changes. Only the view and the key bar change; each tab loads its own data.</summary>
    private void ToggleTab()
    {
        Result = null;
        ActiveTab = ActiveTab == TabKind.Branches ? TabKind.Changes : TabKind.Branches;
        var onChanges = ActiveTab == TabKind.Changes;
        BranchList.Visible = !onChanges;
        ChangesList.Visible = onChanges;
        Keys.Mode = onChanges ? KeyBarMode.Changes : KeyBarMode.Branches;
        Keys.SetNeedsDraw();
        BranchList.SetNeedsDraw();
        ChangesList.SetNeedsDraw();
        Art.DirectionRule = onChanges;
        Art.SetNeedsDraw();

        if (onChanges)
        {
            ChangesTab.Activate();
            return;
        }

        Status.ChangedFiles = null;
        Status.SetNeedsDraw();
        ChangesTab.Deactivate();
        Art.Below = null;
        if (Snapshot.Branches.Count == 0)
            BranchesTab.ShowInitialState();
        else
            BranchesTab.RequestPreview();
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
        SyncBranchKeys();

        Status.Snapshot = s;
        Status.ErrorText = null;
        Status.SetNeedsDraw();

        BranchList.Branches = s.Branches;
        BranchList.Counts = s.Counts;
        BranchList.Selected = BranchesTab.Selected;
        BranchList.SetNeedsDraw();
    }

    /// <summary>
    /// The Branches key bar: m shows when the selected branch can be merged (it is not the current one, and it is ahead of HEAD or
    /// its counts are not known yet), u when there is an undo step. Called whenever the snapshot or the selection changes.
    /// </summary>
    internal void SyncBranchKeys()
    {
        var s = Snapshot;
        var sel = BranchesTab.Selected < s.Branches.Count ? s.Branches[BranchesTab.Selected] : null;
        var state = new BranchesKeyState(s.Branches.Count, BranchesKeyState.MergeOffered(sel, s.Counts), s.HasUndo);
        if (Keys is null || Keys.BranchesState == state)
            return;
        Keys.BranchesState = state;
        Keys.SetNeedsDraw();
    }

    internal void ShowGitError(Exception ex)
    {
        Status.ErrorText = ex.Message;
        Status.SetNeedsDraw();
        LogMessage("git error: " + ex.Message, Bad);
    }

    /// <summary>
    /// Logs each command with its exit code, and the output of the commands that <paramref name="showOutput"/> picks (by default
    /// the `git merge` commands). Each command is one entry: its exact line and, when the simplified pane shows it, its simplified line.
    /// <paramref name="hints"/> tells the simplified line what a commit's message was. Returns the log position of the first command that
    /// <paramref name="anchorOn"/> picks (by default the first `git merge`), or -1.
    /// </summary>
    internal int LogCommands(IEnumerable<GitResult> results, Func<GitResult, bool>? showOutput = null, Func<GitResult, bool>? anchorOn = null,
        CommandHints? hints = null)
    {
        var merges = (GitResult r) => r.Command.StartsWith("git merge", StringComparison.Ordinal);
        showOutput ??= merges;
        anchorOn ??= merges;
        var h = hints ?? CommandHints.None;

        var mergeIndex = -1;
        foreach (var r in results)
        {
            if (mergeIndex < 0 && anchorOn(r))
                mergeIndex = Log.Position;
            Log.Add(CommandDisplay.Entry(r, h, showOutput(r)));
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
