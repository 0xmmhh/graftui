namespace Graft.Git;

/// <summary>Raw output of one git invocation.</summary>
public sealed record GitResult(string Command, int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}
