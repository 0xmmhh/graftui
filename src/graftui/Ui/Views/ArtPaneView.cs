using graftui.Ui.Scenes;
using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;

namespace graftui.Ui.Views;

/// <summary>
/// The preview and animation pane: a direction line, the art, its caption and the commit list.
/// Layout (pane rows): 1 direction, 2 rule, 3.. art (AH rows), caption (one or two lines), rule, then commit rows.
/// </summary>
/// <summary>The rows of one pane's art, caption and commit area, in the pane's own rows (see <see cref="ArtPaneView.Rows"/>).</summary>
public readonly record struct PaneRows(int ArtRows, int CaptionRow, int CaptionRows, int RuleRow, int BelowTop, int BelowRows);

public sealed class ArtPaneView : PaneView
{
    private const int MaxArtWidth = 70;

    // Art heights: the tall art of the Changes tab, the 9-row art of the Branches tab (the same graph without its two empty rows),
    // and the short art. Tall art needs at least TallMinInner inner rows; the 9-row art needs CompactTallMinInner.
    private const int TallRows = 11;
    private const int CompactTallRows = 9;
    private const int ShortRows = 8;
    private const int SingleLineRows = 5;
    private const int TallMinInner = 17;
    private const int CompactTallMinInner = 15;

    private static readonly Style Dim = new(Ink.Dim);
    private static readonly Style FrameStyle = new(Ink.Frame);

    private Func<ArtGeometry, SceneFrame>? _scene;

    /// <summary>
    /// The scene drawn in the art. Setting it also clears <see cref="SingleLine"/>, so a scene set this way has the normal height.
    /// Use <see cref="ShowScene"/> when a scene should have a height of its own.
    /// </summary>
    public Func<ArtGeometry, SceneFrame>? Scene
    {
        get => _scene;
        set
        {
            _scene = value;
            SingleLine = false;
        }
    }

    /// <summary>
    /// True while the Branches preview of a straight graph is drawn in 5 rows instead of 9: the line stays on its row, and the caption,
    /// rule and commit list move up. Only with <see cref="CompactArt"/>. Reset whenever <see cref="Scene"/> is set.
    /// </summary>
    public bool SingleLine { get; set; }

    /// <summary>
    /// Shows <paramref name="scene"/> in the art and sets its height: <paramref name="singleLine"/> only for a straight graph's preview
    /// on the Branches tab. One call, so the height can never be left over from an earlier scene.
    /// </summary>
    public void ShowScene(Func<ArtGeometry, SceneFrame>? scene, bool singleLine = false)
    {
        _scene = scene;
        SingleLine = singleLine;
    }

    /// <summary>Direction line for a given width. Built on demand so names can be cut to the pane.</summary>
    public Func<int, IReadOnlyList<Seg>> Direction { get; set; } = _ => [];

    private CommitPanel? _panel;
    private IReadOnlyList<IReadOnlyList<Seg>>? _below;

    /// <summary>The commit list under the caption, or null when the pane shows none (the Changes tab). Setting it clears <see cref="BelowFor"/>.</summary>
    public CommitPanel? Panel
    {
        get => _panel;
        set
        {
            _panel = value;
            BelowFor = null;
        }
    }

    /// <summary>Width of the whole screen. At least <see cref="CommitPanels.SideBySideMinCols"/> lists the commit groups side by side.</summary>
    public int ScreenCols { get; set; }

    /// <summary>
    /// Lines drawn under the caption in place of <see cref="Panel"/> (the Changes tab's summary). Setting it clears <see cref="BelowFor"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Seg>>? Below
    {
        get => _below;
        set
        {
            _below = value;
            BelowFor = null;
        }
    }

    /// <summary>
    /// Lines under the rule, made for the pane's width and the rows that are free there (a paused rebase's conflicted files). Drawn in
    /// place of <see cref="Below"/> and <see cref="Panel"/>. Setting either of those clears it, so set this one last.
    /// </summary>
    public Func<int, int, IReadOnlyList<IReadOnlyList<Seg>>>? BelowFor { get; set; }

    public Caption? FallbackCaption { get; set; }

    /// <summary>
    /// Set while the name box is open. A caption that needs two lines takes them only when the rows under it keep a label row and
    /// the field row (see <see cref="TwoLineCaption"/>); otherwise it is cut to one line. When it wraps, the box moves down a row.
    /// </summary>
    public bool CaptionKeepsBelowRows { get; set; }

    /// <summary>The rule under the direction line. Off on Branches, where the direction line stands alone; the row stays blank.</summary>
    public bool DirectionRule { get; set; } = true;

    /// <summary>
    /// True on the Branches tab: the tall art is 9 rows, the graph's own rows with no empty row above or below, instead of 11.
    /// The Changes tab keeps 11.
    /// </summary>
    public bool CompactArt { get; set; }

    // The art's height in a pane with ih inner rows.
    private static int ArtRows(int ih, bool compact, bool singleLine)
    {
        if (compact && singleLine)
            return SingleLineRows;
        if (compact)
            return ih >= CompactTallMinInner ? CompactTallRows : ShortRows;
        return ih >= TallMinInner ? TallRows : ShortRows;
    }

    /// <summary>
    /// Where the art's grid is drawn in a pane of the given size (viewport coordinates), or null when no art fits.
    /// Used by the views and by the message form, which is laid over the art.
    /// </summary>
    public static (int X, int Y, int W, int H)? ArtArea(int paneW, int paneH, bool compact = false, bool singleLine = false)
    {
        var cw = paneW - 4;
        var ih = paneH - 2;
        var ah = ArtRows(ih, compact, singleLine);
        var gw = Math.Min(cw, MaxArtWidth);
        if (cw <= 0 || ih <= 0 || ArtGeometry.For(gw, ah) is null)
            return null;
        return (2 + (cw - gw) / 2, 3, gw, ah);
    }

    /// <summary>
    /// Where the name box of the Branches tab's new-branch and rename forms goes, in a pane of the given size (viewport coordinates):
    /// the field's X, its row, its width, and whether a label row sits above it. Null when no commit row is free under the caption.
    /// Labels and the field share the rows under the caption that the commit list would use.
    /// </summary>
    public static (int X, int Y, int W, bool LabelRow)? NameField(int paneW, int paneH, bool captionTwoLines = false, bool compact = false,
        bool singleLine = false)
    {
        var l = CaptionLayout(paneH, captionTwoLines, compact, singleLine);
        if (l.BelowRows == 0 || ArtArea(paneW, paneH, compact, singleLine) == null)
            return null;
        var label = l.BelowRows >= 2;
        // The rows under the caption are drawn at x=4; "name     " is 9 columns wide, so the field starts at 13.
        return (4 + 9, l.BelowTop + (label ? 1 : 0), Math.Max(1, paneW - 15), label);
    }

    /// <summary>
    /// Whether a caption of this text is drawn on two lines in a pane of this size. With <paramref name="keepsBelowRows"/> (the name box
    /// is open) it is only when the rows under the two-line caption still hold a label and the field.
    /// </summary>
    public static bool TwoLineCaption(string text, int paneW, int paneH, bool keepsBelowRows, bool compact = false, bool singleLine = false)
    {
        if (TextFit.Wrap2(text, paneW - 4).Second is null)
            return false;
        return !keepsBelowRows || CaptionLayout(paneH, true, compact, singleLine).BelowRows >= 2;
    }

    /// <summary>
    /// Where the art, the caption, the rule and the rows under them sit in a pane of height <paramref name="paneH"/>, in the pane's own
    /// rows. A two-line caption takes one commit row, and only when a commit row is free; otherwise it falls back to one line.
    /// </summary>
    public static PaneRows Rows(int paneH, bool twoLines, bool compact = false, bool singleLine = false)
    {
        var ih = paneH - 2;
        var ah = ArtRows(ih, compact, singleLine);
        var commitRows = Math.Max(0, ih - 4 - ah);
        if (twoLines && commitRows > 0)
            return new PaneRows(ah, 3 + ah, 2, 5 + ah, 6 + ah, commitRows - 1);
        return new PaneRows(ah, 3 + ah, 1, 4 + ah, 5 + ah, commitRows);
    }

    /// <summary>The caption's rows, the rule's row and the rows under the rule, as <see cref="Rows"/> gives them.</summary>
    public static (int CaptionRows, int RuleRow, int BelowTop, int BelowRows) CaptionLayout(int paneH, bool twoLines, bool compact = false,
        bool singleLine = false)
    {
        var r = Rows(paneH, twoLines, compact, singleLine);
        return (r.CaptionRows, r.RuleRow, r.BelowTop, r.BelowRows);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        ClearAll();
        DrawFrame();

        var pw = Viewport.Width;
        var ph = Viewport.Height;
        var cw = pw - 4;
        var ih = ph - 2;
        if (cw <= 0 || ih <= 0)
            return true;

        DrawSegs(2, 1, Direction(cw), cw);
        if (DirectionRule)
            DrawRule(2, 2, cw);

        var area = ArtArea(pw, ph, CompactArt, SingleLine);
        SceneFrame? frame = null;
        if (area is { } scene && Scene is not null)
            frame = Scene(ArtGeometry.For(scene.W, scene.H)!);

        if (area is { } art && frame is not null)
        {
            var grid = frame.Grid;
            for (var y = 0; y < art.H; y++)
                for (var x = 0; x < art.W; x++)
                {
                    var cell = grid[x, y];
                    Paint.Put(this, art.X + x, art.Y + y, cell.Ch, Palette.Get(cell.Style));
                }
        }

        var caption = area is null
            ? new Caption("art needs a wider pane", Dim)
            : frame?.Caption ?? FallbackCaption;
        (string First, string? Second) split;
        if (caption is null)
            split = ("", null);
        else if (CaptionKeepsBelowRows && !TwoLineCaption(caption.Text, pw, ph, keepsBelowRows: true, compact: CompactArt, singleLine: SingleLine))
            split = (TextFit.Fit(caption.Text, cw), null);
        else
            split = TextFit.Wrap2(caption.Text, cw);
        var (first, second) = split;
        var layout = Rows(ph, second != null, CompactArt, SingleLine);
        if (caption is not null)
        {
            if (layout.CaptionRows == 2)
            {
                DrawCentred(first, layout.CaptionRow, cw, caption.Style);
                DrawCentred(second!, layout.CaptionRow + 1, cw, caption.Style);
            }
            else
                DrawCentred(TextFit.Fit(caption.Text, cw), layout.CaptionRow, cw, caption.Style);
        }

        DrawRule(2, layout.RuleRow, cw);

        var below = BelowFor?.Invoke(cw - 2, layout.BelowRows) ?? Below;
        if (below is not null)
        {
            for (var k = 0; k < layout.BelowRows && k < below.Count; k++)
                DrawSegs(4, layout.BelowTop + k, below[k], cw - 2);
            return true;
        }

        if (Panel is { } panel)
        {
            var lines = CommitPanels.Render(panel, cw - 2, layout.BelowRows, ScreenCols >= CommitPanels.SideBySideMinCols, DateTimeOffset.UtcNow);
            for (var k = 0; k < lines.Count; k++)
                DrawSegs(4, layout.BelowTop + k, lines[k], cw - 2);
        }
        return true;
    }

    private void DrawCentred(string text, int y, int cw, Style style)
    {
        var cols = TextFit.Columns(text);
        DrawText(2 + (cw - cols) / 2, y, text, cw, style);
    }

    private void DrawRule(int x, int y, int cols)
    {
        var attr = Palette.Get(FrameStyle);
        for (var i = 0; i < cols; i++)
            Paint.Put(this, x + i, y, '╌', attr);
    }
}
