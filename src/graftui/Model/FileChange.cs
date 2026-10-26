namespace graftui.Model;

/// <summary>What git says happened to one path: a porcelain v2 status letter, or the untracked and conflict markers.</summary>
public enum ChangeKind { Modified, Added, Deleted, Renamed, Copied, TypeChanged, Untracked, Conflicted }

/// <summary>Which area a row is in: the staging area (index) or the working tree.</summary>
public enum ChangeSide { Staged, Unstaged }

/// <summary>One row of the Changes list. OrigPath is set for staged renames and copies only.</summary>
public sealed record FileChange(string Path, string? OrigPath, ChangeSide Side, ChangeKind Kind)
{
    /// <summary>The status letter shown in the list: M A D R C T ? U.</summary>
    public char Letter => Kind switch
    {
        ChangeKind.Modified => 'M',
        ChangeKind.Added => 'A',
        ChangeKind.Deleted => 'D',
        ChangeKind.Renamed => 'R',
        ChangeKind.Copied => 'C',
        ChangeKind.TypeChanged => 'T',
        ChangeKind.Untracked => '?',
        _ => 'U',
    };

    /// <summary>The letter the file shows in the staging area once it is staged (an untracked file becomes an added one).</summary>
    public char LetterAfterStage => Kind == ChangeKind.Untracked ? 'A' : Letter;

    /// <summary>The letter the file shows in the working tree once it is unstaged (a newly added file is untracked again).</summary>
    public char LetterAfterUnstage => Kind is ChangeKind.Added or ChangeKind.Renamed or ChangeKind.Copied ? '?' : Letter;

    /// <summary>An untracked directory with its own .git: git lists it with a trailing slash, and graftui won't stage it.</summary>
    public bool IsNestedRepo => Kind == ChangeKind.Untracked && Path.EndsWith('/');
}
