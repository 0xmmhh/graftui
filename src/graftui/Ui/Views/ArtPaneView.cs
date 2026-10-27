using graftui.Ui.Scenes;
using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;

namespace graftui.Ui.Views;

/// <summary>
/// The preview and animation pane: a direction line, the art, its caption and the commit list.
/// Layout (pane rows): 1 direction, 2 rule, 3.. art (AH rows), caption (one or two lines), rule, then commit rows.
/// </summary>
public sealed class ArtPaneView : PaneView
{
    private const int MaxArtWidth = 70;

    private static readonly Style Dim = new(Ink.Dim), Fg = new(Ink.Fg);
    private static readonly Style FrameStyle = new(Ink.Frame);

    public Func<ArtGeometry, SceneFrame>? Scene { get; set; }

    /// <summary>Direction line for a given width. Built on demand so names can be cut to the pane.</summary>
    public Func<int, IReadOnlyList<Seg>> Direction { get; set; } = _ => [];

    public IReadOnlyList<CommitRow> Commits { get; set; } = [];

    /// <summary>Lines drawn under the caption in place of <see cref="Commits"/> (the Changes tab's summary).</summary>
    public IReadOnlyList<IReadOnlyList<Seg>>? Below { get; set; }

    public Caption? FallbackCaption { get; set; }

    /// <summary>
    /// Where the art's grid is drawn in a pane of the given size (viewport coordinates), or null when no art fits.
    /// Used by the views and by the message form, which is laid over the art.
    /// </summary>
    public static (int X, int Y, int W, int H)? ArtArea(int paneW, int paneH)
    {
        var cw = paneW - 4;
        var ih = paneH - 2;
        var ah = ih >= 17 ? 11 : 8;
        var gw = Math.Min(cw, MaxArtWidth);
        if (cw <= 0 || ih <= 0 || ArtGeometry.For(gw, ah) is null)
            return null;
        return (2 + (cw - gw) / 2, 3, gw, ah);
    }

    /// <summary>
    /// Rows of the caption and the rows under it, for a pane of height <paramref name="paneH"/>. A two-line caption
    /// takes one commit row, and only when a commit row is free; otherwise it falls back to one line.
    /// </summary>
    public static (int CaptionRows, int RuleRow, int BelowTop, int BelowRows) CaptionLayout(int paneH, bool twoLines)
    {
        var ih = paneH - 2;
        var ah = ih >= 17 ? 11 : 8;
        var commitRows = Math.Clamp(ih - 4 - ah, 0, 4);
        return twoLines && commitRows > 0
            ? (2, 5 + ah, 6 + ah, commitRows - 1)
            : (1, 4 + ah, 5 + ah, commitRows);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        ClearAll();
        DrawFrame();

        var pw = Viewport.Width;
        var ph = Viewport.Height;
        var cw = pw - 4;
        var ih = ph - 2;
        if (cw <= 0 || ih <= 0)
            return true;

        var ah = ih >= 17 ? 11 : 8;

        DrawSegs(2, 1, Direction(cw), cw);
        DrawRule(2, 2, cw);

        var area = ArtArea(pw, ph);
        SceneFrame? frame = null;
        if (area is { } scene && Scene is not null)
            frame = Scene(ArtGeometry.For(scene.W, scene.H)!);

        if (area is { } art && frame is not null)
        {
            var grid = frame.Grid;
            for (var y = 0; y < art.H; y++)
                for (var x = 0; x < art.W; x++)
                {
                    var cell = grid[x, y];
                    Paint.Put(this, art.X + x, art.Y + y, cell.Ch, Palette.Get(cell.Style));
                }
        }

        var caption = area is null
            ? new Caption("art needs a wider pane", Dim)
            : frame?.Caption ?? FallbackCaption;
        var (first, second) = caption is null ? ("", null) : TextFit.Wrap2(caption.Text, cw);
        var layout = CaptionLayout(ph, second is not null);
        if (caption is not null)
        {
            if (layout.CaptionRows == 2)
            {
                DrawCentred(first, 3 + ah, cw, caption.Style);
                DrawCentred(second!, 4 + ah, cw, caption.Style);
            }
            else
                DrawCentred(TextFit.Fit(caption.Text, cw), 3 + ah, cw, caption.Style);
        }

        DrawRule(2, layout.RuleRow, cw);

        if (Below is { } below)
        {
            for (var k = 0; k < layout.BelowRows && k < below.Count; k++)
                DrawSegs(4, layout.BelowTop + k, below[k], cw - 2);
            return true;
        }

        for (var k = 0; k < layout.BelowRows && k < Commits.Count; k++)
        {
            var row = Commits[k];
            DrawSegs(4, layout.BelowTop + k,
            [
                new Seg(row.Dot.ToString(), row.DotStyle),
                new Seg(" ", Fg),
                new Seg(row.Hash.PadRight(7), row.HashStyle),
                new Seg(" ", Fg),
                new Seg(row.Subject, row.SubjectStyle),
            ], cw - 2);
        }
        return true;
    }

    private void DrawCentred(string text, int y, int cw, Style style)
    {
        var cols = TextFit.Columns(text);
        DrawText(2 + (cw - cols) / 2, y, text, cw, style);
    }

    private void DrawRule(int x, int y, int cols)
    {
        var attr = Palette.Get(FrameStyle);
        for (var i = 0; i < cols; i++)
            Paint.Put(this, x + i, y, '╌', attr);
    }
}
