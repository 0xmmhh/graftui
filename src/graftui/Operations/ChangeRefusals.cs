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

    public const string NoCommitYet = "there are no commits yet: commit once before discarding or stashing";
    public const string NothingToStash = "Nothing to stash: the working tree is clean";
    public const string StashEmpty = "The stash is empty";
    public const string StashMoved = "the stash changed since you pressed Z; nothing was popped";

    public static string NoUnstaged(string path) =>
        $"{path} has no unstaged changes to discard; its staged changes stay (Space unstages them)";

    public static string IntentToAdd(string path) => $"{path} was added with git add -N: graftui won't discard it";

    public static string NestedRepoDiscard(string path) => $"{path} is a separate git repository: graftui won't discard it";

    public static string NestedRepoStash(string path) => $"{path} is a separate git repository: graftui won't stash or pop over it";

    public static string Submodule(string path) => $"{path} is a submodule: graftui won't discard, stash or pop over it";

    public static string HandPickedNotConfirmed(string path) => $"{path} has hand-picked staged changes: confirm before unstaging them";
}
