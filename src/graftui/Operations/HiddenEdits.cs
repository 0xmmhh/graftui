using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Tracked files flagged assume-unchanged whose work-tree file differs from the index. git status does not list them,
/// and reset --hard overwrites them.
/// </summary>
internal static class HiddenEdits
{
    /// <summary>The paths, in git's order. Throws InvalidOperationException when git fails.</summary>
    public static IReadOnlyList<string> Find(GitClient git, List<GitResult> log)
    {
        // `ls-files -v` prints "<tag> <path>". The tag is a lowercase 'h' when git was told to ignore the file's
        // changes (assume-unchanged). A skip-worktree file is 'S', or 's' when it also carries assume-unchanged,
        // and those files are not hidden edits here.
        // With no flagged entries this is the only git call, so the normal case stays cheap.
        var flagged = Call(git, log, "ls-files", "-v", "-z");
        if (!flagged.Success)
            throw new InvalidOperationException(GitClient.Describe(flagged));
        var hidden = new List<string>();
        var records = flagged.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        foreach (var record in records)
        {
            if (record.Length < 3 || record[0] != 'h')
                continue;
            var path = record[2..];
            if (path.Length > 0 && Differs(git, log, path))
                hidden.Add(path);
        }
        return hidden;
    }

    /// <summary>True when the work tree's copy of the flagged path is not what the index holds.</summary>
    private static bool Differs(GitClient git, List<GitResult> log, string path)
    {
        var full = Path.Combine(git.RepoPath, path);
        // A missing file does not count: a reset only brings it back.
        if (!SequenceGit.InWorkTree(full))
            return false;
        var index = Literal(git, log, "ls-files", "-s", "-z", "--", path);
        if (!index.Success)
            throw new InvalidOperationException(GitClient.Describe(index));
        // Each record is "<mode> <blob> <stage>", then the path; the mode and the blob are needed.
        var parts = index.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return true;
        var fields = parts[0].Split(' ');
        if (fields.Length < 2)
            return true;
        // A tracked link is stored as its target text: the link is compared by its target, so one that points
        // where the index says is not an edit, and a re-pointed one is.
        if (fields[0] == "120000")
            return LinkDiffers(git, log, full, fields[1]);
        // Anything else that is not a plain file (a folder where the index has a file, a link) counts as an edit.
        if (!SequenceGit.HoldsFile(full))
            return true;
        var work = Literal(git, log, "hash-object", "--", path);
        if (!work.Success)
            throw new InvalidOperationException(GitClient.Describe(work));
        return work.StdOut.Trim() != fields[1];
    }

    /// <summary>
    /// True when the link at a tracked link's path does not point at the target the index holds. A path that is
    /// not a link at all (a folder, a plain file) counts, because a reset would overwrite what is there.
    /// </summary>
    private static bool LinkDiffers(GitClient git, List<GitResult> log, string full, string blob)
    {
        var target = new FileInfo(full).LinkTarget;
        if (target is null)
            return true;
        var stored = Call(git, log, "cat-file", "blob", blob);
        if (!stored.Success)
            throw new InvalidOperationException(GitClient.Describe(stored));
        return stored.StdOut != target;
    }

    private static GitResult Call(GitClient git, List<GitResult> log, params string[] args)
    {
        var r = git.Run(args);
        log.Add(r);
        return r;
    }

    /// <summary>Like <see cref="Call"/>, with literal pathspecs: a path named like an option or a pathspec prefix stays a path.</summary>
    private static GitResult Literal(GitClient git, List<GitResult> log, params string[] args)
    {
        var r = git.RunLiteral(args);
        log.Add(r);
        return r;
    }
}
