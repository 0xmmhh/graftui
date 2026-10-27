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
    public void Two_line_caption_in_a_tall_pane_uses_the_tall_art_rows()
    {
        // Pane height 24: the art is 11 rows, so the rule moves to 16 and the commit rows start at 17.
        Assert.Equal((2, 16, 17, 3), ArtPaneView.CaptionLayout(24, twoLines: true));
    }
}
