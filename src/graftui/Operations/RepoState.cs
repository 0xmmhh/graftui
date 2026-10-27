using graftui.Git;

namespace graftui.Operations;

/// <summary>Repo-level facts that more than one operation needs.</summary>
internal static class RepoState
{
    /// <summary>
    /// The operation that owns the index and is still in progress ("merge", "cherry-pick", "revert" or "rebase"), or null.
    /// <paramref name="call"/> runs a git command and returns its result.
    /// </summary>
    public static string? InProgress(Func<string[], GitResult> call, string gitDir)
    {
        foreach (var (name, kind) in new[] { ("MERGE_HEAD", "merge"), ("CHERRY_PICK_HEAD", "cherry-pick"), ("REVERT_HEAD", "revert") })
        {
            if (call(["rev-parse", "-q", "--verify", name]).Success)
                return kind;
        }
        if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply")))
            return "rebase";
        return null;
    }

    /// <summary>
    /// The git dir as a full path built from RepoPath, or null when git cannot say. The command is added to <paramref name="log"/>.
    /// It is not resolved through symlinks, so it stays comparable with RepoPath (see <see cref="ArgPath"/>).
    /// </summary>
    public static string? GitDir(GitClient git, List<GitResult> log)
    {
        var r = git.Run("rev-parse", "--git-dir");
        log.Add(r);
        var dir = r.StdOut.Trim();
        return r.Success && dir.Length > 0 ? Path.GetFullPath(Path.Combine(git.RepoPath, dir)) : null;
    }

    /// <summary>
    /// A file path to pass to git: relative to RepoPath when the file is inside the repo, otherwise the full path.
    /// A "../" path would be wrong whenever RepoPath and the file name the same folder through different symlinks (macOS /var).
    /// </summary>
    public static string ArgPath(GitClient git, string path)
    {
        var rel = Path.GetRelativePath(git.RepoPath, path);
        return rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : rel;
    }
}
