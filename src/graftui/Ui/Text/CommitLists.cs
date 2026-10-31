using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

/// <summary>One row of the commit list: a dot, a short hash and a subject, each in its own style.</summary>
public sealed record CommitRow(char Dot, Style DotStyle, string Hash, Style HashStyle, string Subject, Style SubjectStyle);

/// <summary>
/// One branch's part of the commit list: its header (name and count), its rows newest first, and how many commits it has in all.
/// Total can be more than Rows.Count, because the rows are only the newest few.
/// </summary>
public sealed record CommitGroup(IReadOnlyList<Seg> Header, IReadOnlyList<CommitRow> Rows, int Total);

/// <summary>
/// The commit list under the art. Left is the selected branch (after a merge, main); Right is main, when it is listed beside it.
/// Footer rows (the conflicting files) go under both groups.
/// </summary>
public sealed record CommitPanel(CommitGroup Left, CommitGroup? Right, IReadOnlyList<CommitRow> Footer);

/// <summary>The commit list under the art: what the branches look like before a merge and what they became after it.</summary>
public static class CommitLists
{
    private static readonly Style Fg = new(Ink.Fg), Dim = new(Ink.Dim);
    private static readonly Style CurB = new(Ink.Cur, true), IncB = new(Ink.Inc, true), MrgB = new(Ink.Mrg, true), BadB = new(Ink.Bad, true);

    /// <summary>
    /// Before the merge: the selected branch's own commits (newest first) beside main's own commits that the branch lacks.
    /// When the branch is not ahead of main, or main has nothing the branch lacks, the group shows the tip.
    /// </summary>
    public static CommitPanel Idle(GraphLayout L)
    {
        if (L.Kind is GraphKind.Same or GraphKind.UpToDate)
            return new CommitPanel(TipGroup(L), null, []);

        var left = new CommitGroup(
            [new Seg(L.Selected, IncB), new Seg($"  {L.Ahead} new", Dim)],
            OwnRows(L.SelectedOwn, IncB),
            L.Ahead);
        return new CommitPanel(left, MainGroup(L), []);
    }

    /// <summary>After the merge, by outcome. <paramref name="mergeCommit"/> is the new merge commit, when there is one.</summary>
    public static CommitPanel After(GraphLayout L, MergeOutcome o, CommitInfo? mergeCommit)
    {
        switch (o.Kind)
        {
            case MergeKind.MergeCommit:
            {
                List<CommitRow> rows =
                [
                    new CommitRow('◉', MrgB, MergeScenes.Short(o.NewHead), new Style(Ink.Hash), mergeCommit?.Subject ?? "", MrgB),
                    .. OwnRows(L.SelectedOwn, IncB),
                ];
                return new CommitPanel(new CommitGroup(CurHeader(L, $"  {L.Ahead + 1} new"), rows, L.Ahead + 1), null, []);
            }

            case MergeKind.FastForward:
                return new CommitPanel(new CommitGroup(CurHeader(L, $"  {L.Ahead} new"), OwnRows(L.SelectedOwn, CurB), L.Ahead), null, []);

            case MergeKind.Conflict:
                var files = o.ConflictFiles
                    .Select(file => new CommitRow('✕', BadB, file, BadB, "conflict (merge aborted)", BadB))
                    .ToList();
                return Idle(L) with { Footer = files };

            default: // UpToDate and Failed
                return new CommitPanel(TipGroup(L), null, []);
        }
    }

    /// <summary>
    /// The panel after a switch: the same columns as before, and only the "(current)" header moves to the branch that is now current,
    /// as the graph's label does. <paramref name="L"/> is the layout the switch was drawn from (its Selected is the new current).
    /// </summary>
    public static CommitPanel Switched(GraphLayout L)
    {
        var now = L.Selected;
        var was = L.Detached ? "HEAD" : L.Current;
        if (L.Kind is GraphKind.Same or GraphKind.UpToDate)
            return new CommitPanel(new CommitGroup(NowHeader(now, "  tip"), [R(L.SelectedTipCommit, CurB)], 1), null, []);

        var left = new CommitGroup(NowHeader(now, $"  {L.Ahead} new"), OwnRows(L.SelectedOwn, CurB), L.Ahead);
        var right = L.Behind == 0
            ? new CommitGroup(WasHeader(was, "  tip"), [R(L.CurrentTipCommit, IncB)], 1)
            : new CommitGroup(WasHeader(was, $"  {L.Behind} new"), OwnRows(L.CurrentOwn, IncB), L.Behind);
        return new CommitPanel(left, right, []);
    }

    private static IReadOnlyList<Seg> NowHeader(string name, string tail) =>
        [new Seg(name, CurB), new Seg(" (current)", Dim), new Seg(tail, Dim)];

    private static IReadOnlyList<Seg> WasHeader(string name, string tail) =>
        [new Seg(name, IncB), new Seg(tail, Dim)];

    // The current branch's header: its name with "(current)" (or "HEAD (detached)"), then the tail (a count or "tip").
    private static IReadOnlyList<Seg> CurHeader(GraphLayout L, string tail) =>
    [
        new Seg(L.Detached ? "HEAD" : L.Current, CurB),
        new Seg(L.Detached ? " (detached)" : " (current)", Dim),
        new Seg(tail, Dim),
    ];

    private static CommitGroup TipGroup(GraphLayout L) =>
        new(CurHeader(L, "  tip"), [R(L.CurrentTipCommit, CurB)], 1);

    // main's side of a comparison: the commits it has and the branch lacks (Behind), or its tip when there are none.
    private static CommitGroup MainGroup(GraphLayout L) =>
        L.Behind == 0
            ? TipGroup(L)
            : new CommitGroup(CurHeader(L, $"  {L.Behind} new"), OwnRows(L.CurrentOwn, CurB), L.Behind);

    // The stored lists run oldest first; the commit list shows the newest first.
    private static List<CommitRow> OwnRows(IEnumerable<CommitInfo> oldestFirst, Style ink) =>
        oldestFirst.Reverse().Select(c => R(c, ink)).ToList();

    private static CommitRow R(CommitInfo c, Style ink) =>
        new(MergeScenes.Dot(c), ink, c.ShortHash, new Style(Ink.Hash), c.Subject, Fg);
}

/// <summary>
/// Draws a <see cref="CommitPanel"/> as the lines of the commit area. Pure: every line is exactly the width asked for, and the
/// lines never go past the number of rows asked for.
/// </summary>
public static class CommitPanels
{
    /// <summary>Screens at least this wide list the two groups side by side; narrower screens stack them.</summary>
    public const int SideBySideMinCols = 70;

    private static readonly Style Fg = new(Ink.Fg), Dim = new(Ink.Dim), Frame = new(Ink.Frame);

    public static IReadOnlyList<IReadOnlyList<Seg>> Render(CommitPanel p, int width, int rows, bool sideBySide)
    {
        if (width <= 0 || rows <= 0)
            return [];

        return p.Right is { } right && sideBySide
            ? SideBySide(p, right, width, rows)
            : Stacked(p, width, rows);
    }

    // One header row, then the body of both groups side by side, then the footer rows across the full width.
    private static IReadOnlyList<IReadOnlyList<Seg>> SideBySide(CommitPanel p, CommitGroup right, int width, int rows)
    {
        var leftW = (width - 3) / 2;
        var rightW = width - 3 - leftW;

        if (rows == 1)
            return [Join(Cell(FirstRow(p.Left), leftW), Cell(FirstRow(right), rightW))];

        var footer = Math.Min(p.Footer.Count, Math.Max(0, rows - 2));
        var budget = rows - 1 - footer;

        List<IReadOnlyList<Seg>> lines = [Join(Header(p.Left.Header, leftW), Header(right.Header, rightW))];
        var leftBody = Body(p.Left, budget, leftW);
        var rightBody = Body(right, budget, rightW);
        for (var i = 0; i < Math.Max(leftBody.Count, rightBody.Count); i++)
            lines.Add(Join(i < leftBody.Count ? leftBody[i] : Cell([], leftW), i < rightBody.Count ? rightBody[i] : Cell([], rightW)));

        lines.AddRange(p.Footer.Take(footer).Select(row => Cell(RowSegs(row), width)));
        return lines;
    }

    // Each group has a header line (or, when the rows are too few for headers, only its first row), then the footer.
    private static IReadOnlyList<IReadOnlyList<Seg>> Stacked(CommitPanel p, int width, int rows)
    {
        var groups = p.Right is { } right ? new[] { p.Left, right } : new[] { p.Left };
        List<IReadOnlyList<Seg>> lines = [];

        if (rows < 2 * groups.Length)
        {
            var footer = Math.Min(p.Footer.Count, Math.Max(0, rows - groups.Length));
            var body = rows - footer;
            foreach (var group in groups)
                if (lines.Count < body && group.Rows.Count > 0)
                    lines.Add(Cell(RowSegs(group.Rows[0]), width));
            lines.AddRange(p.Footer.Take(footer).Select(row => Cell(RowSegs(row), width)));
            return lines;
        }

        var footerRows = Math.Min(p.Footer.Count, rows - 2 * groups.Length);
        var budgets = Budgets(groups, rows - footerRows - groups.Length);
        for (var i = 0; i < groups.Length; i++)
        {
            lines.Add(Header(groups[i].Header, width));
            lines.AddRange(Body(groups[i], budgets[i], width));
        }
        lines.AddRange(p.Footer.Take(footerRows).Select(row => Cell(RowSegs(row), width)));
        return lines;
    }

    // Rows for each stacked group, from the body rows left. The first group (the selected branch) takes what it needs first,
    // so the second (main) keeps at most half, and the first then takes what the second leaves.
    private static int[] Budgets(IReadOnlyList<CommitGroup> groups, int avail)
    {
        if (groups.Count == 1)
            return [avail];

        var second = Math.Min(groups[1].Total, avail / 2);
        var first = Math.Min(groups[0].Total, avail - second);
        second = Math.Min(groups[1].Total, avail - first);
        return [first, second];
    }

    // A group's rows within a budget of lines. When the rows do not all fit, the newest rows come first and a last line says how many
    // are left. With one line, only the newest row shows.
    private static List<IReadOnlyList<Seg>> Body(CommitGroup g, int budget, int width)
    {
        List<IReadOnlyList<Seg>> lines = [];
        if (budget <= 0)
            return lines;

        var known = g.Rows.Count;
        if (g.Total <= budget && known >= g.Total)
        {
            lines.AddRange(g.Rows.Select(row => Cell(RowSegs(row), width)));
            return lines;
        }

        if (budget == 1 && known > 0)
        {
            lines.Add(Cell(RowSegs(g.Rows[0]), width));
            return lines;
        }

        var shown = Math.Min(known, budget - 1);
        lines.AddRange(g.Rows.Take(shown).Select(row => Cell(RowSegs(row), width)));
        var more = g.Total - shown;
        if (more > 0)
            lines.Add(Cell([new Seg($"+{more} more", Dim)], width));
        return lines;
    }

    // The header fits its width. When it does not, the count at its end is kept and the name is cut.
    private static IReadOnlyList<Seg> Header(IReadOnlyList<Seg> header, int width)
    {
        var total = header.Sum(s => TextFit.Columns(TextFit.Sanitize(s.Text)));
        if (total <= width || header.Count < 2)
            return Cell(header, width);

        var tail = header[^1];
        var tailCols = TextFit.Columns(TextFit.Sanitize(tail.Text));
        if (tailCols >= width - 4)
            return Cell(header, width);

        return [.. Cell(header.Take(header.Count - 1).ToList(), width - tailCols), tail];
    }

    private static IReadOnlyList<Seg> FirstRow(CommitGroup g) =>
        g.Rows.Count > 0 ? RowSegs(g.Rows[0]) : [];

    private static IReadOnlyList<Seg> RowSegs(CommitRow r) =>
    [
        new Seg(r.Dot.ToString(), r.DotStyle),
        new Seg(" ", Fg),
        new Seg(r.Hash.PadRight(7), r.HashStyle),
        new Seg(" ", Fg),
        new Seg(r.Subject, r.SubjectStyle),
    ];

    private static IReadOnlyList<Seg> Join(IReadOnlyList<Seg> left, IReadOnlyList<Seg> right) =>
    [
        .. left,
        new Seg(" ", Fg),
        new Seg("│", Frame),
        new Seg(" ", Fg),
        .. right,
    ];

    // Exactly `width` columns: the segments cut at the width (with an ellipsis), then padded with spaces.
    private static IReadOnlyList<Seg> Cell(IReadOnlyList<Seg> segs, int width)
    {
        List<Seg> cell = [];
        var used = 0;
        foreach (var s in segs)
        {
            if (used >= width)
                break;

            var text = TextFit.Sanitize(s.Text);
            var cols = TextFit.Columns(text);
            if (used + cols <= width)
            {
                cell.Add(s with { Text = text });
                used += cols;
                continue;
            }

            var cut = TextFit.Fit(text, width - used);
            cell.Add(s with { Text = cut });
            used += TextFit.Columns(cut);
            break;
        }

        if (used < width)
            cell.Add(new Seg(new string(' ', width - used), Fg));
        return cell;
    }
}
