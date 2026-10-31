using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;

namespace graftui.Ui.Views;

/// <summary>
/// The Commands pane. Shows the newest lines, unless an anchor is set: then the anchor entry is at the top
/// as far as the end of the log allows (v1's rule, computed at draw time so it survives a resize).
/// Each git invocation is one <see cref="LogEntry"/>. The pane shows either the simplified lines or the exact lines
/// (<see cref="ShowExact"/>, toggled with v); the entries and the anchor are the same in both.
/// Message lines wrap to the pane width, so a long refusal or failure is read in full; command and output lines are cut,
/// except a failed command, which wraps.
/// </summary>
public sealed class LogView : PaneView
{
    private const int MaxLines = 1000;

    /// <summary>What the empty pane says: the one thing a new user needs to know about it.</summary>
    internal const string EmptyHint = "git commands graftui runs show up here";

    private static readonly Style Dim = new(Ink.Dim), Fg = new(Ink.Fg), FgB = new(Ink.Fg, true);
    private static readonly Style Ok = new(Ink.Ok), Bad = new(Ink.Bad);

    private readonly List<LogEntry> _entries = [];
    private int _anchor = -1;   // index into _entries
    private int _dropped;       // entries trimmed from the front so far
    private int _exactLines;    // lines held in the exact lists, for the limit

    public LogView()
    {
        Title = "Commands";
    }

    /// <summary>The number of entries kept.</summary>
    public int Count => _entries.Count;

    /// <summary>True: the exact lines are shown. False (the default): the simplified lines.</summary>
    public bool ShowExact { get; private set; }

    /// <summary>
    /// Position of the next entry in the whole log, counting trimmed entries too. Positions never change when
    /// old entries are dropped, so a position taken now still names the same entry later.
    /// </summary>
    public int Position => _dropped + _entries.Count;

    /// <summary>Appends a message line, the same on both lists. Clears any anchor.</summary>
    public void Add(LogLine line) => Add(new LogEntry([line], [line]));

    /// <summary>Appends an entry. Drops the oldest entries beyond the line limit. Clears any anchor.</summary>
    public void Add(LogEntry entry)
    {
        _entries.Add(entry);
        _exactLines += entry.Exact.Count;
        while (_entries.Count > 1 && _exactLines > MaxLines)
        {
            _exactLines -= _entries[0].Exact.Count;
            _entries.RemoveAt(0);
            _dropped++;
        }
        ClearAnchor();
        UpdateFooter();
        SetNeedsDraw();
    }

    /// <summary>True when the list on screen has no lines, so the empty hint is shown instead.</summary>
    internal bool ShowsEmptyHint => VisibleLines().Count == 0;

    /// <summary>Switches between the simplified and the exact list. Entries, positions and the anchor do not change.</summary>
    public void ToggleExact()
    {
        ShowExact = !ShowExact;
        UpdateFooter();
        SetNeedsDraw();
    }

    /// <summary>Anchors the view at a <see cref="Position"/>. A position that has already been trimmed anchors at the oldest entry kept.</summary>
    public void SetAnchor(int position)
    {
        _anchor = Math.Clamp(position - _dropped, 0, Math.Max(0, _entries.Count - 1));
        SetNeedsDraw();
    }

    public void ClearAnchor() => _anchor = -1;

    /// <summary>The key hint on the bottom border: "v all commands" on the simplified list, "v simplified" on the exact one.</summary>
    private void UpdateFooter()
    {
        if (_entries.Count == 0)
        {
            FooterSegs = null;
            return;
        }
        var hint = ShowExact ? " simplified" : " all commands";
        FooterSegs = [new Seg(" ", Fg), new Seg("v", FgB), new Seg(hint, Dim), new Seg(" ", Fg)];
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        ClearAll();
        DrawFrame();

        var w = Viewport.Width;
        var rows = Viewport.Height - 2;
        var maxCols = w - 4;
        if (rows <= 0 || maxCols <= 0)
            return true;

        // The lines of the list on screen, each tagged with the index of its entry. The anchor is the first line of that entry or later.
        var flat = VisibleLines();
        if (flat.Count == 0)
        {
            DrawText(2, 1, EmptyHint, maxCols, Dim);
            return true;
        }

        var rowsOf = flat.Select(f => Rows(f.Line, maxCols)).ToList();
        var anchorLine = AnchorLine(flat.Select(f => f.Entry).ToList(), _anchor);
        var top = TopLine(rowsOf.Select(r => r.Count).ToList(), rows, anchorLine);
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

    // The lines the pane shows now: the simplified or the exact list of every entry, each tagged with the index of its entry.
    private List<(LogLine Line, int Entry)> VisibleLines()
    {
        var flat = new List<(LogLine Line, int Entry)>();
        for (var i = 0; i < _entries.Count; i++)
            foreach (var line in ShowExact ? _entries[i].Exact : _entries[i].Simple)
                flat.Add((line, i));
        return flat;
    }

    /// <summary>
    /// The first line of the anchored entry or of a later one, given the entry index of each line; -1 when there is no anchor
    /// or no line from it on (the newest lines are shown then).
    /// </summary>
    internal static int AnchorLine(IReadOnlyList<int> entryOfLine, int anchorEntry)
    {
        if (anchorEntry < 0)
            return -1;
        for (var i = 0; i < entryOfLine.Count; i++)
            if (entryOfLine[i] >= anchorEntry)
                return i;
        return -1;
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
    /// A failed command wraps without cutting, as "$ command  [exit N]" in the failure colour.
    /// Other command and output lines take one row, cut at the pane width.
    /// </summary>
    internal static IReadOnlyList<LogLine> Rows(LogLine line, int maxCols)
    {
        if (line.Kind == LogKind.Command && line.Failed)
            return FailedRows(line, maxCols);

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

    /// <summary>
    /// A failed command: "$ command" wrapped at spaces, with a word longer than the pane broken over rows (never cut), then
    /// "  [exit N]" after the last word when it fits, else on a row of its own. All in the failure colour.
    /// </summary>
    private static IReadOnlyList<LogLine> FailedRows(LogLine line, int maxCols)
    {
        var texts = TextFit.WrapExact("$ " + line.Text, maxCols);
        var suffix = $"  [exit {line.ExitCode}]";
        if (texts.Count > 0 && TextFit.Columns(texts[^1] + suffix) <= maxCols)
            texts[^1] += suffix;
        else
            texts.Add(TextFit.Fit(suffix.TrimStart(), maxCols));
        return texts.Select(t => new LogLine(LogKind.Message, t, 0, Bad)).ToList();
    }

    /// <summary>
    /// The text of a successful simplified command at maxCols: cut with "…" when it is too long, so the "  ✔" after it always shows.
    /// The row is "$ " + text + "  ✔", which takes <see cref="SimpleOkReserve"/> columns beside the text.
    /// </summary>
    internal static string SimpleOkText(string text, int maxCols) => TextFit.Fit(text, maxCols - SimpleOkReserve);

    /// <summary>Columns beside the text of a successful simplified command: "$ " at the start and "  ✔" at the end.</summary>
    internal const int SimpleOkReserve = 5;

    private void DrawLine(int x, int y, LogLine l, int maxCols)
    {
        switch (l.Kind)
        {
            case LogKind.Command when l.Simple && l.ExitCode == 0:
                DrawSegs(x, y,
                [
                    new Seg("$", Dim),
                    new Seg(" ", Fg),
                    new Seg(SimpleOkText(l.Text, maxCols), Fg),
                    new Seg("  ", Fg),
                    new Seg("✔", Ok),
                ], maxCols);
                break;

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
