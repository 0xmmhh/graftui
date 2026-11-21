using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

/// <summary>What the merge card says: a merged or fast-forwarded branch, a stopped conflict, up to date, a failure, or a refusal.</summary>
public enum DigestKind { Merged, FastForwarded, Stopped, UpToDate, Failed, Refused }

/// <summary>
/// What the card says. Hash: the new HEAD (merge commit or fast-forward tip). Before: HEAD when m was pressed. Ahead: the commits the
/// branch brought (from the pre-merge layout). Rollback: the command that put the repo back ("git merge --abort" / "git reset --merge"),
/// null when none ran. Message: git's message (Failed) or the refusal reason (Refused). Into "" = detached HEAD.
/// </summary>
public sealed record MergeCard(DigestKind Kind, string Branch, string Into, string? Hash, string? Before, int Ahead,
    IReadOnlyList<string> ConflictFiles, string Message, string? Rollback, bool Verified, bool CanPlay, bool CanUndo);

/// <summary>
/// The words of the merge card: its title, the two head lines, the key hint, and the ghost and list title it implies. Pure: no git,
/// no views. "cur" in the texts is the current branch, or "HEAD" when HEAD is detached.
/// </summary>
public static class DigestText
{
    /// <summary>The key hint when g can play but u cannot undo.</summary>
    public const string PlayHint = "g play";

    /// <summary>The key hint when g and u both work.</summary>
    public const string PlayUndoHint = "g play · u undo";

    /// <summary>The title of the list under the graph after a merge or a fast-forward: the commits that came in.</summary>
    public const string CameInTitle = "commits that came in (newest first)";

    private static readonly Style Ok = new(Ink.Ok);
    private static readonly Style Bad = new(Ink.Bad);
    private static readonly Style Warn = new(Ink.Warn);
    private static readonly Style Dim = new(Ink.Dim);
    private static readonly Style HashStyle = new(Ink.Hash);

    // The "✔ verified" badge and the warning that replaces it when graftui could not check the repo.
    private static readonly Seg Verified = new("✔ verified", Ok);
    private static readonly Seg NotVerified = new("! not verified: check git status", Warn);

    /// <summary>The card for a merge that git ran: its outcome, the verify result, and the commits the branch brought.</summary>
    public static MergeCard FromOutcome(MergeOutcome o, bool verified, string? before, int ahead)
    {
        var kind = KindOf(o.Kind);
        var canUndo = (kind == DigestKind.Merged || kind == DigestKind.FastForwarded) && o.Undo is not null;
        return new MergeCard(kind, o.Branch, o.Into, o.NewHead, before, ahead, o.ConflictFiles, o.Message,
            RollbackCommand(o.Commands), verified, CanPlay: true, CanUndo: canUndo);
    }

    /// <summary>The card for a merge that was refused before git ran: nothing was changed, so nothing is verified and nothing can play.</summary>
    public static MergeCard Refused(string branch, string into, string reason) =>
        new(DigestKind.Refused, branch, into, null, null, 0, [], reason, null, false, CanPlay: false, CanUndo: false);

    /// <summary>The title of the pane: "Merged feature → main", or "Tried feature → main" for anything that did not merge.</summary>
    public static string Title(MergeCard c)
    {
        if (c.Kind == DigestKind.Merged || c.Kind == DigestKind.FastForwarded)
            return "Merged " + c.Branch + " → " + Cur(c);
        return "Tried " + c.Branch + " → " + Cur(c);
    }

    /// <summary>Sheet row 0 without the key hint: what happened, in one line.</summary>
    public static IReadOnlyList<Seg> Headline(MergeCard c)
    {
        var segs = new List<Seg>();
        if (c.Kind == DigestKind.Merged)
        {
            segs.Add(new Seg("✔ Merged " + c.Branch + " into " + Cur(c), Ok));
        }
        else if (c.Kind == DigestKind.FastForwarded)
        {
            segs.Add(new Seg("✔ Fast-forwarded " + Cur(c) + " to " + Seven(c.Hash), Ok));
        }
        else if (c.Kind == DigestKind.Stopped)
        {
            segs.Add(new Seg("✕ Merge stopped: " + ConflictPhrase(c.ConflictFiles), Bad));
        }
        else if (c.Kind == DigestKind.UpToDate)
        {
            segs.Add(new Seg("✔ Already up to date", Ok));
            segs.Add(new Seg(" · nothing to merge", Dim));
        }
        else if (c.Kind == DigestKind.Failed)
        {
            segs.Add(new Seg("✕ Merge failed: " + FirstLine(c.Message), Bad));
        }
        else
        {
            segs.Add(new Seg("! Refused: " + ChangeRefusals.Short(c.Message), Warn));
        }

        return segs;
    }

    /// <summary>Sheet row 1: the detail. What graftui checked, and whether the check passed.</summary>
    public static IReadOnlyList<Seg> Detail(MergeCard c)
    {
        if (c.Kind == DigestKind.Merged)
            return MergedDetail(c);
        if (c.Kind == DigestKind.FastForwarded)
            return FastForwardedDetail(c);
        if (c.Kind == DigestKind.Stopped)
            return StoppedDetail(c);
        if (c.Kind == DigestKind.Refused)
            return RefusedDetail(c);
        return UnchangedDetail(c);
    }

    /// <summary>The key hint for the sheet's row 0: "g play", "g play · u undo", or "" when g cannot play.</summary>
    public static string Hint(MergeCard c)
    {
        if (!c.CanPlay)
            return "";
        if (c.CanUndo)
            return PlayUndoHint;
        return PlayHint;
    }

    /// <summary>
    /// The line with the hint after it, at the right edge: the hint ends on the last text column (width - 1). Only when the line and the hint
    /// fit with at least three columns to spare; otherwise the line is returned as it is.
    /// </summary>
    public static IReadOnlyList<Seg> WithHint(IReadOnlyList<Seg> line, string hint, int width)
    {
        var result = new List<Seg>(line);
        if (hint.Length == 0)
            return result;

        var used = TextFit.Columns(JoinText(line));
        var hintWidth = TextFit.Columns(hint);
        if (used + hintWidth + 3 >= width)
            return result;

        var gap = width - used - hintWidth;
        result.Add(new Seg(new string(' ', gap), Dim));
        result.Add(new Seg(hint, Dim));
        return result;
    }

    /// <summary>Sheet row 0 while git runs: "merging feature into main…". There is no spinner: calm mode has no motion.</summary>
    public static IReadOnlyList<Seg> Running(string branch, string into)
    {
        var target = into.Length == 0 ? "HEAD" : into;
        return [new Seg("merging " + branch + " into " + target + "…", Dim)];
    }

    /// <summary>The ghost the lane graph draws after the merge: the done state of a merge, a fast-forward or a stop; none otherwise.</summary>
    public static GhostKind Ghost(MergeCard c)
    {
        if (c.Kind == DigestKind.Merged)
            return GhostKind.Merged;
        if (c.Kind == DigestKind.FastForwarded)
            return GhostKind.FastForwarded;
        if (c.Kind == DigestKind.Stopped)
            return GhostKind.Stopped;
        return GhostKind.None;
    }

    /// <summary>The title of the list under the graph: the commits that came in after a merge, else the usual incoming title.</summary>
    public static string ListTitle(MergeCard c)
    {
        if (c.Kind == DigestKind.Merged || c.Kind == DigestKind.FastForwarded)
            return CameInTitle;
        return PreviewText.IncomingTitle;
    }

    /// <summary>
    /// The command that put the repo back: "git merge --abort" when that ran and succeeded, else "git reset --merge" when that did, else null.
    /// A command is named only when it ran.
    /// </summary>
    internal static string? RollbackCommand(IReadOnlyList<GitResult> commands)
    {
        if (RanOk(commands, "merge", "--abort"))
            return "git merge --abort";
        if (RanOk(commands, "reset", "--merge"))
            return "git reset --merge";
        return null;
    }

    /// <summary>"app.txt conflicts", "a.txt and b.txt conflict", or "a.txt and 2 more files conflict".</summary>
    internal static string ConflictPhrase(IReadOnlyList<string> files)
    {
        if (files.Count == 0)
            return "a conflict";
        if (files.Count == 1)
            return TextFit.Sanitize(files[0]) + " conflicts";
        if (files.Count == 2)
            return TextFit.Sanitize(files[0]) + " and " + TextFit.Sanitize(files[1]) + " conflict";
        return TextFit.Sanitize(files[0]) + " and " + (files.Count - 1) + " more files conflict";
    }

    private static DigestKind KindOf(MergeKind kind)
    {
        if (kind == MergeKind.MergeCommit)
            return DigestKind.Merged;
        if (kind == MergeKind.FastForward)
            return DigestKind.FastForwarded;
        if (kind == MergeKind.Conflict)
            return DigestKind.Stopped;
        if (kind == MergeKind.UpToDate)
            return DigestKind.UpToDate;
        return DigestKind.Failed;
    }

    // Row 1 of a merge that made a commit. Verified: the hash, the badge and what was checked. Not verified: the hash and the warning.
    private static IReadOnlyList<Seg> MergedDetail(MergeCard c)
    {
        var segs = new List<Seg>
        {
            new Seg("new merge commit ", Dim),
            new Seg(Seven(c.Hash), HashStyle),
            new Seg(" · ", Dim),
        };
        if (c.Verified)
        {
            segs.Add(Verified);
            segs.Add(new Seg(": " + Cur(c) + " moved, tree clean", Dim));
        }
        else
        {
            segs.Add(NotVerified);
        }

        return segs;
    }

    // Row 1 of a fast-forward: how many commits came in, that there is no merge commit, and the badge.
    private static IReadOnlyList<Seg> FastForwardedDetail(MergeCard c)
    {
        var segs = new List<Seg>();
        if (c.Ahead > 0)
            segs.Add(new Seg(MergeScenes.Plural(c.Ahead, "commit") + " now on " + Cur(c) + ", no merge commit · ", Dim));
        else
            segs.Add(new Seg(Cur(c) + " now at " + Seven(c.Hash) + ", no merge commit · ", Dim));

        segs.Add(c.Verified ? Verified : NotVerified);
        return segs;
    }

    // Row 1 of a conflict: the rollback that ran (when one did), then where the branch is, verified or not.
    private static IReadOnlyList<Seg> StoppedDetail(MergeCard c)
    {
        var segs = new List<Seg>();
        if (c.Rollback is not null)
            segs.Add(new Seg("graftui ran " + c.Rollback + " · ", Dim));

        if (!c.Verified || c.Before is null)
        {
            segs.Add(NotVerified);
            return segs;
        }

        segs.Add(new Seg(Cur(c) + " unchanged at ", Dim));
        segs.Add(new Seg(Seven(c.Before), HashStyle));
        segs.Add(new Seg(" · ", Dim));
        segs.Add(Verified);
        return segs;
    }

    // Row 1 of a refusal: nothing was changed, and a detached HEAD says what to do.
    private static IReadOnlyList<Seg> RefusedDetail(MergeCard c)
    {
        var text = "nothing was changed";
        if (c.Into.Length == 0)
            text += " · switch to a branch first";
        return [new Seg(text, Dim)];
    }

    // Row 1 of an up-to-date merge, a failure: where the branch is, verified or not.
    private static IReadOnlyList<Seg> UnchangedDetail(MergeCard c)
    {
        if (!c.Verified || c.Before is null)
            return [NotVerified];

        return
        [
            new Seg(Cur(c) + " unchanged at ", Dim),
            new Seg(Seven(c.Before), HashStyle),
            new Seg(" · ", Dim),
            Verified,
        ];
    }

    // The current branch's name for the words: "HEAD" when HEAD is detached.
    private static string Cur(MergeCard c) => c.Into.Length == 0 ? "HEAD" : c.Into;

    // A hash cut to seven characters; "" when there is none.
    private static string Seven(string? hash)
    {
        if (string.IsNullOrEmpty(hash))
            return "";
        return hash.Length > 7 ? hash.Substring(0, 7) : hash;
    }

    // The first line of git's message, sanitized so nothing from git can move the cursor.
    private static string FirstLine(string message)
    {
        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return "";
        return TextFit.Sanitize(lines[0]);
    }

    // The text of a line of segments, joined.
    private static string JoinText(IReadOnlyList<Seg> line)
    {
        var text = "";
        foreach (var seg in line)
            text += seg.Text;
        return text;
    }

    // Whether a git command with exactly these arguments ran and succeeded.
    private static bool RanOk(IReadOnlyList<GitResult> commands, string first, string second)
    {
        foreach (var command in commands)
        {
            if (command.Success && command.Args.Count == 2 && command.Args[0] == first && command.Args[1] == second)
                return true;
        }

        return false;
    }
}
