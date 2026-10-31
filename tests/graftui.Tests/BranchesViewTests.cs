using graftui.Model;
using graftui.Ui.Views;

namespace graftui.Tests;

public class BranchesViewTests
{
    [Fact]
    public void WantedWidth_has_no_hash_column()
    {
        var branches = new[] { new Branch("main", "c111111", "c111111", true), new Branch("feature/conflict", "d222222", "d222222", false) };
        var counts = new Dictionary<string, (int Ahead, int Behind)> { ["feature/conflict"] = (1, 1) };

        // border 2 + mark and space 2 + longest name 16 + space 1 + "↑1↓1" 4
        Assert.Equal(25, BranchesView.WantedWidth(branches, counts));
    }

    [Fact]
    public void Ab_header_names_the_current_branch_and_is_cut_to_its_width()
    {
        Assert.Equal("vs main", BranchesView.AbHeader("main", 20));
        Assert.Equal("vs feature/…", BranchesView.AbHeader("feature/conflict", 12));
    }

    [Fact]
    public void Ab_header_is_left_out_when_there_is_no_room_for_even_a_short_one()
    {
        Assert.Equal("", BranchesView.AbHeader("main", 3));
    }

    [Fact]
    public void Current_name_is_the_checked_out_branch_and_HEAD_when_none_is()
    {
        var branches = new[] { new Branch("feature/x", "d222222", "d222222", false), new Branch("main", "c111111", "c111111", true) };

        Assert.Equal("main", BranchesView.CurrentName(branches));
        Assert.Equal("HEAD", BranchesView.CurrentName([new Branch("feature/x", "d222222", "d222222", false)]));
    }

    [Fact]
    public void Scroll_top_stays_at_the_first_branch_when_the_first_is_selected()
    {
        Assert.Equal(0, BranchesView.ScrollTop(sel: 0, top: 0, count: 10, listH: 3));
    }

    [Fact]
    public void Scroll_top_shows_the_last_branch_at_the_bottom_of_the_list()
    {
        Assert.Equal(7, BranchesView.ScrollTop(sel: 9, top: 0, count: 10, listH: 3));
    }

    [Fact]
    public void Scroll_top_with_one_row_shows_only_the_selected_branch()
    {
        Assert.Equal(4, BranchesView.ScrollTop(sel: 4, top: 0, count: 10, listH: 1));
    }

    [Fact]
    public void A_list_shorter_than_the_pane_never_scrolls()
    {
        Assert.Equal(0, BranchesView.ScrollTop(sel: 2, top: 0, count: 3, listH: 10));
        Assert.Equal(0, BranchesView.ScrollTop(sel: 2, top: 2, count: 3, listH: 10));
    }

    [Fact]
    public void A_jump_from_the_first_to_the_last_branch_scrolls_to_the_bottom()
    {
        Assert.Equal(15, BranchesView.ScrollTop(sel: 19, top: 0, count: 20, listH: 5));
    }

    [Fact]
    public void A_jump_back_to_the_first_branch_scrolls_to_the_top()
    {
        Assert.Equal(0, BranchesView.ScrollTop(sel: 0, top: 15, count: 20, listH: 5));
    }

    [Fact]
    public void The_header_ends_on_the_last_inner_column()
    {
        var x = BranchesView.HeaderX(innerW: 20, headerCols: 6);

        Assert.Equal(14, x);
        Assert.Equal(19, x + 6 - 1);
    }

    [Fact]
    public void Has_counts_is_true_only_when_a_branch_other_than_the_current_one_has_counts()
    {
        var branches = new[] { new Branch("feature/x", "d222222", "d222222", false), new Branch("main", "c111111", "c111111", true) };

        Assert.True(BranchesView.HasCounts(branches, new Dictionary<string, (int Ahead, int Behind)> { ["feature/x"] = (1, 0) }));
        Assert.False(BranchesView.HasCounts(branches, new Dictionary<string, (int Ahead, int Behind)> { ["main"] = (0, 0) }));
        Assert.False(BranchesView.HasCounts(branches, new Dictionary<string, (int Ahead, int Behind)>()));
    }
}
