namespace graftui.Operations;

/// <summary>
/// The refusal texts of the branch operations (switch, new, rename, delete, stash-merge-pop and their undo).
/// Kept apart from <see cref="ChangeRefusals"/> so the wording of each tab lives in one place.
/// </summary>
public static class BranchRefusals
{
    public const string InvalidName = "Not a valid branch name: use letters, digits and - _ . / with no spaces";
    public const string Detached = ChangeRefusals.Detached;
    public const string NoCommitYet = "there are no commits yet: commit once before creating a branch";

    public static string Exists(string name) => $"A branch named {name} already exists";

    public static string NoBranch(string name) => $"there is no branch named {name}";

    public static string AlreadyOn(string name) => $"already on {name}";

    public static string Current(string name) => $"{name} is the current branch: switch to another branch before deleting it";

    /// <summary>HEAD has commits that no branch holds: switching would leave them only in the reflog.</summary>
    public static string DetachedOrphans(int count) =>
        $"HEAD has {count} commit(s) on no branch: switching away would leave them only in the reflog";

    /// <summary>The changed paths that a switch would overwrite. The first path is named, the rest are counted.</summary>
    public static string WouldOverwrite(IReadOnlyList<string> files)
    {
        var first = files.Count > 0 ? files[0] : "a file";
        var more = files.Count > 1 ? $" +{files.Count - 1} more" : "";
        var noun = files.Count > 1 ? "changes" : "change";
        return $"switching would overwrite your {noun} to {first}{more}: commit or stash it first";
    }

    public static string UpToDate(string branch, string into) => $"{branch} is already part of {into}: nothing to merge";

    public static string NotMergedNow(string name) => $"{name} has commits that the current branch doesn't have; nothing was deleted";

    public static string TipMoved(string name) => $"{name} moved since you pressed the key; nothing changed";

    public static string UndoBranchGone(string name) => $"{name} no longer exists, so there is nothing to undo";

    public static string UndoBranchExists(string name) => $"a branch named {name} exists again, so undoing would replace it";

    public static string UndoWouldLose(string other, string name) =>
        $"{other} has moved away from {name}'s commits, so removing {name} would lose them";

    public const string UndoMergeStateChanged = "files have changed since the merge, so undoing would overwrite them";
}
