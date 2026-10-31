using graftui.Git;

namespace graftui.Model;

public enum MergeKind { FastForward, MergeCommit, UpToDate, Conflict, Failed }

public sealed record MergeRequest(string Branch);

public sealed record MergeOutcome(
    MergeKind Kind,
    string Branch,
    string Into,
    string? NewHead,
    IReadOnlyList<string> ConflictFiles,
    string Message,
    IReadOnlyList<GitResult> Commands,
    UndoRecord? Undo = null);

/// <summary>Dirty: true only when the one reason to refuse is uncommitted changes (the stash-merge-pop question is offered then).</summary>
public sealed record MergeCheck(string CurrentBranch, string? Refusal, IReadOnlyList<GitResult> Commands, bool Dirty = false)
{
    public bool CanMerge => Refusal is null;
}
