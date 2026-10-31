using graftui.Model;

namespace graftui.Ui.Text;

/// <summary>
/// What the Changes tab's key bar needs: how many rows there are, which side the selected row is on, and which kinds of change
/// exist. The bar lists only the keys that act now. Keys that are hidden still work as before (and refuse when they cannot act).
/// </summary>
public sealed record ChangesKeyState(int RowCount, ChangeSide? SelectedSide, bool HasUnstaged, bool HasStaged, bool HasHead, bool HasStash,
    bool HasUndo)
{
    public static readonly ChangesKeyState Empty = new(0, null, false, false, false, false, false);
}

/// <summary>
/// What the Branches tab's key bar needs: how many branches there are, whether m can merge the selected branch, and whether u has a step.
/// </summary>
public sealed record BranchesKeyState(int BranchCount, bool CanMerge, bool HasUndo, bool CanSwitch = false, bool CanCreate = false,
    bool CanRename = false, bool CanDelete = false)
{
    public static readonly BranchesKeyState Empty = new(0, false, false);

    /// <summary>
    /// What the bar needs for the selected branch: s switches to a branch that is not current; n needs a commit and a current branch (not
    /// detached); R works on any branch; D deletes a branch that is not current; m is <see cref="MergeOffered"/>.
    /// </summary>
    public static BranchesKeyState For(RepoSnapshot s, Branch? sel) => new(s.Branches.Count, MergeOffered(sel, s.Counts), s.HasUndo,
        CanSwitch: sel is { IsCurrent: false },
        CanCreate: s.HeadHash is not null && s.CurrentBranch.Length > 0,
        CanRename: sel is not null,
        CanDelete: sel is { IsCurrent: false });

    /// <summary>
    /// m shows for a branch that is not the current one and is ahead of HEAD. An unknown count shows m too: the merge checks and explains.
    /// </summary>
    public static bool MergeOffered(Branch? selected, IReadOnlyDictionary<string, (int Ahead, int Behind)> counts) =>
        selected is { IsCurrent: false } && (!counts.TryGetValue(selected.Name, out var c) || c.Ahead > 0);
}
