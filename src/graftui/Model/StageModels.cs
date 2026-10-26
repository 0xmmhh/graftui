using graftui.Git;

namespace graftui.Model;

/// <summary>What the user asked the staging area to do. File is null for the two "all" actions.</summary>
public enum StageAction { Stage, Unstage, StageAll, UnstageAll }

public sealed record StageRequest(StageAction Action, FileChange? File);

/// <summary>Result of the checks before a stage or unstage. Refusal is null when the operation may run.</summary>
public sealed record StageCheck(string? Refusal, bool HasHead, IReadOnlyList<FileChange> Moving, IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

/// <summary>Result of a stage or unstage. Undo is always null: staging moves no ref.</summary>
public sealed record StageOutcome(
    bool Success,
    StageAction Action,
    IReadOnlyList<FileChange> Moved,
    WorkingStatus? After,
    string Message,
    IReadOnlyList<GitResult> Commands,
    UndoRecord? Undo = null);
