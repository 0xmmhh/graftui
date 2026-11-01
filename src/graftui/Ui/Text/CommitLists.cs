using graftui.Model;
using graftui.Ui.Scenes;

namespace graftui.Ui.Text;

/// <summary>
/// One row of the commit list: a dot, a short hash and a subject, each in its own style. Date is the commit's date, when it has one
/// (the age is drawn at the end of the row).
/// </summary>
public sealed record CommitRow(char Dot, Style DotStyle, string Hash, Style HashStyle, string Subject, Style SubjectStyle, DateTimeOffset? Date = null);

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
    private static readonly Style MrgB = new(Ink.Mrg, true), BadB = new(Ink.Bad, true);

    // A branch's header and dots are in its own lane colour: the current branch's, and the selected branch's.
    private static Style CurB(GraphLayout L) => new(BranchInks.Current(L), true);

    private static Style SelB(GraphLayout L) => new(BranchInks.Selected(L), true);

    /// <summary>
    /// Before the merge: the selected branch's own commits (newest first) beside main's own commits that the branch lacks.
    /// When the branch is not ahead of main, or main has nothing the branch lacks, the group shows the tip.
    /// </summary>
    public static CommitPanel Idle(GraphLayout L)
    {
        if (L.Kind is GraphKind.Same or GraphKind.UpToDate)
            return new CommitPanel(TipGroup(L), null, []);

        var left = new CommitGroup(
            [new Seg(L.Selected, SelB(L)), new Seg($"  {L.Ahead} new", Dim)],
            NewRows(L.SelectedNew, L.SelectedOwn, SelB(L)),
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
                    new CommitRow('◉', MrgB, MergeScenes.Short(o.NewHead), new Style(Ink.Hash), mergeCommit?.Subject ?? "", MrgB, mergeCommit?.Date),
                    .. NewRows(L.SelectedNew, L.SelectedOwn, SelB(L)),
                ];
                return new CommitPanel(new CommitGroup(CurHeader(L, $"  {L.Ahead + 1} new"), rows, L.Ahead + 1), null, []);
            }

            case MergeKind.FastForward:
                return new CommitPanel(new CommitGroup(CurHeader(L, $"  {L.Ahead} new"), NewRows(L.SelectedNew, L.SelectedOwn, CurB(L)), L.Ahead), null, []);

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
        var nowB = SelB(L);
        var wasB = CurB(L);
        if (L.Kind is GraphKind.Same or GraphKind.UpToDate)
            return new CommitPanel(new CommitGroup(NowHeader(now, nowB, "  tip"), [R(L.SelectedTipCommit, nowB)], 1), null, []);

        var left = new CommitGroup(NowHeader(now, nowB, $"  {L.Ahead} new"), NewRows(L.SelectedNew, L.SelectedOwn, nowB), L.Ahead);
        var right = L.Behind == 0
            ? new CommitGroup(WasHeader(was, wasB, "  tip"), [R(L.CurrentTipCommit, wasB)], 1)
            : new CommitGroup(WasHeader(was, wasB, $"  {L.Behind} new"), NewRows(L.CurrentNew, L.CurrentOwn, wasB), L.Behind);
        return new CommitPanel(left, right, []);
    }

    // The header of the branch that is now current, in its own lane colour (bold), with "(current)" after its name.
    private static IReadOnlyList<Seg> NowHeader(string name, Style bold, string tail) =>
        [new Seg(name, bold), new Seg(" (current)", Dim), new Seg(tail, Dim)];

    // The header of the branch that was current, in its own lane colour (bold).
    private static IReadOnlyList<Seg> WasHeader(string name, Style bold, string tail) =>
        [new Seg(name, bold), new Seg(tail, Dim)];

    // The current branch's header: its name with "(current)" (or "HEAD (detached)"), then the tail (a count or "tip").
    private static IReadOnlyList<Seg> CurHeader(GraphLayout L, string tail) =>
    [
        new Seg(L.Detached ? "HEAD" : L.Current, CurB(L)),
        new Seg(L.Detached ? " (detached)" : " (current)", Dim),
        new Seg(tail, Dim),
    ];

    private static CommitGroup TipGroup(GraphLayout L) =>
        new(CurHeader(L, "  tip"), [R(L.CurrentTipCommit, CurB(L))], 1);

    // main's side of a comparison: the commits it has and the branch lacks (Behind), or its tip when there are none.
    private static CommitGroup MainGroup(GraphLayout L) =>
        L.Behind == 0
            ? TipGroup(L)
            : new CommitGroup(CurHeader(L, $"  {L.Behind} new"), NewRows(L.CurrentNew, L.CurrentOwn, CurB(L)), L.Behind);

    // The stored lists run oldest first; the commit list shows the newest first.
    private static List<CommitRow> OwnRows(IEnumerable<CommitInfo> oldestFirst, Style ink) =>
        oldestFirst.Reverse().Select(c => R(c, ink)).ToList();

    // The rows of a group: the commits its header counts, newest first, from the layout's full list (up to 50). When the layout has
    // no full list, the few commits the graph keeps (oldest first) stand in.
    private static List<CommitRow> NewRows(IReadOnlyList<CommitInfo> newestFirst, IEnumerable<CommitInfo> graphOldestFirst, Style ink)
    {
        if (newestFirst.Count == 0)
            return OwnRows(graphOldestFirst, ink);
        return newestFirst.Select(c => R(c, ink)).ToList();
    }

    private static CommitRow R(CommitInfo c, Style ink) =>
        new(MergeScenes.Dot(c), ink, c.ShortHash, new Style(Ink.Hash), c.Subject, Fg, c.Date);
}

/// <summary>
/// Draws a <see cref="CommitPanel"/> as the lines of the commit area. Pure: every line is exactly the width asked for, and the
/// lines never go past the number of rows asked for.
/// </summary>
public static class CommitPanels
{
    /// <summary>Screens at least this wide list the two groups side by side; narrower screens stack them.</summary>
    public const int SideBySideMinCols = 70;

    // The fewest columns a subject keeps when its age is drawn after it. Narrower rows leave the age out.
    private const int MinSubjectCols = 4;

    // The columns a short hash is padded to.
    private const int HashCols = 7;

    private static readonly Style Fg = new(Ink.Fg), Dim = new(Ink.Dim), Frame = new(Ink.Frame);

    /// <summary>The lines of the commit area. <paramref name="now"/> is the time the commit ages are counted from.</summary>
    public static IReadOnlyList<IReadOnlyList<Seg>> Render(CommitPanel p, int width, int rows, bool sideBySide, DateTimeOffset now)
    {
        if (width <= 0 || rows <= 0)
            return [];

        return p.Right is { } right && sideBySide
            ? SideBySide(p, right, width, rows, now)
            : Stacked(p, width, rows, now);
    }

    // One header row, then the body of both groups side by side, then the footer rows across the full width.
    private static IReadOnlyList<IReadOnlyList<Seg>> SideBySide(CommitPanel p, CommitGroup right, int width, int rows, DateTimeOffset now)
    {
        var leftW = (width - 3) / 2;
        var rightW = width - 3 - leftW;

        if (rows == 1)
            return [Join(FirstCell(p.Left, leftW, now), FirstCell(right, rightW, now))];

        var footer = Math.Min(p.Footer.Count, Math.Max(0, rows - 2));
        var budget = rows - 1 - footer;

        List<IReadOnlyList<Seg>> lines = [Join(Header(p.Left.Header, leftW), Header(right.Header, rightW))];
        var leftBody = Body(p.Left, budget, leftW, now);
        var rightBody = Body(right, budget, rightW, now);
        for (var i = 0; i < Math.Max(leftBody.Count, rightBody.Count); i++)
            lines.Add(Join(i < leftBody.Count ? leftBody[i] : Cell([], leftW), i < rightBody.Count ? rightBody[i] : Cell([], rightW)));

        lines.AddRange(p.Footer.Take(footer).Select(row => RowCell(row, width, now)));
        return lines;
    }

    private static bool HasRows(CommitGroup group)
    {
        return group.Rows.Count > 0;
    }

    // Each group has a header line (or, when the rows are too few for headers, only its first row), then the footer.
    private static IReadOnlyList<IReadOnlyList<Seg>> Stacked(CommitPanel p, int width, int rows, DateTimeOffset now)
    {
        var groups = p.Right is { } right ? new[] { p.Left, right } : new[] { p.Left };
        List<IReadOnlyList<Seg>> lines = [];

        if (rows < 2 * groups.Length)
        {
            var footer = Math.Min(p.Footer.Count, Math.Max(0, rows - groups.Length));
            var body = rows - footer;
            // lines is still empty here, so taking the first `body` groups that have a row
            // adds the same rows as a "while there is room" check would.
            foreach (var group in groups.Where(HasRows).Take(body))
                lines.Add(RowCell(group.Rows[0], width, now));
            lines.AddRange(p.Footer.Take(footer).Select(row => RowCell(row, width, now)));
            return lines;
        }

        var footerRows = Math.Min(p.Footer.Count, rows - 2 * groups.Length);
        var budgets = Budgets(groups, rows - footerRows - groups.Length);
        for (var i = 0; i < groups.Length; i++)
        {
            lines.Add(Header(groups[i].Header, width));
            lines.AddRange(Body(groups[i], budgets[i], width, now));
        }
        lines.AddRange(p.Footer.Take(footerRows).Select(row => RowCell(row, width, now)));
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
    private static List<IReadOnlyList<Seg>> Body(CommitGroup g, int budget, int width, DateTimeOffset now)
    {
        List<IReadOnlyList<Seg>> lines = [];
        if (budget <= 0)
            return lines;

        var known = g.Rows.Count;
        if (g.Total <= budget && known >= g.Total)
        {
            lines.AddRange(g.Rows.Select(row => RowCell(row, width, now)));
            return lines;
        }

        if (budget == 1 && known > 0)
        {
            lines.Add(RowCell(g.Rows[0], width, now));
            return lines;
        }

        var shown = Math.Min(known, budget - 1);
        lines.AddRange(g.Rows.Take(shown).Select(row => RowCell(row, width, now)));
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

    // The first row of a group, or blank when the group has none.
    private static IReadOnlyList<Seg> FirstCell(CommitGroup g, int width, DateTimeOffset now) =>
        g.Rows.Count > 0 ? RowCell(g.Rows[0], width, now) : Cell([], width);

    // One commit row, exactly `width` columns: dot, hash and subject, then the age in dim at the end. The subject is cut first.
    // When fewer than MinSubjectCols would be left for it, the age is left out and the row is drawn as dot, hash and subject only.
    private static IReadOnlyList<Seg> RowCell(CommitRow r, int width, DateTimeOffset now)
    {
        if (!r.Date.HasValue)
            return Cell(RowSegs(r), width);

        var age = CommitAge.Short(r.Date.Value, now);
        var hash = r.Hash.PadRight(HashCols);
        var prefixCols = 1 + 1 + TextFit.Columns(hash) + 1; // dot, space, hash, space
        var subjectCols = width - prefixCols - 1 - age.Length; // one space, then the age
        if (subjectCols < MinSubjectCols)
            return Cell(RowSegs(r), width);

        var subject = TextFit.Fit(TextFit.Sanitize(r.Subject), subjectCols);
        subject += new string(' ', subjectCols - TextFit.Columns(subject));
        return
        [
            new Seg(r.Dot.ToString(), r.DotStyle),
            new Seg(" ", Fg),
            new Seg(hash, r.HashStyle),
            new Seg(" ", Fg),
            new Seg(subject, r.SubjectStyle),
            new Seg(" ", Fg),
            new Seg(age, Dim),
        ];
    }

    private static IReadOnlyList<Seg> RowSegs(CommitRow r) =>
    [
        new Seg(r.Dot.ToString(), r.DotStyle),
        new Seg(" ", Fg),
        new Seg(r.Hash.PadRight(HashCols), r.HashStyle),
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
