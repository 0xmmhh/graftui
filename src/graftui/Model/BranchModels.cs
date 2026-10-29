using graftui.Git;

namespace graftui.Model;

public sealed record SwitchRequest(string Branch);

public sealed record SwitchCheck(string? Refusal, string From, string Target, string TargetTip, string Head, int Changed,
    IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

public enum SwitchKind { Switched, Refused, Failed }

/// <summary>Blocking: the changed paths that git refused to overwrite (empty unless Refused).</summary>
public sealed record SwitchOutcome(SwitchKind Kind, string From, string To, int CameAlong, IReadOnlyList<string> Blocking,
    string Message, IReadOnlyList<GitResult> Commands);

public sealed record CreateBranchRequest(string Name);

/// <summary>NameProblem: the refusal is about the typed name (invalid or taken), so the box stays open.</summary>
public sealed record CreateBranchCheck(string? Refusal, bool NameProblem, string Name, string From, string Tip,
    IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

public sealed record RenameBranchRequest(string Old, string New);

public sealed record RenameBranchCheck(string? Refusal, bool NameProblem, string Old, string New, string Tip, bool IsCurrent,
    IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

/// <summary>ExpectedTip: the tip the user was asked about (null = skip). Force: both questions were answered (unmerged branch).</summary>
public sealed record DeleteBranchRequest(string Name, string? ExpectedTip = null, bool Force = false);

/// <summary>Merged: the tip is an ancestor of HEAD. Ahead: commits HEAD lacks. Lost: commits on no other local branch.</summary>
public sealed record DeleteBranchCheck(string? Refusal, string Name, string Current, string Tip, bool Merged, int Ahead, int Lost,
    IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

public enum BranchChangeKind { Done, Failed }

/// <summary>
/// One outcome type for create, rename and delete. Name is the branch's name afterwards (NEW for a rename), OldName the old one.
/// </summary>
public sealed record BranchChangeOutcome(BranchChangeKind Kind, string Name, string? OldName, string Tip, string Message,
    IReadOnlyList<GitResult> Commands, UndoRecord? Undo = null, int Lost = 0);

public sealed record StashMergeRequest(string Branch);

public sealed record StashMergeCheck(string? Refusal, string Into, string Head, string GitDir, int Changed,
    IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

/// <summary>Merged covers fast-forward, merge commit and up to date (Merge.Kind says which).</summary>
public enum StashMergeKind { Merged, MergeConflict, PopConflict, Failed }

public sealed record StashMergeOutcome(StashMergeKind Kind, MergeOutcome? Merge, IReadOnlyList<string> ConflictFiles, string Message,
    IReadOnlyList<GitResult> Commands, UndoRecord? Undo = null);
