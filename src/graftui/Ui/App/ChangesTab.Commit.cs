using System.Diagnostics;
using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace graftui.Ui.App;

/// <summary>
/// Commit and amend: the message box, the three questions before amending a pushed commit, and the commit's run and result.
/// </summary>
internal sealed partial class ChangesTab
{
    // ---------- commit and amend: the message box ----------

    /// <summary>
    /// Checks the repo for a commit or amend on a worker (Busy meanwhile). A refusal is shown; an amend of a pushed commit asks
    /// three questions first; otherwise the message box opens.
    /// </summary>
    private void BeginCommitInput(bool amend)
    {
        if (_app.Busy)
            return;
        if (!ArtFits())
        {
            // The message box is laid over the art, so there is nowhere to type when no art fits.
            _app.LogMessage("Refused: widen the window to write a commit message", WarnB);
            return;
        }

        BeginBusy();
        var op = _app.CommitOp;
        Task.Run(() =>
        {
            try
            {
                var check = op.CheckRepo(amend);
                _app.Ui(() =>
                {
                    if (!check.CanRun)
                    {
                        Refuse(check.Commands, check.Refusal!);
                        return;
                    }
                    _app.LogCommands(check.Commands);
                    EndBusy();
                    if (amend && check.PushedTo.Count > 0)
                    {
                        BeginAsk(check);
                        return;
                    }
                    var (subject, body, blank) = FormText(check, amend);
                    OpenForm(amend, check, subject, body, pushedConfirmed: false, blankAfterSubject: blank);
                });
            }
            catch (Exception ex)
            {
                _app.Ui(() =>
                {
                    _app.ShowGitError(ex);
                    EndBusy();
                });
            }
        }, CancellationToken.None);
    }

    /// <summary>Keys while the message box is open: Esc cancels, Tab moves between the fields, Enter or Ctrl+Enter saves.</summary>
    internal void HandleInputKey(Key e)
    {
        var subject = _app.SubjectField;
        // The window shrank under the open box: its fields are 0 wide and out of sight, so keys are taken but go nowhere.
        if (!ArtFits() && e != Key.Esc)
        {
            e.Handled = true;
            return;
        }
        if (_stashForm)
        {
            HandleStashKey(e);
            return;
        }

        if (e == Key.Esc)
        {
            e.Handled = true;
            CloseForm();
            _flash = new Caption((_formAmend ? "Amend" : "Commit") + " cancelled: nothing changed", Dim);
            ShowBrowse();
            return;
        }

        if (e == Key.Tab || e == Key.Tab.WithShift)
        {
            e.Handled = true;
            if (subject.HasFocus)
                _app.BodyField.SetFocus();
            else
                subject.SetFocus();
            return;
        }

        // Enter saves from the subject. Ctrl+Enter saves from either field; a terminal sends Ctrl+J as Key.J.WithCtrl or as LF,
        // and LF arrives as Ctrl+Enter. In the body, a plain Enter adds a line.
        if (e == Key.Enter.WithCtrl || e == Key.J.WithCtrl || (e == Key.Enter && subject.HasFocus))
        {
            e.Handled = true;
            SubmitForm();
        }
        // Anything else is left unhandled, so the focused field edits.
    }

    /// <summary>
    /// Keys while a question is shown. Every key is taken. A yes/no question (discard, hand-picked unstage) is answered first.
    /// Otherwise the pushed-amend questions run: their state machine decides what a key does, the yes key moves on, n, N or Esc
    /// cancels, anything else is ignored, and the third yes opens the box.
    /// </summary>
    internal void HandleAskKey(Key e)
    {
        if (_yesNo is { } question)
        {
            HandleYesNoKey(e, question);
            return;
        }

        e.Handled = true;
        var key = e == Key.Esc ? PushedAmendQuestions.EscapeKey : CharOf(e);
        // The window shrank under the question: it is not drawn, so an answer must not count. Cancel still works.
        if (!PushedAmendQuestions.IsCancel(key) && !ArtFits())
            return;
        switch (_questions.Press(key))
        {
            case QuestionResult.Advanced:
                ShowAsk();
                break;
            case QuestionResult.Confirmed:
                var check = _askCheck!;
                _askCheck = null;
                var (subject, body, blank) = FormText(check, amend: true);
                OpenForm(amend: true, check, subject, body, pushedConfirmed: true, blankAfterSubject: blank);
                break;
            case QuestionResult.Cancelled:
                CancelAsk();
                break;
        }
    }

    private void CancelAsk()
    {
        _askCheck = null;
        Mode = ChangesMode.Browse;
        _app.Keys.Mode = KeyBarMode.Changes;
        _app.Keys.SetNeedsDraw();
        _app.LogMessage("Amend cancelled", Dim);
        _flash = new Caption("Amend cancelled: nothing changed", Dim);
        ShowBrowse();
    }

    /// <summary>The typed character of a key ('\0' when it has none). Shared with the Branches tab's questions.</summary>
    internal static char CharOf(Key e)
    {
        var rune = e.AsRune;
        return rune.Value is > 0 and < 0x10000 ? (char)rune.Value : '\0';
    }

    /// <summary>Starts the three questions for amending a commit that a remote branch already has.</summary>
    private void BeginAsk(CommitCheck check)
    {
        Mode = ChangesMode.Ask;
        _app.Keys.Mode = KeyBarMode.Ask;
        _askCheck = check;
        _questions.Start();
        var remote = AskRemote(check);
        _app.LogMessage($"Warning: {Short(check.OldHead)} is already on {remote}", WarnB);
        ShowAsk();
    }

    private void ShowAsk()
    {
        var check = _askCheck!;
        var art = _app.Art;
        var shortHash = Short(check.OldHead);
        var remote = AskRemote(check);
        var subject = check.OldCommit?.Subject ?? "";

        var step = _questions.Step;
        var question = _questions.Question;
        var context = step switch
        {
            1 => $"This commit is already on {remote}; amending it means you'd have to force-push.",
            2 => $"Anyone who already has {remote} keeps the old commit {shortHash}. After a force-push, their history no longer matches yours.",
            _ => $"graftui never force-pushes. You will have to run git push --force-with-lease yourself. Amend {shortHash} anyway?",
        };
        var yes = _questions.YesKey;
        var caption = $"{yes} yes · n no";
        _app.Keys.AskKey = yes.ToString();
        _app.Keys.SetNeedsDraw();

        art.Title = "Amend pushed commit?";
        art.Direction = cols => [new Seg("amend        ", Dim), new Seg(TextFit.Fit($"{shortHash} {subject}", Math.Max(8, cols - 13)), FgB)];
        art.Below = HeadBelow();
        art.Panel = null;
        art.FallbackCaption = null;
        art.Scene = geo =>
        {
            var grid = AreasScenes.Static(geo, Before());
            MergeScenes.MessageOverlay(grid, question, context);
            return new SceneFrame(grid, new Caption(caption, Warn));
        };
        art.SetNeedsDraw();
    }

    /// <summary>
    /// The message box's starting text: an amend starts from the old message; a commit starts empty. BlankAfterSubject
    /// tells whether the old message had a blank line after its subject, so saving keeps the message's layout.
    /// </summary>
    private static (string Subject, string Body, bool BlankAfterSubject) FormText(CommitCheck check, bool amend)
    {
        if (!amend)
            return ("", "", true);
        var parts = CommitMessage.Split(check.OldMessage);
        return (parts.Subject, parts.Body, parts.BlankAfterSubject);
    }

    private static string AskRemote(CommitCheck check) =>
        check.PushedTo.Count > 1 ? $"{check.PushedTo[0]} and {check.PushedTo.Count - 1} more" : check.PushedTo[0];

    /// <summary>
    /// Shows the message box: the subject and body fields over the art, with the counters and the hint. The fields take
    /// the focus; the art pane becomes focusable while the box is open.
    /// </summary>
    private void OpenForm(bool amend, CommitCheck check, string subject, string body, bool pushedConfirmed, bool blankAfterSubject)
    {
        _formCheck = check;
        _formAmend = amend;
        _formPushedConfirmed = pushedConfirmed;
        _formBlankAfterSubject = blankAfterSubject;
        _formError = null;
        Mode = ChangesMode.Input;
        _app.Keys.Mode = KeyBarMode.Input;
        _app.Keys.SetNeedsDraw();

        var art = _app.Art;
        art.CanFocus = true;
        foreach (var field in new View[] { _app.SubjectField, _app.BodyField })
        {
            field.CanFocus = true;
            field.Visible = true;
        }
        _app.SubjectField.Text = subject;
        _app.SubjectField.InsertionPoint = subject.Length;
        _app.BodyField.Text = body;

        art.Title = amend ? "Amend" : "Commit";
        var staged = Snapshot?.Status.Staged.Count ?? check.Staged.Count;
        var branch = check.Branch.Length == 0 ? "HEAD" : check.Branch;
        art.Direction = cols => ChangesText.OpDirection(amend ? "amend" : "commit", MergeScenes.Plural(staged, "staged file"),
            [new Seg(branch, CurB), new Seg(" (current)", Dim)], cols);
        art.Below = HeadBelow();
        art.Panel = null;
        art.FallbackCaption = null;
        art.Scene = geo => FormFrame(geo);
        art.SetNeedsDraw();
        _app.SubjectField.SetFocus();
    }

    private SceneFrame FormFrame(ArtGeometry geo)
    {
        var subject = _app.SubjectField.Text ?? "";
        var body = _app.BodyField.Text ?? "";
        var longest = body.Split('\n').Select(TextFit.Columns).DefaultIfEmpty(0).Max();
        var frame = AreasScenes.Form(geo, Before(), _formAmend, TextFit.Columns(subject), longest, _app.BodyField.HasFocus);

        var caption = _formError is not null
            ? new Caption(_formError, Warn)
            : new Caption(CommitFormHint.For(_formAmend, _app.BodyField.HasFocus), Dim);
        return new SceneFrame(frame.Grid, caption);
    }

    /// <summary>A blank subject keeps the box open with a message. Otherwise the box closes and the commit starts.</summary>
    private void SubmitForm()
    {
        var subject = _app.SubjectField.Text ?? "";
        if (subject.Trim().Length == 0)
        {
            _formError = ChangeRefusals.EmptySubject;
            _app.Art.SetNeedsDraw();
            return;
        }

        var check = _formCheck!;
        var request = new CommitRequest(_formAmend, subject, _app.BodyField.Text ?? "", check.OldHead, _formPushedConfirmed,
            _formBlankAfterSubject);
        CloseForm();
        StartCommit(request);
    }

    /// <summary>Hides the message box and gives the art back to the browse screen. The caller decides what shows next.</summary>
    private void CloseForm()
    {
        Mode = ChangesMode.Browse;
        _formError = null;
        _stashForm = false;
        foreach (var field in new View[] { _app.SubjectField, _app.BodyField })
        {
            field.Visible = false;
            field.CanFocus = false;
        }
        _app.Art.CanFocus = false;
        _app.Keys.Mode = KeyBarMode.Changes;
        _app.Keys.SetNeedsDraw();
    }

    /// <summary>
    /// One commit or amend on a worker: Check, the quit gate, the animation, Run, then the result. The commit animation's final
    /// frame stays on the art, and the status and branch snapshot are reloaded afterwards.
    /// </summary>
    private void StartCommit(CommitRequest request)
    {
        var before = Before();
        IReadOnlyList<IReadOnlyList<Seg>> below = HeadBelow();
        var commitOp = _app.CommitOp;
        var animator = _app.ChangesAnimator;
        var areaOp = request.Amend ? AreasOp.Amend : AreasOp.Commit;
        BeginBusy();

        IAreasAnimation? anim = null;
        Func<int, IReadOnlyList<Seg>>? direction = null;
        _app.BeginChangeOutcome();
        _app.ChangesTask = Task.Run<string?>(() =>
        {
            try
            {
                var o = RunGated(commitOp, request, c => c.Refusal, () => _app.Gate.IsQuitting,
                    check => _app.Ui(() => Refuse(check.Commands, check.Refusal ?? "")),
                    check => _app.Ui(() =>
                    {
                        _app.LogCommands(check.Commands);
                        var branch = check.Branch;
                        var count = check.Staged.Count;
                        anim = new ChangesAnimation(areaOp, before, check.Staged, request.Amend ? "Amending…" : "Committing…");
                        direction = cols => ChangesText.OpDirection(request.Amend ? "amend" : "commit", MergeScenes.Plural(count, "staged file"),
                            [new Seg(branch, CurB), new Seg(" (current)", Dim)], cols);
                        animator.Begin(anim, direction, below);
                    }));
                if (o is null)
                    return null;

                var result = CommitResult(o, before, request);
                var failureLog = o.Kind == CommitKind.Failed ? $"{(request.Amend ? "Amend" : "Commit")} failed: {o.Message}" : null;
                _app.Ui(() => animator.SetPending(result, o.Commands, failureLog,
                    () => AfterChange(o.Kind != CommitKind.Failed, _ => 0, anim, direction),
                    new CommandHints(request.Subject, request.Body)));
                return CommitSummary(o, request);
            }
            catch (Exception ex)
            {
                _app.Ui(() => FailUnexpectedly(ex, request.Amend ? "Amend" : "Commit"));
                return $"graftui: {(request.Amend ? "amend" : "commit")} failed: {ex.Message}";
            }
        }, CancellationToken.None);
    }

    /// <summary>The line printed after exit for a commit: what happened, and where HEAD is now.</summary>
    private static string CommitSummary(CommitOutcome o, CommitRequest request)
    {
        var verb = request.Amend ? "amend" : "commit";
        if (o.Kind == CommitKind.Failed)
            return $"graftui: {verb} failed: {o.Message}; HEAD unchanged at {HeadText(o.OldHead)}";
        if (request.Amend)
            return $"graftui: amended {Short(o.NewHead)} on {o.Branch} (replaces {Short(o.OldHead)}); HEAD is now {Short(o.NewHead)}";
        return $"graftui: committed {Short(o.NewHead)} on {o.Branch}: {request.Subject.Trim()}; HEAD is now {Short(o.NewHead)}";
    }

    /// <summary>The animation's result for a commit outcome: the new areas, and the caption (success or failure).</summary>
    private AreasResult CommitResult(CommitOutcome o, AreasModel before, CommitRequest request)
    {
        if (o.Kind == CommitKind.Failed)
        {
            var verb = request.Amend ? "Amend" : "Commit";
            return new AreasResult(false, before, new Caption($"✕ {verb} failed: {o.Message}", BadB), "Failed", o.Message);
        }

        // The new areas come from a fresh read; if that fails, the staged rows are shown as gone, which is what a commit does.
        AreasModel after;
        try
        {
            var snap = _app.RootReader.LoadChanges(CancellationToken.None);
            after = new AreasModel(snap.Status.Unstaged, snap.Status.Staged, snap.Head, snap.Stash);
        }
        catch
        {
            after = new AreasModel(before.Unstaged, [], before.Head, before.Stash);
        }

        if (request.Amend)
            return new AreasResult(true, after, new Caption($"✔ Amended: {Short(o.NewHead)} replaces {Short(o.OldHead)}", OkB), "Amended");

        var subject = TextFit.Fit(CommitMessage.Split(request.Subject).Subject, 40);
        return new AreasResult(true, after, new Caption($"✔ Committed {Short(o.NewHead)} on {o.Branch}: {subject}", OkB), "Committed");
    }
}
