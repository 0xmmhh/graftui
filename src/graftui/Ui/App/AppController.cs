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
    private readonly BranchListWidth _listWidth;

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
        SwitchOp = new SwitchOperation(rootGit);
        CreateBranchOp = new CreateBranchOperation(rootGit);
        RenameBranchOp = new RenameBranchOperation(rootGit);
        DeleteBranchOp = new DeleteBranchOperation(rootGit);
        StashMergeOp = new StashMergeOperation(rootGit);
        RebaseOp = new RebaseOperation(rootGit);
        ResumeOp = new ResumeSequenceOperation(rootGit);
        Snapshot = initial;
        _listWidth = new BranchListWidth(BranchesView.WantedWidth(initial.Branches, initial.Counts));
        _repoDisplay = repoDisplay;
        BranchesTab = new BranchesTab(this);
        ChangesTab = new ChangesTab(this);
        ChangesAnimator = new ChangesAnimator(this);
        BranchAnimator = new BranchAnimator(this);
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

    private volatile int _changeExitCode;
    private volatile bool _changeLeftPause;

    /// <summary>
    /// The exit code the latest change asks for: 1 when a stash-merge-pop conflicted or failed, or a rebase stopped at a conflict (as a
    /// failed merge exits 1), else 0. Program reads it after the change's worker has finished.
    /// </summary>
    public int ChangeExitCode => _changeExitCode;

    /// <summary>True when <see cref="ChangeExitCode"/> was set by a rebase left paused: if the pause is gone at exit, its code goes too.</summary>
    public bool ChangeLeftPause => _changeLeftPause;

    internal void SetChangeExitCode(int code, bool leftPause = false)
    {
        _changeExitCode = code;
        _changeLeftPause = leftPause;
    }

    /// <summary>Called when a new change starts, so an outcome drawn for an earlier change does not count for this one.</summary>
    internal void BeginChangeOutcome()
    {
        _changeOutcomeShown = false;
        SetChangeExitCode(0);
    }

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
    internal SwitchOperation SwitchOp { get; }
    internal CreateBranchOperation CreateBranchOp { get; }
    internal RenameBranchOperation RenameBranchOp { get; }
    internal DeleteBranchOperation DeleteBranchOp { get; }
    internal StashMergeOperation StashMergeOp { get; }
    internal RebaseOperation RebaseOp { get; }
    internal ResumeSequenceOperation ResumeOp { get; }
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

    /// <summary>Where the subject field sits: the commit and stash form's area, or the one-line name box's rows (new branch, rename).</summary>
    internal enum FieldSpot { Areas, BranchName }

    private FieldSpot _fieldPlacement;

    internal FieldSpot FieldPlacement
    {
        get => _fieldPlacement;
        set
        {
            _fieldPlacement = value;
            Art?.SetNeedsLayout();
        }
    }

    internal BranchesTab BranchesTab { get; }
    internal ChangesTab ChangesTab { get; }
    internal ChangesAnimator ChangesAnimator { get; }
    internal BranchAnimator BranchAnimator { get; }
    internal Animator Animator { get; }

    internal ScreenLayout CurrentLayout() => ScreenLayout.Compute(App.Screen.Width, App.Screen.Height, _listWidth.Width);

    /// <summary>True when the whole art, and so the message box's fields, fits inside the art pane at this size.</summary>
    internal bool ArtFits()
    {
        var area = ArtPaneView.ArtArea(Art.Viewport.Width, Art.Viewport.Height, Art.CompactArt);
        return area != null && area.Value.Y + area.Value.H <= Art.Viewport.Height;
    }

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
            CompactArt = true,
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
        BranchList.PausedBranch = SequenceStatus.PausedBranch(Snapshot);
        _tooSmall.Cols = App.Screen.Width;
        _tooSmall.Rows = App.Screen.Height;
        Art.ScreenCols = App.Screen.Width;
        BranchesTab.ShowInitialState();
        SyncBranchKeys();

        window.Add(Status, BranchList, ChangesList, Art, Log, Keys, _tooSmall);

        App.ScreenChanged += (_, _) =>
        {
            // A resize lays the art out at a new size anyway, so a held list width is applied with it.
            ReleaseBranchWidth();
            _tooSmall.Cols = App.Screen.Width;
            _tooSmall.Rows = App.Screen.Height;
            Art.ScreenCols = App.Screen.Width;
            window.SetNeedsLayout();
            _tooSmall.SetNeedsDraw();
            Art.SetNeedsDraw();
            BranchList.SetNeedsDraw();
            Log.SetNeedsDraw();
        };
        // The todo list hides its art by the pane's height. The viewport is current only after the layout, which runs after ScreenChanged.
        Art.ViewportChanged += (_, _) => BranchesTab.RefreshTodoArt();
        App.Keyboard.KeyDown += OnKeyDown;

        BranchesTab.RequestPreview();
        ReloadCountsAsync();
        return window;
    }

    /// <summary>The subject line's row: the name box's row while the branch name is being typed, else the subject row of the art.</summary>
    private int SubjectY()
    {
        if (FieldPlacement == FieldSpot.BranchName)
            return NameBoxOrZero().Y;
        var area = FieldArea();
        if (area is null)
            return 0;
        return area.Value.Y + AreasLayout.For(area.Value.W, area.Value.H).SubjectRow;
    }

    /// <summary>The subject line's width: nothing when the screen is too small, the name box's width while the branch name is typed, else the art's width less the form's gutter.</summary>
    private int SubjectWidth()
    {
        if (CurrentLayout().TooSmall)
            return 0;
        if (FieldPlacement == FieldSpot.BranchName)
            return NameBoxOrZero().W;
        var area = FieldArea();
        if (area is null)
            return 0;
        return area.Value.W - AreasLayout.FieldX - 7;
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
            X = Pos.Func(_ => FieldPlacement == FieldSpot.BranchName ? NameBoxOrZero().X : FieldX()),
            Y = Pos.Func(_ => SubjectY()),
            Width = Dim.Func(_ => SubjectWidth()),
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
        SubjectField.SetScheme(Palette.FieldScheme(false, bold: true));
        BodyField.SetScheme(Palette.FieldScheme(false, bold: false));
        SubjectField.TextChanged += (_, _) => Art.SetNeedsDraw();
        BodyField.ContentsChanged += (_, _) => Art.SetNeedsDraw();
        SubjectField.HasFocusChanged += (_, _) => RestyleFields();
        BodyField.HasFocusChanged += (_, _) => RestyleFields();
        Art.Add(SubjectField, BodyField);
    }

    /// <summary>
    /// Gives the subject field the focus with the cursor after its text. A text field selects all its text when it takes the
    /// focus, and an amend or a rename opens with the old text, so the focus is taken first and the selection cleared after it:
    /// typing then adds to the end instead of replacing the old text.
    /// </summary>
    internal void FocusSubjectAtEnd()
    {
        SubjectField.SetFocus();
        SubjectField.ClearAllSelection();
        SubjectField.InsertionPoint = (SubjectField.Text ?? "").Length;
    }

    /// <summary>
    /// The focused field takes the selection colour; the other one keeps the chip shade. The form's labels, its hint and the key
    /// bar follow the focus too, so they are drawn again.
    /// </summary>
    private void RestyleFields()
    {
        SubjectField.SetScheme(Palette.FieldScheme(SubjectField.HasFocus, bold: true));
        BodyField.SetScheme(Palette.FieldScheme(BodyField.HasFocus, bold: false));
        SubjectField.SetNeedsDraw();
        BodyField.SetNeedsDraw();
        // The bar and the hint line depend on the focused field: Enter saves from the subject only.
        Keys.BodyFocused = BodyField.HasFocus;
        Keys.SetNeedsDraw();
        Art.SetNeedsDraw();
    }

    /// <summary>The art's area inside the art pane, in the pane's own coordinates; null when no art fits.</summary>
    private (int X, int Y, int W, int H)? FieldArea() =>
        ArtPaneView.ArtArea(Art.Viewport.Width, Art.Viewport.Height);

    private int FieldX() => FieldArea() is { } a ? a.X + AreasLayout.FieldX : 0;

    /// <summary>The name box's field position and width in the art pane; all zero when no row is free for it.</summary>
    private (int X, int Y, int W) NameBoxOrZero() =>
        ArtPaneView.NameField(Art.Viewport.Width, Art.Viewport.Height, BranchesTab.NameCaptionTwoLines(Art.Viewport.Width, Art.Viewport.Height), Art.CompactArt)
            is { } n ? (n.X, n.Y, n.W) : (0, 0, 0);

    /// <summary>The name box moves when its caption wraps or unwraps, so its field is placed again.</summary>
    internal void RelayoutNameBox()
    {
        Art?.SetNeedsLayout();
        _window?.SetNeedsLayout();
    }

    // ---------- keys ----------

    private void OnKeyDown(object? sender, Key e)
    {
        // A branch change's animation holds Esc once git has answered: Esc would quit graftui before the change's last frame. Other keys are refused by Busy.
        if (e == Key.Esc && BranchAnimator.HoldsEsc)
        {
            e.Handled = true;
            return;
        }

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
        if (ActiveTab == TabKind.Branches && BranchesTab.Mode == BranchesMode.Input)
        {
            BranchesTab.HandleInputKey(e);
            return;
        }
        if (ActiveTab == TabKind.Branches && BranchesTab.Mode == BranchesMode.Ask)
        {
            BranchesTab.HandleAskKey(e);
            return;
        }

        // v switches the Commands pane between the simplified and the exact list, also while git is busy (it only changes the display).
        if (e == Key.V)
        {
            e.Handled = true;
            Log.ToggleExact();
            return;
        }

        // The rebase's confirm screen takes every key but v, so Tab does not leave it.
        if (ActiveTab == TabKind.Branches && BranchesTab.Mode == BranchesMode.Confirm)
        {
            BranchesTab.HandleConfirmKey(e);
            return;
        }

        // The todo list of an interactive rebase takes every key but v as well: its keys edit the plan, and Tab does not leave it.
        if (ActiveTab == TabKind.Branches && BranchesTab.Mode == BranchesMode.Todo)
        {
            BranchesTab.HandleTodoKey(e);
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

        // A paused rebase takes the Branches keys: Enter, S and X continue or abort it, the other branch keys are refused.
        if (IsPaused)
        {
            BranchesTab.HandlePauseKey(e);
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
        else if (e == Key.S)
        {
            e.Handled = true;
            BranchesTab.StartSwitch();
        }
        else if (e == Key.N)
        {
            e.Handled = true;
            BranchesTab.BeginCreate();
        }
        else if (e == Key.R.WithShift)
        {
            e.Handled = true;
            BranchesTab.BeginRename();
        }
        else if (e == Key.D.WithShift)
        {
            e.Handled = true;
            BranchesTab.AskDelete();
        }
        else if (e == Key.R)
        {
            e.Handled = true;
            BranchesTab.BeginRebase();
        }
        else if (e == Key.I)
        {
            e.Handled = true;
            BranchesTab.BeginInteractive();
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
        BranchesTab.ForgetPauseNote();
        ReleaseBranchWidth();
        ActiveTab = ActiveTab == TabKind.Branches ? TabKind.Changes : TabKind.Branches;
        var onChanges = ActiveTab == TabKind.Changes;
        BranchList.Visible = !onChanges;
        ChangesList.Visible = onChanges;
        Keys.Mode = onChanges ? KeyBarMode.Changes : KeyBarMode.Branches;
        Keys.SetNeedsDraw();
        BranchList.SetNeedsDraw();
        ChangesList.SetNeedsDraw();
        Art.DirectionRule = onChanges;
        Art.CompactArt = !onChanges;
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
        _listWidth.Offer(BranchesView.WantedWidth(s.Branches, s.Counts));
        _window?.SetNeedsLayout();
        BranchesTab.KeepSelection(selName, s);
        SyncBranchKeys();

        Status.Snapshot = s;
        Status.ErrorText = null;
        Status.SetNeedsDraw();

        BranchList.Branches = s.Branches;
        BranchList.Counts = s.Counts;
        BranchList.Selected = BranchesTab.Selected;
        BranchList.PausedBranch = SequenceStatus.PausedBranch(s);
        BranchList.SetNeedsDraw();
    }

    /// <summary>True while a rebase or cherry-pick that graftui started is paused. Only that pause is shown as one.</summary>
    internal bool IsPaused => SequenceStatus.IsPaused(Snapshot);

    /// <summary>The pause's caption for the Changes tab, or null when nothing is paused.</summary>
    internal Caption? PauseCaption() => IsPaused ? SequenceStatus.Caption(Snapshot.Paused!) : null;

    /// <summary>
    /// What the repo holds now, read after the UI has closed: a pause graftui started that is still there, or a sequence of graftui's that
    /// is still in progress but cannot be read. Program prints the matching exit line and exits 1 for a conflict.
    /// </summary>
    public SequenceRead ReadSequenceAtExit() => new SequenceReader(RootGit).ReadSequence();

    /// <summary>Holds the branch list's width where it is, so the art does not reflow under a switch's last frame.</summary>
    internal void HoldBranchWidth() => _listWidth.Hold();

    /// <summary>Applies the width the list wants, if it was held back. Called on a selection move, a change, a tab change, undo and a resize.</summary>
    internal void ReleaseBranchWidth()
    {
        if (_listWidth.Release())
            _window?.SetNeedsLayout();
    }

    /// <summary>
    /// The Branches key bar: the keys that act for the selected branch (m, s, n, R, D), and u when there is an undo step. Called whenever
    /// the snapshot or the selection changes.
    /// </summary>
    internal void SyncBranchKeys()
    {
        var s = Snapshot;
        var sel = BranchesTab.Selected < s.Branches.Count ? s.Branches[BranchesTab.Selected] : null;
        var state = BranchesKeyState.For(s, sel);
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

    /// <summary>
    /// Reads the snapshot with counts on a worker. On the UI thread it is applied, unless a merge or another snapshot has changed things
    /// since the read started. <paramref name="then"/> runs there afterwards, once the read is applied or dropped: a screen that depends on
    /// the snapshot (the pause screen) is then drawn from a fresh read.
    /// </summary>
    internal void ReloadCountsAsync(Action? then = null)
    {
        var seq = SnapshotSeq;
        Task.Run(() =>
        {
            RepoSnapshot? s = null;
            Exception? failed = null;
            try
            {
                s = Reader.LoadSnapshot(withCounts: true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failed = ex;
            }
            Ui(() => CountsRead(s, failed, seq, then));
        });
    }

    /// <summary>
    /// The UI thread's part of a counts read. A failed read shows its error and leaves the snapshot as it was. <paramref name="then"/> runs
    /// in both cases: a screen that depends on the snapshot (the pause screen) is still due, and the snapshot in place already says what it is.
    /// </summary>
    internal void CountsRead(RepoSnapshot? s, Exception? failed, int seq, Action? then)
    {
        if (failed is not null)
            ShowGitError(failed);
        else if (s is not null && !Busy && seq == SnapshotSeq)
            ApplySnapshot(s);
        then?.Invoke();
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

    /// <summary>The ink of a branch in the snapshot's list: its lane colour, or the foreground for HEAD or a name that is not listed.</summary>
    internal Ink InkOf(string name) => BranchInks.Named(Snapshot.Branches, name);

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

    /// <summary>The layout of a paused rebase's branch against its onto, from the branch's old tip; null when a read fails.</summary>
    internal GraphLayout? TryLoadRebaseLayout(RepoSnapshot snap, string branch, string oldTip, string onto)
    {
        try
        {
            return Reader.LoadRebaseLayout(branch, oldTip, onto, snap, CancellationToken.None);
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
