using graftui.Ui.Views;

namespace graftui.Tests;

/// <summary>
/// The todo list hides its art on a short pane (ArtPaneView.ListNeedsWholePane). The rule is checked here directly, by pane height,
/// because the screens the app accepts (64 by 22 and up) no longer reach the short heights through the window.
/// </summary>
public class ArtHideRuleTests
{
    [Fact]
    public void A_compact_pane_of_fifteen_rows_gives_the_whole_pane_to_the_todo_list()
    {
        Assert.True(ArtPaneView.ListNeedsWholePane(15, compact: true));
    }

    [Fact]
    public void A_compact_pane_of_nineteen_rows_keeps_the_art()
    {
        Assert.False(ArtPaneView.ListNeedsWholePane(19, compact: true));
    }
}
