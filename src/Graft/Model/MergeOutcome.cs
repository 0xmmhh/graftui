using Graft.Git;

namespace Graft.Model;

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

public sealed record MergeCheck(string CurrentBranch, string? Refusal, IReadOnlyList<GitResult> Commands)
{
    public bool CanMerge => Refusal is null;
}
