using graftui.Ui.Text;
using graftui.Ui.Views;

namespace graftui.Tests;

/// <summary>The new look's tab row: its text and which tab is bright. Pure: the row's segments, no screen.</summary>
public class TabRowViewTests
{
    [Fact]
    public void The_row_names_both_tabs_with_a_bar_between_them()
    {
        var segs = TabRowView.Segs(false);

        Assert.Equal(new[] { "Branches", " ", "│", " ", "Changes" }, segs.Select(s => s.Text));
        Assert.Equal("Branches │ Changes", string.Concat(segs.Select(s => s.Text)));
        Assert.Equal(18, string.Concat(segs.Select(s => s.Text)).Length);
    }

    [Fact]
    public void With_branches_active_the_branches_name_is_bright_and_bold_and_the_changes_name_is_dim()
    {
        var segs = TabRowView.Segs(false);

        Assert.Equal(new Style(Ink.Fg, true), segs[0].Style);
        Assert.Equal(new Style(Ink.Dim), segs[4].Style);
        Assert.Equal(new Style(Ink.Frame), segs[2].Style);
    }

    [Fact]
    public void With_changes_active_the_two_names_swap_their_brightness()
    {
        var segs = TabRowView.Segs(true);

        Assert.Equal(new Style(Ink.Dim), segs[0].Style);
        Assert.Equal(new Style(Ink.Fg, true), segs[4].Style);
    }
}
