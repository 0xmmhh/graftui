namespace graftui.Ui.Text;

/// <summary>Row and column budget for the panes. TopHeight holds the branches and the art; LogHeight is the Commands pane.</summary>
public sealed record ScreenLayout(bool TooSmall, int Cols, int Rows, int BranchWidth, int TopHeight, int LogHeight)
{
    public const int MinCols = 64, MinRows = 22;

    public const int MinBranchWidth = 30;

    /// <summary>
    /// <paramref name="wantedBranchWidth"/> is the branches pane width that shows every name in full. The pane is
    /// never narrower than <see cref="MinBranchWidth"/> and never wider than about 40% of the screen, so long names
    /// are cut only when that upper bound is hit.
    /// </summary>
    public static ScreenLayout Compute(int cols, int rows, int wantedBranchWidth)
    {
        var logHeight = Math.Clamp(rows / 4, 5, 11);
        var topHeight = rows - 2 - logHeight;
        var upper = Math.Max(MinBranchWidth, cols * 2 / 5);
        var branchWidth = Math.Min(upper, Math.Max(MinBranchWidth, wantedBranchWidth));
        var tooSmall = cols < MinCols || rows < MinRows;
        return new ScreenLayout(tooSmall, cols, rows, branchWidth, topHeight, logHeight);
    }
}
