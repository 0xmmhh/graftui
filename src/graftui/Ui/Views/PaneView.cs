using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;

namespace graftui.Ui.Views;

/// <summary>
/// A bordered pane with a rounded frame. The title is the inherited <see cref="View.Title"/>, drawn in the frame's top edge.
/// Panes are never focusable: key handling is done at application level.
/// </summary>
public abstract class PaneView : View
{
    protected PaneView()
    {
        CanFocus = false;
    }

    protected void ClearAll() => Paint.ClearAll(this);

    protected void DrawFrame()
    {
        var w = Viewport.Width;
        var h = Viewport.Height;
        if (w < 2 || h < 2)
            return;

        var frame = Palette.Get(new Style(Ink.Frame));
        Paint.Put(this, 0, 0, '╭', frame);
        Paint.Put(this, w - 1, 0, '╮', frame);
        Paint.Put(this, 0, h - 1, '╰', frame);
        Paint.Put(this, w - 1, h - 1, '╯', frame);
        for (var x = 1; x < w - 1; x++)
        {
            Paint.Put(this, x, 0, '─', frame);
            Paint.Put(this, x, h - 1, '─', frame);
        }
        for (var y = 1; y < h - 1; y++)
        {
            Paint.Put(this, 0, y, '│', frame);
            Paint.Put(this, w - 1, y, '│', frame);
        }

        if (!string.IsNullOrEmpty(Title))
            Paint.Text(this, 2, 0, $" {Title} ", w - 4, new Style(Ink.Fg));
    }

    protected int DrawText(int x, int y, string s, int maxCols, Style st, bool sel = false) =>
        Paint.Text(this, x, y, s, maxCols, st, sel);

    protected int DrawSegs(int x, int y, IReadOnlyList<Seg> segs, int maxCols) =>
        Paint.Segs(this, x, y, segs, maxCols);
}
