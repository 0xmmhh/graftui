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
}
