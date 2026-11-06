using graftui.Model;
using graftui.Operations;

namespace graftui.Git;

/// <summary>
/// What the repo holds, read in one pass (<see cref="SequenceReader.ReadSequence"/>). Stop is the stop of the sequence in progress: graftui's
/// own (Ours true), a sequence graftui did not start (Ours false), or null when nothing is in progress or git cannot say. OldTip is the old
/// tip graftui saved, set only with its own stop. Unreadable is "rebase" or "cherry-pick" when graftui's own sequence is still in progress
/// but its stop cannot be read.
/// </summary>
public sealed record SequenceRead(SequenceStop? Stop, string? OldTip, string? Unreadable);

/// <summary>
/// Read-only views of a rebase or a cherry-pick in progress, for the screen. Nothing here is added to a command log or changes
/// the repo, and nothing throws: a read that fails gives null, or an empty result.
/// </summary>
public sealed class SequenceReader(GitClient git)
{
    private static readonly SequenceRead Nothing = new(null, null, null);

    /// <summary>The stop the paused rebase or cherry-pick is at (see <see cref="ReadSequence"/>). Null when nothing is in progress, or git cannot say.</summary>
    public SequenceStop? ReadPaused() => ReadSequence().Stop;

    /// <summary>
    /// Reads what git holds and whether graftui started it (<see cref="SequenceOwnership.Owned"/>). A sequence graftui did not start is
    /// not ours, and is refused everywhere; its stop carries Ours false and only its kind. Never throws.
    /// </summary>
    public SequenceRead ReadSequence()
    {
        try
        {
            var log = new List<GitResult>();
            var gitDir = RepoState.GitDir(git, log);
            if (gitDir is null)
                return Nothing;

            var kind = RepoState.InProgress(args => BranchGit.Call(git, log, args), gitDir);
            if (kind is null)
                return Nothing;

            var state = SequenceOwnership.Owned(gitDir, kind);
            if (state is null)
                return new SequenceRead(NotOurs(kind == "rebase" ? SequenceKind.Rebase : SequenceKind.CherryPick), null, null);

            var stop = state.Kind == SequenceKind.CherryPick
                ? SequenceGit.ReadPickStop(git, state, log)
                : SequenceGit.ReadRebaseStop(git, gitDir, state, log);
            return stop is null ? new SequenceRead(null, null, kind) : new SequenceRead(stop, state.OldTip, null);
        }
        catch (Exception)
        {
            return Nothing;
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
