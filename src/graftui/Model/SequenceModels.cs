using System.Text.Json.Serialization;
using graftui.Git;

namespace graftui.Model;

// Models of the interactive rebase and the cherry-pick. Plain data: the operations fill them, the screen reads them.

public enum RebaseAction { Pick, Reword, Edit, Squash, Fixup, Drop }

/// <summary>
/// One commit of a rebase plan. Hash is the full hash of the ORIGINAL commit. Date is the committer date, for the todo list's age;
/// it is null when the row came from a state file, which does not keep it (the state file ignores it both ways).
/// </summary>
public sealed record RebaseRow(string Hash, string ShortHash, string Subject, RebaseAction Action, [property: JsonIgnore] DateTimeOffset? Date = null);

/// <summary>Plan is the user's plan (oldest first); null means the default plan (every commit a pick).</summary>
public sealed record RebaseRequest(string Onto, IReadOnlyList<RebaseRow>? Plan = null, string? ExpectedHead = null);

public sealed record RebaseCheck(string? Refusal, string Branch, string Onto, string OntoTip, string Head,
    IReadOnlyList<RebaseRow> Rows, string GitDir, IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

public sealed record CherryPickRequest(IReadOnlyList<string> Commits, string? ExpectedHead = null);

/// <summary>Ordered: the selected commits, oldest first (the order they are picked in).</summary>
public sealed record CherryPickCheck(string? Refusal, string Branch, string Head, IReadOnlyList<CommitInfo> Ordered,
    string GitDir, IReadOnlyList<GitResult> Commands)
{
    public bool CanRun => Refusal is null;
}

public enum SequenceKind { Rebase, CherryPick }

/// <summary>Conflict: git stopped on a conflict. Edit: an edit stop. Reword and Squash: graftui's own message pauses.</summary>
public enum PauseKind { Conflict, Edit, Reword, Squash }

/// <summary>
/// Text: a regular file git may have written markers into; it is resolved once the file is held with no marker line. Binary: git kept
/// one side without markers (a binary file or a symlink). Submodule: a gitlink on a side; it is refused, not asked about. AddedOurs and
/// AddedTheirs: only stage 2 (ours) or only stage 3 (theirs) has the file, and the base has none. DeletedOurs: the base and theirs have
/// it and ours (stage 2) deleted it; DeletedTheirs is the same with theirs deleting it. DeletedBoth: only the base has it.
/// </summary>
public enum ConflictKind { Text, Binary, Submodule, AddedOurs, AddedTheirs, DeletedOurs, DeletedTheirs, DeletedBoth }

/// <summary>Which version the work tree holds now: the stage 2 blob, the stage 3 blob, something else, or no file.</summary>
public enum TreeSide { Ours, Theirs, Edited, Deleted }

/// <summary>
/// A conflicted file without markers, and the version the user agreed to keep. Version pins an Edited file to the exact content the question
/// showed (its blob hash, or a link's target text), so an answer never covers a later edit; it is "" for Ours, Theirs and Deleted, whose
/// side already names the content.
/// </summary>
public sealed record KeptFile(string Path, ConflictKind Kind, TreeSide Side, string Version = "");

/// <summary>
/// A conflicted file. Resolved is only ever true for a Text file the work tree holds with no markers left. Ours and Theirs are the stage 2
/// and stage 3 blob hashes from <c>git ls-files -u</c>; "" when that stage is missing.
/// </summary>
public sealed record ConflictFile(string Path, bool Resolved, ConflictKind Kind = ConflictKind.Text, string Ours = "", string Theirs = "");

/// <summary>
/// Commit/ShortCommit: the ORIGINAL commit the stop is about. Step/Total: 1-based index in the plan rows (rebase) or picks.
/// Message: reword = old full message; squash = target message + "\n\n" + each squash row's message. Folded: squash/fixup hashes of the group.
/// Ours: false when no graftui state file matches (the stop then carries only Kind and Branch "").
/// Rows: a rebase's plan rows, oldest first, as the state file holds them; null for a cherry-pick or a stop graftui did not start.
/// The screen draws a paused rebase from them after a restart.
/// </summary>
public sealed record SequenceStop(SequenceKind Kind, PauseKind Pause, string Branch, string Onto, string Commit, string ShortCommit,
    string Subject, IReadOnlyList<ConflictFile> Files, int Step, int Total, string Message, IReadOnlyList<string> Folded, bool Ours,
    IReadOnlyList<RebaseRow>? Rows = null);

public enum ResumeAction { Continue, AmendAndContinue, Skip, Abort, Message }

/// <summary>
/// Message: the new message for a reword or combine pause. ExpectedCommit: the commit the pause showed, if the caller knows it.
/// Keep: the marker-less files the user said yes to keeping (the question's answer), or null.
/// </summary>
public sealed record ResumeRequest(ResumeAction Action, string? Message = null, string? ExpectedCommit = null,
    IReadOnlyList<KeptFile>? Keep = null);

/// <summary>Keep is set only when the one obstacle is marker-less files that the user has not yet confirmed: the question asks for them.</summary>
public sealed record ResumeCheck(string? Refusal, SequenceStop? Stop, string GitDir, IReadOnlyList<GitResult> Commands,
    IReadOnlyList<KeptFile>? Keep = null)
{
    public bool CanRun => Refusal is null;
}

public enum SequenceOutcomeKind { Done, Paused, Aborted, Refused, Failed }

/// <summary>New: the commit the original became, or null when it was dropped, folded, skipped or already applied.</summary>
public sealed record CommitMove(string Old, string? New);

public sealed record SequenceOutcome(SequenceOutcomeKind Kind, SequenceKind Op, string Branch, string Onto, string OldTip,
    string? NewTip, SequenceStop? Stop, IReadOnlyList<CommitMove> Moves,
    IReadOnlyList<string> Skipped, IReadOnlyList<string> Resolved, IReadOnlyList<string> Reworded,
    IReadOnlyList<string> Amended, IReadOnlyList<string> AlreadyApplied,
    string Message, IReadOnlyList<GitResult> Commands, UndoRecord? Undo = null);
