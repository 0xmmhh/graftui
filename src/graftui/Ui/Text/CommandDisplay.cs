using System.Text;
using graftui.Git;

namespace graftui.Ui.Text;

/// <summary>What the operation knows about a command that git does not: the commit message, for `git commit -m`.</summary>
public sealed record CommandHints(string? Subject = null, string? Body = null)
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
        "rev-parse", "symbolic-ref", "status", "diff", "for-each-ref", "ls-files", "ls-tree", "log", "rev-list",
        "merge-base", "show", "cat-file", "write-tree", "mktree", "commit-tree",
    ];

    // Flags that add nothing a reader needs. Dropping them only changes what is shown.
    private static readonly HashSet<string> DroppedFlags =
    [
        "-q", "--quiet", "--no-gpg-sign", "--no-show-signature", "--no-color", "--no-ext-diff", "--no-edit",
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
    /// True when git failed. Exit 1 is an answer, not a failure, for the queries that use it to say "no":
    /// rev-parse with -q or --verify, symbolic-ref -q, diff --quiet and merge-base.
    /// </summary>
    public static bool IsFailure(GitResult r)
    {
        if (r.ExitCode == 0)
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
            "stash" => a.Count > 1 && a[1] == "list",
            "update-index" => a.Contains("--refresh"),
            "update-ref" => a.Skip(1).Any(x => x.StartsWith(GraftuiRefPrefix, StringComparison.Ordinal)),
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

    /// <summary>Quotes an argument for display. Plain words stay bare; empty, spaced or special arguments go in double quotes.</summary>
    internal static string Quote(string arg, bool always = false)
    {
        if (!always && arg.Length > 0 && arg.All(IsPlain))
            return arg;

        var sb = new StringBuilder(arg.Length + 2).Append('"');
        foreach (var c in arg)
        {
            if (c is '\\' or '"' or '$' or '`')
                sb.Append('\\');
            sb.Append(c);
        }

        return sb.Append('"').ToString();
    }

    private static bool IsPlain(char c) => char.IsAsciiLetterOrDigit(c) || "_./:@{}^~=,+-".Contains(c);

    private static string Text(GitResult r, CommandHints h)
    {
        if (r.Args.Count == 0)
            return r.Command;

        if (r.Args[0] == "commit")
            return CommitText(r.Args, h);

        return "git " + string.Join(' ', Tidy(r.Args).Select(t => Quote(t)));
    }

    /// <summary>`git commit [--amend] -m "subject" [-m "first body line[…]"]`. The message file is not shown.</summary>
    private static string CommitText(IReadOnlyList<string> args, CommandHints h)
    {
        var sb = new StringBuilder("git commit");
        if (args.Contains("--amend"))
            sb.Append(" --amend");

        if (h.Subject is not null)
            sb.Append(" -m ").Append(Quote(h.Subject, always: true));

        if (FirstBodyLine(h.Body) is { } body)
            sb.Append(" -m ").Append(Quote(body.More ? body.Line + "…" : body.Line, always: true));

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

        if (a.StartsWith("--source=", StringComparison.Ordinal) && IsHash(a["--source=".Length..]))
            return "--source=" + a["--source=".Length..][..7];

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
            "rev-parse" => a.Any(x => x is "-q" or "--quiet" or "--verify"),
            "symbolic-ref" => a.Any(x => x is "-q" or "--quiet"),
            "diff" => a.Contains("--quiet"),
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
            && a[0] == "update-ref"
            && a[1].StartsWith(UndoRefPrefix, StringComparison.Ordinal)
            && a[3].Length == 0;
    }
}
