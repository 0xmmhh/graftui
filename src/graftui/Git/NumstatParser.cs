using System.Globalization;
using graftui.Model;

namespace graftui.Git;

/// <summary>
/// Reads the output of `git diff --numstat -z` into the files a branch's new commits change. Pure: it runs no git.
/// Each record is "added TAB deleted TAB path NUL". A rename is "added TAB deleted TAB NUL old NUL new NUL", so its path field is empty
/// and the two paths follow as their own records. A binary file has "-" for both counts. Paths are raw (they may hold tabs), so only
/// the first two tabs split a record.
/// </summary>
internal static class NumstatParser
{
    /// <summary>
    /// The files of the output, or null when a record is malformed: fewer than three tab fields, a count that is neither a number nor "-",
    /// an empty record in the middle, or a rename without both of its paths. The first <see cref="IncomingFiles.FilesMax"/> files are kept;
    /// Count counts all of them.
    /// </summary>
    public static IncomingFiles? Parse(string stdout)
    {
        var files = new List<IncomingFile>();
        var count = 0;
        var parts = stdout.Split('\0');
        var i = 0;
        while (i < parts.Length)
        {
            var record = parts[i];
            i++;

            if (record.Length == 0)
            {
                // The empty text after the last NUL ends the output. An empty record anywhere else is malformed.
                if (i == parts.Length)
                    break;
                return null;
            }

            var fields = record.Split('\t', 3);
            if (fields.Length != 3)
                return null;
            if (!TryCount(fields[0], out var added) || !TryCount(fields[1], out var deleted))
                return null;

            var path = fields[2];
            string? oldPath = null;
            if (path.Length == 0)
            {
                // A rename: the old path and the new path are the next two records.
                if (i + 1 >= parts.Length)
                    return null;
                oldPath = parts[i];
                path = parts[i + 1];
                i += 2;
                if (oldPath.Length == 0 || path.Length == 0)
                    return null;
            }

            count++;
            if (files.Count < IncomingFiles.FilesMax)
                files.Add(new IncomingFile(path, oldPath, added, deleted));
        }

        return new IncomingFiles(files, count);
    }

    // A count is a whole number, or "-" for a binary file (then the count is null). Anything else is malformed.
    private static bool TryCount(string text, out int? count)
    {
        count = null;
        if (text == "-")
            return true;

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            count = number;
            return true;
        }
        return false;
    }
}
