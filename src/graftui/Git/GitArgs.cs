namespace graftui.Git;

/// <summary>
/// The git words that the operations repeat. Values are exactly what git is given. Used as <c>GitArgs.UpdateRef</c>,
/// so the reader sees where each word comes from.
/// </summary>
internal static class GitArgs
{
    public const string RevParse = "rev-parse";
    public const string Verify = "--verify";
    public const string UpdateRef = "update-ref";
    public const string Stash = "stash";
    public const string StashTop = "stash@{0}";
    public const string CommitTree = "commit-tree";
    public const string NoGpgSign = "--no-gpg-sign";
    public const string MergeHead = "MERGE_HEAD";
    public const string Config = "config";
    public const string Switch = "switch";
    public const string Quiet = "--quiet";
    public const string SourcePrefix = "--source=";
}
