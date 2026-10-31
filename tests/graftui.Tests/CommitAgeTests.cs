using graftui.Ui.Text;

namespace graftui.Tests;

public class CommitAgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static string AgeOf(TimeSpan before) => CommitAge.Short(Now - before, Now);

    [Fact]
    public void Under_a_minute_and_future_commits_are_now()
    {
        Assert.Equal("now", AgeOf(TimeSpan.FromSeconds(30)));
        Assert.Equal("now", AgeOf(TimeSpan.FromSeconds(59)));
        Assert.Equal("now", CommitAge.Short(Now.AddMinutes(5), Now));
    }

    [Fact]
    public void Minutes_and_hours_count_from_their_first_full_unit()
    {
        Assert.Equal("1m", AgeOf(TimeSpan.FromSeconds(60)));
        Assert.Equal("59m", AgeOf(TimeSpan.FromMinutes(59)));
        Assert.Equal("1h", AgeOf(TimeSpan.FromHours(1)));
        Assert.Equal("23h", AgeOf(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59)));
    }

    [Fact]
    public void Days_and_weeks_count_whole_days()
    {
        Assert.Equal("1d", AgeOf(TimeSpan.FromHours(24)));
        Assert.Equal("6d", AgeOf(TimeSpan.FromDays(6) + TimeSpan.FromHours(23)));
        Assert.Equal("1w", AgeOf(TimeSpan.FromDays(7)));
        Assert.Equal("4w", AgeOf(TimeSpan.FromDays(29)));
    }

    [Fact]
    public void Months_use_thirty_day_months_and_stop_at_eleven()
    {
        Assert.Equal("1mo", AgeOf(TimeSpan.FromDays(30)));
        Assert.Equal("11mo", AgeOf(TimeSpan.FromDays(359)));
    }

    [Fact]
    public void From_360_days_the_age_is_one_year_not_twelve_months()
    {
        Assert.Equal("1y", AgeOf(TimeSpan.FromDays(360)));
        Assert.Equal("1y", AgeOf(TimeSpan.FromDays(364)));
    }

    [Fact]
    public void Years_use_365_day_years()
    {
        Assert.Equal("1y", AgeOf(TimeSpan.FromDays(365)));
        Assert.Equal("1y", CommitAge.Short(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero), Now));
        Assert.Equal("2y", AgeOf(TimeSpan.FromDays(800)));
    }
}
