using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;

namespace graftui.Ui.Views;

/// <summary>
/// A bordered pane with a rounded or a square frame (<see cref="SquareCorners"/>). The title is the inherited <see cref="View.Title"/>,
/// drawn in the frame's top edge, or <see cref="TitleSegs"/> when those are set (a title with more than one colour, such as
/// "Branches │ Changes"). Panes are never focusable: key handling is done at application level.
/// </summary>
public abstract class PaneView : View
{
    protected PaneView()
    {
        CanFocus = false;
    }

    /// <summary>Title in several styles. When set, it replaces <see cref="View.Title"/> in the frame.</summary>
    public IReadOnlyList<Seg>? TitleSegs { get; set; }

    /// <summary>The new look: square corners ┌┐└┘ and the title one column in, as the mock. False: rounded ╭╮╰╯ and the title at x 2.</summary>
    public bool SquareCorners { get; set; }

    /// <summary>A hint drawn right-aligned in the bottom border (for example "v all commands"). Left out when it does not fit beside the frame.</summary>
    public IReadOnlyList<Seg>? FooterSegs { get; set; }

    protected void ClearAll() => Paint.ClearAll(this);

    /// <summary>The four corners of a frame: square ┌┐└┘ for the new look, rounded ╭╮╰╯ for the old one.</summary>
    internal static (char TopLeft, char TopRight, char BottomLeft, char BottomRight) Corners(bool square)
    {
        if (square)
            return ('┌', '┐', '└', '┘');
        return ('╭', '╮', '╰', '╯');
    }

    protected void DrawFrame()
    {
        var w = Viewport.Width;
        var h = Viewport.Height;
        if (w < 2 || h < 2)
            return;

        var frame = Palette.Get(new Style(Ink.Frame));
        var corners = Corners(SquareCorners);
        Paint.Put(this, 0, 0, corners.TopLeft, frame);
        Paint.Put(this, w - 1, 0, corners.TopRight, frame);
        Paint.Put(this, 0, h - 1, corners.BottomLeft, frame);
        Paint.Put(this, w - 1, h - 1, corners.BottomRight, frame);
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

        // The title starts one column in with square corners, two in with rounded ones (its own space comes first).
        var titleX = SquareCorners ? 1 : 2;
        var titleCols = SquareCorners ? w - 3 : w - 4;
        if (TitleSegs is { } segs)
        {
            List<Seg> padded = [new Seg(" ", new Style(Ink.Fg)), .. segs, new Seg(" ", new Style(Ink.Fg))];
            Paint.Segs(this, titleX, 0, padded, titleCols);
        }
        else if (!string.IsNullOrEmpty(Title))
            Paint.Text(this, titleX, 0, $" {Title} ", titleCols, new Style(Ink.Fg));

        if (FooterSegs is { } footer)
        {
            var cols = footer.Sum(s => TextFit.Columns(s.Text));
            if (cols > 0 && cols <= w - 4)
                Paint.Segs(this, w - 2 - cols, h - 1, footer, cols);
        }
    }

    protected int DrawText(int x, int y, string s, int maxCols, Style st, bool sel = false) =>
        Paint.Text(this, x, y, s, maxCols, st, sel);

    protected int DrawSegs(int x, int y, IReadOnlyList<Seg> segs, int maxCols) =>
        Paint.Segs(this, x, y, segs, maxCols);
}
