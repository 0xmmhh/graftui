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
}
