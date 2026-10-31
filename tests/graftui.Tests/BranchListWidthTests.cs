using graftui.Ui.App;

namespace graftui.Tests;

public class BranchListWidthTests
{
    [Fact]
    public void An_offer_takes_effect_at_once_when_nothing_is_held()
    {
        var width = new BranchListWidth(20);

        width.Offer(26);

        Assert.Equal(26, width.Width);
        Assert.False(width.Held);
    }

    [Fact]
    public void A_held_width_ignores_offers_until_it_is_released()
    {
        var width = new BranchListWidth(20);
        width.Hold();

        width.Offer(26);

        Assert.True(width.Held);
        Assert.Equal(20, width.Width);
    }

    [Fact]
    public void Release_applies_the_latest_offer_and_reports_the_change()
    {
        var width = new BranchListWidth(20);
        width.Hold();
        width.Offer(24);
        width.Offer(26);

        var changed = width.Release();

        Assert.True(changed);
        Assert.Equal(26, width.Width);
        Assert.False(width.Held);
    }

    [Fact]
    public void Release_with_no_offer_keeps_the_width_and_reports_nothing()
    {
        var width = new BranchListWidth(20);
        width.Hold();

        Assert.False(width.Release());
        Assert.Equal(20, width.Width);
        Assert.False(width.Held);
    }

    [Fact]
    public void Release_of_an_offer_equal_to_the_width_reports_no_change()
    {
        var width = new BranchListWidth(20);
        width.Hold();
        width.Offer(20);

        Assert.False(width.Release());
        Assert.Equal(20, width.Width);
    }

    [Fact]
    public void Release_without_a_hold_does_nothing()
    {
        var width = new BranchListWidth(20);

        Assert.False(width.Release());
        Assert.Equal(20, width.Width);
    }

    [Fact]
    public void Offers_after_a_release_apply_again()
    {
        var width = new BranchListWidth(20);
        width.Hold();
        width.Offer(26);
        width.Release();

        width.Offer(30);

        Assert.Equal(30, width.Width);
    }
}
