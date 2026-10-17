namespace Graft.Ui;

/// <summary>Row and column budget for the panes. TopHeight holds the branches and the art; LogHeight is the Commands pane.</summary>
public sealed record ScreenLayout(bool TooSmall, int Cols, int Rows, int BranchWidth, int TopHeight, int LogHeight)
{
    public const int MinCols = 64, MinRows = 22;

    public static ScreenLayout Compute(int cols, int rows)
    {
        var logHeight = Math.Clamp(rows / 4, 5, 11);
        var topHeight = rows - 2 - logHeight;
        var branchWidth = Math.Clamp(cols / 3, 24, 34);
        var tooSmall = cols < MinCols || rows < MinRows;
        return new ScreenLayout(tooSmall, cols, rows, branchWidth, topHeight, logHeight);
    }
}
