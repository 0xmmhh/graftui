using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Ui.Scenes;

/// <summary>A rendered frame: the art grid and the caption under it (null when the scene has none).</summary>
public sealed record SceneFrame(ArtGrid Grid, Caption? Caption);

/// <summary>
/// The scenes, ported from the design mock. Each function draws one frame from a layout, the effective
/// time <c>te</c> (what the animation has reached), the raw elapsed time <c>t</c> (for spinners and blinks),
/// and the outcome once git has returned one.
/// </summary>
public static class MergeScenes
{
    private static readonly Style Fg = new(Ink.Fg), FgB = new(Ink.Fg, true), Dim = new(Ink.Dim);
    private static readonly Style Cur = new(Ink.Cur), CurB = new(Ink.Cur, true);
    private static readonly Style Inc = new(Ink.Inc), IncB = new(Ink.Inc, true);
    private static readonly Style Mrg = new(Ink.Mrg), MrgB = new(Ink.Mrg, true);
    private static readonly Style Ok = new(Ink.Ok), OkB = new(Ink.Ok, true);
    private static readonly Style Bad = new(Ink.Bad), BadB = new(Ink.Bad, true);
    private static readonly Style Warn = new(Ink.Warn), WarnB = new(Ink.Warn, true);
    private static readonly Style Hash = new(Ink.Hash);

    private const string SpinFrames = "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";
    internal const string WaitingText = "! git is still running (waiting for a password prompt or hook?)";

    /// <summary>Both branches drawn at rest, no animation. Used for the preview and as the base of every scene.</summary>
    public static SceneFrame Preview(ArtGeometry g, GraphLayout L)
    {
        var grid = new ArtGrid(g.Width, g.Height);
        DrawShared(grid, g, L);
        switch (L.Kind)
        {
            case GraphKind.Diverged:
            case GraphKind.Unrelated:
                DrawRowA(grid, g, L.CurrentOwn, Cur, L.CurrentOwnMore);
                DrawRowB(grid, g, L.SelectedOwn, Inc, L.SelectedOwnMore, connect: L.Kind != GraphKind.Unrelated);
                CurLabel(grid, g, g.TipX(L.CurrentOwn.Count), g.A, L);
                SelLabel(grid, g, g.TipX(L.SelectedOwn.Count), g.B, L);
                break;
            case GraphKind.FastForward:
                DrawRowA(grid, g, L.SelectedOwn, Inc, L.SelectedOwnMore);
                CurLabel(grid, g, g.XB, g.A, L);
                SelLabel(grid, g, g.TipX(L.SelectedOwn.Count), g.A, L);
                break;
            case GraphKind.UpToDate:
                DrawRowA(grid, g, L.CurrentOwn, Cur, L.CurrentOwnMore);
                CurLabel(grid, g, g.TipX(L.CurrentOwn.Count), g.A, L);
                SelLabel(grid, g, g.XB, g.A, L);
                break;
            default: // Same: both labels sit on the one commit when the selected branch is a different name
                CurLabel(grid, g, g.XB, g.A, L);
                SelLabel(grid, g, g.XB, g.A, L);
                break;
        }
        return new SceneFrame(grid, null);
    }

    /// <summary>Dims the art and draws a box with the reason. Used for refusals (detached HEAD, dirty tree, ...).</summary>
    public static void RefusedOverlay(ArtGrid grid, string reason) => MessageOverlay(grid, "Refused", reason);

    /// <summary>Dims the art and draws a box with a title and a message. Refusals use "Refused"; failed changes use "Failed".</summary>
    public static void MessageOverlay(ArtGrid grid, string title, string text)
    {
        grid.DimAll();
        var w0 = grid.Width;
        var h0 = grid.Height;

        var maxLine = Math.Max(10, w0 - 10);
        var lines = Wrap(TextFit.Sanitize(text), maxLine);
        var inner = Math.Max(7, lines.Select(TextFit.Columns).DefaultIfEmpty(0).Max());
        var w = inner + 4;
        if (w > w0)
        {
            inner = w0 - 4;
            w = w0;
            lines = lines.Select(l => TextFit.Fit(l, inner)).ToList();
        }
        if (lines.Count > h0 - 3)
            lines = lines.Take(Math.Max(0, h0 - 3)).ToList();

        var h = lines.Count + 3;
        var x0 = (w0 - w) / 2;
        var y0 = Math.Max(0, (h0 - h) / 2);
        grid.Clear(Math.Max(0, x0 - 2), Math.Min(w0 - 1, x0 + w + 1), y0, y0 + h - 1);

        grid.Text(x0, y0, "┌" + new string('─', w - 2) + "┐", Warn);
        grid.Text(x0, y0 + 1, "│ " + Pad(title, inner) + " │", WarnB);
        for (var i = 0; i < lines.Count; i++)
            grid.Text(x0, y0 + 2 + i, "│ " + Pad(lines[i], inner) + " │", Warn);
        grid.Text(x0, y0 + h - 1, "└" + new string('─', w - 2) + "┘", Warn);
    }

    /// <summary>A merge: both lines grow together, meet at a join, then either a gold merge commit or a red conflict abort.</summary>
    public static SceneFrame Merge(ArtGeometry g, GraphLayout L, long te, long t, MergeOutcome? o, bool waiting)
    {
        var grid = new ArtGrid(g.Width, g.Height);
        var s = g.Step;
        var tx = g.TipX(L.CurrentOwn.Count);
        var sx = g.TipX(L.SelectedOwn.Count);
        var mx = Math.Max(tx, sx) + s;

        DrawShared(grid, g, L);
        DrawRowA(grid, g, L.CurrentOwn, Cur, L.CurrentOwnMore);
        DrawRowB(grid, g, L.SelectedOwn, Inc, L.SelectedOwnMore, connect: L.Kind != GraphKind.Unrelated);
        SelLabel(grid, g, sx, g.B, L);

        // Paths the two lines follow to the join: the current line goes right along row A; the selected line goes along row B and up.
        var mp = new List<PathCell>();
        for (var x = tx + 1; x < mx; x++)
            mp.Add(new PathCell(x, g.A, '━', '─'));
        var fp = new List<PathCell>();
        for (var x = sx + 1; x < mx; x++)
            fp.Add(new PathCell(x, g.B, '━', '─'));
        fp.Add(new PathCell(mx, g.B, '┛', '╯'));
        for (var y = g.B - 1; y > g.A; y--)
            fp.Add(new PathCell(mx, y, '┃', '│'));

        var running = Running(waiting, $"{Spin(t)} Merging {L.Selected} into {L.Current}…");

        if (o is null || te < MergeAnimation.MergeGrow)
        {
            var k = Ease(Math.Min(te, MergeAnimation.MergeGrow) / (double)MergeAnimation.MergeGrow);
            DrawPath(grid, mp, Round(k * mp.Count), CurB, heavy: true);
            DrawPath(grid, fp, Round(k * fp.Count), IncB, heavy: true);
            grid.Put(mx, g.A, '◌', Dim);
            CurLabel(grid, g, tx, g.A, L);
            return new SceneFrame(grid, running);
        }

        var ft = te - MergeAnimation.MergeGrow;
        if (o.Kind == MergeKind.Conflict)
        {
            if (ft < MergeAnimation.MergeFlash)
            {
                DrawPath(grid, mp, mp.Count, BadB, heavy: true);
                DrawPath(grid, fp, fp.Count, BadB, heavy: true);
                var on = (t / 110) % 2 == 0;
                grid.Put(mx, g.A, '✕', on ? BadB : FgB);
                if (on)
                {
                    grid.Put(mx - 2, g.A - 1, '╲', Bad);
                    grid.Put(mx + 2, g.A - 1, '╱', Bad);
                    grid.Put(mx - 2, g.A + 1, '╱', Bad);
                    grid.Put(mx + 2, g.A + 1, '╲', Bad);
                }
                CurLabel(grid, g, tx, g.A, L);
                return new SceneFrame(grid, new Caption("✕ Aborting the merge…", Bad));
            }

            if (ft < MergeAnimation.MergeFlash + MergeAnimation.MergeRetract)
            {
                var r = 1 - (ft - MergeAnimation.MergeFlash) / (double)MergeAnimation.MergeRetract;
                DrawPath(grid, mp, Round(r * mp.Count), Bad, heavy: true);
                DrawPath(grid, fp, Round(r * fp.Count), Bad, heavy: true);
                grid.Put(mx, g.A, '✕', BadB);
                CurLabel(grid, g, tx, g.A, L);
                return new SceneFrame(grid, new Caption("✕ Aborting the merge…", Bad));
            }

            grid.Put(mx, g.A, '✕', Bad);
            CurLabel(grid, g, tx, g.A, L);
            return new SceneFrame(grid, new Caption(ConflictCaption(o, L.Current), BadB));
        }

        // Merge commit
        if (ft < MergeAnimation.MergeFlash)
        {
            DrawPath(grid, mp, mp.Count, MrgB, heavy: true);
            DrawPath(grid, fp, fp.Count, MrgB, heavy: true);
            grid.Put(mx, g.A, '◉', Blink(t, MrgB, FgB));
            CurLabel(grid, g, tx, g.A, L);
            return new SceneFrame(grid, running);
        }

        DrawPath(grid, mp, mp.Count, Cur, heavy: false);
        DrawPath(grid, fp, fp.Count, Inc, heavy: false);
        grid.Put(mx, g.A, '◉', MrgB);
        CurLabel(grid, g, mx, g.A, L);
        return new SceneFrame(grid, new Caption($"✔ Merge commit {Short(o.NewHead)} created on {L.Current}", OkB));
    }

    /// <summary>A fast-forward: the selected line is drawn in as the current tip moves onto it.</summary>
    public static SceneFrame FastForward(ArtGeometry g, GraphLayout L, long te, long t, MergeOutcome? o, bool waiting)
    {
        var grid = new ArtGrid(g.Width, g.Height);
        DrawShared(grid, g, L);

        var tip = g.TipX(L.SelectedOwn.Count);
        var px = te < MergeAnimation.FfGrow
            ? g.XB + (int)Math.Round(Ease(te / (double)MergeAnimation.FfGrow) * (tip - g.XB), MidpointRounding.AwayFromZero)
            : tip;

        var p = g.XB;
        for (var i = 0; i < L.SelectedOwn.Count; i++)
        {
            var commit = L.SelectedOwn[i];
            var x = g.XB + g.Step * (i + 1);
            for (var xx = p + 1; xx < x; xx++)
                grid.Put(xx, g.A, i == 0 && L.SelectedOwnMore ? '┄' : '─', xx <= px ? Cur : Inc);
            grid.Put(x, g.A, Dot(commit), x <= px ? Cur : Inc);
            p = x;
        }

        SelLabel(grid, g, tip, g.A, L);
        CurLabel(grid, g, px, g.A, L);

        var running = Running(waiting, $"{Spin(t)} Fast-forwarding {L.Current} to {L.Selected}…");
        if (o is null || te < MergeAnimation.FfGrow)
            return new SceneFrame(grid, running);

        var ft = te - MergeAnimation.FfGrow;
        if (ft < MergeAnimation.FfFlash)
        {
            grid.Put(tip, g.A, Dot(L.SelectedTipCommit), Blink(t, OkB, FgB));
            return new SceneFrame(grid, running);
        }

        var ahead = Plural(L.Ahead, "commit");
        return new SceneFrame(grid, new Caption(
            $"✔ Fast-forward: {L.Current} is now at {Short(o.NewHead)} (+{ahead}, no merge commit)", OkB));
    }

    /// <summary>Already up to date: the current line is drawn, the selected tip is already on it, and the base pulses.</summary>
    public static SceneFrame UpToDate(ArtGeometry g, GraphLayout L, long te, long t, MergeOutcome? o, bool waiting)
    {
        var grid = new ArtGrid(g.Width, g.Height);
        DrawShared(grid, g, L);
        DrawRowA(grid, g, L.CurrentOwn, Cur, L.CurrentOwnMore);
        CurLabel(grid, g, g.TipX(L.CurrentOwn.Count), g.A, L);
        SelLabel(grid, g, g.XB, g.A, L);

        if (o is null || te < MergeAnimation.UtdPulse)
        {
            var baseCommit = L.Kind == GraphKind.Same ? L.CurrentTipCommit : L.SelectedTipCommit;
            grid.Put(g.XB, g.A, Dot(baseCommit), Blink(t, IncB, CurB, 150));
            return new SceneFrame(grid, Running(waiting, $"{Spin(t)} Merging {L.Selected} into {L.Current}…"));
        }

        return new SceneFrame(grid, new Caption($"Already up to date: {L.Selected} is already part of {L.Current}", Dim));
    }

    /// <summary>A failed merge: the plain preview with the failure in the caption.</summary>
    public static SceneFrame Failed(ArtGeometry g, GraphLayout L, MergeOutcome o)
    {
        var frame = Preview(g, L);
        return new SceneFrame(frame.Grid, new Caption($"✕ Merge failed: {o.Message}", BadB));
    }

    // ---------- helpers ----------

    private readonly record struct PathCell(int X, int Y, char Heavy, char Light);

    internal static char Dot(CommitInfo c) => c.IsMerge ? '◉' : '●';

    internal static string Short(string? hash) =>
        string.IsNullOrEmpty(hash) ? "" : hash[..Math.Min(7, hash.Length)];

    internal static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    internal static double Ease(double p) => p < 0.5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2;

    internal static Style Blink(long t, Style a, Style b, int ms = 110) => (t / ms) % 2 == 1 ? b : a;

    internal static char Spin(long t) => SpinFrames[(int)((t / 80) % SpinFrames.Length)];

    private static int Round(double x) => (int)Math.Round(x, MidpointRounding.AwayFromZero);

    private static Caption Running(bool waiting, string text) =>
        waiting ? new Caption(WaitingText, Warn) : new Caption(text, Fg);

    private static string ConflictCaption(MergeOutcome o, string cur) =>
        o.ConflictFiles.Count == 0
            ? $"✕ Merge aborted: {cur} unchanged"
            : $"✕ CONFLICT in {ConflictFiles(o.ConflictFiles)}: merge aborted, {cur} unchanged";

    // " app.txt" / " app.txt +2 more"; long names are shortened so the caption stays near the art width.
    internal static string ConflictFiles(IReadOnlyList<string> files)
    {
        var first = files[0];
        if (TextFit.Columns(first) > 24)
            first = "…" + first[^23..];
        return first + (files.Count > 1 ? $" +{files.Count - 1} more" : "");
    }

    private static void DrawShared(ArtGrid grid, ArtGeometry g, GraphLayout L)
    {
        var s = g.Step;
        var n = L.Shared.Count;
        for (var i = 0; i < n; i++)
        {
            var x = g.XB - s * (n - 1 - i);
            if (i > 0)
                grid.HLine(x - s + 1, x - 1, g.A, Cur);
            else if (L.SharedMore)
                grid.HLine(x - s + 2, x - 1, g.A, Dim, '┄');
            grid.Put(x, g.A, Dot(L.Shared[i]), Cur);
        }
    }

    private static void DrawRowA(ArtGrid grid, ArtGeometry g, IReadOnlyList<CommitInfo> list, Style st, bool more,
        Func<CommitInfo, Style>? colorFor = null)
    {
        DrawRow(grid, g, g.A, list, st, more, colorFor);
    }

    private static void DrawRowB(ArtGrid grid, ArtGeometry g, IReadOnlyList<CommitInfo> list, Style st, bool more,
        bool connect = true, Func<CommitInfo, Style>? colorFor = null)
    {
        if (connect)
        {
            grid.VLine(g.XB, g.A + 1, g.B - 1, st);
            grid.Put(g.XB, g.B, '╰', st);
        }
        DrawRow(grid, g, g.B, list, st, more, colorFor);
    }

    private static void DrawRow(ArtGrid grid, ArtGeometry g, int row, IReadOnlyList<CommitInfo> list, Style st, bool more,
        Func<CommitInfo, Style>? colorFor)
    {
        var px = g.XB;
        for (var i = 0; i < list.Count; i++)
        {
            var x = g.XB + g.Step * (i + 1);
            grid.HLine(px + 1, x - 1, row, st, more && i == 0 ? '┄' : '─');
            grid.Put(x, row, Dot(list[i]), colorFor?.Invoke(list[i]) ?? st);
            px = x;
        }
    }

    private static void DrawPath(ArtGrid grid, List<PathCell> path, int n, Style st, bool heavy)
    {
        for (var i = 0; i < n && i < path.Count; i++)
        {
            var c = path[i];
            grid.Put(c.X, c.Y, heavy ? c.Heavy : c.Light, st);
        }
    }

    // Label: a marker on the branch line and the name (with an optional dim extra) on the row beyond it.
    private static void Label(ArtGrid grid, ArtGeometry g, int x, int row, bool up, string name, string? extra, Ink ink)
    {
        var inkStyle = new Style(ink);
        grid.Put(x, up ? row - 1 : row + 1, up ? '▾' : '▴', inkStyle);

        var ty = up ? row - 2 : row + 2;
        var w = g.Width;
        // Labels are one cell per rune (see ArtGrid.Text), so they are measured in runes, not columns.
        var nameText = name;
        if (Runes(nameText) > w)
            nameText = TextFit.Fit(nameText, w);
        var extraText = extra;
        if (Runes(nameText) + Runes(extraText ?? "") > w)
            extraText = null;
        var len = Runes(nameText) + Runes(extraText ?? "");
        var tx = Math.Max(0, Math.Min(x, w - len));

        grid.Text(tx, ty, nameText, new Style(ink, true));
        if (extraText is not null)
            grid.Text(tx + Runes(nameText), ty, extraText, Dim);
    }

    private static int Runes(string s) => s.EnumerateRunes().Count();

    private static void CurLabel(ArtGrid grid, ArtGeometry g, int x, int row, GraphLayout L)
    {
        var name = L.Detached ? "HEAD" : L.Current;
        var extra = L.Detached ? " (detached)" : " (current)";
        Label(grid, g, x, row, up: true, name, extra, Ink.Cur);
    }

    private static void SelLabel(ArtGrid grid, ArtGeometry g, int x, int row, GraphLayout L)
    {
        // The selected branch is the current one: its label is the current label already. Another branch at the same commit gets its own.
        if (L.Kind == GraphKind.Same && L.Selected == L.Current)
            return;
        Label(grid, g, x, row, up: false, L.Selected, extra: null, Ink.Inc);
    }

    private static string Pad(string s, int cols) => s + new string(' ', Math.Max(0, cols - TextFit.Columns(s)));

    /// <summary>Greedy word wrap by columns. A word longer than the line is cut with "…".</summary>
    private static List<string> Wrap(string text, int maxCols)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = TextFit.Columns(raw) > maxCols ? TextFit.Fit(raw, maxCols) : raw;
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (TextFit.Columns(candidate) <= maxCols)
            {
                current = candidate;
                continue;
            }
            lines.Add(current);
            current = word;
        }
        if (current.Length > 0)
            lines.Add(current);
        return lines;
    }
}
