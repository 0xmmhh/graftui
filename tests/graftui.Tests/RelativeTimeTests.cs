using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>The short age of a todo row: its words and where each unit starts.</summary>
public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static string AgeOf(TimeSpan before) => RelativeTime.Short(Now - before, Now);

    [Fact]
    public void Under_a_minute_and_future_dates_read_just_now()
    {
        Assert.Equal("just now", AgeOf(TimeSpan.Zero));
        Assert.Equal("just now", AgeOf(TimeSpan.FromSeconds(59)));
        Assert.Equal("just now", RelativeTime.Short(Now.AddMinutes(5), Now));
    }

    [Fact]
    public void Minutes_and_hours_count_from_their_first_full_unit()
    {
        Assert.Equal("1m ago", AgeOf(TimeSpan.FromMinutes(1)));
        Assert.Equal("59m ago", AgeOf(TimeSpan.FromMinutes(59)));
        Assert.Equal("1h ago", AgeOf(TimeSpan.FromHours(1)));
        Assert.Equal("23h ago", AgeOf(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59)));
    }

    [Fact]
    public void Days_count_whole_days_up_to_thirty()
    {
        Assert.Equal("1d ago", AgeOf(TimeSpan.FromHours(24)));
        Assert.Equal("3d ago", AgeOf(TimeSpan.FromDays(3) + TimeSpan.FromHours(5)));
        Assert.Equal("29d ago", AgeOf(TimeSpan.FromDays(29)));
    }

    [Fact]
    public void Months_use_thirty_day_months_and_stop_at_eleven()
    {
        Assert.Equal("1mo ago", AgeOf(TimeSpan.FromDays(30)));
        Assert.Equal("11mo ago", AgeOf(TimeSpan.FromDays(359)));
    }

    [Fact]
    public void From_360_days_the_age_is_one_year_not_twelve_months()
    {
        Assert.Equal("1y ago", AgeOf(TimeSpan.FromDays(360)));
        Assert.Equal("1y ago", AgeOf(TimeSpan.FromDays(364)));
    }

    [Fact]
    public void Years_use_365_day_years()
    {
        Assert.Equal("1y ago", AgeOf(TimeSpan.FromDays(365)));
        Assert.Equal("2y ago", AgeOf(TimeSpan.FromDays(800)));
    }
}
