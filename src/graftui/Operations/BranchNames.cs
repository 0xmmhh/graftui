using graftui.Git;

namespace graftui.Operations;

/// <summary>The check on a typed branch name, shared by create and rename (and their Check runs).</summary>
internal static class BranchNames
{
    /// <summary>
    /// Null when <paramref name="name"/> is a valid new branch name that is not taken; otherwise the refusal.
    /// allowExisting is the name that may already exist (null = none). Every git call made is added to <paramref name="log"/>.
    /// </summary>
    public static string? Problem(GitClient git, string name, string? allowExisting, List<GitResult> log)
    {
        // Refused before git runs: an empty name, surrounding spaces, "@" (which git accepts as a branch name), or a leading '-'.
        if (name.Length == 0 || name.Trim() != name || name == "@" || name.StartsWith('-'))
            return BranchRefusals.InvalidName;

        // check-ref-format exits 128 for an invalid name. It prints the name back when it is valid, but `@{-1}` also
        // exits 0 and prints the previous branch's name, so the printed text must equal the typed one.
        var format = git.Run("check-ref-format", "--branch", name);
        log.Add(format);
        if (!format.Success || format.StdOut.Trim() != name)
            return BranchRefusals.InvalidName;

        var taken = git.Run(GitArgs.RevParse, "-q", GitArgs.Verify, BranchGit.HeadsPrefix + name);
        log.Add(taken);
        if (taken.Success)
            return name == allowExisting ? null : BranchRefusals.Exists(name);
        if (taken.ExitCode == 1)
            return null;
        return GitClient.Describe(taken);
    }
}
