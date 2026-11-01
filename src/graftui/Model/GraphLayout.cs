namespace graftui.Model;

/// <summary>How the current branch and the selected branch relate. Drives which scene is drawn.</summary>
public enum GraphKind { Same, UpToDate, FastForward, Diverged, Unrelated }

/// <summary>
/// Everything the preview needs about two branch tips. Lists are oldest→newest and capped at 3 commits;
/// the matching <c>More</c> flag says whether older commits were left out.
/// </summary>
public sealed record GraphLayout(
    string Current, bool Detached, string Selected,
    string CurrentTip, string SelectedTip, string? MergeBase,
    IReadOnlyList<CommitInfo> CurrentOwn, bool CurrentOwnMore,
    IReadOnlyList<CommitInfo> SelectedOwn, bool SelectedOwnMore,
    IReadOnlyList<CommitInfo> Shared, bool SharedMore,
    CommitInfo CurrentTipCommit, CommitInfo SelectedTipCommit,
    int Ahead, int Behind, GraphKind Kind)
{
    /// <summary>
    /// Newest first, up to 50: the commits Ahead counts (on the selected branch only). Empty when the layout was not read from git.
    /// </summary>
    public IReadOnlyList<CommitInfo> SelectedNew { get; init; } = [];

    /// <summary>Newest first, up to 50: the commits Behind counts (on the current branch only). Empty when the layout was not read from git.</summary>
    public IReadOnlyList<CommitInfo> CurrentNew { get; init; } = [];

    /// <summary>The current branch's lane (<see cref="BranchLanes.None"/> when HEAD is detached or the layout was not stamped).</summary>
    public int CurrentLane { get; init; } = BranchLanes.None;

    /// <summary>The selected branch's lane (<see cref="BranchLanes.None"/> when the layout was not stamped or the name is not listed).</summary>
    public int SelectedLane { get; init; } = BranchLanes.None;

    /// <summary>This layout with both lanes read from the branch list it was loaded with.</summary>
    public GraphLayout WithLanes(IReadOnlyList<Branch> branches) =>
        this with
        {
            CurrentLane = Detached ? BranchLanes.None : BranchLanes.LaneOf(branches, Current),
            SelectedLane = BranchLanes.LaneOf(branches, Selected),
        };

    /// <summary>Placeholder layout used when the real one cannot be read. Tip commits carry only their hash.</summary>
    public static GraphLayout Minimal(string cur, bool detached, string sel, string curTip, string selTip) =>
        new(cur, detached, sel, curTip, selTip, null,
            [], false, [], false, [], false,
            Stub(curTip), Stub(selTip), 0, 0, GraphKind.Diverged);

    private static CommitInfo Stub(string hash) =>
        new(hash, hash[..Math.Min(7, hash.Length)], 1, "");
}
