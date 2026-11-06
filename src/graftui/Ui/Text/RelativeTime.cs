namespace graftui.Ui.Text;

/// <summary>
/// How long ago a date was, as a short phrase for the end of the todo list: "just now", "5m ago", "2h ago", "3d ago", "4mo ago",
/// "1y ago". Pure: the caller passes the clock.
/// </summary>
public static class RelativeTime
{
    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;
    private const int DaysPerMonth = 30;
    private const int DaysPerYear = 365;
    private const int MonthsPerYear = 12;

    /// <summary>
    /// The age of <paramref name="date"/> at <paramref name="now"/>. Under a minute, and any date in the future (clock skew), is "just now".
    /// Days and months are whole counts (30-day months). Months stop at 11, so a date of 360 to 364 days old reads "1y ago", not "12mo ago".
    /// </summary>
    public static string Short(DateTimeOffset date, DateTimeOffset now)
    {
        var span = now - date;
        if (span < TimeSpan.FromMinutes(1))
            return "just now";
        if (span.TotalMinutes < MinutesPerHour)
            return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < HoursPerDay)
            return $"{(int)span.TotalHours}h ago";

        var days = (int)span.TotalDays;
        if (days < DaysPerMonth)
            return $"{days}d ago";

        var months = days / DaysPerMonth;
        if (months < MonthsPerYear)
            return $"{months}mo ago";
        return $"{Math.Max(1, days / DaysPerYear)}y ago";
    }
}
