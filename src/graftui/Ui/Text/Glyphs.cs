using System.Text;

namespace graftui.Ui.Text;

/// <summary>
/// The characters the new Branches screen draws with. Unicode is the default. Basic is the plain-ASCII set, picked by
/// <c>--glyphs basic</c> or <c>GRAFTUI_GLYPHS=basic</c>: every lane glyph is ASCII, and the text marks elsewhere on screen
/// (✔ ✕ → ↑↓ · … and the frame lines) map to plain characters through <see cref="Drawn"/>.
/// <see cref="Current"/> is the set the drawing reads. Program sets it once, before the screen starts, as Palette.Apply does for colours.
/// </summary>
public sealed record Glyphs(
    char Dot, char Pulse, char GhostEmpty, char GhostHalf, char DashH, char DashV, char Gap,
    char FastForward, char Conflict, char MergeMark, string Spinner,
    char Line, char VLine, char ForkMid, char ForkEnd, char MergeUp, char MergeDown, char TeeUp, char TeeDown, char MergeMid = '┤')
{
    /// <summary>The default set.</summary>
    public static readonly Glyphs Unicode = new(
        Dot: '●', Pulse: '◉', GhostEmpty: '◌', GhostHalf: '◍', DashH: '┄', DashV: '┆', Gap: '┈',
        FastForward: '⇢', Conflict: '✕', MergeMark: '⑂', Spinner: "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏",
        Line: '─', VLine: '│', ForkMid: '├', ForkEnd: '╰', MergeUp: '╯', MergeDown: '╮', TeeUp: '┴', TeeDown: '┬', MergeMid: '┤');

    /// <summary>
    /// The plain-ASCII set. Lines and corners become - | +, the round, dashed and arrow glyphs the characters below, and the
    /// spinner |/-\ (four frames instead of ten).
    /// Table of the lane glyphs: ● *   ◉ @   ◌ ◍ o   ⇢ >   ✕ x   ⑂ Y   ┄ -   ┆ :   ┈ .   lines ─ -   │ |   corners and tees ├ ╰ ╯ ╮ ┴ ┬ ┤ +.
    /// </summary>
    public static readonly Glyphs Basic = new(
        Dot: '*', Pulse: '@', GhostEmpty: 'o', GhostHalf: 'o', DashH: '-', DashV: ':', Gap: '.',
        FastForward: '>', Conflict: 'x', MergeMark: 'Y', Spinner: "|/-\\",
        Line: '-', VLine: '|', ForkMid: '+', ForkEnd: '+', MergeUp: '+', MergeDown: '+', TeeUp: '+', TeeDown: '+', MergeMid: '+');

    /// <summary>The set the drawing uses. Unicode unless --glyphs basic or GRAFTUI_GLYPHS=basic was given.</summary>
    public static Glyphs Current { get; set; } = Unicode;

    /// <summary>
    /// The text marks outside the lane set, each with one plain character so no column moves. Every key here is drawn through Paint,
    /// so the table covers the new look and the old look's Paint-drawn text alike. Mapping (Unicode → Basic):
    /// · and … → . ; → > ; ← &lt; ; ↑ ^ ; ↓ v ; ⇧ S ; ↶ &lt; ; ✔ + ; ✕ x ; ▸ > ; ▴ ^ ; ▾ v ; — – -  ; ± + ; × x ;
    /// │ | ; ─ - ; ━ - ; ┃ | ; ╌ ┄ - ; ┆ : ; ┈ . ; ╲ \ ; ╱ / ; ▶ > ; ✂ % ; ✖ x ; ┛ + ;
    /// ┌ ┐ └ ┘ ├ ┤ ┬ ┴ ╭ ╮ ╰ ╯ + ; ● * ; ◉ @ ; ◌ ◍ o ; ⇢ > ; ⑂ Y.
    /// Anything not in the table (user text such as branch names, the braille spinner of the Unicode set) is drawn as it is.
    /// </summary>
    internal static readonly IReadOnlyDictionary<char, char> BasicText = new Dictionary<char, char>
    {
        ['·'] = '.', ['…'] = '.',
        ['→'] = '>', ['←'] = '<', ['↑'] = '^', ['↓'] = 'v', ['⇧'] = 'S', ['↶'] = '<',
        ['✔'] = '+', ['✕'] = 'x', ['▸'] = '>', ['▴'] = '^', ['▾'] = 'v',
        ['—'] = '-', ['–'] = '-', ['±'] = '+', ['×'] = 'x',
        ['│'] = '|', ['─'] = '-', ['╌'] = '-', ['┄'] = '-', ['┆'] = ':', ['┈'] = '.',
        ['┌'] = '+', ['┐'] = '+', ['└'] = '+', ['┘'] = '+', ['├'] = '+', ['┤'] = '+', ['┬'] = '+', ['┴'] = '+',
        ['╭'] = '+', ['╮'] = '+', ['╰'] = '+', ['╯'] = '+',
        ['━'] = '-', ['┃'] = '|', ['┛'] = '+', ['╲'] = '\\', ['╱'] = '/', ['▶'] = '>', ['✂'] = '%', ['✖'] = 'x',
        ['●'] = '*', ['◉'] = '@', ['◌'] = 'o', ['◍'] = 'o', ['⇢'] = '>', ['⑂'] = 'Y',
    };

    /// <summary>The rune to draw: itself, or its plain stand-in when <see cref="Current"/> is Basic.</summary>
    public static Rune Drawn(Rune rune) => PlainFor(rune, Current);

    /// <summary>The rune to draw in <paramref name="set"/>: the rune itself, or its plain stand-in in Basic. Other runes stay as they are.</summary>
    internal static Rune PlainFor(Rune rune, Glyphs set)
    {
        if (!ReferenceEquals(set, Basic) || rune.Value > char.MaxValue)
            return rune;
        if (BasicText.TryGetValue((char)rune.Value, out var plain))
            return new Rune(plain);
        return rune;
    }
}
