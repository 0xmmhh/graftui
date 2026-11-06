using graftui.Ui.Views;

namespace graftui.Tests;

public class CaptionLayoutTests
{
    [Fact]
    public void Two_line_caption_at_80_by_24_leaves_one_commit_row()
    {
        // Pane height 16: the art is 8 rows, so the caption is rows 3 and 4 and the commit rows start at 6+8.
        Assert.Equal((2, 13, 14, 1), ArtPaneView.CaptionLayout(16, twoLines: true));
    }

    [Fact]
    public void One_line_caption_at_80_by_24_keeps_both_commit_rows()
    {
        Assert.Equal((1, 12, 13, 2), ArtPaneView.CaptionLayout(16, twoLines: false));
    }

    [Fact]
    public void Two_line_caption_falls_back_to_one_line_with_no_commit_row()
    {
        Assert.Equal((1, 12, 13, 0), ArtPaneView.CaptionLayout(14, twoLines: true));
    }

    [Fact]
    public void Two_line_caption_in_a_tall_pane_uses_the_tall_art_rows_and_every_commit_row_that_is_free()
    {
        // Pane height 24: the art is 11 rows, so the rule moves to 16 and the commit rows start at 17. No cap: all 6 rows are free.
        Assert.Equal((2, 16, 17, 6), ArtPaneView.CaptionLayout(24, twoLines: true));
    }

    [Fact]
    public void Compact_art_in_a_tall_pane_leaves_the_rule_and_the_commit_rows_two_rows_higher()
    {
        // Pane height 22 on Branches: 9 rows of art, so the rule is at 13 and 7 commit rows start at 14.
        Assert.Equal((1, 13, 14, 7), ArtPaneView.CaptionLayout(22, twoLines: false, compact: true));
    }

    [Fact]
    public void Compact_art_below_the_tall_threshold_uses_the_short_art()
    {
        Assert.Equal((1, 12, 13, 1), ArtPaneView.CaptionLayout(15, twoLines: false, compact: true));
    }

    [Fact]
    public void Single_line_art_gives_the_commit_list_four_more_rows()
    {
        // Pane height 22: the 9-row art leaves 7 commit rows, the 5-row single-line art leaves 11.
        Assert.Equal((1, 9, 10, 11), ArtPaneView.CaptionLayout(22, twoLines: false, compact: true, singleLine: true));
        Assert.Equal(7 + 4, ArtPaneView.CaptionLayout(22, twoLines: false, compact: true, singleLine: true).BelowRows);
    }

    [Fact]
    public void At_pane_height_15_the_single_line_art_leaves_four_commit_rows_and_the_caption_on_row_8()
    {
        // 64x22 terminal: the Branches pane is 15 rows tall. Single-line: 5 art rows, so the caption is on row 8 and 4 commit rows follow.
        var single = ArtPaneView.Rows(15, twoLines: false, compact: true, singleLine: true);

        Assert.Equal(5, single.ArtRows);
        Assert.Equal(8, single.CaptionRow);
        Assert.Equal(9, single.RuleRow);
        Assert.Equal(10, single.BelowTop);
        Assert.Equal(4, single.BelowRows);
    }

    [Fact]
    public void At_pane_height_15_the_normal_art_leaves_one_commit_row_and_the_caption_on_row_11()
    {
        var normal = ArtPaneView.Rows(15, twoLines: false, compact: true, singleLine: false);

        Assert.Equal(8, normal.ArtRows);
        Assert.Equal(11, normal.CaptionRow);
        Assert.Equal(12, normal.RuleRow);
        Assert.Equal(13, normal.BelowTop);
        Assert.Equal(1, normal.BelowRows);
    }

    [Fact]
    public void A_two_line_caption_moves_the_second_caption_row_down_and_takes_one_commit_row()
    {
        var two = ArtPaneView.Rows(22, twoLines: true, compact: true);

        Assert.Equal(2, two.CaptionRows);
        Assert.Equal(12, two.CaptionRow);
        Assert.Equal(6, two.BelowRows);
    }

    [Fact]
    public void Single_line_art_only_applies_on_the_branches_tab()
    {
        Assert.Equal(ArtPaneView.CaptionLayout(22, twoLines: false), ArtPaneView.CaptionLayout(22, twoLines: false, singleLine: true));
    }

    [Fact]
    public void Single_line_art_keeps_its_line_on_the_same_screen_row()
    {
        var normal = ArtPaneView.ArtArea(80, 22, compact: true);
        var single = ArtPaneView.ArtArea(80, 22, compact: true, singleLine: true);

        Assert.Equal(normal!.Value.Y, single!.Value.Y);
        Assert.Equal(5, single.Value.H);
    }

    [Fact]
    public void A_hidden_art_moves_the_caption_to_the_art_row_and_gives_the_rows_under_the_rule_its_space()
    {
        var hidden = ArtPaneView.Rows(15, twoLines: false, compact: true, singleLine: false, hidden: true);

        Assert.Equal(3, hidden.CaptionRow);
        Assert.Equal(0, hidden.ArtRows);
        Assert.Equal(9, hidden.BelowRows);
    }

    [Fact]
    public void The_todo_list_hides_the_art_only_on_a_short_pane()
    {
        Assert.True(ArtPaneView.ListNeedsWholePane(15, compact: true));
        Assert.False(ArtPaneView.ListNeedsWholePane(21, compact: true));
    }
}
