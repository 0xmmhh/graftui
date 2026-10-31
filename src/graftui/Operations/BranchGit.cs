using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// The git reads and calls shared by the branch operations (switch, new, rename, delete). Each call is added to the log.
/// The reads throw when git fails, so an operation's Check or Run turns the throw into its refusal or failure.
/// </summary>
internal static class BranchGit
{
    public const string HeadsPrefix = "refs/heads/";

    /// <summary>`git status` in the form StatusParser reads, with untracked files listed one by one.</summary>
    public static readonly string[] StatusArgs = ["status", "--porcelain=v2", "-z", "--untracked-files=all"];

    public static GitResult Call(GitClient git, List<GitResult> log, params string[] args)
    {
        var r = git.Run(args);
        log.Add(r);
        return r;
    }

    /// <summary>The hash of a name, "" when the name does not exist (exit 1). Throws when git fails.</summary>
    public static string RefOrEmpty(GitClient git, List<GitResult> log, string name)
    {
        var r = Call(git, log, "rev-parse", "-q", "--verify", name);
        if (r.Success)
            return r.StdOut.Trim();
        if (r.ExitCode == 1)
            return "";
        throw new InvalidOperationException(GitClient.Describe(r));
    }

    /// <summary>The full name HEAD points at (refs/heads/x), "" when HEAD is detached. Throws when git fails.</summary>
    public static string SymbolicHead(GitClient git, List<GitResult> log)
    {
        // symbolic-ref exits 1 quietly on a detached HEAD.
        var r = Call(git, log, "symbolic-ref", "-q", "HEAD");
        if (r.Success)
            return r.StdOut.Trim();
        if (r.ExitCode == 1)
            return "";
        throw new InvalidOperationException(GitClient.Describe(r));
    }

    /// <summary>A count from `git rev-list --count`. Throws when git fails or prints no number.</summary>
    public static int Count(GitClient git, List<GitResult> log, params string[] args)
    {
        var r = Call(git, log, ["rev-list", "--count", .. args]);
        if (!r.Success || !int.TryParse(r.StdOut.Trim(), out var n))
            throw new InvalidOperationException(r.Success ? "could not count commits" : GitClient.Describe(r));
        return n;
    }

    /// <summary>True when <paramref name="ancestor"/> is reachable from <paramref name="descendant"/>. Throws on any git failure but exit 1.</summary>
    public static bool IsAncestor(GitClient git, List<GitResult> log, string ancestor, string descendant)
    {
        var r = Call(git, log, "merge-base", "--is-ancestor", ancestor, descendant);
        if (r.Success)
            return true;
        if (r.ExitCode == 1)
            return false;
        throw new InvalidOperationException(GitClient.Describe(r));
    }

    /// <summary>
    /// What blocks a branch change before it starts: no git dir, or another operation (merge, rebase...) owns the index.
    /// Null when nothing blocks it.
    /// </summary>
    public static string? Blocked(GitClient git, List<GitResult> log)
    {
        var gitDir = RepoState.GitDir(git, log);
        if (gitDir is null)
            return "could not find the git dir";
        var inProgress = RepoState.InProgress(args => Call(git, log, args), gitDir);
        return inProgress is null ? null : ChangeRefusals.InProgress(inProgress);
    }

    /// <summary>Every path the status lists: each row's path and the original path of a rename.</summary>
    public static IEnumerable<string> Paths(WorkingStatus status) =>
        status.Rows.SelectMany(f => new[] { f.Path, f.OrigPath }.OfType<string>());

    /// <summary>The first line of git's error text, or of its output, or the exit code when it printed nothing.</summary>
    public static string FirstLine(GitResult r)
    {
        var lines = r.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            lines = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault() ?? $"git exited with {r.ExitCode}";
    }

    /// <summary>A hash shortened to seven characters, as the screen shows it.</summary>
    public static string Short(string hash) => hash.Length > 7 ? hash[..7] : hash;

    /// <summary>A name that a branch command must not take: empty, or starting with '-' (git would read it as an option).</summary>
    public static bool IsUnsafeName(string name) => name.Length == 0 || name.StartsWith('-');
}
