using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

public class DirectionLineTests
{
    private static string Text(IEnumerable<Seg> segs) => string.Concat(segs.Select(s => s.Text));

    [Fact]
    public void Browse_names_both_branches_with_their_distance_and_no_current_label()
    {
        Assert.Equal("feature  is 1 ahead, 1 behind  main", Text(DirectionLine.Browse(Layouts.Diverged(), 80)));
    }

    [Fact]
    public void Browse_does_not_cut_a_name_that_fits_at_66_columns()
    {
        var layout = Layouts.Diverged() with { Selected = "feature/conflict" };

        var text = Text(DirectionLine.Browse(layout, 66));

        Assert.Equal("feature/conflict  is 1 ahead, 1 behind  main", text);
        Assert.DoesNotContain("…", text);
    }

    [Fact]
    public void Browse_cuts_only_the_long_name_and_keeps_the_short_one_whole()
    {
        var longName = "feature/" + new string('x', 90);
        var layout = Layouts.Diverged() with { Selected = longName };

        var segs = DirectionLine.Browse(layout, 66);

        Assert.True(TextFit.Columns(Text(segs)) <= 66);
        Assert.StartsWith("feature/", segs[0].Text);
        Assert.EndsWith("…", segs[0].Text);
        Assert.Equal("main", segs[^1].Text);
    }

    [Fact]
    public void Browse_labels_a_detached_head_and_keeps_the_label()
    {
        var layout = Layouts.Diverged() with { Current = "", Detached = true };

        var text = Text(DirectionLine.Browse(layout, 80));

        Assert.Equal("feature  is 1 ahead, 1 behind  HEAD (detached)", text);
        Assert.DoesNotContain("(current)", text);
    }

    [Fact]
    public void Merge_keeps_the_current_label_and_fits_both_names()
    {
        var text = Text(DirectionLine.Merge("feature/conflict", "main", false, 66));

        Assert.Equal("merge        feature/conflict  ──▶ into  main (current)", text);
    }
}
