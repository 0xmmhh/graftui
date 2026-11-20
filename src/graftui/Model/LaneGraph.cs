namespace graftui.Model;

/// <summary>One commit as git gave it: its hash, its short hash, its parent hashes (first parent first) and its subject.</summary>
public sealed record GraphCommit(string Hash, string ShortHash, IReadOnlyList<string> Parents, string Subject);

/// <summary>
/// Base: the current branch's row. Branch: a branch with commits of its own. Side: a branch that was merged in, named from the merge.
/// </summary>
public enum LaneKind { Base, Branch, Side }

/// <summary>A commit placed on a lane. Column 0 is the oldest column shown.</summary>
public sealed record LaneCommit(string Hash, string ShortHash, string Subject, int Column, bool IsMerge);

/// <summary>Fork: a lane starts from a commit of another lane (or the base). Merge: a commit of one lane is merged into another lane.</summary>
public enum LinkKind { Fork, Merge }

/// <summary>
/// A connector between two rows. Fork: from the commit the lane was made on (FromLane, FromColumn) to the lane's first commit.
/// Merge: from a commit on FromLane into the merge commit (ToLane, ToColumn). FromColumn is -1 for a fork from older history,
/// which is drawn coming in from the left.
/// </summary>
public sealed record LaneLink(LinkKind Kind, int FromLane, int FromColumn, int ToLane, int ToColumn);

/// <summary>
/// One row. Index is its row (0 = the base). Name is the branch's name, or for a Side lane the name read from the merge subject
/// ("" when there is none). ColorLane is the BranchLanes lane; a Side lane takes its parent's. Parent is the row it hangs under
/// (-1 for the base and for unrelated lanes). Hidden counts the older commits left out (the per-lane cap). Gap is the column of
/// the dashed gap drawn for those commits or for a fork from the left, else -1.
/// </summary>
public sealed record Lane(int Index, LaneKind Kind, string Name, int ColorLane, int Parent, IReadOnlyList<LaneCommit> Commits,
    int Hidden, int Gap);

/// <summary>
/// A branch with no commit of its own (its tip is another branch's commit, or on the base). Its tip is drawn at (Lane, Column).
/// Column -1 means the tip is not in the window.
/// </summary>
public sealed record BranchPointer(string Name, int Lane, int Column);

/// <summary>
/// The whole graph. Columns is the highest column used + 1 (the ghost of the selected branch goes there). BaseMore: the base has
/// older commits than are shown. OthersCut: git's list of the other branches' commits hit its cap. LeftOut: branch lanes dropped
/// to fit <see cref="LaneGraphBuilder.MaxLanes"/>.
/// </summary>
public sealed record LaneGraph(string BaseName, IReadOnlyList<Lane> Lanes, IReadOnlyList<LaneLink> Links,
    IReadOnlyList<BranchPointer> Pointers, int Columns, bool BaseMore, bool OthersCut, int LeftOut)
{
    /// <summary>The graph of nothing: no commits or no branches.</summary>
    public static readonly LaneGraph Empty = new("", [], [], [], 0, false, false, 0);

    /// <summary>
    /// The row of a branch's own lane (the base's lane for the current branch), or -1. Pointer branches and unknown names give -1.
    /// Side lanes are skipped: their names come from merge subjects and are not branches.
    /// </summary>
    public int LaneOf(string branch)
    {
        for (var i = 0; i < Lanes.Count; i++)
        {
            if (Lanes[i].Kind != LaneKind.Side && Lanes[i].Name == branch)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Where a branch's tip is drawn: the last commit of its lane, else its pointer. (-1, -1) when the name is unknown or the tip
    /// is not in the window.
    /// </summary>
    public (int Lane, int Column) TipOf(string branch)
    {
        var lane = LaneOf(branch);
        if (lane >= 0 && Lanes[lane].Commits.Count > 0)
        {
            var newest = Lanes[lane].Commits[Lanes[lane].Commits.Count - 1];
            return (lane, newest.Column);
        }

        foreach (var pointer in Pointers)
        {
            if (pointer.Name == branch)
            {
                if (pointer.Column < 0)
                    return (-1, -1);
                return (pointer.Lane, pointer.Column);
            }
        }

        return (-1, -1);
    }

    /// <summary>
    /// The rows that would come in if the branch were merged into the base: the rows of its history that the base does not have.
    /// That is its own row; each row it was forked from, as far as the fork point (a fork from the base is not counted, since the
    /// base has it already); and each Side lane merged into one of those at or before the column the history reaches on that row.
    /// A side lane merged after the fork point is not in the history, so it is left out. Side lanes merged into a side lane that is
    /// in the history come in too. Sorted ascending. Empty for an unknown branch.
    /// </summary>
    public IReadOnlyList<int> IncomingLanes(string branch)
    {
        var result = new List<int>();
        var lane = LaneOf(branch);
        if (lane < 0)
            return result;

        var reach = HistoryReach(lane);
        for (var i = 0; i < Lanes.Count; i++)
        {
            if (InHistory(reach, i))
                result.Add(i);
        }

        return result;
    }

    // For each row, the column up to which the branch's history reaches on that row (-1: none of it). The branch's own row reaches
    // all of it. The rows are walked until nothing reaches further.
    private int[] HistoryReach(int lane)
    {
        var reach = new int[Lanes.Count];
        for (var i = 0; i < reach.Length; i++)
            reach[i] = -1;
        reach[lane] = int.MaxValue;

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var link in Links)
            {
                var through = ReachThrough(reach, link);
                if (through > reach[link.FromLane])
                {
                    reach[link.FromLane] = through;
                    changed = true;
                }
            }
        }

        return reach;
    }

    // How far back along the link's source row the history reaches through this link, or -1 when the link brings nothing in.
    private int ReachThrough(int[] reach, LaneLink link)
    {
        if (link.Kind == LinkKind.Fork)
        {
            // A fork from the base (row 0) is already on the base, and an OffLeft fork has no column to reach back to.
            if (link.FromLane == 0 || link.FromColumn < 0 || !InHistory(reach, link.ToLane))
                return -1;
            return link.FromColumn;
        }

        // A merge brings in its source only when the merge commit it lands on is in the history. Only side lanes count here: a
        // branch merged in keeps its own row and is not pulled in by this rule.
        if (Lanes[link.FromLane].Kind != LaneKind.Side || reach[link.ToLane] < link.ToColumn)
            return -1;
        return link.FromColumn;
    }

    // Whether any commit of the row is in the history: the history reaches its first commit's column.
    private bool InHistory(int[] reach, int row) => reach[row] >= Lanes[row].Commits[0].Column;
}
