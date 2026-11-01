using graftui.Model;
using graftui.Ui.Text;
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

    private static readonly IReadOnlyDictionary<string, (int Ahead, int Behind)> NoCounts =
        new Dictionary<string, (int Ahead, int Behind)>();

    [Fact]
    public void The_branch_name_on_the_top_bar_is_bold_in_the_current_branch_lane()
    {
        var branches = new[] { new Branch("feature/conflict", "h", "h", false), new Branch("main", "h", "h", true) };
        var snap = new RepoSnapshot("main", "h", true, branches, NoCounts);

        Assert.Equal(new Style(Ink.Lane0, true), StatusBarView.BranchStyle(snap));
    }

    [Fact]
    public void A_detached_head_on_the_top_bar_is_bold_in_the_foreground()
    {
        var branches = new[] { new Branch("main", "h", "h", false) };
        var snap = new RepoSnapshot("", "h", true, branches, NoCounts);

        Assert.Equal(new Style(Ink.Fg, true), StatusBarView.BranchStyle(snap));
    }
}
