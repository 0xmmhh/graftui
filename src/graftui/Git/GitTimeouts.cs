using System.Globalization;

namespace graftui.Git;

/// <summary>
/// How long one git process may run. <see cref="Read"/> applies to <see cref="GitClient.RunRead"/>; <see cref="Write"/> applies to every
/// other call (Run, RunWithEnv, RunLiteral, RunWithIndex). A process that runs longer is killed together with its children.
/// </summary>
public sealed record GitTimeouts(TimeSpan Read, TimeSpan Write)
{
    public static readonly GitTimeouts Default = new(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(10));

    /// <summary>
    /// A short label for a time: whole minutes ("10 min") when it is a whole number of minutes, at least one; else whole seconds
    /// ("90 s") when it is a whole number of seconds, at least one; else milliseconds ("300 ms").
    /// </summary>
    internal static string Describe(TimeSpan time)
    {
        var ms = (long)time.TotalMilliseconds;
        if (ms >= 60_000 && ms % 60_000 == 0)
            return (ms / 60_000).ToString(CultureInfo.InvariantCulture) + " min";
        if (ms >= 1_000 && ms % 1_000 == 0)
            return (ms / 1_000).ToString(CultureInfo.InvariantCulture) + " s";
        return ms.ToString(CultureInfo.InvariantCulture) + " ms";
    }
}
