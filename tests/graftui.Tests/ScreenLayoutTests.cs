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
        Assert.Equal(5, layout.LogHeight);
        Assert.Equal(15, layout.TopHeight);
        Assert.Equal(24, layout.BranchWidth);
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
