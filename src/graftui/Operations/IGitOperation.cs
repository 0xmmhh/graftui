namespace graftui.Operations;

/// <summary>Shared shape of every operation that changes a repo.</summary>
/// <remarks>
/// Check: read-only preconditions; never writes; returns a refusal or the go-ahead, plus the commands it ran.
/// Run: re-checks anything that can change after Check, runs git, then
///   Verify: classifies the result from repo state (HEAD, parents, MERGE_HEAD), never from git's text;
///   Rollback: on failure or interruption, undoes only this operation's own changes and reports what it could not undo.
/// Run never throws. Its outcome lists every command that ran, and an UndoRecord when refs moved.
/// Run is only called with a check that allowed the operation.
/// </remarks>
public interface IGitOperation<TRequest, TCheck, TOutcome>
{
    TCheck Check(TRequest request);
    TOutcome Run(TRequest request, TCheck check);
}
