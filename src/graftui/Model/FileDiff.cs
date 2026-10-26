namespace graftui.Model;

public enum DiffLineKind { Hunk, Added, Removed, Context, Note }

/// <summary>One line of a diff as shown in the Changes view. Text keeps git's leading +, - or space.</summary>
public sealed record DiffLine(DiffLineKind Kind, string Text);

public sealed record FileDiff(FileChange File, IReadOnlyList<DiffLine> Lines);
