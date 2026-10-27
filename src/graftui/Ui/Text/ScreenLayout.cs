namespace graftui.Ui.Text;

/// <summary>Row and column budget for the panes. TopHeight holds the branches and the art; LogHeight is the Commands pane.</summary>
public sealed record ScreenLayout(bool TooSmall, int Cols, int Rows, int BranchWidth, int TopHeight, int LogHeight)
{
    public const int MinCols = 64, MinRows = 22;

    public const int MinBranchWidth = 30;

    /// <summary>The narrowest the branches pane goes, used only when the screen is too narrow for <see cref="MinBranchWidth"/>.</summary>
    public const int NarrowBranchWidth = 24;

    /// <summary>The art pane never gets narrower than this, so the branches pane gives up columns first.</summary>
    public const int MinArtPaneWidth = 40;

    /// <summary>
    /// <paramref name="wantedBranchWidth"/> is the branches pane width that shows every name in full. The pane is
    /// never narrower than <see cref="MinBranchWidth"/> and never wider than about 40% of the screen, so long names
    /// are cut only when that upper bound is hit. Below 70 columns the pane shrinks, down to
    /// <see cref="NarrowBranchWidth"/>, so the art pane keeps <see cref="MinArtPaneWidth"/> columns.
    /// </summary>
    public static ScreenLayout Compute(int cols, int rows, int wantedBranchWidth)
    {
        var logHeight = Math.Clamp(rows / 4, 5, 11);
        var topHeight = rows - 2 - logHeight;
        var floor = Math.Max(NarrowBranchWidth, Math.Min(MinBranchWidth, cols - MinArtPaneWidth));
        var upper = Math.Max(floor, Math.Min(cols * 2 / 5, cols - MinArtPaneWidth));
        var branchWidth = Math.Min(upper, Math.Max(floor, wantedBranchWidth));
        var tooSmall = cols < MinCols || rows < MinRows;
        return new ScreenLayout(tooSmall, cols, rows, branchWidth, topHeight, logHeight);
    }
}
