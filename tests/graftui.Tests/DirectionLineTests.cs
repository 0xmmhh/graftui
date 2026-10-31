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

        Assert.Equal("merge        feature/conflict  -> into  main (current)", text);
    }

    [Fact]
    public void Switch_names_both_branches_at_44_columns()
    {
        var segs = DirectionLine.Switch("main", "feature/fast-forward", 44);

        Assert.Equal("switch       main  ->  feature/fast-forward", Text(segs));
        Assert.Equal(new Style(Ink.Cur, true), segs[1].Style);
        Assert.Equal(new Style(Ink.Inc, true), segs[3].Style);
    }

    [Fact]
    public void Switch_cuts_the_long_name_and_keeps_the_fixed_parts_at_30_columns()
    {
        var segs = DirectionLine.Switch("feature/fast-forward", "main", 29);

        Assert.Equal("switch       featu…  ->  main", Text(segs));
        Assert.True(TextFit.Columns(Text(segs)) <= 30);
    }

    [Fact]
    public void New_branch_shows_the_name_and_the_tip_at_44_columns()
    {
        var segs = DirectionLine.NewBranch("topic", "abc1234", 44);

        Assert.Equal("new branch   topic  at abc1234", Text(segs));
        Assert.Equal(new Style(Ink.Hash), segs[3].Style);
    }

    [Fact]
    public void New_branch_names_the_branch_once_and_the_name_segment_is_the_only_copy()
    {
        var segs = DirectionLine.NewBranch("topic", "abc1234", 80);

        Assert.Equal(4, segs.Count);
        Assert.Equal("topic", segs[1].Text);
        Assert.Equal(1, Text(segs).Split("topic").Length - 1);
    }

    [Fact]
    public void New_branch_with_no_name_yet_leaves_the_name_empty()
    {
        Assert.Equal("new branch     at abc1234", Text(DirectionLine.NewBranch("", "abc1234", 44)));
    }

    [Fact]
    public void Rename_shows_the_old_name_alone_while_the_box_is_open()
    {
        Assert.Equal("rename       feature/picks", Text(DirectionLine.Rename("feature/picks", null, 44)));
    }

    [Fact]
    public void Rename_names_both_at_44_columns_and_cuts_the_new_one()
    {
        var text = Text(DirectionLine.Rename("feature/picks", "feature/cherries", 43));

        Assert.Equal("rename       feature/picks  ->  feature/ch…", text);
        Assert.True(TextFit.Columns(text) <= 44);
    }

    [Fact]
    public void Delete_names_the_branch_and_cuts_it_at_20_columns()
    {
        Assert.Equal("delete       feature/conflict", Text(DirectionLine.Delete("feature/conflict", 44)));
        var cut = Text(DirectionLine.Delete("feature/" + new string('x', 40), 20));

        Assert.StartsWith("delete       ", cut);
        Assert.EndsWith("…", cut);
    }

    [Fact]
    public void Stash_merge_uses_the_thirteen_column_verb_and_the_into_line()
    {
        var text = Text(DirectionLine.StashMerge("feature/fast-forward", "main", 66));

        Assert.Equal("stash, merge feature/fast-forward  -> into  main (current)", text);
        Assert.Equal(new Style(Ink.Dim), DirectionLine.StashMerge("a", "main", 66)[0].Style);
    }

    [Fact]
    public void Stash_merge_cuts_both_names_at_20_columns_without_losing_the_verb()
    {
        var text = Text(DirectionLine.StashMerge("feature/fast-forward", "main", 20));

        Assert.StartsWith("stash, merge ", text);
        Assert.Contains("…", text);
    }
}
