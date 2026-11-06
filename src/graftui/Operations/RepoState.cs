using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>Repo-level facts that more than one operation needs.</summary>
internal static class RepoState
{
    /// <summary>
    /// The operation that owns the index and is still in progress ("merge", "cherry-pick", "revert" or "rebase"), or null.
    /// A multi-commit cherry-pick whose current pick was committed by hand has no CHERRY_PICK_HEAD, but git keeps its sequencer folder
    /// until the last pick, so that folder counts as a cherry-pick in progress too.
    /// <paramref name="call"/> runs a git command and returns its result.
    /// </summary>
    public static string? InProgress(Func<string[], GitResult> call, string gitDir)
    {
        foreach (var (name, kind) in new[] { (GitArgs.MergeHead, "merge"), ("CHERRY_PICK_HEAD", "cherry-pick"), ("REVERT_HEAD", "revert") })
        {
            if (call([GitArgs.RevParse, "-q", GitArgs.Verify, name]).Success)
                return kind;
        }
        if (SequenceGit.PickInProgress(gitDir))
            return "cherry-pick";
        if (SequenceGit.RebaseInProgress(gitDir))
            return "rebase";
        return null;
    }

    /// <summary>
    /// The git dir as a full path built from RepoPath, or null when git cannot say. The command is added to <paramref name="log"/>.
    /// It is not resolved through symlinks, so it stays comparable with RepoPath (see <see cref="ArgPath"/>).
    /// </summary>
    public static string? GitDir(GitClient git, List<GitResult> log)
    {
        var r = git.Run(GitArgs.RevParse, "--git-dir");
        log.Add(r);
        var dir = r.StdOut.Trim();
        return r.Success && dir.Length > 0 ? Path.GetFullPath(Path.Combine(git.RepoPath, dir)) : null;
    }

    /// <summary>`git ls-files -s -z` output: "mode blob stage<TAB>path" per NUL-terminated record. Lists only the entries git printed.</summary>
    public static List<IndexEntry> ParseIndex(string nulSeparated)
    {
        var entries = new List<IndexEntry>();
        foreach (var record in nulSeparated.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab < 0)
                continue;
            var fields = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2)
                entries.Add(new IndexEntry(record[(tab + 1)..], fields[0], fields[1]));
        }
        return entries;
    }

    /// <summary>
    /// The index entries of <paramref name="paths"/> (literal), sorted by path. A path with no entry gets Mode "" and Blob "".
    /// Null when git fails. The command is added to <paramref name="log"/>.
    /// </summary>
    public static IReadOnlyList<IndexEntry>? IndexEntries(GitClient git, IEnumerable<string> paths, List<GitResult> log)
    {
        var sorted = paths.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (sorted.Count == 0)
            return [];

        var r = git.RunLiteral(["ls-files", "-s", "-z", "--", .. sorted]);
        log.Add(r);
        if (!r.Success)
            return null;

        var found = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        foreach (var e in ParseIndex(r.StdOut))
            found[e.Path] = e;
        return sorted.Select(p => found.TryGetValue(p, out var e) ? e : new IndexEntry(p, "", "")).ToList();
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
