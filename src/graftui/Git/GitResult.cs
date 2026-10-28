namespace graftui.Git;

/// <summary>Raw output of one git invocation.</summary>
public sealed record GitResult(string Command, int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;

    /// <summary>The arguments after "git", as they were run, before any tidying. Empty for results not made by GitClient.</summary>
    public IReadOnlyList<string> Args { get; init; } = [];
}
