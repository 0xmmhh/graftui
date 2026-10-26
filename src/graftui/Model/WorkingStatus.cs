namespace graftui.Model;

/// <summary>The Changes list: staged rows and unstaged rows, each in git's order.</summary>
public sealed record WorkingStatus(IReadOnlyList<FileChange> Staged, IReadOnlyList<FileChange> Unstaged)
{
    public static WorkingStatus Empty { get; } = new([], []);

    /// <summary>Staged rows, then unstaged rows. The list's selection indexes this.</summary>
    public IReadOnlyList<FileChange> Rows => [.. Staged, .. Unstaged];

    public bool HasConflicts => Unstaged.Any(f => f.Kind == ChangeKind.Conflicted);

    /// <summary>Number of distinct paths that have a change on either side (a file can be in both).</summary>
    public int ChangedPaths => Staged.Concat(Unstaged).Select(f => f.Path).Distinct().Count();
}
