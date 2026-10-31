using graftui.Ui.Views;

namespace graftui.Tests;

public class StatusBarViewTests
{
    [Fact]
    public void The_path_stops_three_columns_before_the_right_side()
    {
        Assert.Equal(62, StatusBarView.PathMax(110, 40));
        Assert.Equal(16, StatusBarView.PathMax(64, 40));
        Assert.Equal(28, StatusBarView.PathMax(64, 28));
    }

    [Fact]
    public void A_narrow_window_leaves_no_room_for_the_path_and_the_right_side_starts_at_the_left_end()
    {
        Assert.Equal(0, StatusBarView.PathMax(30, 40));
        Assert.Equal(StatusBarView.LeftEnd, StatusBarView.RightStart(30, 40));
    }
}
