namespace graftui.Model;

/// <summary>
/// The branch list and working-tree state at one moment.
/// CurrentBranch is "" when HEAD is detached; HeadHash is null when there are no commits yet.
/// Counts maps branch name to (Ahead, Behind) relative to HEAD. Missing entries are unknown.
/// </summary>
public sealed record RepoSnapshot(
    string CurrentBranch,
    string? HeadHash,
    bool Clean,
    IReadOnlyList<Branch> Branches,
    IReadOnlyDictionary<string, (int Ahead, int Behind)> Counts);
