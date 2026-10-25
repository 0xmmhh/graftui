namespace graftui.Model;

/// <summary>A ref moved from one full hash to another.</summary>
public sealed record RefChange(string Ref, string Before, string After);

/// <summary>What an operation changed, so the change can be reported or undone. Only refs are recorded.</summary>
public sealed record UndoRecord(string Operation, IReadOnlyList<RefChange> Changes);
