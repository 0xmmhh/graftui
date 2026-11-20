namespace graftui.Ui.Text;

/// <summary>
/// Row and column budget for the panes. TopHeight holds the branches and the art. LogHeight is the Commands pane when it is closed: one
/// row (the strip), or the framed dock from <see cref="DockMinRows"/> rows up. OpenLogHeight is the height of the full Commands list when
/// it is open: it covers the bottom rows of the top panes, and the art underneath keeps its size.
/// PanesY is the row the top panes start on: 1 under the old look, 2 under the tab row of the new look. LogY is the row of the Commands
/// pane when it is closed. Framed is the new look (the tab row, square frames, the dock).
/// </summary>
public sealed record ScreenLayout(bool TooSmall, int Cols, int Rows, int BranchWidth, int TopHeight, int LogHeight, int OpenLogHeight,
    int PanesY, int LogY, bool Framed)
{
    public const int MinCols = 64, MinRows = 22;

    public const int MinBranchWidth = 30;

    /// <summary>The narrowest the branches pane goes, used only when the screen is too narrow for <see cref="MinBranchWidth"/>.</summary>
    public const int NarrowBranchWidth = 24;

    /// <summary>The art pane never gets narrower than this, so the branches pane gives up columns first.</summary>
    public const int MinArtPaneWidth = 40;

    /// <summary>The top panes' first row under the old look (the top line is row 0).</summary>
    public const int OldPanesY = 1;

    /// <summary>The top panes' first row under the new look: the tab row is row 1.</summary>
    public const int FramedPanesY = 2;

    /// <summary>The new look's branches pane: this share of the width (percent), then clamped to the range below.</summary>
    public const int BranchSharePercent = 26;

    /// <summary>The new look's branches pane is never wider than this, however wide the screen is.</summary>
    public const int MaxFramedBranchWidth = 42;

    /// <summary>From this many rows up the Commands pane is the framed dock; below it, the one-row strip.</summary>
    public const int DockMinRows = 26;

    /// <summary>The dock's inner rows: this share of the screen rows (percent), clamped to 1..<see cref="MaxDockRows"/>.</summary>
    public const int DockSharePercent = 8;

    /// <summary>The most inner rows the dock gets.</summary>
    public const int MaxDockRows = 6;

    /// <summary>
    /// <paramref name="wantedBranchWidth"/> is the branches pane width that shows every name in full. The pane is
    /// never narrower than <see cref="MinBranchWidth"/> and never wider than about 40% of the screen, so long names
    /// are cut only when that upper bound is hit. Below 70 columns the pane shrinks, down to
    /// <see cref="NarrowBranchWidth"/>, so the art pane keeps <see cref="MinArtPaneWidth"/> columns.
    /// The Commands strip is always one row; the open list is a third of the rows, from 5 to 12.
    /// </summary>
    public static ScreenLayout Compute(int cols, int rows, int wantedBranchWidth)
    {
        const int logHeight = 1;
        var topHeight = rows - OldPanesY - logHeight - 1;
        var openLogHeight = Math.Clamp(rows / 3, 5, 12);
        var floor = Math.Max(NarrowBranchWidth, Math.Min(MinBranchWidth, cols - MinArtPaneWidth));
        var upper = Math.Max(floor, Math.Min(cols * 2 / 5, cols - MinArtPaneWidth));
        var branchWidth = Math.Min(upper, Math.Max(floor, wantedBranchWidth));
        var tooSmall = cols < MinCols || rows < MinRows;
        var logY = OldPanesY + topHeight;
        return new ScreenLayout(tooSmall, cols, rows, branchWidth, topHeight, logHeight, openLogHeight, OldPanesY, logY, false);
    }

    /// <summary>
    /// The new look's layout, with these rules (integer maths; the "+ 50" rounds half up, as Math.Round would not):
    /// the branches pane is 26% of the columns, clamped to 30..42; the art keeps 40 columns, so the pane is at most cols - 40; below 70
    /// columns it is at least 24 (NarrowBranchWidth).
    /// The Commands dock is 8% of the rows (+ 50 for rounding), clamped to 1..6 inner rows, plus its two border rows, from 26 rows up;
    /// below 26 rows it is the one-row strip.
    /// The top panes start on row 2 under the tab row and end on the row above the Commands pane. The key bar is the last row.
    /// The open list is a third of the rows, clamped to 5..12, and never shorter than the closed pane.
    /// </summary>
    public static ScreenLayout ComputeFramed(int cols, int rows)
    {
        var wanted = Math.Clamp((cols * BranchSharePercent + 50) / 100, MinBranchWidth, MaxFramedBranchWidth);
        var capped = Math.Min(wanted, cols - MinArtPaneWidth);
        var branchWidth = Math.Max(capped, NarrowBranchWidth);

        var logHeight = 1;
        if (rows >= DockMinRows)
        {
            var innerRows = Math.Clamp((rows * DockSharePercent + 50) / 100, 1, MaxDockRows);
            logHeight = innerRows + 2;
        }
        var topHeight = Math.Max(0, rows - FramedPanesY - logHeight - 1);
        var logY = FramedPanesY + topHeight;
        var openLogHeight = Math.Max(Math.Clamp(rows / 3, 5, 12), logHeight);
        var tooSmall = cols < MinCols || rows < MinRows;
        return new ScreenLayout(tooSmall, cols, rows, branchWidth, topHeight, logHeight, openLogHeight, FramedPanesY, logY, true);
    }
}
