using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;

namespace graftui.Ui.Views;

/// <summary>
/// The Commands pane. Shows the newest lines, unless an anchor is set: then the anchor line is at the top
/// as far as the end of the log allows (v1's rule, computed at draw time so it survives a resize).
/// Message lines wrap to the pane width, so a long refusal or failure is read in full; command and output lines are cut.
/// </summary>
public sealed class LogView : PaneView
{
    private const int MaxLines = 1000;

    private static readonly Style Dim = new(Ink.Dim), Fg = new(Ink.Fg);
    private static readonly Style Ok = new(Ink.Ok), Bad = new(Ink.Bad);

    private readonly List<LogLine> _lines = [];
    private int _anchor = -1;   // index into _lines
    private int _dropped;       // lines trimmed from the front so far

    public LogView()
    {
        Title = "Commands";
    }

    public int Count => _lines.Count;

    /// <summary>
    /// Position of the next line in the whole log, counting trimmed lines too. Positions never change when
    /// old lines are dropped, so a position taken now still names the same line later.
    /// </summary>
    public int Position => _dropped + _lines.Count;

    /// <summary>Appends a line. Drops the oldest lines beyond the limit. Clears any anchor.</summary>
    public void Add(LogLine line)
    {
        _lines.Add(line);
        if (_lines.Count > MaxLines)
        {
            var excess = _lines.Count - MaxLines;
            _lines.RemoveRange(0, excess);
            _dropped += excess;
        }
        ClearAnchor();
        SetNeedsDraw();
    }

    /// <summary>Anchors the view at a <see cref="Position"/>. A position that has already been trimmed anchors at the oldest line kept.</summary>
    public void SetAnchor(int position)
    {
        _anchor = Math.Clamp(position - _dropped, 0, Math.Max(0, _lines.Count - 1));
        SetNeedsDraw();
    }

    public void ClearAnchor() => _anchor = -1;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        ClearAll();
        DrawFrame();

        var w = Viewport.Width;
        var rows = Viewport.Height - 2;
        var maxCols = w - 4;
        if (rows <= 0 || maxCols <= 0)
            return true;

        var rowsOf = _lines.Select(l => Rows(l, maxCols)).ToList();
        var top = TopLine(rowsOf.Select(r => r.Count).ToList(), rows, _anchor);
        var y = 0;
        for (var i = top; i < rowsOf.Count && y < rows; i++)
            foreach (var row in rowsOf[i])
            {
                if (y >= rows)
                    break;
                DrawLine(2, 1 + y++, row, maxCols);
            }
        return true;
    }

    /// <summary>
    /// Index of the first visible line, given how many rows each line takes. Without an anchor (anchor &lt; 0) that is
    /// the newest lines that fit. With one, the anchor is the top line, but never past those (no blank space below the log).
    /// The newest line is always the bottom line, even when it is taller than the pane.
    /// </summary>
    internal static int TopLine(IReadOnlyList<int> heights, int rows, int anchor)
    {
        var count = heights.Count;
        var bottom = count;
        var used = 0;
        while (bottom > 0 && used + heights[bottom - 1] <= rows)
            used += heights[--bottom];
        if (bottom == count && count > 0)
            bottom = count - 1;
        return anchor >= 0 ? Math.Min(anchor, bottom) : bottom;
    }

    /// <summary>
    /// The rows a line takes at maxCols. A message wraps at word boundaries and each row keeps the line's leading spaces.
    /// Command and output lines take one row, cut at the pane width.
    /// </summary>
    internal static IReadOnlyList<LogLine> Rows(LogLine line, int maxCols)
    {
        if (line.Kind != LogKind.Message)
            return [line];

        var text = TextFit.Sanitize(line.Text);
        var indent = new string(' ', text.Length - text.TrimStart(' ').Length);
        var width = maxCols - indent.Length;
        if (width < 1)
            return [new LogLine(LogKind.Message, TextFit.Fit(text, maxCols), 0, line.Style)];

        var words = TextFit.WrapWords(text, width);
        if (words.Count == 0)
            return [new LogLine(LogKind.Message, "", 0, line.Style)];
        return words.Select(w => new LogLine(LogKind.Message, indent + w, 0, line.Style)).ToList();
    }

    private void DrawLine(int x, int y, LogLine l, int maxCols)
    {
        switch (l.Kind)
        {
            case LogKind.Command:
                DrawSegs(x, y,
                [
                    new Seg("$", Dim),
                    new Seg(" ", Fg),
                    new Seg(l.Text, Fg),
                    new Seg("  ", Fg),
                    new Seg($"[exit {l.ExitCode}]", l.ExitCode == 0 ? Ok : Bad),
                ], maxCols);
                break;

            case LogKind.Output:
                var failed = l.Text.StartsWith("CONFLICT", StringComparison.Ordinal)
                    || l.Text.StartsWith("error:", StringComparison.Ordinal)
                    || l.Text.StartsWith("fatal:", StringComparison.Ordinal);
                DrawText(x, y, "    " + l.Text, maxCols, failed ? Bad : Dim);
                break;

            default:
                DrawText(x, y, l.Text, maxCols, l.Style);
                break;
        }
    }
}
