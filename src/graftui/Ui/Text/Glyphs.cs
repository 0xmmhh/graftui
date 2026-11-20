namespace graftui.Ui.Text;

/// <summary>
/// The characters the new Branches screen draws with. Unicode is the default; Basic is the plain-ASCII fallback (wired in later).
/// The box-drawing lines (Line to TeeDown) are the same in both, so Basic only changes the round, dashed and arrow glyphs.
/// </summary>
public sealed record Glyphs(
    char Dot, char Pulse, char GhostEmpty, char GhostHalf, char DashH, char DashV, char Gap,
    char FastForward, char Conflict, char MergeMark, string Spinner,
    char Line, char VLine, char ForkMid, char ForkEnd, char MergeUp, char MergeDown, char TeeUp, char TeeDown)
{
    /// <summary>The default set.</summary>
    public static readonly Glyphs Unicode = new(
        Dot: '●', Pulse: '◉', GhostEmpty: '◌', GhostHalf: '◍', DashH: '┄', DashV: '┆', Gap: '┈',
        FastForward: '⇢', Conflict: '✕', MergeMark: '⑂', Spinner: "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏",
        Line: '─', VLine: '│', ForkMid: '├', ForkEnd: '╰', MergeUp: '╯', MergeDown: '╮', TeeUp: '┴', TeeDown: '┬');

    /// <summary>The plain-ASCII set, for terminals without the Unicode glyphs.</summary>
    public static readonly Glyphs Basic = new(
        Dot: '*', Pulse: '@', GhostEmpty: 'o', GhostHalf: 'o', DashH: '-', DashV: ':', Gap: '.',
        FastForward: '>', Conflict: 'x', MergeMark: 'Y', Spinner: "|/-\\",
        Line: '─', VLine: '│', ForkMid: '├', ForkEnd: '╰', MergeUp: '╯', MergeDown: '╮', TeeUp: '┴', TeeDown: '┬');
}
