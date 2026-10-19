namespace Graft.Git;

/// <summary>One commit as shown in the graph and the commit lists.</summary>
public sealed record CommitInfo(string Hash, string ShortHash, int ParentCount, string Subject)
{
    public bool IsMerge => ParentCount > 1;
}
