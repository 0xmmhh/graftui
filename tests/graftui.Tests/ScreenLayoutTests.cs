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
        Assert.Equal(30, layout.BranchWidth);
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
    public void One_column_below_the_minimum_is_too_small()
    {
        Assert.True(ScreenLayout.Compute(63, 22, 0).TooSmall);
    }
}
