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
}
