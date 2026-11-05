using graftui.Model;
using graftui.Operations;

namespace graftui.Git;

/// <summary>
/// Read-only views of a rebase or a cherry-pick in progress, for the screen. Nothing here is added to a command log or changes
/// the repo, and nothing throws: a read that fails gives null, or an empty result.
/// </summary>
public sealed class SequenceReader(GitClient git)
{
    /// <summary>
    /// The stop the paused rebase or cherry-pick is at. Null when nothing is in progress, or git cannot say.
    /// Ours is false when no graftui state matches the git operation (a rebase or a merge that graftui did not start).
    /// </summary>
    public SequenceStop? ReadPaused()
    {
        try
        {
            var log = new List<GitResult>();
            var gitDir = RepoState.GitDir(git, log);
            if (gitDir is null)
                return null;

            var kind = RepoState.InProgress(args => BranchGit.Call(git, log, args), gitDir);
            if (kind is null)
                return null;

            var expected = kind switch
            {
                "rebase" => SequenceKind.Rebase,
                "cherry-pick" => SequenceKind.CherryPick,
                _ => (SequenceKind?)null,
            };
            var state = SequenceStateFile.Load(gitDir);
            if (expected is null || state is null || state.Kind != expected)
                return NotOurs(expected ?? SequenceKind.CherryPick);

            return expected == SequenceKind.Rebase
                ? SequenceGit.ReadRebaseStop(git, gitDir, state, log)
                : SequenceGit.ReadPickStop(git, state, log);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The full hashes of the commits on <paramref name="selectedTip"/> whose change is already on <paramref name="currentTip"/>
    /// (git marks them with '='). Empty when git fails.
    /// </summary>
    public IReadOnlySet<string> AlreadyApplied(string currentTip, string selectedTip)
    {
        try
        {
            var r = git.Run("rev-list", "--right-only", "--cherry-mark", "--no-merges", currentTip + "..." + selectedTip);
            if (!r.Success)
                return new HashSet<string>();

            return r.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith('='))
                .Select(line => line[1..])
                .ToHashSet();
        }
        catch (Exception)
        {
            return new HashSet<string>();
        }
    }

    /// <summary>Each path with whether its file in <paramref name="repoPath"/> is free of conflict markers.</summary>
    public static IReadOnlyList<ConflictFile> ConflictState(string repoPath, IReadOnlyList<string> paths) =>
        SequenceGit.ConflictState(repoPath, paths);

    /// <summary>The stop of a git sequence that graftui did not start (or whose state cannot be used).</summary>
    private static SequenceStop NotOurs(SequenceKind kind) =>
        new(kind, PauseKind.Conflict, "", "", "", "", "", [], 0, 0, "", [], Ours: false);
}
