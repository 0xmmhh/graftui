namespace graftui.Model;

/// <summary>One commit as shown in the graph and the commit lists.</summary>
public sealed record CommitInfo(string Hash, string ShortHash, int ParentCount, string Subject, DateTimeOffset? Date = null)
{
    public bool IsMerge => ParentCount > 1;
}
