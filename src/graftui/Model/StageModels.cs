using graftui.Git;

namespace graftui.Model;

/// <summary>What the user asked the staging area to do. File is null for the two "all" actions.</summary>
public enum StageAction { Stage, Unstage, StageAll, UnstageAll }

/// <summary>
/// What the user asked the staging area to do. File is null for the two "all" actions.
/// HandPickedConfirmed is set when the user agreed to unstage staged changes that differ from the work tree too.
/// </summary>
public sealed record StageRequest(StageAction Action, FileChange? File, bool HandPickedConfirmed = false);

/// <summary>
/// Result of the checks before a stage or unstage. Refusal is null when the operation may run.
/// HandPicked lists the moving paths whose staged version differs from the work tree as well (see <see cref="StageOperation"/>).
/// </summary>
public sealed record StageCheck(string? Refusal, bool HasHead, IReadOnlyList<FileChange> Moving, IReadOnlyList<GitResult> Commands, IReadOnlyList<string> HandPicked)
{
    public bool CanRun => Refusal is null;
}

/// <summary>Result of a stage or unstage. Undo is set only for a hand-picked unstage: it moves no ref, but it changes the index.</summary>
public sealed record StageOutcome(
    bool Success,
    StageAction Action,
    IReadOnlyList<FileChange> Moved,
    WorkingStatus? After,
    string Message,
    IReadOnlyList<GitResult> Commands,
    UndoRecord? Undo = null);
