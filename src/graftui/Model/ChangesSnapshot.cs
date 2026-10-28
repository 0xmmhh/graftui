namespace graftui.Model;

/// <summary>
/// The current branch and its last commits. Commits are oldest to newest, at most four; More is true when older ones exist.
/// Branch is "" when HEAD is detached; HeadHash is null when there are no commits yet.
/// </summary>
public sealed record HeadLine(string Branch, string? HeadHash, IReadOnlyList<CommitInfo> Commits, bool More)
{
    public bool Detached => Branch.Length == 0;

    public CommitInfo? Tip => Commits.LastOrDefault();
}

/// <summary>
/// Everything the Changes tab shows at one moment. RawStatus is git's output, kept to tell whether a refresh changed anything.
/// Stash is the stash list's count and newest entry. HasUndo is true when an undo step is saved.
/// </summary>
public sealed record ChangesSnapshot(WorkingStatus Status, HeadLine Head, string RawStatus, StashSummary Stash, bool HasUndo = false);
