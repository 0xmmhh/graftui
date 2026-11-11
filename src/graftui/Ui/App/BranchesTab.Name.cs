using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Input;

namespace graftui.Ui.App;

/// <summary>
/// The one-line name box of n (a new branch at the current tip) and R (rename the selected branch). The box is a field on the art
/// pane's commit rows, with its label above it when there is room. Enter checks the name and runs the change; a name that git
/// refuses (invalid or taken) keeps the box open with the reason under the art. Esc cancels, and Tab is swallowed.
/// </summary>
internal sealed partial class BranchesTab
{
    private static readonly Style FgB = new(Ink.Fg, true);

    private const string NewHint = "Enter creates it · Esc cancels";
    private const string RenameHint = "Enter renames · Esc cancels";
    private const string NameLabel = "name     ";

    // The open box: whether it renames, the name it renames (rename) or the tip the new branch starts at (create), its layout under the
    // art, and the reason of the last refused name (shown in place of the hint until the next Enter or Esc).
    private bool _nameRename;
    private string _nameOld = "";
    private string _nameTip = "";
    private GraphLayout? _nameLayout;
    private Caption? _nameNote;

    /// <summary>n: opens the name box for a new branch at the current branch's tip. A detached HEAD or an empty repo is refused on the art.</summary>
    internal void BeginCreate()
    {
        var layout = _app.CurrentLayout();
        var snap = _app.Snapshot;
        if (_app.Busy || Mode != BranchesMode.Browse || layout.TooSmall)
            return;

        if (snap.HeadHash is null || snap.CurrentBranch.Length == 0)
        {
            var reason = snap.CurrentBranch.Length == 0 ? BranchRefusals.Detached : BranchRefusals.NoCommitYet;
            var tipShort = BranchGit.Short(snap.HeadHash ?? "");
            ShowBranchRefused([], reason, null, cols => DirectionLine.NewBranch("", tipShort, cols));
            return;
        }
        if (NameBox() is null)
        {
            _app.LogMessage("Refused: widen the window to type a branch name", WarnB);
            return;
        }

        var cur = snap.CurrentBranch;
        var tip = snap.HeadHash ?? "";
        var current = snap.Branches.FirstOrDefault(b => b.IsCurrent);
        var cached = current is null ? null : CachedLayout(snap, current);
        LoadForNameBox(
            () => Or(cached ?? (current is null ? null : _app.TryLoadLayout(snap, current)), snap, cur, tip),
            loaded => OpenNameForm(rename: false, cur, tip, loaded));
    }

    /// <summary>R: opens the name box to rename the selected branch (the current one included), with its old name in the box.</summary>
    internal void BeginRename()
    {
        var layout = _app.CurrentLayout();
        var snap = _app.Snapshot;
        if (_app.Busy || Mode != BranchesMode.Browse || layout.TooSmall || snap.Branches.Count == 0)
            return;
        if (NameBox() is null)
        {
            _app.LogMessage("Refused: widen the window to type a branch name", WarnB);
            return;
        }

        var b = snap.Branches[_selected];
        var cached = CachedLayout(snap, b);
        LoadForNameBox(() => Or(cached ?? _app.TryLoadLayout(snap, b), snap, b.Name, b.Hash),
            loaded => OpenNameForm(rename: true, b.Name, b.Hash, loaded));
    }

    /// <summary>Keys in the name box. Esc cancels, Tab is swallowed, Enter submits, and any other key edits the name.</summary>
    internal void HandleInputKey(Key e)
    {
        if (e == Key.Esc)
        {
            e.Handled = true;
            if (!_app.Busy)
                CancelNameForm();
            return;
        }

        // While a check runs, or when the window shrank and the box is not drawn, keys are taken but go nowhere.
        var shown = _app.ArtFits() && NameBox() is not null;
        if (!shown || _app.Busy || e == Key.Tab || e == Key.Tab.WithShift)
        {
            e.Handled = true;
            return;
        }

        if (e == Key.Enter)
        {
            e.Handled = true;
            SubmitNameForm();
        }
        // Anything else is left to the field, which edits the name.
    }

    /// <summary>
    /// Shows the box: the field on the commit rows (its label above it when the pane has two rows free), the preview of the layout
    /// under it, and the hint as the caption. Opening takes the keys; the field has the focus. Internal so the tests can open it without a git worker.
    /// </summary>
    internal void OpenNameForm(bool rename, string old, string tip, GraphLayout layout)
    {
        _nameRename = rename;
        _nameOld = old;
        _nameTip = tip;
        _nameLayout = layout;
        _nameNote = null;
        Mode = BranchesMode.Input;

        _app.FieldPlacement = AppController.FieldSpot.BranchName;
        _app.Art.CanFocus = true;
        _app.Art.CaptionKeepsBelowRows = true;

        var subject = _app.SubjectField;
        subject.CanFocus = true;
        subject.Visible = true;
        subject.Text = rename ? old : "";
        _app.BodyField.Visible = false;
        _app.BodyField.CanFocus = false;

        var labelRow = NameBox() is { LabelRow: true };
        var label = rename ? $"Rename {old} to" : $"New branch at {BranchGit.Short(tip)} (switches to it)";
        IReadOnlyList<IReadOnlyList<Seg>> below = labelRow
            ? [[new Seg(label, FgB)], [new Seg(NameLabel, Muted)]]
            : [[new Seg(NameLabel, Muted)]];

        var art = _app.Art;
        art.Title = rename ? "Rename" : "New branch";
        if (rename)
        {
            var oldInk = _app.InkOf(old);
            art.Direction = cols => DirectionLine.Rename(old, oldInk, null, cols);
        }
        else
            art.Direction = cols => DirectionLine.NewBranch("", BranchGit.Short(tip), cols);
        art.Below = below;
        art.Panel = null;
        art.FallbackCaption = null;
        var hint = rename ? RenameHint : NewHint;
        art.Scene = geo => new SceneFrame(MergeScenes.Preview(geo, layout).Grid, _nameNote ?? new Caption(hint, Muted));

        _app.Keys.Mode = KeyBarMode.Message;
        _app.Keys.MessageEnterLabel = rename ? " rename" : " create";
        _app.Keys.SetNeedsDraw();
        art.SetNeedsDraw();
        _app.FocusSubjectAtEnd();
    }

    /// <summary>
    /// Enter. A rename to the same name closes the box with a note. Otherwise the name is checked on a worker: a name problem keeps the
    /// box open, another refusal closes it and shows the refusal screen, and a name that is fine runs the change.
    /// </summary>
    private void SubmitNameForm()
    {
        var name = _app.SubjectField.Text ?? "";
        var layout = _nameLayout;
        if (layout is null)
            return;

        var rename = _nameRename;
        var old = _nameOld;
        var tip = _nameTip;
        if (rename && name == old)
        {
            CloseNameForm();
            const string same = "Same name: nothing changed";
            _app.LogMessage(same, Muted);
            ShowFlash(layout, new Caption(same, Muted));
            return;
        }

        var direction = NameDirection(rename, old, _app.InkOf(old), name, tip);
        StartWork();
        Task.Run(() =>
        {
            try
            {
                if (rename)
                {
                    var check = _app.RenameBranchOp.Check(new RenameBranchRequest(old, name));
                    _app.Ui(() => AfterNameCheck(check.Refusal, check.NameProblem, check.Commands, direction, () => StartRename(old, name)));
                }
                else
                {
                    var check = _app.CreateBranchOp.Check(new CreateBranchRequest(name));
                    _app.Ui(() => AfterNameCheck(check.Refusal, check.NameProblem, check.Commands, direction, () => StartCreate(name)));
                }
            }
            catch (Exception ex)
            {
                // The box stays open, so the name can be changed and tried again.
                _app.Ui(() =>
                {
                    _app.ShowGitError(ex);
                    EndWork();
                });
            }
        }, CancellationToken.None);
    }

    /// <summary>Runs on the UI thread after the name's check: a name problem keeps the box, any other refusal closes it, a pass runs the change.</summary>
    private void AfterNameCheck(string? refusal, bool nameProblem, IReadOnlyList<GitResult> commands,
        Func<int, IReadOnlyList<Seg>> direction, Action start)
    {
        EndWork();
        if (refusal is null)
        {
            CloseNameForm();
            start();
            return;
        }

        if (nameProblem)
        {
            _app.LogCommands(commands);
            _app.LogMessage("Refused: " + refusal, WarnB);
            _nameNote = new Caption(refusal, Warn);
            _app.RelayoutNameBox();
            _app.Art.SetNeedsDraw();
            return;
        }

        CloseNameForm();
        ShowBranchRefused(commands, refusal, _nameLayout, direction);
    }

    private void CancelNameForm()
    {
        var text = _nameRename ? "Rename cancelled: nothing changed" : "New branch cancelled: nothing changed";
        var layout = _nameLayout;
        CloseNameForm();
        _app.LogMessage(text, Muted);
        ShowFlash(layout, new Caption(text, Muted));
    }

    /// <summary>Hides the box and gives the art back to the browse keys. The caller decides what shows next.</summary>
    private void CloseNameForm()
    {
        Mode = BranchesMode.Browse;
        _nameNote = null;
        _app.FieldPlacement = AppController.FieldSpot.Areas;
        _app.Art.CaptionKeepsBelowRows = false;
        _app.Art.CanFocus = false;

        var subject = _app.SubjectField;
        subject.Visible = false;
        subject.CanFocus = false;
        _app.Keys.Mode = KeyBarMode.Branches;
        _app.Keys.MessageEnterLabel = " stash";
        _app.Keys.SetNeedsDraw();
    }

    /// <summary>
    /// Runs a layout load on a worker while the tab is busy, then opens the box on the UI thread. The layout is read on the worker,
    /// so <paramref name="load"/> must not touch the views or the cache.
    /// </summary>
    private void LoadForNameBox(Func<GraphLayout> load, Action<GraphLayout> open)
    {
        StartWork();
        Task.Run(() =>
        {
            try
            {
                var layout = load();
                _app.Ui(() =>
                {
                    EndWork();
                    open(layout);
                });
            }
            catch (Exception ex)
            {
                _app.Ui(() =>
                {
                    _app.ShowGitError(ex);
                    RestoreAfterError();
                });
            }
        }, CancellationToken.None);
    }

    /// <summary>The name box's place in the art pane, or null when no commit row is free for it (the window is too small).</summary>
    private (int X, int Y, int W, bool LabelRow)? NameBox() =>
        ArtPaneView.NameField(_app.Art.Viewport.Width, _app.Art.Viewport.Height, NameCaptionTwoLines(_app.Art.Viewport.Width, _app.Art.Viewport.Height), _app.Art.CompactArt);

    /// <summary>
    /// Whether the name box's caption (the refusal, or the hint) takes two lines in a pane of this size. The box moves down a row then.
    /// </summary>
    internal bool NameCaptionTwoLines(int paneW, int paneH) =>
        ArtPaneView.TwoLineCaption(_nameNote?.Text ?? (_nameRename ? RenameHint : NewHint), paneW, paneH, keepsBelowRows: true, compact: _app.Art.CompactArt);

    /// <summary>The direction line while the name is checked or refused: the typed name in the change's own line.</summary>
    private static Func<int, IReadOnlyList<Seg>> NameDirection(bool rename, string old, Ink oldInk, string name, string tip) =>
        rename
            ? cols => DirectionLine.Rename(old, oldInk, name, cols)
            : cols => DirectionLine.NewBranch(name, BranchGit.Short(tip), cols);
}
