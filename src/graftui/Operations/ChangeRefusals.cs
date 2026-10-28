namespace graftui.Operations;

/// <summary>
/// The refusal texts of the Changes tab. Shared by the operations and their tests, so the wording lives in one place.
/// </summary>
public static class ChangeRefusals
{
    public const string Detached = "HEAD is detached: switch to a branch first";
    public const string NothingStaged = "nothing staged: Space stages the selected file, a stages everything";
    public const string NothingToStage = "nothing to stage";
    public const string NothingIsStaged = "nothing is staged";
    public const string Conflicts = "resolve the conflicted files first (marked U)";
    public const string NoCommitToAmend = "there is no commit to amend yet";
    public const string EmptySubject = "Write a subject first: one short line saying what the commit does";
    public const string HeadMoved = "HEAD moved since you started; nothing was committed";

    public static string ConflictFile(string path) => $"{path} has a merge conflict: resolve it outside graftui first";

    public static string Stale(string path) => $"{path} changed since the list was read; the list is now refreshed";

    public static string NestedRepo(string path) => $"{path} is a separate git repository: graftui won't stage it";

    /// <summary>kind is one of merge, cherry-pick, revert or rebase.</summary>
    public static string InProgress(string kind) => $"a {kind} is in progress: finish or abort it first";

    public static string PushedNotConfirmed(string remoteBranch) =>
        $"this commit is already on {remoteBranch}; press A again to confirm amending it";

    public const string NothingToDiscard = "Nothing to discard";
    public const string NoCommitYet = "there are no commits yet: commit once before discarding or stashing";
    public const string NothingToStash = "Nothing to stash: the working tree is clean";
    public const string StashEmpty = "The stash is empty";
    public const string StashMoved = "the stash changed since you pressed Z; nothing was popped";

    public static string NoUnstaged(string path) =>
        $"{path} has no unstaged changes to discard; its staged changes stay (Space unstages them)";

    public static string IntentToAdd(string path) => $"{path} was added with git add -N: graftui won't discard it";

    public static string NestedRepoDiscard(string path) => $"{path} is a separate git repository: graftui won't discard it";

    public static string NestedRepoStash(string path) => $"{path} is a separate git repository: graftui won't stash or pop over it";

    /// <summary>A saved state that graftui keeps under a ref until the user deals with it. what says whose state it is.</summary>
    public static string RescueSaved(string what, string reference) =>
        $"{what} is saved in {reference}: inspect it with git show {reference}, restore it with git restore --source={reference} --worktree -- ., or delete it with git update-ref -d {reference}";

    public static string IntentToAddStash(string path) => $"{path} was added with git add -N: graftui won't stash or pop over it";

    public static string Submodule(string path) => $"{path} is a submodule: graftui won't discard, stash or pop over it";

    public static string HandPickedNotConfirmed(string path) => $"{path} has hand-picked staged changes: confirm before unstaging them";

    public const string NothingToUndo = "Nothing to undo";
    public const string UndoDirty = "you have uncommitted changes: commit or stash them before undoing this";
    public const string UndoStashGone = "that stash entry is gone, so there is nothing to bring back";
    public const string UndoStashMoved = "the stash has changed since, so undoing would mix up its entries";
    public const string UndoStateChanged = "files have changed since the pop, so undoing would overwrite them";

    public static string UndoChanged(string path) => $"{path} has changed since, so undoing would overwrite it";

    public static string UndoHeadMoved(string branch) => $"{branch} has moved since, so undoing would lose commits";

    public static string UndoWrongBranch(string branch) => $"that commit is on {branch}: switch back to it first";

    /// <summary>The step's record cannot be read, or lacks a field its kind needs. reference is the undo ref.</summary>
    public static string UndoUnreadable(string reference) =>
        $"the undo history is unreadable ({reference}); delete it with git update-ref -d {reference}";

    /// <summary>git could not list the undo refs at all, so no step can be named.</summary>
    public static string UndoListFailed(string detail) => $"could not read the undo history: {detail}";
}
