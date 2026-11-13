using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Input;

namespace graftui.Ui.App;

/// <summary>
/// The yes/no questions on the Branches tab (delete: one question when merged, two on different keys when not). A question takes
/// every key: its yes key runs its action, n or Esc cancels, and any other key is ignored. It is drawn over the preview.
/// </summary>
internal sealed partial class BranchesTab
{
    /// <summary>
    /// A question: its title, its text (in the overlay), the key that answers yes, what a yes does, the direction line and the layout under
    /// the overlay. The rest is set with initialisers: the labels after the yes and n keys and the caption for a cancel (required), and
    /// optionally the same question in fewer words for an art too short for the full text (<c>ShortText</c>). <c>OnNo</c> replaces the flash
    /// of a cancel (the pause screen shows its own note); <c>Base</c> draws the scene under the overlay in place of the layout's preview.
    /// </summary>
    private sealed record BranchQuestion(string Title, string Text, char YesKey, Action OnYes, Func<int, IReadOnlyList<Seg>> Direction,
        GraphLayout? Layout)
    {
        public required string YesLabel { get; init; }

        public required string NoLabel { get; init; }

        public required string CancelText { get; init; }

        public string? ShortText { get; init; }

        public Action? OnNo { get; init; }

        public Func<ArtGeometry, ArtGrid>? Base { get; init; }
    }

    private BranchQuestion? _question;

    /// <summary>Shows a question over the preview. The art must fit, or nothing is asked.</summary>
    private void BeginQuestion(BranchQuestion q)
    {
        _shown = null;
        if (!_app.ArtFits())
        {
            const string tooSmall = "Refused: widen the window to answer this question";
            _app.LogMessage(tooSmall, WarnB);
            ShowTooSmall(q, new Caption(tooSmall, Warn));
            return;
        }

        _app.CloseLog();
        _question = q;
        Mode = BranchesMode.Ask;
        _app.Keys.Mode = KeyBarMode.Ask;
        _app.Keys.AskKey = q.YesKey.ToString();
        _app.Keys.AskYesLabel = q.YesLabel;
        _app.Keys.AskNoLabel = q.NoLabel;
        _app.Keys.SetNeedsDraw();

        var art = _app.Art;
        art.Title = q.Title;
        art.Direction = q.Direction;
        art.Below = null;
        art.Panel = Panel(q.Layout);
        art.FallbackCaption = null;
        art.Scene = geo =>
        {
            var grid = QuestionBase(q, geo);
            MergeScenes.MessageOverlay(grid, q.Title, q.Text, q.ShortText);
            return new SceneFrame(grid, new Caption($"{q.YesKey}{q.YesLabel} · n{q.NoLabel}", Warn));
        };
        art.SetNeedsDraw();
    }

    /// <summary>
    /// A question the art cannot hold: its caption goes over the picture the question would have drawn. A question with its own picture (the
    /// pause's art) keeps that picture, so the art is never blanked; the others show the preview of their layout.
    /// </summary>
    private void ShowTooSmall(BranchQuestion q, Caption caption)
    {
        _shown = null;
        if (q.Base is null)
        {
            ShowFlash(q.Layout, caption);
            return;
        }

        _app.Art.Scene = geo => new SceneFrame(q.Base(geo), caption);
        _app.Art.SetNeedsDraw();
    }

    /// <summary>
    /// Keys while a question is shown. Every key is taken. The yes key counts only with the art on screen; n, N or Esc cancels with
    /// the question's cancel text over the preview.
    /// </summary>
    internal void HandleAskKey(Key e)
    {
        e.Handled = true;
        var q = _question;
        if (q is null)
        {
            CloseQuestion();
            return;
        }

        var escape = e == Key.Esc;
        var key = escape ? '\0' : char.ToLowerInvariant(ChangesTab.CharOf(e));
        if (key == q.YesKey && _app.ArtFits())
        {
            CloseQuestion();
            q.OnYes();
            return;
        }
        if (key == 'n' || escape)
        {
            CloseQuestion();
            _app.LogMessage(q.CancelText, Muted);
            if (q.OnNo is not null)
                q.OnNo();
            else
                ShowFlash(q.Layout, new Caption(q.CancelText, Muted));
        }
    }

    /// <summary>The scene under a question: its own base when it gives one, else the layout's preview, else an empty art.</summary>
    private static ArtGrid QuestionBase(BranchQuestion q, ArtGeometry geo)
    {
        if (q.Base is not null)
            return q.Base(geo);
        return q.Layout is null ? new ArtGrid(geo.Width, geo.Height) : MergeScenes.Preview(geo, q.Layout).Grid;
    }

    /// <summary>Closes the question and gives the keys back to the Branches tab. The caller decides what shows next.</summary>
    private void CloseQuestion()
    {
        _question = null;
        Mode = BranchesMode.Browse;
        _app.Keys.Mode = KeyBarMode.Branches;
        _app.Keys.AskKey = "y";
        _app.Keys.AskYesLabel = " yes";
        _app.Keys.AskNoLabel = " no";
        _app.Keys.SetNeedsDraw();
    }

    /// <summary>The preview of a layout with one caption under it (a cancelled question's note). A null layout leaves an empty art.</summary>
    private void ShowFlash(GraphLayout? layout, Caption caption)
    {
        _shown = null;
        var art = _app.Art;
        art.Title = "Preview";
        art.Direction = cols => layout is null ? Array.Empty<Seg>() : DirectionLine.Browse(layout, cols);
        art.Below = null;
        art.Panel = Panel(layout);
        art.FallbackCaption = null;
        // A flash over a straight graph keeps the single-line art, like the preview it stands in for.
        art.ShowScene(geo => new SceneFrame(layout is null ? new ArtGrid(geo.Width, geo.Height) : MergeScenes.Preview(geo, layout).Grid, caption),
            layout != null && IsStraight(layout));
        art.SetNeedsDraw();
    }
}
