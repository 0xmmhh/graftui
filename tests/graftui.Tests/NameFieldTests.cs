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
    public void A_pane_with_no_commit_row_under_the_caption_has_no_name_box()
    {
        Assert.Null(ArtPaneView.NameField(48, 12));
    }

    [Fact]
    public void A_pane_too_narrow_for_the_art_has_no_name_box()
    {
        Assert.Null(ArtPaneView.NameField(10, 16));
    }
}
