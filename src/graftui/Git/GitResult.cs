namespace graftui.Git;

/// <summary>
/// Raw output of one git invocation. <see cref="Command"/> is the command as it reads on screen (arguments quoted where needed,
/// see <see cref="GitCommandLine"/>); git was started from <see cref="Args"/>.
/// </summary>
public sealed record GitResult(string Command, int ExitCode, string StdOut, string StdErr)
{
    /// <summary>The exit code when graftui stopped git for running longer than its timeout (like coreutils `timeout`).</summary>
    public const int TimedOutExitCode = 124;

    /// <summary>The exit code when graftui stopped a read because its token was cancelled (like Ctrl+C).</summary>
    public const int CancelledExitCode = 130;

    public bool Success => ExitCode == 0;

    /// <summary>True when graftui stopped git because it ran longer than its timeout (ExitCode is 124).</summary>
    public bool TimedOut { get; init; }

    /// <summary>True when graftui stopped a read because its token was cancelled (ExitCode is 130). Never set on writes.</summary>
    public bool Cancelled { get; init; }

    /// <summary>The arguments after "git", as they were run, before any tidying. Empty for results not made by GitClient.</summary>
    public IReadOnlyList<string> Args { get; init; } = [];
}
