using graftui.Model;

namespace graftui.Tests;

public class GraphLayoutTests
{
    [Fact]
    public void Minimal_layout_is_diverged_and_stubs_tips_to_seven_characters()
    {
        var layout = GraphLayout.Minimal("main", false, "feature", "c111111111", "d222222222");

        Assert.Equal(GraphKind.Diverged, layout.Kind);
        Assert.Equal("main", layout.Current);
        Assert.Equal("feature", layout.Selected);
        Assert.Equal("c111111", layout.CurrentTipCommit.ShortHash);
        Assert.Equal("d222222", layout.SelectedTipCommit.ShortHash);
    }
}
