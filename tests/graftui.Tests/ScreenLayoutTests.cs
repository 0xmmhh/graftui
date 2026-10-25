using graftui.Ui.Text;

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
    public void One_column_below_the_minimum_is_too_small()
    {
        Assert.True(ScreenLayout.Compute(63, 22, 0).TooSmall);
    }
}
