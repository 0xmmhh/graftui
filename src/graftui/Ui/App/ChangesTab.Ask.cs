using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Input;

namespace graftui.Ui.App;

/// <summary>
/// The yes/no questions on the Changes tab: discard, and unstaging hand-picked staged changes. The amend questions are separate
/// (<see cref="PushedAmendQuestions"/>). A question takes every key: y runs its action, n or Esc cancels, and anything else is ignored.
/// </summary>
internal sealed partial class ChangesTab
{
    /// <summary>
    /// A question on the art: its title, the question and its context in the overlay, the labels after the y and n keys, the
    /// caption for a cancel, the direction line, and what a yes does.
    /// </summary>
    private sealed record YesNo(string Title, string Question, string Context, string YesLabel, string NoLabel, string CancelText,
        Func<int, IReadOnlyList<Seg>> Direction, Action OnYes);

    private YesNo? _yesNo;

    /// <summary>Shows a question over the art. The art must fit, or the question is not drawn and nothing is asked.</summary>
    private void BeginYesNo(YesNo q)
    {
        if (!ArtFits())
        {
            _app.LogMessage("Refused: widen the window to answer this question", WarnB);
            return;
        }

        _yesNo = q;
        _result = null;
        _flash = null;
        Mode = ChangesMode.Ask;
        _app.Keys.Mode = KeyBarMode.Ask;
        _app.Keys.AskKey = "y";
        _app.Keys.AskYesLabel = q.YesLabel;
        _app.Keys.AskNoLabel = q.NoLabel;
        _app.Keys.SetNeedsDraw();

        var art = _app.Art;
        art.Title = q.Title;
        art.Direction = q.Direction;
        art.Below = Summary();
        art.Commits = [];
        art.FallbackCaption = null;
        art.Scene = geo =>
        {
            var grid = AreasScenes.Static(geo, Before());
            MergeScenes.MessageOverlay(grid, q.Question, q.Context);
            return new SceneFrame(grid, new Caption($"y{q.YesLabel} · n{q.NoLabel}", Warn));
        };
        art.SetNeedsDraw();
    }

    /// <summary>Keys while a question is shown. Cancel always works; y needs the art on screen, so an unseen answer does not count.</summary>
    private void HandleYesNoKey(Key e, YesNo q)
    {
        e.Handled = true;
        var escape = e == Key.Esc;
        var key = escape ? '\0' : char.ToLowerInvariant(CharOf(e));

        if (key == 'y' && ArtFits())
        {
            CloseYesNo();
            q.OnYes();
            return;
        }
        if (key == 'n' || escape)
        {
            CloseYesNo();
            _app.LogMessage(q.CancelText, Dim);
            _flash = new Caption(q.CancelText, Dim);
            ShowBrowse();
        }
    }

    /// <summary>Closes the question and gives the keys back to the browse screen. The caller decides what shows next.</summary>
    private void CloseYesNo()
    {
        _yesNo = null;
        Mode = ChangesMode.Browse;
        ResetAskKeys();
        _app.Keys.Mode = KeyBarMode.Changes;
        _app.Keys.SetNeedsDraw();
    }

    private void ResetAskKeys()
    {
        _app.Keys.AskKey = "y";
        _app.Keys.AskYesLabel = " yes";
        _app.Keys.AskNoLabel = " no";
    }

    /// <summary>
    /// Asks before unstaging paths whose staged version differs from the work tree too: the staged version then exists only in
    /// the index. <paramref name="onYes"/> runs the unstage with the confirmation set.
    /// </summary>
    private void AskHandPicked(IReadOnlyList<string> paths, Action onYes)
    {
        var what = paths.Count == 1 ? paths[0] : MergeScenes.Plural(paths.Count, "file");
        var question = paths.Count == 1
            ? $"{paths[0]} has hand-picked staged changes; unstage them all?"
            : $"{MergeScenes.Plural(paths.Count, "file")} have hand-picked staged changes; unstage them all?";
        BeginYesNo(new YesNo(
            Title: "Unstage",
            Question: question,
            Context: "Only the staging area has this version; u restores it exactly.",
            YesLabel: " unstage",
            NoLabel: " keep",
            CancelText: "Unstage cancelled: nothing changed",
            Direction: cols => ChangesText.OpDirection("unstage", what, [new Seg("working tree", Dim)], cols),
            OnYes: onYes));
    }
}
