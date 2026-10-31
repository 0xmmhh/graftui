using System.Text;

namespace graftui.Model;

/// <summary>
/// Which colour slot ("lane") each branch has. Pure: it reads only the branch names and their order in the list, never which
/// branch is current or selected, so a branch keeps its colour when the selection or the current branch moves. The colours
/// themselves live in the theme.
/// </summary>
public static class BranchLanes
{
    /// <summary>Lanes 0..Count-1. Every theme has this many lane colours.</summary>
    public const int Count = 6;

    /// <summary>No lane: a detached HEAD, or a name that is not in the list.</summary>
    public const int None = -1;

    /// <summary>The trunk's lane: "main", or "master" when there is no "main".</summary>
    public const int Trunk = 0;

    // Lanes 1..PoolSize are shared by every branch that is not the trunk.
    private const int PoolSize = Count - 1;

    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    /// <summary>The lane of each branch, in the same order as the list. The list is git's order (byte-sorted by name).</summary>
    public static int[] Assign(IReadOnlyList<Branch> branches)
    {
        var lanes = new int[branches.Count];
        var used = new bool[Count];
        var trunk = TrunkName(branches);
        var previous = None;
        for (var i = 0; i < branches.Count; i++)
        {
            var name = branches[i].Name;
            var lane = Trunk;
            if (name != trunk)
            {
                var preferred = Preferred(name);
                lane = FirstFree(preferred, used);
                if (lane == None)
                    lane = FirstUnlike(preferred, previous);
            }

            used[lane] = true;
            lanes[i] = lane;
            previous = lane;
        }

        return lanes;
    }

    /// <summary>The lane of the named branch, or <see cref="None"/> when it is not listed.</summary>
    public static int LaneOf(IReadOnlyList<Branch> branches, string name)
    {
        var lanes = Assign(branches);
        for (var i = 0; i < branches.Count; i++)
        {
            if (branches[i].Name == name)
                return lanes[i];
        }

        return None;
    }

    /// <summary>"main" when it is listed, else "master" when it is listed, else null.</summary>
    internal static string? TrunkName(IReadOnlyList<Branch> branches)
    {
        if (IsListed(branches, "main"))
            return "main";
        if (IsListed(branches, "master"))
            return "master";
        return null;
    }

    /// <summary>The lane a name starts from: 1 + its hash modulo the pool. Always 1..PoolSize.</summary>
    internal static int Preferred(string name) => 1 + (int)(Fnv1a(name) % PoolSize);

    /// <summary>32-bit FNV-1a of the name's UTF-8 bytes. Unlike string.GetHashCode it is the same in every run and on every machine.</summary>
    internal static uint Fnv1a(string s)
    {
        var h = FnvOffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            h ^= b;
            h = unchecked(h * FnvPrime);
        }

        return h;
    }

    // The first free pool lane, probing from the preferred one and wrapping around. None when every pool lane is taken.
    private static int FirstFree(int preferred, bool[] used)
    {
        for (var k = 0; k < PoolSize; k++)
        {
            var lane = PoolLane(preferred, k);
            if (!used[lane])
                return lane;
        }

        return None;
    }

    // The first pool lane from the preferred one (wrapping) that is not the previous branch's lane, so list neighbours never share one.
    private static int FirstUnlike(int preferred, int previous)
    {
        for (var k = 0; k < PoolSize; k++)
        {
            var lane = PoolLane(preferred, k);
            if (lane != previous)
                return lane;
        }

        return preferred;
    }

    // The k-th pool lane counted from the preferred one: k = 0 is the preferred lane itself.
    private static int PoolLane(int preferred, int k) => 1 + (preferred - 1 + k) % PoolSize;

    private static bool IsListed(IReadOnlyList<Branch> branches, string name)
    {
        foreach (var b in branches)
        {
            if (b.Name == name)
                return true;
        }

        return false;
    }
}
