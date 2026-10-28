using graftui.Ui.Text;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace graftui.Ui.Views;

/// <summary>The truecolor palette from the design mock. Terminal.Gui falls back to 16 colours on terminals without truecolor.</summary>
public static class Palette
{
    public static Color Term { get; private set; } = new(0x12, 0x16, 0x1d);
    public static Color Frame { get; private set; } = new(0x2b, 0x34, 0x42);
    public static Color Fg { get; private set; } = new(0xd5, 0xdc, 0xe5);
    public static Color Dim { get; private set; } = new(0x6c, 0x7a, 0x8c);
    public static Color Cur { get; private set; } = new(0x4f, 0xd1, 0xc5);
    public static Color SelRow { get; private set; } = new(0x22, 0x31, 0x4a);
    public static Color Inc { get; private set; } = new(0xc3, 0x9b, 0xff);
    public static Color Mrg { get; private set; } = new(0xf5, 0xc4, 0x51);
    public static Color Ok { get; private set; } = new(0x7e, 0xe7, 0x87);
    public static Color Bad { get; private set; } = new(0xff, 0x6b, 0x6b);
    public static Color Warn { get; private set; } = new(0xf0, 0xa3, 0x5e);
    public static Color Hash { get; private set; } = new(0xb3, 0x9b, 0x62);

    /// <summary>
    /// Makes <paramref name="t"/> the active colours. Call once at startup, before <c>Application.Create()</c>;
    /// views read these values when they draw. Tests must not call it (it is global state).
    /// </summary>
    public static void Apply(Theme t)
    {
        Term = t.Term;
        Frame = t.Frame;
        Fg = t.Fg;
        Dim = t.Dim;
        Chip = t.Chip;
        SelRow = t.SelRow;
        Cur = t.Cur;
        Inc = t.Inc;
        Mrg = t.Mrg;
        Ok = t.Ok;
        Bad = t.Bad;
        Warn = t.Warn;
        Hash = t.Hash;
    }

    /// <summary>Attribute for a semantic style. Background is the selection colour for selected rows, otherwise the terminal background.</summary>
    public static Attribute Get(Style s, bool selectedRow = false) =>
        new(ForeColor(s.Ink), selectedRow ? SelRow : Term, s.Bold ? TextStyle.Bold : TextStyle.None);

    /// <summary>A key label in the key bar: dark text on a dim chip.</summary>
    public static Attribute KeyChip => new(Term, Dim);

    public static Scheme RootScheme => new(new Attribute(Fg, Term));

    /// <summary>Background of the message box's fields: a shade darker than the terminal, so the typed text stands out.</summary>
    public static Color Chip { get; private set; } = new(0x1a, 0x20, 0x29);

    /// <summary>
    /// One text field's colours: light text on the field's shade, or on the selection colour while the field has focus.
    /// <paramref name="bold"/> is for the subject line.
    /// </summary>
    public static Scheme FieldScheme(bool focused, bool bold) =>
        new(new Attribute(Fg, focused ? SelRow : Chip, bold ? TextStyle.Bold : TextStyle.None));

    /// <summary>Colours of the message box's subject and body: light text on <see cref="Chip"/>.</summary>
    public static Scheme InputScheme => new(new Attribute(Fg, Chip));

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
