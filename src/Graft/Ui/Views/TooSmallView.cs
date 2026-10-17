using Graft.Ui;
using Terminal.Gui.ViewBase;

namespace Graft.Ui.Views;

/// <summary>Shown instead of the panes when the terminal is below the minimum size.</summary>
public sealed class TooSmallView : View
{
    private static readonly Style Warn = new(Ink.Warn, true), Dim = new(Ink.Dim);

    public int Cols { get; set; }

    public int Rows { get; set; }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        Paint.ClearAll(this);
        var w = Viewport.Width;
        var h = Viewport.Height;
        if (w <= 0 || h <= 0)
            return true;

        var first = $"graft needs a terminal of at least {ScreenLayout.MinCols}×{ScreenLayout.MinRows}";
        var second = $"now {Cols}×{Rows}";
        var y = Math.Max(0, h / 2 - 1);
        Centred(first, y, Warn, w);
        Centred(second, y + 1, Dim, w);
        return true;
    }

    private void Centred(string text, int y, Style st, int w)
    {
        var fitted = TextFit.Fit(text, w);
        var cols = TextFit.Columns(fitted);
        Paint.Text(this, Math.Max(0, (w - cols) / 2), y, fitted, w, st);
    }
}
