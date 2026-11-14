using System.Security.Cryptography;

namespace graftui.Operations;

/// <summary>
/// Names for graftui's temporary files in the git dir (a commit message, a path list, a temporary index). Each name is new:
/// <c>&lt;stem&gt;-&lt;pid&gt;-&lt;8 hex&gt;</c>, so two graftui windows on one repo never write over each other's files.
/// The file is created empty, and only when no file of that name exists, so the name is taken before it is handed out.
/// A file left behind by a crash is not cleaned up: its name is never used again.
/// </summary>
internal static class GitDirTemp
{
    public const string Message = "graftui-msg", UndoMessage = "graftui-undo-msg", Paths = "graftui-paths",
        Index = "graftui-index", SequenceMessage = "graftui-sequence-msg";

    private const int Tries = 5;

    /// <summary>
    /// Creates a new empty file named <c>&lt;stem&gt;-&lt;pid&gt;-&lt;8 hex&gt;</c> in <paramref name="gitDir"/> and returns its path.
    /// Throws an IOException when no free name was found (or the folder cannot be written).
    /// </summary>
    public static string NewPath(string gitDir, string stem)
    {
        IOException? last = null;
        for (var i = 0; i < Tries; i++)
        {
            var path = Path.Combine(gitDir, Name(stem));
            try
            {
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                }
                return path;
            }
            catch (IOException ex) when (File.Exists(path) || Directory.Exists(path))
            {
                // Taken by another file: try another name.
                last = ex;
            }
        }
        throw new IOException($"could not create a temporary file in {gitDir}", last);
    }

    /// <summary>A name for <paramref name="stem"/>: the stem, this process's id and 8 random hex digits.</summary>
    internal static string Name(string stem)
    {
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        return $"{stem}-{Environment.ProcessId}-{random}";
    }

    /// <summary>True when <paramref name="fileName"/> is one of this stem's names (used by the tests and nothing else).</summary>
    internal static bool IsNameOf(string fileName, string stem)
    {
        var prefix = $"{stem}-{Environment.ProcessId}-";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var tail = fileName[prefix.Length..];
        return tail.Length == 8 && tail.All(Uri.IsHexDigit);
    }
}
