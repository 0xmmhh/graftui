using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

public class DirectionLineTests
{
    [Fact]
    public void Browse_names_both_branches_with_their_distance()
    {
        var text = string.Concat(DirectionLine.Browse(Layouts.Diverged(), 80).Select(s => s.Text));

        Assert.Equal("feature  is 1 ahead, 1 behind  main (current)", text);
    }
}
