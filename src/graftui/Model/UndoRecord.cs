using System.Text.Json.Serialization;

namespace graftui.Model;

/// <summary>A ref moved from one full hash to another. An empty Before or After means the ref did not exist.</summary>
public sealed record RefChange(string Ref, string Before, string After);

public enum UndoKind { Commit, Amend, Merge, Discard, Stash, Pop, Unstage }

/// <summary>One stage-0 index entry from `git ls-files -s -z`. Mode "" = path absent from the index.</summary>
public sealed record IndexEntry(string Path, string Mode, string Blob);

/// <summary>
/// One undoable step, stored as the message of a commit under refs/graftui/undo/. Only the fields that the kind needs are set.
/// What each kind records, and how UndoOperation checks it before it runs, is described on UndoOperation.
/// </summary>
public sealed record UndoRecord(
    UndoKind Kind,
    string What,                                   // "commit", "merge of feature/x", "discard of f.txt", "stash", "stash pop", "unstage of f.txt"
    IReadOnlyList<RefChange> Changes,              // commit, amend, merge: the branch move
    string? Branch = null,
    string? Head = null,                           // HEAD right after the step
    string? StashCommit = null,                    // discard: W; stash: S; pop: S
    string? StashMessage = null,                   // pop: reflog subject (%gs) to store back
    string? StashBelow = null,                     // refs/stash after the step without our entry ("" = none)
    string? Path = null,                           // discard
    string? Backup = null,                         // discard: W (tracked) or U (untracked)
    bool Untracked = false,                        // discard of an untracked file
    IReadOnlyList<IndexEntry>? IndexBefore = null, // discard: F's entry (guard); unstage: entries to restore
    IReadOnlyList<IndexEntry>? IndexAfter = null,  // unstage: entries the guard expects now
    string? StateBefore = null,                    // pop: snapshot commit (see WorkState)
    string? StateAfter = null)                     // pop
{
    /// <summary>The kind as a lowercase word. Not stored: the JSON has only Kind.</summary>
    [JsonIgnore]
    public string Operation => Kind.ToString().ToLowerInvariant();
}
