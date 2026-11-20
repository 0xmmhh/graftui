namespace graftui.Model;

/// <summary>
/// One file the selected branch's new commits change. Added and Deleted are null for a binary file (git prints "-"). OldPath is the
/// path before a rename, else null; Path is the path after it.
/// </summary>
public sealed record IncomingFile(string Path, string? OldPath, int? Added, int? Deleted);

/// <summary>
/// The files from the merge base to the branch tip: the first <see cref="FilesMax"/> of them in git's order, and how many there are in all.
/// </summary>
public sealed record IncomingFiles(IReadOnlyList<IncomingFile> Files, int Count)
{
    /// <summary>The most files kept for the detail block. Count still counts them all.</summary>
    public const int FilesMax = 200;
}

/// <summary>Where the files read of the selected branch stands: not needed for it, running, read, or failed (summary only).</summary>
public enum IncomingFilesState { NotNeeded, Reading, Read, Failed }
