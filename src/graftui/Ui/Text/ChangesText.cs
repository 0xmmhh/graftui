using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

/// <summary>
/// The words of the Changes tab: the direction line, the hint under the diff, the operation lines and the summary.
/// Pure text: no git, no views.
/// </summary>
public static class ChangesText
{
    private const int VerbCols = 13;
    private const string Arrow = "  ->  ";

    private static readonly Style Fg = new(Ink.Fg), FgB = new(Ink.Fg, true), Dim = new(Ink.Dim);
    private static readonly Style Ok = new(Ink.Ok), Inc = new(Ink.Inc);
    private static readonly Style CurB = new(Ink.Cur, true), Hash = new(Ink.Hash);

    /// <summary>The line above the diff: the selected file and its state, or the clean-tree line when no row is selected.</summary>
    public static IReadOnlyList<Seg> BrowseDirection(FileChange? f, HeadLine h, int max)
    {
        if (f is null)
            return [new Seg("working tree clean", Ok), new Seg($"   on {BranchName(h)}", Dim)];

        var rest = $"   {(f.Side == ChangeSide.Staged ? "staged" : "not staged")} · {StatusWord(f)}";
        var path = TextFit.Fit(f.Path, Math.Max(8, max - TextFit.Columns(rest)));
        return [new Seg(path, FgB), new Seg(rest, Dim)];
    }

    /// <summary>What a status letter means in words.</summary>
    public static string StatusWord(FileChange f) => f.Kind switch
    {
        ChangeKind.Modified => "modified",
        ChangeKind.Untracked => "new, not tracked yet",
        ChangeKind.Added => "new",
        ChangeKind.Deleted => "deleted",
        ChangeKind.Renamed => f.OrigPath is null ? "renamed" : $"renamed from {f.OrigPath}",
        ChangeKind.Copied => f.OrigPath is null ? "copied" : $"copied from {f.OrigPath}",
        ChangeKind.TypeChanged => "type changed",
        _ => "conflicted",
    };

    /// <summary>
    /// The line of an operation: the verb (padded to 13 columns), what it acts on, an arrow, and the target.
    /// Example: "stage        README.md  ->  staging area".
    /// </summary>
    public static IReadOnlyList<Seg> OpDirection(string verb13, string what, IReadOnlyList<Seg> target, int max)
    {
        var verb = verb13.PadRight(VerbCols);
        var fixedCols = VerbCols + TextFit.Columns(Arrow) + target.Sum(s => TextFit.Columns(s.Text));
        var whatText = TextFit.Fit(what, Math.Max(8, max - fixedCols));
        return
        [
            new Seg(verb, Dim),
            new Seg(whatText, FgB),
            new Seg(Arrow, Dim),
            .. target,
        ];
    }

    /// <summary>
    /// The line of an operation with no target: the verb (padded to 13 columns) and what it acts on, with no arrow.
    /// Example: "stash pop    wip test".
    /// </summary>
    public static IReadOnlyList<Seg> PlainDirection(string verb13, string what, int max)
    {
        var verb = verb13.PadRight(VerbCols);
        return [new Seg(verb, Dim), new Seg(TextFit.Fit(what, Math.Max(8, max - VerbCols)), FgB)];
    }

    /// <summary>The name of what an operation acts on: "README.md", or "3 files" when it covers every row.</summary>
    public static string What(IReadOnlyList<FileChange> files, bool all) =>
        all ? MergeScenes.Plural(files.Count, "file") : files[0].Path;

    /// <summary>
    /// The one line under the art: "HEAD  main be0126e  ·  stash 0  ·  Add app". The stash count comes before the subject, so a
    /// narrow pane drops the subject first. Null stash leaves the count out.
    /// </summary>
    public static IReadOnlyList<Seg> HeadStashLine(HeadLine h, StashSummary? stash)
    {
        var line = new List<Seg> { new("HEAD  ", Dim), new(BranchName(h), CurB) };
        var tip = h.Tip;
        if (tip is null)
            line.Add(new Seg(" no commits", Dim));
        else
        {
            line.Add(new Seg(" ", Fg));
            line.Add(new Seg(tip.ShortHash, Hash));
        }

        if (stash is not null)
        {
            line.Add(new Seg("  ·  ", Dim));
            line.Add(new Seg($"stash {stash.Count}", stash.Count > 0 ? Inc : Dim));
        }

        if (tip is not null)
        {
            line.Add(new Seg("  ·  ", Dim));
            line.Add(new Seg(TextFit.Fit(tip.Subject, 40), Fg));
        }
        return line;
    }

    private static string BranchName(HeadLine h) => h.Detached ? "HEAD (detached)" : h.Branch;
}
