using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;

namespace graftui.Ui.Views;

/// <summary>
/// The Commands pane. Shows the newest lines, unless an anchor is set: then the anchor line is at the top
/// as far as the end of the log allows (v1's rule, computed at draw time so it survives a resize).
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

        var top = TopLine(_lines.Count, rows, _anchor);
        for (var i = 0; i < rows && top + i < _lines.Count; i++)
            DrawLine(2, 1 + i, _lines[top + i], maxCols);
        return true;
    }

    /// <summary>
    /// Index of the first visible line. Without an anchor (anchor &lt; 0) that is the newest lines. With one, the
    /// anchor is the top line, but never past the newest lines (no blank space below the log).
    /// </summary>
    internal static int TopLine(int count, int rows, int anchor)
    {
        var bottom = Math.Max(0, count - rows);
        return anchor >= 0 ? Math.Min(anchor, bottom) : bottom;
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
