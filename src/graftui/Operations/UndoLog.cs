using System.Text;
using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>One undo step as stored: its ref, its commit, and its record (null when the message cannot be read).</summary>
internal sealed record UndoEntry(string Ref, string Commit, UndoRecord? Step);

/// <summary>
/// The undo history: one commit per step under refs/graftui/undo/NNNNNN, the newest with the highest number.
/// A step commit has the empty tree (or, for an unstage, the saved index entries), and its parents are the commits
/// the step needs to bring its work back, so those stay reachable. Its message holds the record (see <see cref="UndoJson"/>).
/// Push never throws. Top throws when git cannot list the refs.
/// </summary>
internal sealed class UndoLog(GitClient git)
{
    public const string Prefix = "refs/graftui/undo/", MessageFile = "GRAFTUI_UNDO_MSG";
    public const int Max = 50;

    private string? _gitDir;

    /// <summary>Saves a step. Returns null on success, otherwise what went wrong; the step is then not saved.</summary>
    public string? Push(UndoRecord r, List<GitResult> log)
    {
        try
        {
            var gitDir = _gitDir ??= RepoState.GitDir(git, log);
            if (gitDir is null)
                return "could not find the git dir";

            var tree = r.Kind == UndoKind.Unstage ? UnstageTree(r, gitDir, log) : Hash(Call(log, "mktree"));
            if (tree is null)
                return "could not make the undo step's tree";

            var msgPath = Path.Combine(gitDir, MessageFile);
            try
            {
                File.WriteAllText(msgPath, UndoJson.Message(r), new UTF8Encoding(false));

                var args = new List<string> { GitArgs.CommitTree, GitArgs.NoGpgSign, tree };
                foreach (var parent in KeptCommits(r))
                    args.AddRange(["-p", parent]);
                args.AddRange(["-F", RepoState.ArgPath(git, msgPath)]);

                var commit = Hash(Call(log, args.ToArray()));
                if (commit is null)
                    return "could not write the undo step";

                var refProblem = AddRef(commit, log);
                if (refProblem is not null)
                    return refProblem;

                TryCap(log);
                return null;
            }
            finally
            {
                TryDelete(msgPath);
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The newest step, or null when there are none. Throws when git cannot list the refs.</summary>
    public UndoEntry? Top(List<GitResult> log)
    {
        var refs = Refs(log);
        if (refs.Length == 0)
            return null;

        var (name, oid) = refs[^1];
        var message = Call(log, "log", "-1", "--format=%B", oid, "--");
        var step = message.Success ? UndoJson.Parse(message.StdOut) : null;
        return new UndoEntry(name, oid, step);
    }

    /// <summary>Removes a step. Returns true when the ref is gone. The ref is only removed while it still points at the step.</summary>
    public bool Drop(UndoEntry e, List<GitResult> log) =>
        Call(log, GitArgs.UpdateRef, "-d", e.Ref, e.Commit).Success;

    /// <summary>Parents that keep the step's commits reachable: the branch tips it moved, the stash commit and the pop snapshots.</summary>
    private static IEnumerable<string> KeptCommits(UndoRecord r) =>
        r.Changes.SelectMany(c => new[] { c.Before, c.After })
            .Append(r.StashCommit)
            .Append(r.StateBefore)
            .Append(r.StateAfter)
            .OfType<string>()
            .Where(h => h.Length > 0)
            .Distinct();

    /// <summary>A tree with the saved index entries of an unstage, so their blobs are reachable. Built in a temporary index.</summary>
    private string? UnstageTree(UndoRecord r, string gitDir, List<GitResult> log)
    {
        var tempIndex = Path.Combine(gitDir, WorkState.TempIndex);
        try
        {
            TryDelete(tempIndex);
            foreach (var e in (r.IndexBefore ?? []).Where(e => e.Mode.Length > 0))
            {
                var add = CallWithIndex(tempIndex, log, "update-index", "--add", "--cacheinfo", $"{e.Mode},{e.Blob},{e.Path}");
                if (!add.Success)
                    return null;
            }
            return Hash(CallWithIndex(tempIndex, log, "write-tree"));
        }
        finally
        {
            TryDelete(tempIndex);
        }
    }

    /// <summary>Creates the next numbered ref at <paramref name="commit"/>. Create-only, retried once after re-reading the numbers.</summary>
    private string? AddRef(string commit, List<GitResult> log)
    {
        GitResult? last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var refs = Refs(log);
            var highest = refs.Select(r => Number(r.Ref)).DefaultIfEmpty(0).Max();
            var name = Prefix + (highest + 1).ToString("D6");
            last = Call(log, GitArgs.UpdateRef, name, commit, "");
            if (last.Success)
                return null;
        }
        return $"could not record the undo step: {GitClient.Describe(last!)}";
    }

    /// <summary>Drops the oldest steps beyond <see cref="Max"/>. A failure here leaves extra steps in place, which is harmless.</summary>
    private void TryCap(List<GitResult> log)
    {
        try
        {
            var refs = Refs(log);
            for (var i = 0; i < refs.Length - Max; i++)
                Call(log, GitArgs.UpdateRef, "-d", refs[i].Ref, refs[i].Oid);
        }
        catch (InvalidOperationException)
        {
            // Listing failed: keep the extra steps and try again on the next push.
        }
    }

    /// <summary>All steps as (ref, commit), oldest first. Throws when git cannot list them.</summary>
    private (string Ref, string Oid)[] Refs(List<GitResult> log)
    {
        var r = Call(log, "for-each-ref", "--sort=refname", "--format=%(refname)%00%(objectname)", Prefix);
        if (!r.Success)
            throw new InvalidOperationException(GitClient.Describe(r));

        return r.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\0'))
            .Where(parts => parts.Length == 2)
            .Select(parts => (parts[0], parts[1]))
            .ToArray();
    }

    private static int Number(string name) =>
        name.StartsWith(Prefix, StringComparison.Ordinal) && int.TryParse(name.AsSpan(Prefix.Length), out var n) ? n : 0;

    private GitResult Call(List<GitResult> log, params string[] args)
    {
        var r = git.Run(args);
        log.Add(r);
        return r;
    }

    private GitResult CallWithIndex(string indexFile, List<GitResult> log, params string[] args)
    {
        var r = git.RunWithIndex(indexFile, args);
        log.Add(r);
        return r;
    }

    private static string? Hash(GitResult r)
    {
        var hash = r.StdOut.Trim();
        return r.Success && hash.Length > 0 ? hash : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is overwritten by the next step.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
