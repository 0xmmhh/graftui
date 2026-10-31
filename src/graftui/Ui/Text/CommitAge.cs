namespace graftui.Ui.Text;

/// <summary>
/// How long ago a commit was, in a few columns: "now", "5m", "2h", "3d", "2w", "4mo", "1y". Pure: the caller passes the clock.
/// </summary>
public static class CommitAge
{
    private const int SecondsPerMinute = 60;
    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;
    private const int DaysPerWeek = 7;
    private const int DaysPerMonth = 30;
    private const int DaysPerYear = 365;

    /// <summary>
    /// The short age of a commit at <paramref name="now"/>. Under a minute, and any commit in the future, is "now".
    /// Days, weeks, months and years are whole counts (30-day months, 365-day years).
    /// </summary>
    public static string Short(DateTimeOffset when, DateTimeOffset now)
    {
        var span = now - when;
        if (span.TotalSeconds < SecondsPerMinute)
            return "now";
        if (span.TotalMinutes < MinutesPerHour)
            return $"{(int)span.TotalMinutes}m";
        if (span.TotalHours < HoursPerDay)
            return $"{(int)span.TotalHours}h";

        var days = (int)span.TotalDays;
        if (days < DaysPerWeek)
            return $"{days}d";
        if (days < DaysPerMonth)
            return $"{days / DaysPerWeek}w";
        if (days < DaysPerYear)
            return $"{days / DaysPerMonth}mo";
        return $"{days / DaysPerYear}y";
    }
}
