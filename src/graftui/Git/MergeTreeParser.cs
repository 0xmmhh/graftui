using System.Globalization;
using graftui.Model;

namespace graftui.Git;

/// <summary>
/// Reads the output of `git merge-tree --write-tree -z`. Pure: no git, no I/O.
/// The output is a tree oid, then one record per conflicted file (`mode oid stage TAB path`), then (after an empty token) one record per
/// conflict message (`count, count paths, conflict type, message`). Only the conflict type is read; the message is not stable.
/// </summary>
public static class MergeTreeParser
{
    // Types are read with their spacing made uniform (see NormalizeType): "CONFLICT(binary)" and "CONFLICT (binary)" are the same.
    private const string BinaryType = "CONFLICT(binary)";
    private const string RenameTypePrefix = "CONFLICT(rename";
    private const string ConflictTypePrefix = "CONFLICT(";

    /// <summary>True when the output starts with a tree oid, which is what a merge that went through (exit 0) or ended in conflicts prints.</summary>
    internal static bool HasTree(string stdout) => IsOid(stdout.Split('\0')[0]);

    /// <summary>
    /// The files that conflict, or null when the output is not merge-tree's (no leading tree oid, or a record that does not parse).
    /// An empty list is a valid answer: a conflict with no file entry (a directory rename, for example).
    /// Files come in the order git lists them, then any path named only by a conflict message.
    /// A path that only merged automatically (`Auto-merging`) is not listed.
    /// </summary>
    public static IReadOnlyList<ForecastFile>? ParseConflicts(string stdout)
    {
        var tokens = stdout.Split('\0');
        if (!IsOid(tokens[0]))
            return null;

        var order = new List<string>();
        var stages = new Dictionary<string, StageSet>();
        var types = new Dictionary<string, List<string>>();

        var i = 1;
        while (i < tokens.Length && tokens[i].Length > 0)
        {
            if (!ReadStageRecord(tokens[i], order, stages))
                return null;
            i++;
        }

        // The empty token that ends the file records; the messages follow it.
        if (i < tokens.Length && tokens[i].Length == 0)
            i++;

        while (i < tokens.Length && tokens[i].Length > 0)
        {
            i = ReadMessageRecord(tokens, i, order, types);
            if (i < 0)
                return null;
        }

        var files = new List<ForecastFile>();
        foreach (var path in order)
        {
            StageSet set;
            if (stages.TryGetValue(path, out var found))
                set = found;
            else
                set = new StageSet();

            List<string>? pathTypes;
            if (types.TryGetValue(path, out var foundTypes))
                pathTypes = foundTypes;
            else
                pathTypes = null;

            files.Add(new ForecastFile(path, KindOf(set, pathTypes)));
        }
        return files;
    }

    /// <summary>A 40- or 64-digit lowercase hex object name.</summary>
    internal static bool IsOid(string s)
    {
        if (s.Length != 40 && s.Length != 64)
            return false;
        foreach (var c in s)
        {
            if (!char.IsAsciiHexDigitLower(c))
                return false;
        }
        return true;
    }

    // One file record: `<mode> <oid> <stage>TAB<path>`. Returns false when it does not parse.
    private static bool ReadStageRecord(string record, List<string> order, Dictionary<string, StageSet> stages)
    {
        var tab = record.IndexOf('\t');
        if (tab < 0)
            return false;

        var meta = record[..tab].Split(' ');
        if (meta.Length != 3)
            return false;
        if (!int.TryParse(meta[2], NumberStyles.None, CultureInfo.InvariantCulture, out var stage) || stage < 1 || stage > 3)
            return false;

        var path = record[(tab + 1)..];
        StageSet set;
        if (stages.TryGetValue(path, out var found))
        {
            set = found;
        }
        else
        {
            set = new StageSet();
            stages[path] = set;
            order.Add(path);
        }
        set.Add(stage);
        return true;
    }

    // One message record starting at tokens[start]. Returns the index after it, or -1 when it does not parse.
    private static int ReadMessageRecord(string[] tokens, int start, List<string> order, Dictionary<string, List<string>> types)
    {
        if (!int.TryParse(tokens[start], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            return -1;

        var i = start + 1;
        var paths = new List<string>();
        for (var k = 0; k < count; k++)
        {
            if (i >= tokens.Length)
                return -1;
            paths.Add(tokens[i]);
            i++;
        }

        // The type, then the message (not read). A record needs both.
        if (i + 1 >= tokens.Length)
            return -1;
        var type = NormalizeType(tokens[i]);

        foreach (var path in paths)
        {
            List<string> list;
            if (types.TryGetValue(path, out var found))
            {
                list = found;
            }
            else
            {
                list = new List<string>();
                types[path] = list;
            }
            list.Add(type);

            // A conflict message names a path even when it has no stage entries, so it is listed. Auto-merging is not a conflict.
            if (type.StartsWith(ConflictTypePrefix, StringComparison.Ordinal) && !order.Contains(path))
                order.Add(path);
        }
        return i + 2;
    }

    // git writes "CONFLICT (contents)" with a space in most versions; the space is not part of the meaning, so it is dropped.
    internal static string NormalizeType(string type) => type.Replace("CONFLICT (", "CONFLICT(", StringComparison.Ordinal);

    // The kind of one conflicted path. The first rule that matches wins: the conflict type, then the stages present.
    private static ForecastConflict KindOf(StageSet set, List<string>? types)
    {
        if (HasType(types, BinaryType))
            return ForecastConflict.Binary;
        if (HasTypePrefix(types, RenameTypePrefix))
            return ForecastConflict.Rename;

        if (set.One && set.Three && !set.Two)
            return ForecastConflict.DeletedCurrent;
        if (set.One && set.Two && !set.Three)
            return ForecastConflict.DeletedSelected;
        if (set.Two && set.Three && !set.One)
            return ForecastConflict.BothAdded;
        if (set.One && set.Two && set.Three)
            return ForecastConflict.Content;
        return ForecastConflict.Other;
    }

    private static bool HasType(List<string>? types, string type)
    {
        if (types is null)
            return false;
        foreach (var t in types)
        {
            if (t == type)
                return true;
        }
        return false;
    }

    private static bool HasTypePrefix(List<string>? types, string prefix)
    {
        if (types is null)
            return false;
        foreach (var t in types)
        {
            if (t.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Which conflict stages (1 = base, 2 = current, 3 = selected) a path has.</summary>
    private sealed class StageSet
    {
        public bool One { get; private set; }
        public bool Two { get; private set; }
        public bool Three { get; private set; }

        public void Add(int stage)
        {
            if (stage == 1)
                One = true;
            else if (stage == 2)
                Two = true;
            else
                Three = true;
        }
    }
}
