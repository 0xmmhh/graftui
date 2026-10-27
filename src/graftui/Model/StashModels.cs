using graftui.Git;

namespace graftui.Model;

/// <summary>The stash list as the Changes tab shows it: how many entries, and the newest one.</summary>
public sealed record StashSummary(int Count, string? TopHash, string? TopMessage)
{
    public static StashSummary None { get; } = new(0, null, null);
}

/// <summary>Discard the unstaged change to one path. The request holds the row the user saw.</summary>
public sealed record DiscardRequest(FileChange File);

/// <summary>Result of the checks before a discard. Row is the row as it is now; Refusal is null when the discard may run.</summary>
public sealed record DiscardCheck(string? Refusal, FileChange? Row, string Head, string Branch, string GitDir, IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

public enum DiscardKind { Discarded, Failed }

/// <summary>Result of a discard. StashRef is the entry that holds the discarded change (null when none was made).</summary>
public sealed record DiscardOutcome(
    DiscardKind Kind,
    string Path,
    string? StashRef,
    string Message,
    WorkingStatus? After,
    IReadOnlyList<GitResult> Commands,
    UndoRecord? Undo = null);

/// <summary>Stash everything (tracked and untracked files). A null or blank Message means git's default message.</summary>
public sealed record StashRequest(string? Message);

/// <summary>Result of the checks before a stash. Moving is every row that the stash will take.</summary>
public sealed record StashCheck(string? Refusal, IReadOnlyList<FileChange> Moving, string Head, string GitDir, IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

public enum StashKind { Stashed, Failed }

/// <summary>Result of a stash. Label is the stash's own subject, such as "On main: wip" (null when none was made).</summary>
public sealed record StashOutcome(
    StashKind Kind,
    IReadOnlyList<FileChange> Moved,
    string? Label,
    string Message,
    IReadOnlyList<GitResult> Commands,
    UndoRecord? Undo = null);

/// <summary>Pop the newest stash entry. ExpectedTop is the stash commit the user saw; null skips that check.</summary>
public sealed record PopRequest(string? ExpectedTop);

/// <summary>Result of the checks before a pop. Top is the stash commit that the pop will take.</summary>
public sealed record PopCheck(string? Refusal, string Top, string TopMessage, string Head, string GitDir, IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

/// <summary>
/// Result of a pop. Conflict means git could not apply the entry: the state was put back exactly and the entry is kept.
/// Refused means git stopped before it changed anything.
/// </summary>
public enum PopKind { Popped, Conflict, Refused, Failed }

public sealed record PopOutcome(
    PopKind Kind,
    string TopMessage,
    IReadOnlyList<string> ConflictFiles,
    WorkingStatus? After,
    string Message,
    IReadOnlyList<GitResult> Commands,
    UndoRecord? Undo = null);
