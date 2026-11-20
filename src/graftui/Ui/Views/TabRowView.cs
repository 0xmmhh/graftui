using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;

namespace graftui.Ui.Views;

/// <summary>
/// The new look's tab row (row 1): "Branches │ Changes", the active tab bright and bold, the other dim, the bar in the frame colour.
/// Tab switches the tabs (the app decides); this row only shows which one is on. It takes no focus and no keys.
/// </summary>
public sealed class TabRowView : View
{
    private static readonly Style Active = new(Ink.Fg, true);
    private static readonly Style Inactive = new(Ink.Dim);
    private static readonly Style Bar = new(Ink.Frame);
    private static readonly Style Plain = new(Ink.Fg);

    public TabRowView()
    {
        CanFocus = false;
    }

    /// <summary>True when the Changes tab is the active one. Branches is the start tab.</summary>
    public bool ChangesActive { get; set; }

    /// <summary>The row's segments from x 2: "Branches", " ", "│", " ", "Changes". The active tab is bright and bold, the other dim.</summary>
    internal static IReadOnlyList<Seg> Segs(bool changesActive)
    {
        var branches = changesActive ? Inactive : Active;
        var changes = changesActive ? Active : Inactive;
        return [new Seg("Branches", branches), new Seg(" ", Plain), new Seg("│", Bar), new Seg(" ", Plain), new Seg("Changes", changes)];
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        Paint.ClearAll(this);
        var w = Viewport.Width;
        if (w > 3)
            Paint.Segs(this, 2, 0, Segs(ChangesActive), w - 3);
        return true;
    }
}
