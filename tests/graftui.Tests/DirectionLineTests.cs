using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

public class DirectionLineTests
{
    private static string Text(IEnumerable<Seg> segs) => string.Concat(segs.Select(s => s.Text));

    [Fact]
    public void Browse_names_both_branches_with_their_distance_and_no_current_label()
    {
        Assert.Equal("feature  is 1 ahead of, 1 behind  main", Text(DirectionLine.Browse(Layouts.Diverged(), 80)));
    }

    [Fact]
    public void Browse_does_not_cut_a_name_that_fits_at_66_columns()
    {
        var layout = Layouts.Diverged() with { Selected = "feature/conflict" };

        var text = Text(DirectionLine.Browse(layout, 66));

        Assert.Equal("feature/conflict  is 1 ahead of, 1 behind  main", text);
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
    public void Browse_keeps_the_long_wording_when_both_names_fit()
    {
        var layout = Layouts.Diverged() with { Selected = "feature/ff-to-merge", Current = "feature/conflict", Ahead = 4, Behind = 5 };

        Assert.Equal("feature/ff-to-merge  is 4 ahead of, 5 behind  feature/conflict", Text(DirectionLine.Browse(layout, 80)));
    }

    [Fact]
    public void Browse_uses_the_narrow_form_when_the_names_do_not_fit_beside_the_long_wording()
    {
        var layout = Layouts.Diverged() with { Selected = "feature/ff-to-merge", Current = "feature/conflict", Ahead = 4, Behind = 5 };

        var text = Text(DirectionLine.Browse(layout, 36));

        Assert.Equal("feature/…  ↑4↓5 vs  feature/conflict", text);
        Assert.True(TextFit.Columns(text) <= 36);
    }

    [Fact]
    public void Browse_draws_each_name_in_its_branch_lane()
    {
        var layout = Layouts.Diverged() with { CurrentLane = 0, SelectedLane = 3 };

        var segs = DirectionLine.Browse(layout, 80);

        Assert.Equal(new Style(Ink.Lane3, true), segs[0].Style);
        Assert.Equal(new Style(Ink.Lane0, true), segs[2].Style);
    }

    [Fact]
    public void Browse_of_the_current_branch_alone_uses_its_lane()
    {
        var layout = Layouts.Diverged() with { Selected = "main", SelectedLane = 0, CurrentLane = 0, Kind = GraphKind.Same };

        Assert.Equal(new Style(Ink.Lane0, true), DirectionLine.Browse(layout, 80)[0].Style);
    }

    [Fact]
    public void Loading_names_the_branch_in_its_lane()
    {
        var segs = DirectionLine.Loading("feature", Ink.Lane3);

        Assert.Equal("feature  …", Text(segs));
        Assert.Equal(new Style(Ink.Lane3, true), segs[0].Style);
    }

    [Fact]
    public void Browse_labels_a_detached_head_and_keeps_the_label()
    {
        var layout = Layouts.Diverged() with { Current = "", Detached = true };

        var text = Text(DirectionLine.Browse(layout, 80));

        Assert.Equal("feature  is 1 ahead of, 1 behind  HEAD (detached)", text);
        Assert.DoesNotContain("(current)", text);
    }

    [Fact]
    public void Merge_keeps_the_current_label_and_fits_both_names()
    {
        var text = Text(DirectionLine.Merge("feature/conflict", Ink.Lane3, "main", Ink.Lane0, false, 66));

        Assert.Equal("merge        feature/conflict  -> into  main (current)", text);
    }

    [Fact]
    public void Merge_names_each_branch_in_its_own_lane_and_a_detached_head_in_the_foreground()
    {
        var segs = DirectionLine.Merge("feature", Ink.Lane3, "main", Ink.Lane0, false, 80);
        Assert.Equal(new Style(Ink.Lane3, true), segs[1].Style);
        Assert.Equal(new Style(Ink.Lane0, true), segs[3].Style);

        var detached = DirectionLine.Merge("feature", Ink.Lane3, "", Ink.Fg, true, 80);
        Assert.Equal("merge        feature  -> into  HEAD (detached)", Text(detached));
        Assert.Equal(new Style(Ink.Fg, true), detached[3].Style);
    }

    [Fact]
    public void Switch_names_both_branches_at_44_columns()
    {
        var segs = DirectionLine.Switch("main", Ink.Lane0, "feature/fast-forward", Ink.Lane5, 44);

        Assert.Equal("switch       main  ->  feature/fast-forward", Text(segs));
        Assert.Equal(new Style(Ink.Lane0, true), segs[1].Style);
        Assert.Equal(new Style(Ink.Lane5, true), segs[3].Style);
    }

    [Fact]
    public void Switch_cuts_the_long_name_and_keeps_the_fixed_parts_at_30_columns()
    {
        var segs = DirectionLine.Switch("feature/fast-forward", Ink.Lane5, "main", Ink.Lane0, 29);

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
        var segs = DirectionLine.Rename("feature/picks", Ink.Lane4, null, 44);
        Assert.Equal("rename       feature/picks", Text(segs));
        Assert.Equal(new Style(Ink.Lane4, true), segs[1].Style);
    }

    [Fact]
    public void Rename_names_both_at_44_columns_and_cuts_the_new_one()
    {
        var segs = DirectionLine.Rename("feature/picks", Ink.Lane4, "feature/cherries", 43);
        var text = Text(segs);

        Assert.Equal("rename       feature/picks  ->  feature/ch…", text);
        Assert.True(TextFit.Columns(text) <= 44);
        Assert.Equal(new Style(Ink.Lane4, true), segs[1].Style);
        Assert.Equal(new Style(Ink.Ok, true), segs[3].Style);
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
        var text = Text(DirectionLine.StashMerge("feature/fast-forward", Ink.Lane5, "main", Ink.Lane0, 66));

        Assert.Equal("stash, merge feature/fast-forward  -> into  main (current)", text);
        Assert.Equal(new Style(Ink.Dim), DirectionLine.StashMerge("a", Ink.Lane3, "main", Ink.Lane0, 66)[0].Style);
    }

    [Fact]
    public void Stash_merge_cuts_both_names_at_20_columns_without_losing_the_verb()
    {
        var text = Text(DirectionLine.StashMerge("feature/fast-forward", Ink.Lane5, "main", Ink.Lane0, 20));

        Assert.StartsWith("stash, merge ", text);
        Assert.Contains("…", text);
    }

    [Fact]
    public void Rebase_names_the_current_branch_then_the_onto_branch_at_80_columns()
    {
        var text = Text(DirectionLine.Rebase("feature/picks", Ink.Lane1, "main", Ink.Lane0, 80));

        Assert.Equal("rebase       feature/picks (current)  -> onto  main", text);
    }

    [Fact]
    public void Rebase_cuts_the_long_names_to_fit_the_width()
    {
        var longName = new string('x', 60);

        var segs = DirectionLine.Rebase(longName, Ink.Lane1, "main", Ink.Lane0, 60);
        var text = Text(segs);

        Assert.True(TextFit.Columns(text) <= 60, text);
        Assert.StartsWith("rebase       ", text);
        Assert.EndsWith("  -> onto  main", text);
        Assert.Contains("…", text);
    }

    [Fact]
    public void Rebase_colours_each_name_with_its_branch_lane()
    {
        var segs = DirectionLine.Rebase("feature/picks", Ink.Lane1, "main", Ink.Lane0, 80);

        Assert.Equal(new Style(Ink.Lane1, true), segs.Single(s => s.Text == "feature/picks").Style);
        Assert.Equal(new Style(Ink.Lane0, true), segs.Single(s => s.Text == "main").Style);
    }

    [Fact]
    public void A_tail_is_added_when_it_fits()
    {
        var line = DirectionLine.Rebase("feature/picks", Ink.Lane1, "main", Ink.Lane0, 80);

        var text = Text(DirectionLine.WithTail(line, "   todo", 80));

        Assert.EndsWith("main   todo", text);
    }

    [Fact]
    public void A_tail_that_does_not_fit_is_dropped()
    {
        var line = DirectionLine.Rebase("feature/picks", Ink.Lane1, "main", Ink.Lane0, 80);
        var width = TextFit.Columns(Text(line));

        Assert.Equal(Text(line), Text(DirectionLine.WithTail(line, "   todo", width)));
    }
}
