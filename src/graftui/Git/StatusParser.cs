using graftui.Model;

namespace graftui.Git;

/// <summary>
/// Reads `git status --porcelain=v2 -z --untracked-files=all` into the two areas. Pure: it runs no git.
/// Ordinary and renamed entries give a staged row when X is set and an unstaged row when Y is set.
/// Conflicts give one unstaged row. Ignored (!) and header (#) records are skipped.
/// </summary>
internal static class StatusParser
{
    public static WorkingStatus Parse(string porcelainV2Z)
    {
        var staged = new List<FileChange>();
        var unstaged = new List<FileChange>();
        var records = porcelainV2Z.Split('\0');

        for (var i = 0; i < records.Length; i++)
        {
            var rec = records[i];
            if (rec.Length < 2)
                continue;

            switch (rec[0])
            {
                case '1':
                {
                    // 1 XY sub mH mI mW hH hI path
                    var f = rec.Split(' ', 9);
                    if (f.Length == 9 && f[1].Length == 2)
                        AddEntries(f[1], f[8], null, IsSubmodule(f[2]), staged, unstaged);
                    break;
                }
                case '2':
                {
                    // 2 XY sub mH mI mW hH hI Xscore path, and the next NUL-terminated record is the original path
                    var f = rec.Split(' ', 10);
                    var orig = i + 1 < records.Length ? records[++i] : null;
                    if (f.Length == 10 && f[1].Length == 2)
                        AddEntries(f[1], f[9], orig, IsSubmodule(f[2]), staged, unstaged);
                    break;
                }
                case 'u':
                {
                    // u XY sub m1 m2 m3 mW h1 h2 h3 path: always one unstaged conflict row
                    var f = rec.Split(' ', 11);
                    if (f.Length == 11)
                        unstaged.Add(new FileChange(f[10], null, ChangeSide.Unstaged, ChangeKind.Conflicted, IsSubmodule(f[2])));
                    break;
                }
                case '?':
                {
                    if (rec.Length > 2)
                        unstaged.Add(new FileChange(rec[2..], null, ChangeSide.Unstaged, ChangeKind.Untracked));
                    break;
                }
            }
        }

        return new WorkingStatus(staged, unstaged);
    }

    /// <summary>The "sub" field: N... for a plain file, S followed by c, m and u flags for a submodule.</summary>
    private static bool IsSubmodule(string sub) => sub.StartsWith('S');

    private static void AddEntries(string xy, string path, string? origPath, bool isSubmodule, List<FileChange> staged, List<FileChange> unstaged)
    {
        if (xy[0] != '.')
            staged.Add(new FileChange(path, origPath, ChangeSide.Staged, IndexKind(xy[0]), isSubmodule));
        if (xy[1] != '.')
            unstaged.Add(new FileChange(path, null, ChangeSide.Unstaged, WorktreeKind(xy[1]), isSubmodule));
    }

    private static ChangeKind IndexKind(char x) => x switch
    {
        'A' => ChangeKind.Added,
        'D' => ChangeKind.Deleted,
        'R' => ChangeKind.Renamed,
        'C' => ChangeKind.Copied,
        'T' => ChangeKind.TypeChanged,
        _ => ChangeKind.Modified,
    };

    // Y = 'A' is intent-to-add (git add -N): the file is new to the index, but its content is not staged yet.
    private static ChangeKind WorktreeKind(char y) => y switch
    {
        'A' => ChangeKind.Added,
        'D' => ChangeKind.Deleted,
        'T' => ChangeKind.TypeChanged,
        _ => ChangeKind.Modified,
    };
}
