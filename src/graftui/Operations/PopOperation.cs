using System.Text.RegularExpressions;
using graftui.Git;
using graftui.Model;

namespace graftui.Operations;

/// <summary>
/// Pop for the Changes tab (Z). An ordinary stash entry gets a plain `git stash pop` (no --index), so staged changes come back
/// unstaged, as git does. graftui's own stash-merge entry (see <see cref="StashMergeOperation.MessageStart"/>) is popped with
/// `--index`, so its staged changes stay staged. When git refuses that up front (the staged part cannot apply, tree untouched),
/// the plain pop is used only if it loses nothing: every file's staged version must equal its working-tree version. Otherwise
/// the pop is refused, nothing changes and the entry is kept. A plain pop that runs says that the staged changes came back unstaged.
/// Check reads the repo and refuses when there is no stash, no commit, a merge in progress, conflicts, a nested repo or a
/// submodule. It also refuses when the stash top is not the one the user saw.
/// Run saves the whole state first (WorkState), keeps that copy in refs/graftui/rescue/pop while git moves the files, then pops.
/// If git refuses, nothing has changed and the copy is dropped. If git conflicts, the state is put back exactly from the
/// copy, and the stash entry stays. Run never throws.
/// The undo step is saved after a verified success; until then, and on any failure, the rescue ref keeps the copy.
/// The ref is create-only, and Check refuses while it exists: the user inspects or deletes it first.
/// </summary>
public sealed class PopOperation(GitClient git) : IGitOperation<PopRequest, PopCheck, PopOutcome>
{
    private const string StashRef = "refs/stash";
    public const string RescueRef = "refs/graftui/rescue/pop";
    private static readonly Regex StashMergeSubject = new(
        "^(?:On|WIP on) [^:]+: " + Regex.Escape(StashMergeOperation.MessageStart) + ".+ into .+$", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly string[] StatusArgs = ["status", "--porcelain=v2", "-z", "--untracked-files=all"];

    public PopCheck Check(PopRequest request)
    {
        var log = new List<GitResult>();
        try
        {
            return CheckCore(request, log);
        }
        catch (Exception ex)
        {
            return Refused(ex.Message, log);
        }
    }

    public PopOutcome Run(PopRequest request, PopCheck check)
    {
        var log = new List<GitResult>();
        if (!check.CanRun)
            return Done(PopKind.Refused, "", [], null, check.Refusal ?? "nothing was popped", log);

        try
        {
            return RunCore(request, check, log);
        }
        catch (Exception ex)
        {
            return Done(PopKind.Failed, check.TopMessage, [], null,
                $"{ex.Message}; your state before the pop is saved as {RescueRef} if it was taken", log);
        }
    }

    private PopCheck CheckCore(PopRequest request, List<GitResult> log)
    {
        // 0. The copy from an earlier pop that failed is kept until the user deals with it. A new copy never replaces it.
        var rescue = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, RescueRef);
        if (rescue.Success)
            return Refused(ChangeRefusals.RescueSaved("your state from before an earlier pop", RescueRef), log);
        if (rescue.ExitCode != 1)
            return Refused(GitClient.Describe(rescue), log);

        // 1. A stash entry must exist, and it must be the one the user saw.
        var top = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, StashRef);
        if (!top.Success)
            return Refused(top.ExitCode == 1 ? ChangeRefusals.StashEmpty : GitClient.Describe(top), log);
        var topHash = top.StdOut.Trim();
        if (request.ExpectedTop is not null && request.ExpectedTop != topHash)
            return Refused(ChangeRefusals.StashMoved, log);

        // 2. A commit must exist, and no other operation may own the index.
        var head = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, "HEAD");
        if (!head.Success)
            return Refused(head.ExitCode == 1 ? ChangeRefusals.NoCommitYet : GitClient.Describe(head), log);
        var gitDir = RepoState.GitDir(git, log);
        if (gitDir is null)
            return Refused("could not find the git dir", log);
        var inProgress = RepoState.InProgress(a => Call(log, a), gitDir);
        if (inProgress is not null)
            return Refused(ChangeRefusals.InProgress(inProgress), log);

        // 3. The status: conflicts, a nested repo or a submodule in the way.
        var st = Call(log, StatusArgs);
        if (!st.Success)
            return Refused(GitClient.Describe(st), log);
        var status = StatusParser.Parse(st.StdOut);
        if (status.HasConflicts)
            return Refused(ChangeRefusals.Conflicts, log);
        if (status.Rows.FirstOrDefault(f => f.IsNestedRepo) is { } nested)
            return Refused(ChangeRefusals.NestedRepoStash(nested.Path), log);
        if (status.Rows.FirstOrDefault(f => f.IsSubmodule) is { } sub)
            return Refused(ChangeRefusals.Submodule(sub.Path), log);
        // git add -N makes an index entry with no content; the pop would not restore it, so it is refused as Discard refuses it.
        if (status.Unstaged.FirstOrDefault(f => f.Kind == ChangeKind.Added) is { } intent)
            return Refused(ChangeRefusals.IntentToAddStash(intent.Path), log);

        // 4. The entry's message, as the stash list shows it. It is stored back if the undo step runs.
        var message = Call(log, GitArgs.Stash, "list", "-n", "1", "--format=%gs");
        var topMessage = message.Success
            ? message.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? ""
            : "";

        return new PopCheck(null, topHash, topMessage, head.StdOut.Trim(), gitDir, log);
    }

    private PopOutcome RunCore(PopRequest request, PopCheck check, List<GitResult> log)
    {
        var top = check.Top;
        var head = check.Head;
        var state = new WorkState(git, check.GitDir);

        // 1. Still the same stash entry and the same HEAD.
        var now = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, StashRef);
        if (!now.Success || now.StdOut.Trim() != top || (request.ExpectedTop is not null && request.ExpectedTop != top))
            return Done(PopKind.Failed, check.TopMessage, [], null, "the stash changed since the check; nothing was popped", log);
        var headNow = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, "HEAD");
        if (!headNow.Success || headNow.StdOut.Trim() != head)
            return Done(PopKind.Failed, check.TopMessage, [], null, "HEAD moved since the check; nothing was popped", log);

        // 2. Save the whole state, and keep the copy under a ref while the files move.
        var before = state.Take(log);
        if (before is null)
            return Done(PopKind.Failed, check.TopMessage, [], null, "could not read the working tree; nothing was popped", log);
        var copy = state.Save(before, head, log);
        if (copy is null)
            return Done(PopKind.Failed, check.TopMessage, [], null, "could not save the working tree; nothing was popped", log);
        // Create-only (the empty old value): if the ref exists, the copy it holds is the only one, so it is not overwritten.
        var saved = Call(log, GitArgs.UpdateRef, RescueRef, copy, "");
        if (!saved.Success)
            return Done(PopKind.Failed, check.TopMessage, [], null,
                $"could not save a safety copy ({GitClient.Describe(saved)}); nothing was popped", log);

        // 3. The pop itself. A stash-merge entry is popped with its index first. git refuses --index before it touches the tree
        // when the staged part cannot apply; then the plain pop runs, and the staged changes come back unstaged.
        if (!IsStashMergeEntry(check.TopMessage))
        {
            var plain = Call(log, GitArgs.Stash, "pop");
            return plain.Success ? Popped(state, check, copy, log) : Conflicted(state, check, before, copy, plain, log);
        }

        var indexed = Call(log, GitArgs.Stash, "pop", "--index");
        if (indexed.Success)
            return Popped(state, check, copy, log);
        if (!IndexRefused(indexed) || state.Take(log) != before)
            return Conflicted(state, check, before, copy, indexed, log);

        // The plain pop would apply the working-tree part only. It is run when that loses nothing of the staged part.
        var differing = StagedDifferences(top, log);
        if (differing is null)
            return Unchanged(check, copy,
                "could not compare the staged and working versions of the stash; nothing was popped, the stash entry is kept", log);
        if (differing.Count > 0)
            return Unchanged(check, copy, ChangeRefusals.StagedWouldBeLost(differing), log);

        var fallback = Call(log, GitArgs.Stash, "pop");
        if (!fallback.Success)
            return Conflicted(state, check, before, copy, fallback, log);
        var popped = Popped(state, check, copy, log);
        return popped with { StagedCameBackUnstaged = popped.Kind == PopKind.Popped };
    }

    /// <summary>True for graftui's stash-merge entry, the one whose message names the merge ("On main: graftui: before merging B into I").</summary>
    // A subject that takes too long to match is treated as any other entry: a plain pop.
    internal static bool IsStashMergeEntry(string subject)
    {
        try
        {
            return StashMergeSubject.IsMatch(subject);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>git's refusal of `pop --index` when the staged part cannot apply. The tree is untouched when git says so.</summary>
    private static bool IndexRefused(GitResult r) =>
        (r.StdErr + "\n" + r.StdOut).Contains("Try without --index", StringComparison.Ordinal);

    /// <summary>
    /// The files whose staged version (the stash's index tree, S^2) differs from its working-tree version (S's tree). Empty when a plain
    /// pop loses no staged content. The untracked files (S^3) are not compared. Null when git could not say.
    /// </summary>
    private IReadOnlyList<string>? StagedDifferences(string top, List<GitResult> log)
    {
        var staged = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, top + "^2^{tree}");
        var tree = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, top + "^{tree}");
        if (!staged.Success || !tree.Success)
            return null;
        var diff = Call(log, "diff-tree", "-r", "-z", "--no-renames", "--name-only", staged.StdOut.Trim(), tree.StdOut.Trim());
        return diff.Success
            ? diff.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            : null;
    }

    /// <summary>git refused before it changed anything, and the pop is not run: the copy is dropped and the entry is kept.</summary>
    private PopOutcome Unchanged(PopCheck check, string copy, string message, List<GitResult> log)
    {
        Call(log, GitArgs.UpdateRef, "-d", RescueRef, copy);
        return Done(PopKind.Refused, check.TopMessage, [], null, message, log);
    }

    /// <summary>git popped the entry. Verify it, then save the undo step; the rescue ref is dropped only once the step is saved.</summary>
    private PopOutcome Popped(WorkState state, PopCheck check, string copy, List<GitResult> log)
    {
        var top = check.Top;
        var head = check.Head;

        var st = Call(log, StatusArgs);
        var stillListed = Call(log, GitArgs.Stash, "list", "--format=%H");
        if (!st.Success || !stillListed.Success)
            return Done(PopKind.Failed, check.TopMessage, [], null,
                $"the pop ran, but the result could not be read; your state before the pop is kept as {RescueRef}", log);
        var after = StatusParser.Parse(st.StdOut);
        if (after.HasConflicts || stillListed.StdOut.Split('\n').Any(h => h.Trim() == top))
            return Done(PopKind.Failed, check.TopMessage, [], after,
                $"the pop ran, but the result is not as expected; your state before the pop is kept as {RescueRef}", log);

        var afterState = state.Take(log);
        var afterCommit = afterState is null ? null : state.Save(afterState, head, log);
        if (afterCommit is null)
            return Done(PopKind.Popped, check.TopMessage, [], after,
                $"popped, but could not save the undo step: your state before the pop is kept as {RescueRef}", log);

        var below = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, StashRef);
        var undo = new UndoRecord(UndoKind.Pop, "stash pop", [], Head: head, StashCommit: top, StashMessage: check.TopMessage,
            StashBelow: below.Success ? below.StdOut.Trim() : "", StateBefore: copy, StateAfter: afterCommit);
        var problem = new UndoLog(git).Push(undo, log);
        if (problem is not null)
            return Done(PopKind.Popped, check.TopMessage, [], after,
                $"popped, but could not save the undo step ({problem}); your state before the pop is kept as {RescueRef}", log);

        Call(log, GitArgs.UpdateRef, "-d", RescueRef, copy);
        return Done(PopKind.Popped, check.TopMessage, [], after, $"popped \"{check.TopMessage}\"", log, undo);
    }

    /// <summary>
    /// git did not pop: either it refused before it changed anything, or it applied the entry with conflicts. In both
    /// cases the state is put back exactly from the copy, and the stash entry is kept.
    /// </summary>
    private PopOutcome Conflicted(WorkState state, PopCheck check, WorkSnapshot before, string copy, GitResult pop, List<GitResult> log)
    {
        var top = check.Top;
        var files = Call(log, "diff", "--name-only", "-z", "--diff-filter=U");
        var conflictFiles = files.Success
            ? files.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList()
            : new List<string>();

        // Nothing changed: git refused up front. The copy is not needed any more.
        if (state.Take(log) == before)
        {
            Call(log, GitArgs.UpdateRef, "-d", RescueRef, copy);
            return Done(PopKind.Refused, check.TopMessage, [], null,
                $"git refused the pop ({FirstLine(pop)}); nothing changed, the stash entry is kept", log);
        }

        // Something changed: the files that the pop could have written are removed, then the state is compared.
        var removable = RemovableFromStash(top, log);
        var problem = state.RestoreTo(before, copy, removable, log);
        var stashKept = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, StashRef);
        if (problem is null && stashKept.Success && stashKept.StdOut.Trim() == top)
        {
            Call(log, GitArgs.UpdateRef, "-d", RescueRef, copy);
            var restored = Call(log, StatusArgs);
            var status = restored.Success ? StatusParser.Parse(restored.StdOut) : null;
            if (conflictFiles.Count == 0)
                return Done(PopKind.Failed, check.TopMessage, [], status,
                    $"git stash pop exited {pop.ExitCode}; nothing was popped", log);
            return Done(PopKind.Conflict, check.TopMessage, conflictFiles, status,
                $"pop conflicted in {conflictFiles[0]}; rolled back, the stash entry is kept", log);
        }

        var reason = problem ?? "the stash entry moved during the pop";
        return Done(PopKind.Failed, check.TopMessage, conflictFiles, null,
            $"{reason} Your state before the pop is saved as {RescueRef}.", log);
    }

    /// <summary>The untracked paths the stash carried (its third parent), which git may have written during the pop.</summary>
    private HashSet<string> RemovableFromStash(string top, List<GitResult> log)
    {
        var untracked = Call(log, GitArgs.RevParse, "-q", GitArgs.Verify, top + "^3");
        if (!untracked.Success)
            return [];
        var listed = Call(log, "ls-tree", "-r", "-z", "--name-only", untracked.StdOut.Trim());
        return listed.Success
            ? listed.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet()
            : [];
    }

    private static PopCheck Refused(string reason, List<GitResult> log) =>
        new(reason, "", "", "", "", log);

    private static PopOutcome Done(PopKind kind, string topMessage, IReadOnlyList<string> conflicts, WorkingStatus? after,
        string message, List<GitResult> log, UndoRecord? undo = null) =>
        new(kind, topMessage, conflicts, after, message, log, undo);

    private GitResult Call(List<GitResult> log, params string[] args)
    {
        var r = git.Run(args);
        log.Add(r);
        return r;
    }

    private static string FirstLine(GitResult r)
    {
        var lines = r.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            lines = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault() ?? $"git stash pop exited {r.ExitCode}";
    }
}
