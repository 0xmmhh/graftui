using System.Text;
using graftui.Git;

namespace graftui.Operations;

/// <summary>The two trees that describe the work in progress: the index, and every tracked and untracked file (ignored files left out).</summary>
internal sealed record WorkSnapshot(string IndexTree, string AllTree);

/// <summary>
/// Captures and restores the index and the working tree.
/// Take reads both through a temporary index file, so the real index is not changed.
/// Save stores a snapshot as a commit: its tree is AllTree, its parents are HEAD and an index commit whose tree is IndexTree.
/// RestoreTo is destructive: it resets tracked files to HEAD and then writes the snapshot over them. Callers must have saved
/// the current state (or checked that it is the expected one) before they call it.
/// </summary>
internal sealed class WorkState(GitClient git, string gitDir)
{
    public const string TempIndex = "graftui-index.tmp", PathsFile = "GRAFTUI_PATHS";

    private const int CleanBatch = 200;

    private readonly string _tempIndex = Path.Combine(gitDir, TempIndex);
    private readonly string _pathsFile = Path.Combine(gitDir, PathsFile);

    /// <summary>The current snapshot, or null when any git call fails (for example on unmerged entries).</summary>
    public WorkSnapshot? Take(List<GitResult> log)
    {
        try
        {
            // The index tree fails on unmerged entries; callers refuse conflicts before they get here.
            var indexTree = Hash(Call(log, "write-tree"));
            if (indexTree is null)
                return null;

            // Copying the real index keeps its stat data, so only files that changed are hashed again.
            var indexPath = Call(log, GitArgs.RevParse, "--git-path", "index");
            if (!indexPath.Success)
                return null;
            var realIndex = Path.Combine(git.RepoPath, indexPath.StdOut.Trim());

            TryDelete(_tempIndex);
            if (File.Exists(realIndex))
                File.Copy(realIndex, _tempIndex, overwrite: true);

            var add = CallWithIndex(log, "add", "-A", "--", ".");
            if (!add.Success)
                return null;
            var allTree = Hash(CallWithIndex(log, "write-tree"));
            return allTree is null ? null : new WorkSnapshot(indexTree, allTree);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            TryDelete(_tempIndex);
        }
    }

    /// <summary>
    /// The tree of one path's state: its index entry with its work-tree file (tracked), or the file alone (untracked).
    /// Built in the temporary index, so the real index is not changed. Null when git fails.
    /// </summary>
    public string? PathTree(string path, bool untracked, List<GitResult> log)
    {
        try
        {
            TryDelete(_tempIndex);
            GitResult add;
            if (untracked)
                add = CallWithIndex(log, "add", "--", path);
            else
            {
                // Copying the real index keeps its stat data; only the path is read from the disk.
                var real = Path.Combine(gitDir, "index");
                if (File.Exists(real))
                    File.Copy(real, _tempIndex, overwrite: true);
                add = CallWithIndex(log, "add", "-A", "--", path);
            }
            return add.Success ? Hash(CallWithIndex(log, "write-tree")) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            TryDelete(_tempIndex);
        }
    }

    /// <summary>Stores a snapshot as a commit and returns its hash, or null when git fails.</summary>
    public string? Save(WorkSnapshot s, string head, List<GitResult> log)
    {
        var indexCommit = Hash(Call(log, GitArgs.CommitTree, GitArgs.NoGpgSign, s.IndexTree, "-p", head, "-m", "graftui: index"));
        if (indexCommit is null)
            return null;
        return Hash(Call(log, GitArgs.CommitTree, GitArgs.NoGpgSign, s.AllTree, "-p", head, "-p", indexCommit, "-m", "graftui: working tree"));
    }

    /// <summary>The snapshot stored by <see cref="Save"/>, or null when the commit lacks the expected trees.</summary>
    public WorkSnapshot? Load(string commit, List<GitResult> log)
    {
        var allTree = Hash(Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, commit + "^{tree}"));
        var indexTree = Hash(Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, commit + "^2^{tree}"));
        return allTree is null || indexTree is null ? null : new WorkSnapshot(indexTree, allTree);
    }

    /// <summary>
    /// Puts the working tree and the index back to <paramref name="target"/>, which was saved as <paramref name="targetCommit"/>.
    /// <paramref name="removable"/> lists untracked paths that may be deleted when the snapshot does not have them.
    /// Returns null when the state matches the target afterwards, otherwise what went wrong.
    /// </summary>
    public string? RestoreTo(WorkSnapshot target, string targetCommit, IReadOnlySet<string> removable, List<GitResult> log)
    {
        try
        {
            // Tracked files go back to HEAD; this also clears unmerged index entries.
            var reset = Call(log, "reset", "-q", "--hard", "HEAD");
            if (!reset.Success)
                return Problem("could not reset to HEAD", reset);

            // Files that differ between HEAD and the snapshot are written from the snapshot. A file the snapshot lacks is deleted.
            var diff = Call(log, "diff", "--name-only", "-z", "--no-renames", "HEAD", targetCommit);
            if (!diff.Success)
                return Problem("could not list the changed files", diff);
            if (diff.StdOut.Length > 0)
            {
                File.WriteAllBytes(_pathsFile, new UTF8Encoding(false).GetBytes(diff.StdOut));
                var restore = CallLiteral(log, "restore", "-q", GitArgs.SourcePrefix + targetCommit, "--worktree",
                    "--pathspec-from-file=" + RepoState.ArgPath(git, _pathsFile), "--pathspec-file-nul");
                if (!restore.Success)
                    return Problem("could not restore the files", restore);
            }

            // Untracked files the snapshot does not have, and that the caller said may go, are removed.
            var untracked = Call(log, "ls-files", "-o", "--exclude-standard", "-z");
            if (!untracked.Success)
                return Problem("could not list the untracked files", untracked);
            var present = Split(untracked.StdOut);
            var candidates = present.Where(removable.Contains).ToList();
            if (candidates.Count > 0)
            {
                var inTarget = Call(log, "ls-tree", "-r", "-z", "--name-only", targetCommit);
                if (!inTarget.Success)
                    return Problem("could not list the snapshot's files", inTarget);
                var keep = Split(inTarget.StdOut).ToHashSet();
                candidates = candidates.Where(p => !keep.Contains(p)).ToList();
            }
            for (var i = 0; i < candidates.Count; i += CleanBatch)
            {
                var args = new List<string> { "clean", "-f", "-q", "--" };
                args.AddRange(candidates.Skip(i).Take(CleanBatch));
                var clean = CallLiteral(log, args.ToArray());
                if (!clean.Success)
                    return Problem("could not remove untracked files", clean);
            }

            var readTree = Call(log, "read-tree", target.IndexTree);
            if (!readTree.Success)
                return Problem("could not restore the index", readTree);

            // Exit 1 means some entries still need an update; the check below decides whether the state is right.
            Call(log, "update-index", "-q", "--refresh");

            var now = Take(log);
            return now == target ? null : "the working tree could not be put back exactly; inspect with git status";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
        finally
        {
            TryDelete(_pathsFile);
        }
    }

    private GitResult Call(List<GitResult> log, params string[] args)
    {
        var r = git.Run(args);
        log.Add(r);
        return r;
    }

    private GitResult CallLiteral(List<GitResult> log, params string[] args)
    {
        var r = git.RunLiteral(args);
        log.Add(r);
        return r;
    }

    private GitResult CallWithIndex(List<GitResult> log, params string[] args)
    {
        var r = git.RunWithIndex(_tempIndex, args);
        log.Add(r);
        return r;
    }

    private static string? Hash(GitResult r)
    {
        var hash = r.StdOut.Trim();
        return r.Success && hash.Length > 0 ? hash : null;
    }

    private static string Problem(string what, GitResult r) => $"{what}: {GitClient.Describe(r)}";

    private static string[] Split(string nulSeparated) =>
        nulSeparated.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Leftover temp file: the next Take or RestoreTo overwrites or removes it.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
