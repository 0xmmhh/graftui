using graftui.Ui.Text;
using graftui.Ui.Views;

namespace graftui.Tests;

public class ScreenLayoutTests
{
    [Fact]
    public void Minimum_size_fits_and_splits_rows_as_designed()
    {
        var layout = ScreenLayout.Compute(64, 22, 0);

        Assert.False(layout.TooSmall);
        Assert.Equal(1, layout.LogHeight);
        Assert.Equal(19, layout.TopHeight);
        Assert.Equal(24, layout.BranchWidth);
        // The old look: the panes start on row 1, the strip is the row under them, and the square frame is not on.
        Assert.Equal(1, layout.PanesY);
        Assert.Equal(1 + layout.TopHeight, layout.LogY);
        Assert.False(layout.Framed);
    }

    [Theory]
    [InlineData(64, 22)]
    [InlineData(80, 24)]
    [InlineData(120, 36)]
    [InlineData(200, 60)]
    public void The_commands_strip_is_one_row_and_the_top_panes_get_the_rest(int cols, int rows)
    {
        var layout = ScreenLayout.Compute(cols, rows, 0);

        Assert.Equal(1, layout.LogHeight);
        Assert.Equal(rows - 3, layout.TopHeight);
    }

    [Theory]
    [InlineData(64, 22, 7)]
    [InlineData(80, 24, 8)]
    [InlineData(120, 36, 12)]
    [InlineData(200, 60, 12)]
    public void The_open_commands_list_is_a_third_of_the_rows_from_five_to_twelve(int cols, int rows, int openHeight)
    {
        Assert.Equal(openHeight, ScreenLayout.Compute(cols, rows, 0).OpenLogHeight);
    }

    [Theory]
    [InlineData(64, 22, 0, 24)]
    [InlineData(64, 22, 38, 24)]
    [InlineData(80, 24, 38, 32)]
    [InlineData(100, 30, 20, 30)]
    public void Branch_pane_is_split_so_the_art_pane_keeps_forty_columns(int cols, int rows, int wanted, int expected)
    {
        Assert.Equal(expected, ScreenLayout.Compute(cols, rows, wanted).BranchWidth);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(70)]
    [InlineData(100)]
    public void Art_pane_is_at_least_forty_columns_wide(int cols)
    {
        var layout = ScreenLayout.Compute(cols, 24, 60);

        Assert.True(cols - layout.BranchWidth >= ScreenLayout.MinArtPaneWidth);
    }

    [Fact]
    public void Art_area_is_centred_in_a_wide_pane_below_the_frame_row()
    {
        // Pane 73x21: 69 columns inside the frame and margins, so the art fills the width and starts at column 2.
        Assert.Equal((2, 3, 69, 11), ArtPaneView.ArtArea(73, 21));
    }

    [Fact]
    public void Art_area_at_the_narrow_pane_size_uses_the_short_art()
    {
        Assert.Equal((2, 3, 30, 8), ArtPaneView.ArtArea(34, 15));
    }

    [Fact]
    public void Compact_art_on_the_branches_tab_is_nine_rows_and_the_changes_art_stays_eleven()
    {
        Assert.Equal((5, 3, 70, 9), ArtPaneView.ArtArea(80, 22, compact: true));
        Assert.Equal((5, 3, 70, 11), ArtPaneView.ArtArea(80, 22));
    }

    [Fact]
    public void No_art_area_when_the_pane_is_too_narrow_for_any_art()
    {
        Assert.Null(ArtPaneView.ArtArea(20, 15));
    }

    [Fact]
    public void One_column_below_the_minimum_is_too_small()
    {
        Assert.True(ScreenLayout.Compute(63, 22, 0).TooSmall);
    }

    [Theory]
    [InlineData(120, 40, 31, 32, 5, 12, 34)]
    [InlineData(100, 30, 30, 23, 4, 10, 25)]
    [InlineData(80, 24, 30, 20, 1, 8, 22)]
    [InlineData(64, 22, 24, 18, 1, 7, 20)]
    [InlineData(160, 50, 42, 41, 6, 12, 43)]
    [InlineData(200, 60, 42, 50, 7, 12, 52)]
    public void Framed_sizes(int cols, int rows, int branchWidth, int topHeight, int logHeight, int openLogHeight, int logY)
    {
        var layout = ScreenLayout.ComputeFramed(cols, rows);

        Assert.Equal(branchWidth, layout.BranchWidth);
        Assert.Equal(topHeight, layout.TopHeight);
        Assert.Equal(logHeight, layout.LogHeight);
        Assert.Equal(openLogHeight, layout.OpenLogHeight);
        Assert.Equal(logY, layout.LogY);
        Assert.Equal(2, layout.PanesY);
        Assert.True(layout.Framed);
        // The tab row, the panes, the Commands pane and the key bar fill the screen with no gap.
        Assert.Equal(rows, layout.PanesY + layout.TopHeight + layout.LogHeight + 1);
    }

    [Theory]
    [InlineData(25, 1)]
    [InlineData(26, 4)]
    [InlineData(31, 4)]
    [InlineData(32, 5)]
    [InlineData(44, 6)]
    [InlineData(57, 7)]
    [InlineData(69, 8)]
    [InlineData(100, 8)]
    public void The_dock_collapses_below_26_rows(int rows, int logHeight)
    {
        Assert.Equal(logHeight, ScreenLayout.ComputeFramed(120, rows).LogHeight);
    }

    [Theory]
    [InlineData(70, 30)]
    [InlineData(116, 30)]
    [InlineData(117, 30)]
    [InlineData(119, 31)]
    [InlineData(154, 40)]
    [InlineData(162, 42)]
    [InlineData(300, 42)]
    [InlineData(64, 24)]
    [InlineData(66, 26)]
    public void The_framed_list_is_a_quarter_clamped_30_to_42(int cols, int branchWidth)
    {
        Assert.Equal(branchWidth, ScreenLayout.ComputeFramed(cols, 30).BranchWidth);
    }

    [Fact]
    public void The_framed_open_list_is_never_shorter_than_the_dock_and_ends_on_the_docks_last_row()
    {
        for (var rows = 22; rows <= 120; rows++)
        {
            var layout = ScreenLayout.ComputeFramed(120, rows);

            Assert.True(layout.OpenLogHeight >= layout.LogHeight, $"rows {rows}");
            Assert.True(layout.LogY + layout.LogHeight - layout.OpenLogHeight >= layout.PanesY, $"rows {rows}");
        }
    }

    [Theory]
    [InlineData(63, 22)]
    [InlineData(64, 21)]
    public void Framed_too_small_is_flagged_without_a_negative_top_pane(int cols, int rows)
    {
        var layout = ScreenLayout.ComputeFramed(cols, rows);

        Assert.True(layout.TooSmall);
        Assert.True(layout.TopHeight >= 0);
    }
}
