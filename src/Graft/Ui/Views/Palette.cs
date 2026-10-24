using Graft.Ui.Text;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Graft.Ui.Views;

/// <summary>The truecolor palette from the design mock. Terminal.Gui falls back to 16 colours on terminals without truecolor.</summary>
public static class Palette
{
    public static readonly Color Term = new(0x12, 0x16, 0x1d);
    public static readonly Color Frame = new(0x2b, 0x34, 0x42);
    public static readonly Color Fg = new(0xd5, 0xdc, 0xe5);
    public static readonly Color Dim = new(0x6c, 0x7a, 0x8c);
    public static readonly Color Cur = new(0x4f, 0xd1, 0xc5);
    public static readonly Color SelRow = new(0x22, 0x31, 0x4a);
    public static readonly Color Inc = new(0xc3, 0x9b, 0xff);
    public static readonly Color Mrg = new(0xf5, 0xc4, 0x51);
    public static readonly Color Ok = new(0x7e, 0xe7, 0x87);
    public static readonly Color Bad = new(0xff, 0x6b, 0x6b);
    public static readonly Color Warn = new(0xf0, 0xa3, 0x5e);
    public static readonly Color Hash = new(0xb3, 0x9b, 0x62);

    /// <summary>Attribute for a semantic style. Background is the selection colour for selected rows, otherwise the terminal background.</summary>
    public static Attribute Get(Style s, bool selectedRow = false) =>
        new(ForeColor(s.Ink), selectedRow ? SelRow : Term, s.Bold ? TextStyle.Bold : TextStyle.None);

    /// <summary>A key label in the key bar: dark text on a dim chip.</summary>
    public static Attribute KeyChip => new(Term, Dim);

    public static Scheme RootScheme => new(new Attribute(Fg, Term));

    private static Color ForeColor(Ink ink) => ink switch
    {
        Ink.Fg => Fg,
        Ink.Dim => Dim,
        Ink.Cur => Cur,
        Ink.Inc => Inc,
        Ink.Mrg => Mrg,
        Ink.Ok => Ok,
        Ink.Bad => Bad,
        Ink.Warn => Warn,
        Ink.Hash => Hash,
        Ink.Frame => Frame,
        _ => Fg,
    };
}
