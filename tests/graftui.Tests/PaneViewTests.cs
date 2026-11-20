using graftui.Ui.Views;

namespace graftui.Tests;

/// <summary>The frame's corners: rounded for the old look, square for the new one. Pure: no screen is needed.</summary>
public class PaneViewTests
{
    [Fact]
    public void The_old_look_has_rounded_corners()
    {
        var corners = PaneView.Corners(false);

        Assert.Equal(('╭', '╮', '╰', '╯'), corners);
    }

    [Fact]
    public void The_new_look_has_square_corners()
    {
        var corners = PaneView.Corners(true);

        Assert.Equal(('┌', '┐', '└', '┘'), corners);
    }
}
