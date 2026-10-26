using graftui.Git;

namespace graftui.Model;

/// <summary>
/// A commit or amend request. ExpectedHead is the HEAD the message box was opened on (null skips the check).
/// PushedConfirmed is set only after the three amend warnings were accepted.
/// </summary>
/// <summary>BlankAfterSubject is false only when an amend keeps an old message whose subject wraps onto the next line.</summary>
public sealed record CommitRequest(bool Amend, string Subject, string Body, string? ExpectedHead, bool PushedConfirmed,
    bool BlankAfterSubject = true);

/// <summary>
/// The repo state a commit depends on. OldHead is null on an unborn branch. OldParents, OldMessage and PushedTo
/// are filled for an amend only. PushedTo lists remote-tracking branches that already contain HEAD (e.g. "origin/main").
/// </summary>
public sealed record CommitCheck(
    string? Refusal,
    string Branch,
    string? OldHead,
    IReadOnlyList<string> OldParents,
    CommitInfo? OldCommit,
    string OldMessage,
    IReadOnlyList<string> PushedTo,
    IReadOnlyList<FileChange> Staged,
    string RawStatus,
    string GitDir,
    IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

public enum CommitKind { Committed, Amended, Failed }

public sealed record CommitOutcome(
    CommitKind Kind,
    string Branch,
    string? OldHead,
    string? NewHead,
    CommitInfo? NewCommit,
    string Message,
    IReadOnlyList<GitResult> Commands,
    UndoRecord? Undo = null);
