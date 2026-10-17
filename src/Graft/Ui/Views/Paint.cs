using System.Text;
using Graft.Ui;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Graft.Ui.Views;

/// <summary>
/// Drawing helpers shared by the views. Coordinates are viewport-relative; Terminal.Gui clips anything outside it.
/// Every text helper truncates with "…" so nothing is written past its maximum width.
/// </summary>
internal static class Paint
{
    public static void Put(View v, int x, int y, char ch, Attribute attr)
    {
        v.SetAttribute(attr);
        v.AddRune(x, y, new Rune(ch));
    }

    public static void Fill(View v, int x, int y, int cols, Attribute attr)
    {
        if (cols <= 0)
            return;
        v.SetAttribute(attr);
        v.AddStr(x, y, new string(' ', cols));
    }

    /// <summary>Blanks the whole viewport with the default foreground on the terminal background.</summary>
    public static void ClearAll(View v)
    {
        var attr = Palette.Get(new Style(Ink.Fg));
        for (var y = 0; y < v.Viewport.Height; y++)
            Fill(v, 0, y, v.Viewport.Width, attr);
    }

    /// <summary>Draws text cut to <paramref name="maxCols"/>. Returns the columns used.</summary>
    public static int Text(View v, int x, int y, string s, int maxCols, Attribute attr)
    {
        var fitted = TextFit.Fit(s, maxCols);
        v.SetAttribute(attr);
        var used = 0;
        foreach (var rune in fitted.EnumerateRunes())
        {
            var cols = rune.GetColumns();
            if (cols <= 0)
                continue; // zero-width runes would otherwise land on the previous cell
            v.AddRune(x + used, y, rune);
            used += cols;
        }
        return used;
    }

    public static int Text(View v, int x, int y, string s, int maxCols, Style st, bool selectedRow = false) =>
        Text(v, x, y, s, maxCols, Palette.Get(st, selectedRow));

    /// <summary>Draws segments in order. The first one that does not fit is cut and drawing stops there.</summary>
    public static int Segs(View v, int x, int y, IReadOnlyList<Seg> segs, int maxCols, bool selectedRow = false)
    {
        var used = 0;
        foreach (var seg in segs)
        {
            var remaining = maxCols - used;
            if (remaining <= 0)
                break;
            var fits = TextFit.Columns(TextFit.Sanitize(seg.Text)) <= remaining;
            used += Text(v, x + used, y, seg.Text, remaining, seg.Style, selectedRow);
            if (!fits)
                break;
        }
        return used;
    }
}
