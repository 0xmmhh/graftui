using System.Text;
using graftui.Git;

namespace graftui.Ui.Text;

/// <summary>
/// What the operation knows about a command that git does not: the commit message, for `git commit -m`; and for a rebase, the branch it
/// is rebased onto and whether it was started as an interactive one.
/// </summary>
public sealed record CommandHints(string? Subject = null, string? Body = null, string? Onto = null, bool Interactive = false)
{
    public static readonly CommandHints None = new();
}

public enum DisplayKind { Command, Note }

/// <summary>
/// One line of the simplified Commands pane. <see cref="Exact"/> is true when the text is the command exactly as it ran,
/// so the simplified and exact lists show the same thing.
/// </summary>
public sealed record DisplayLine(DisplayKind Kind, string Text, bool Exact);

/// <summary>
/// Turns one git invocation into the line the simplified Commands pane shows. Pure: it only reads the result.
/// Git still runs exactly as before; this only decides what the user reads.
/// </summary>
public static class CommandDisplay
{
    public const string UndoSavedNote = "↶ saved an undo step (u)";

    private const string UndoRefPrefix = "refs/graftui/undo/";
    private const string GraftuiRefPrefix = "refs/graftui/";
    private const string IndexPrefix = "GIT_INDEX_FILE=";

    // Plumbing and read-only queries: they run, but the simplified pane does not list them unless they fail.
    private static readonly HashSet<string> HiddenSubcommands =
    [
        GitArgs.RevParse, "symbolic-ref", "status", "diff", "for-each-ref", "ls-files", "ls-tree", "log", "rev-list",
        "merge-base", "show", "cat-file", "write-tree", "mktree", GitArgs.CommitTree, "check-ref-format",
    ];

    // Flags that add nothing a reader needs. Dropping them only changes what is shown.
    private static readonly HashSet<string> DroppedFlags =
    [
        "-q", GitArgs.Quiet, GitArgs.NoGpgSign, "--no-show-signature", "--no-color", "--no-ext-diff", "--no-edit", "--no-guess",
    ];

    /// <summary>
    /// The simplified line for <paramref name="r"/>, or null when the simplified pane does not show it.
    /// A failure is always shown exactly as it ran, so the reason is never hidden behind a tidied form.
    /// </summary>
    public static DisplayLine? Simplify(GitResult r, CommandHints h)
    {
        if (IsFailure(r))
            return new DisplayLine(DisplayKind.Command, r.Command, Exact: true);

        if (IsUndoStep(r))
            return new DisplayLine(DisplayKind.Note, UndoSavedNote, Exact: false);

        if (IsHidden(r))
            return null;

        var text = Text(r, h);
        return new DisplayLine(DisplayKind.Command, text, Exact: text == r.Command);
    }

    /// <summary>
    /// The log entry for one git invocation: its exact lines (the command and, when <paramref name="showOutput"/>, its output) and its
    /// simplified lines (none when the simplified pane leaves the command out; the undo note for an undo step).
    /// </summary>
    public static LogEntry Entry(GitResult r, CommandHints h, bool showOutput)
    {
        var failed = IsFailure(r);
        List<LogLine> outputs = showOutput ? OutputLines(r) : [];

        List<LogLine> exact = [new LogLine(LogKind.Command, r.Command, r.ExitCode, Failed: failed), .. outputs];
        var shown = Simplify(r, h);
        List<LogLine> simple = shown switch
        {
            null => [],
            { Kind: DisplayKind.Note } => [new LogLine(LogKind.Message, shown.Text, 0, new Style(Ink.Dim))],
            _ => [new LogLine(LogKind.Command, shown.Text, r.ExitCode, Simple: true, Failed: failed), .. outputs],
        };
        return new LogEntry(exact, simple);
    }

    /// <summary>The non-empty lines of a result's stdout and stderr, each as an output line.</summary>
    internal static List<LogLine> OutputLines(GitResult r) =>
        (r.StdOut + "\n" + r.StdErr)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => new LogLine(LogKind.Output, line))
            .ToList();

    /// <summary>
    /// True when git failed. Exit 1 is an answer, not a failure, for the queries that use it to say "no":
    /// rev-parse with -q or --verify, symbolic-ref -q, diff --quiet and merge-base. check-ref-format answers any non-zero exit.
    /// </summary>
    public static bool IsFailure(GitResult r)
    {
        if (r.ExitCode == 0)
            return false;

        // check-ref-format exits 128 for a name that is not valid: that is the answer, not a failure.
        if (r.Args.Count > 0 && r.Args[0] == "check-ref-format")
            return false;

        return !(r.ExitCode == 1 && IsAnswerQuery(r.Args));
    }

    /// <summary>True for a command the simplified pane leaves out (unless it fails).</summary>
    internal static bool IsHidden(GitResult r)
    {
        if (r.Command.StartsWith(IndexPrefix, StringComparison.Ordinal))
            return true;

        var a = r.Args;
        if (a.Count == 0)
            return false;

        if (HiddenSubcommands.Contains(a[0]))
            return true;

        return a[0] switch
        {
            "branch" => a.Contains("--contains"),
            GitArgs.Stash => a.Count > 1 && a[1] == "list",
            "update-index" => a.Contains("--refresh"),
            GitArgs.UpdateRef => a.Skip(1).Any(x => x.StartsWith(GraftuiRefPrefix, StringComparison.Ordinal)),
            _ => false,
        };
    }

    /// <summary>The arguments as the simplified pane shows them: no noise flags, short hashes, no needless `--`.</summary>
    internal static IReadOnlyList<string> Tidy(IReadOnlyList<string> args)
    {
        var isRestore = args.Count > 0 && args[0] == "restore";
        var staged = args.Contains("--staged");

        var tidied = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (DroppedFlags.Contains(a))
                continue;

            // A restore that touches the index only is written without --worktree.
            if (isRestore && !staged && a == "--worktree")
                continue;

            // `--` only matters when a later path starts with '-'.
            if (a == "--" && !args.Skip(i + 1).Any(x => x.StartsWith('-')))
                continue;

            tidied.Add(ShortHash(a));
        }

        return tidied;
    }

    private static string Text(GitResult r, CommandHints h)
    {
        if (r.Args.Count == 0)
            return r.Command;

        if (r.Args[0] == "commit")
            return CommitText(r.Args, h);

        if (r.Args[0] == "rebase" && h.Onto is not null && r.Args.Contains("-i"))
            return RebaseLine(h.Onto, h.Interactive);

        return GitCommandLine.Render(Tidy(r.Args));
    }

    /// <summary>
    /// `git rebase main`, or `git rebase -i main` for an interactive rebase. Git runs both as `rebase -i` (the engine writes the list of
    /// steps itself), so this is the line the user would type, not the one that ran. `v` shows the exact one.
    /// </summary>
    private static string RebaseLine(string onto, bool interactive)
    {
        List<string> words = ["rebase"];
        if (interactive)
            words.Add("-i");
        words.Add(onto);
        return GitCommandLine.Render(words);
    }

    /// <summary>`git commit [--amend] -m "subject" [-m "first body line[…]"]`. The message file is not shown.</summary>
    private static string CommitText(IReadOnlyList<string> args, CommandHints h)
    {
        var sb = new StringBuilder("git commit");
        if (args.Contains("--amend"))
            sb.Append(" --amend");

        if (h.Subject is not null)
            sb.Append(" -m ").Append(GitCommandLine.Quote(h.Subject, always: true));

        if (FirstBodyLine(h.Body) is { } body)
            sb.Append(" -m ").Append(GitCommandLine.Quote(body.More ? body.Line + "…" : body.Line, always: true));

        return sb.ToString();
    }

    private static (string Line, bool More)? FirstBodyLine(string? body)
    {
        if (body is null)
            return null;

        var lines = body.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0)
            return null;

        return (lines[0].Trim(), lines.Count > 1);
    }

    /// <summary>A full object name (40 or 64 hex digits) becomes its first 7; `--source=` keeps its prefix.</summary>
    private static string ShortHash(string a)
    {
        if (IsHash(a))
            return a[..7];

        if (a.StartsWith(GitArgs.SourcePrefix, StringComparison.Ordinal) && IsHash(a[GitArgs.SourcePrefix.Length..]))
            return GitArgs.SourcePrefix + a[GitArgs.SourcePrefix.Length..][..7];

        return a;
    }

    private static bool IsHash(string s) =>
        (s.Length == 40 || s.Length == 64) && s.All(char.IsAsciiHexDigit);

    private static bool IsAnswerQuery(IReadOnlyList<string> a)
    {
        if (a.Count == 0)
            return false;

        return a[0] switch
        {
            GitArgs.RevParse => a.Any(x => x is "-q" or GitArgs.Quiet or GitArgs.Verify),
            "symbolic-ref" => a.Any(x => x is "-q" or GitArgs.Quiet),
            "diff" => a.Contains(GitArgs.Quiet),
            "merge-base" => true,
            _ => false,
        };
    }

    /// <summary>A successful `update-ref refs/graftui/undo/NNNNNN &lt;commit&gt; ""`: the step was saved.</summary>
    private static bool IsUndoStep(GitResult r)
    {
        var a = r.Args;
        return r.Success
            && a.Count == 4
            && a[0] == GitArgs.UpdateRef
            && a[1].StartsWith(UndoRefPrefix, StringComparison.Ordinal)
            && a[3].Length == 0;
    }
}
