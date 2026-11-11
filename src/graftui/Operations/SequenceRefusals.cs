namespace graftui.Operations;

/// <summary>
/// The refusal texts of the interactive rebase and the cherry-pick. The first four rebase texts and the first
/// two pick texts are the approved wording, word for word.
/// Shared refusals (detached HEAD, a sequence in progress, an empty subject) come from <see cref="ChangeRefusals"/>.
/// </summary>
public static class SequenceRefusals
{
    /// <summary>"first", or "first +N more" when there are more paths: the same shape as BranchRefusals.WouldOverwrite.</summary>
    private static string Named(IReadOnlyList<string> paths)
    {
        var first = paths.Count > 0 ? paths[0] : "a file";
        var more = paths.Count > 1 ? $" +{paths.Count - 1} more" : "";
        return first + more;
    }

    public static string RebaseDirty(IReadOnlyList<string> paths) =>
        $"you have uncommitted changes to {Named(paths)}: commit or stash them before rebasing";

    public static string AlreadyOnTop(string branch, string onto) => $"{branch} is already on top of {onto}: nothing to rebase";

    public static string EmptyPlan(string branch) => $"dropping every commit would leave {branch} empty: keep at least one";

    /// <summary>action is "squash" or "fixup".</summary>
    public static string FoldFirst(string action) => $"{action} needs a commit above it to fold into";

    public static string PickDirty(IReadOnlyList<string> paths) =>
        $"you have uncommitted changes to {Named(paths)}: commit or stash them before picking";

    public static string AlreadyApplied(string shortCommit, string branch, string shortSame) =>
        $"{shortCommit} is already on {branch} (same change as {shortSame}): nothing to pick";

    public static string OnBranch(string branch) => $"these commits are already on {branch}";

    public static string NoOwnCommits(string branch, string onto) => $"{branch} has no commits of its own since {onto}: nothing to rebase";

    public static string HasMerges(string branch, string onto) => $"{branch} has a merge commit since {onto}: graftui won't rebase over merges";

    public static string OntoSelf(string branch) => $"select another branch to rebase {branch} onto";

    public static string Unrelated(string branch, string onto) => $"{branch} and {onto} share no history: nothing to rebase onto";

    public static string PlanStale(string branch) => $"{branch} changed since the plan was made; nothing was rebased";

    /// <summary>Mirrors ChangeRefusals.HeadMoved for a branch that moved between Check and Run.</summary>
    public static string Moved(string branch) => $"{branch} moved since you started; nothing changed";

    public static string MergeCommit(string shortCommit) => $"{shortCommit} is a merge commit: graftui won't pick it";

    public static string NoCommit(string text) => $"there is no commit {text}";

    public const string NothingSelected = "select a commit to pick first";

    public const string NothingPaused = "nothing is paused: there is no rebase or cherry-pick to continue";

    public const string PauseChanged = "the paused step changed since you pressed the button; nothing was done";

    public static string Unresolved(IReadOnlyList<string> paths) => $"{Named(paths)} still has conflict markers: resolve them before continuing";

    /// <summary>A conflicted file with no markers (binary, a link, a one-sided add or a delete): the user says which version to keep.</summary>
    public static string NoMarkers(IReadOnlyList<string> paths) =>
        $"{Named(paths)} has no conflict markers: say which version to keep before continuing";

    /// <summary>The files a keep answer was about changed since the question, and none needs a question now: nothing was done.</summary>
    public const string KeepChanged = "the conflicted files changed since you answered; nothing was done, press Enter again";

    /// <summary>A conflicted submodule: graftui does not stage it, so the user stages it in a shell and then continues.</summary>
    public static string Submodule(string path) => $"{path} is a submodule conflict: stage it in a shell, then press Enter";

    /// <summary>A conflicted path that is a folder in the work tree: git add would stage what is inside it, so the user resolves it in a shell.</summary>
    public static string Folder(string path) => $"{path} is a folder in the work tree: resolve it in a shell (git add or git rm), then press Enter";

    public static string StageFirst(string file) => $"{file} has unstaged changes: stage it first, then Amend & continue";

    public const string SkipOnlyConflict = "only a conflicted commit can be skipped";
}
