using graftui.Ui.Views;

namespace graftui.Tests;

/// <summary>Where the Branches tab's name box goes in the art pane, at the two sizes the screen supports.</summary>
public class NameFieldTests
{
    [Fact]
    public void At_80_by_24_the_box_has_a_label_row_and_a_field_row_under_the_caption()
    {
        // The 80x24 pane is 48x16: the caption is one line, and the commit rows below it are two.
        var field = ArtPaneView.NameField(48, 16);

        Assert.Equal((13, 14, 33, true), field);
    }

    [Fact]
    public void At_64_by_22_the_box_is_the_field_row_only()
    {
        // The 64x22 pane is 40x15: one commit row under the caption, so there is no room for a label.
        var field = ArtPaneView.NameField(40, 15);

        Assert.Equal((13, 13, 25, false), field);
    }

    [Fact]
    public void The_name_box_on_the_branches_tab_sits_under_the_nine_row_art()
    {
        Assert.Equal((13, 15, 65, true), ArtPaneView.NameField(80, 22, compact: true));
    }

    [Fact]
    public void A_pane_with_no_commit_row_under_the_caption_has_no_name_box()
    {
        Assert.Null(ArtPaneView.NameField(48, 12));
    }

    [Fact]
    public void A_pane_too_narrow_for_the_art_has_no_name_box()
    {
        Assert.Null(ArtPaneView.NameField(10, 16));
    }

    /// <summary>
    /// A refusal that does not fit one line takes two when the rows under it still hold a label and the field (100 by 30, a 70 by 21 pane).
    /// The box then sits one row lower.
    /// </summary>
    [Fact]
    public void A_refusal_that_wraps_takes_two_caption_rows_and_moves_the_box_down_when_the_label_and_field_still_fit()
    {
        const string refusal = "Not a valid branch name: use letters, digits and - _ . / with no spaces";

        Assert.True(ArtPaneView.TwoLineCaption(refusal, 70, 21, keepsBelowRows: true));
        Assert.Equal((13, 17, 55, true), ArtPaneView.NameField(70, 21));
        Assert.Equal((13, 18, 55, true), ArtPaneView.NameField(70, 21, captionTwoLines: true));
    }

    [Fact]
    public void A_refusal_that_wraps_is_cut_to_one_line_where_the_box_would_lose_a_row()
    {
        const string refusal = "Not a valid branch name: use letters, digits and - _ . / with no spaces";

        // 64 by 22 (one commit row under the caption) and 80 by 24 (two): a second caption line would take the field's row.
        Assert.False(ArtPaneView.TwoLineCaption(refusal, 40, 15, keepsBelowRows: true));
        Assert.False(ArtPaneView.TwoLineCaption(refusal, 48, 16, keepsBelowRows: true));
    }

    [Fact]
    public void Without_the_name_box_a_caption_wraps_wherever_it_does_not_fit()
    {
        const string refusal = "Not a valid branch name: use letters, digits and - _ . / with no spaces";

        Assert.True(ArtPaneView.TwoLineCaption(refusal, 40, 15, keepsBelowRows: false));
        Assert.False(ArtPaneView.TwoLineCaption("Enter creates it · Esc cancels", 40, 15, keepsBelowRows: true));
    }
}
