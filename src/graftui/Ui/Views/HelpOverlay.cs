using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Ui.Views;

/// <summary>One key in the help popup: the key as the key bar shows it, and what it does.</summary>
public sealed record HelpRow(string Key, string Text);

/// <summary>
/// The ? help popup: every key for the current tab and mode, drawn over the art it was opened from. The key bar keeps only
/// the main keys; this lists the rest too. Rows and title come from the key bar's state (pause kind, selected side, ask and
/// enter labels), so the popup always matches the screen. Pure, so the rows and the box are tested without a terminal.
/// </summary>
public static class HelpOverlay
{
    private static readonly Style Box = new(Ink.Warn);
    private static readonly Style KeyStyle = new(Ink.Fg, true);
    private static readonly Style Desc = new(Ink.Fg);
    private static readonly Style Dim = new(Ink.Dim);

    /// <summary>
    /// The title and rows for the key bar's current mode. The commit form, the message box and the yes/no question are not here:
    /// AppController sends their keys to the tab first, so ? types a question mark in them and never opens the popup.
    /// </summary>
    public static (string Title, IReadOnlyList<HelpRow> Rows) ForBar(KeyBarView bar)
    {
        var mode = bar.Mode;
        if (mode == KeyBarMode.Changes)
            return ("Keys — Changes", ChangesRows(bar.ChangesState));
        if (mode == KeyBarMode.Confirm)
            return ("Keys — Rebase", ConfirmRows());
        if (mode == KeyBarMode.Todo)
            return ("Keys — Todo", TodoRows());
        if (bar.BranchesState.Pause is not null)
            return ("Keys — Branches", PauseRows(bar.BranchesState.Pause.Value));
        return ("Keys — Branches", BranchesRows());
    }

    /// <summary>The caption legend for the key bar's mode: only the Branches browse popup sits over the dim shared-history dots.</summary>
    public static string? LegendFor(KeyBarView bar)
    {
        if (bar.Mode == KeyBarMode.Changes || bar.Mode == KeyBarMode.Confirm || bar.Mode == KeyBarMode.Todo)
            return null;
        if (bar.BranchesState.Pause is not null)
            return null;
        return SharedLegend;
    }

    /// <summary>The Branches tab's keys in browse mode. The bar shows only Tab, m, s, u and ?; the rest still work.</summary>
    internal static IReadOnlyList<HelpRow> BranchesRows() =>
    [
        new HelpRow("Tab", "switch tab"),
        new HelpRow("↑↓", "select branch"),
        new HelpRow("m", "merge into current"),
        new HelpRow("s", "switch to branch"),
        new HelpRow("n", "new branch"),
        new HelpRow("R", "rename branch"),
        new HelpRow("D", "delete branch"),
        new HelpRow("r", "rebase onto selected"),
        new HelpRow("i", "interactive rebase"),
        new HelpRow("u", "undo"),
        new HelpRow("v", "exact commands"),
        new HelpRow("l", "commands list"),
        new HelpRow("Esc", "quit"),
    ];

    /// <summary>The Changes tab's keys in browse mode. The bar shows only Tab, Space, c, u and ?; the rest still work.</summary>
    internal static IReadOnlyList<HelpRow> ChangesRows(ChangesKeyState s)
    {
        var rows = new List<HelpRow>
        {
            new HelpRow("Tab", "switch tab"),
            new HelpRow("↑↓", "select file"),
            new HelpRow("Space", StageText(s.SelectedSide)),
            new HelpRow("a", "stage all"),
            new HelpRow("U", "unstage all"),
            new HelpRow("c", "commit staged"),
            new HelpRow("A", "amend last commit"),
            new HelpRow("x", "discard change"),
            new HelpRow("z", "stash all"),
            new HelpRow("Z", "pop stash"),
            new HelpRow("u", "undo"),
            new HelpRow("v", "exact commands"),
            new HelpRow("l", "commands list"),
            new HelpRow("Esc", "quit"),
        };
        return rows;
    }

    private static string StageText(ChangeSide? side)
    {
        if (side == ChangeSide.Staged)
            return "unstage";
        if (side == ChangeSide.Unstaged)
            return "stage";
        return "stage / unstage";
    }

    /// <summary>The paused rebase's keys. A conflict can be skipped; an edit is amended and continued.</summary>
    internal static IReadOnlyList<HelpRow> PauseRows(PauseKind kind)
    {
        var rows = new List<HelpRow>();
        if (kind == PauseKind.Edit)
            rows.Add(new HelpRow("Enter", "amend & continue"));
        else
            rows.Add(new HelpRow("Enter", "continue"));
        if (kind == PauseKind.Conflict)
            rows.Add(new HelpRow("S", "skip this commit"));
        rows.Add(new HelpRow("X", "abort"));
        rows.Add(new HelpRow("Tab", "changes tab"));
        rows.Add(new HelpRow("v", "exact commands"));
        rows.Add(new HelpRow("l", "commands list"));
        rows.Add(new HelpRow("Esc", "quit, the pause stays"));
        return rows;
    }

    /// <summary>The rebase confirm screen's keys.</summary>
    internal static IReadOnlyList<HelpRow> ConfirmRows() =>
        [
            new HelpRow("Enter", "run the rebase"),
            new HelpRow("v", "exact commands"),
            new HelpRow("l", "commands list"),
            new HelpRow("Esc", "back to preview"),
        ];

    /// <summary>The todo list's keys. The bar shows only some of them; the letters still set the row's action.</summary>
    internal static IReadOnlyList<HelpRow> TodoRows() =>
    [
        new HelpRow("↑↓←→", "move cursor"),
        new HelpRow("⇧↑↓", "move row"),
        new HelpRow("Alt+↑↓", "move row"),
        new HelpRow("J K", "move row"),
        new HelpRow("p", "pick"),
        new HelpRow("r", "reword"),
        new HelpRow("e", "edit"),
        new HelpRow("s", "squash"),
        new HelpRow("f", "fixup"),
        new HelpRow("d", "drop"),
        new HelpRow("Enter", "start"),
        new HelpRow("v", "exact commands"),
        new HelpRow("l", "commands list"),
        new HelpRow("Esc", "close"),
    ];

    /// <summary>The legend for the dim dots of the shared history, shown in the Branches popup's caption.</summary>
    public const string SharedLegend = "dim dots: shared history";

    private const int LegendMinWidth = 60;

    /// <summary>
    /// The popup over the art: the dimmed grid behind it with a centred box of key rows in two columns (one when narrow).
    /// Rows that do not fit are replaced by a note naming how many are hidden. The caption says how the popup closes.
    /// </summary>
    public static SceneFrame Frame(ArtGeometry geo, ArtGrid grid, IReadOnlyList<HelpRow> rows, string? legend = null)
    {
        grid.DimAll();
        if (rows.Count == 0 || geo.Width < 20 || geo.Height < 4)
            return new SceneFrame(grid, new Caption("Widen the window to see every key", Dim));

        // The left column takes the first rows that fit. The right column starts right after the last of them, so no row is skipped,
        // and the note takes the right column's last slot when rows are hidden. Hidden rows only happen when the height is short
        // (room is at least 2, so a hidden row always has a right column to sit in).
        var room = geo.Height - 2;
        var leftCount = Math.Min((rows.Count + 1) / 2, room);
        var rightCount = Math.Min(rows.Count - leftCount, room);
        var hidden = rows.Count - leftCount - rightCount;

        var left = TakeRows(rows, 0, leftCount);
        var right = TakeRows(rows, leftCount, rightCount);
        if (hidden > 0)
            right[right.Count - 1] = MoreNote(hidden, true);

        var leftKey = KeyWidth(left);
        var rightKey = KeyWidth(right);
        var descBudget = geo.Width - 6 - leftKey - rightKey - 2;
        List<HelpRow> single;
        if (descBudget >= 16)
        {
            // Two columns already fit, so only the height hides rows: a taller window shows more.
            var each = descBudget / 2;
            DrawColumns(geo, grid, FitRows(left, leftKey, each), FitRows(right, rightKey, each), leftKey, rightKey);
        }
        else if (FitSingle(grid, geo, rows, room, out single))
        {
            DrawSingle(geo, grid, single);
        }
        else
        {
            return new SceneFrame(grid, new Caption("Widen the window to see every key", Dim));
        }
        // The legend explains something in the art behind the popup, so it goes in the caption, never in a key row that could push Esc out.
        var closes = "? closes · Esc closes";
        if (legend is not null && geo.Width >= LegendMinWidth)
            closes = legend + " · " + closes;
        return new SceneFrame(grid, new Caption(closes, Dim));
    }

    /// <summary>
    /// The note that stands in for the last shown row when rows do not fit. The note takes that row's slot, so the rows it
    /// stands for are the hidden ones plus the slot's own row: <paramref name="hiddenRows"/> counts only the hidden ones.
    /// <paramref name="heightIsLimit"/> is true when two columns already fit, so only a taller window can show more rows. False
    /// when the width is the limit (one column): a wider window can then change to two columns.
    /// </summary>
    private static HelpRow MoreNote(int hiddenRows, bool heightIsLimit)
    {
        var advice = heightIsLimit ? "make the window taller" : "widen the window";
        return new HelpRow("…", (hiddenRows + 1) + " more: " + advice);
    }

    private static List<HelpRow> TakeRows(IReadOnlyList<HelpRow> rows, int start, int count)
    {
        var taken = new List<HelpRow>();
        for (var i = start; i < start + count && i < rows.Count; i++)
            taken.Add(rows[i]);
        return taken;
    }

    private static int KeyWidth(List<HelpRow> rows)
    {
        var width = 0;
        foreach (var row in rows)
            width = Math.Max(width, TextFit.Columns(row.Key));
        return width;
    }

    private static int DescWidth(List<HelpRow> rows)
    {
        var width = 0;
        foreach (var row in rows)
            width = Math.Max(width, TextFit.Columns(row.Text));
        return width;
    }

    private static List<HelpRow> FitRows(List<HelpRow> rows, int keyWidth, int descBudget)
    {
        var fitted = new List<HelpRow>();
        foreach (var row in rows)
            fitted.Add(new HelpRow(PadKey(row.Key, keyWidth), TextFit.Fit(row.Text, descBudget)));
        return fitted;
    }

    private static string PadKey(string key, int width) => key + new string(' ', Math.Max(0, width - TextFit.Columns(key)));

    private static void DrawColumns(ArtGeometry geo, ArtGrid grid, List<HelpRow> left, List<HelpRow> right, int leftKey, int rightKey)
    {
        var lines = Math.Max(left.Count, right.Count);
        var leftW = leftKey + 1 + DescWidth(left);
        var rightW = rightKey + 1 + DescWidth(right);
        var boxW = Math.Min(geo.Width, leftW + 2 + rightW + 4);
        var boxH = lines + 2;
        var x0 = Math.Max(0, (geo.Width - boxW) / 2);
        var y0 = Math.Max(0, (geo.Height - boxH) / 2);
        DrawBox(grid, x0, y0, boxW, boxH);
        for (var i = 0; i < lines; i++)
        {
            var y = y0 + 1 + i;
            var x = x0 + 2;
            if (i < left.Count)
            {
                grid.Text(x, y, left[i].Key, KeyStyle);
                grid.Text(x + leftKey + 1, y, left[i].Text, Desc);
            }
            x += leftW + 2;
            if (i < right.Count)
            {
                grid.Text(x, y, right[i].Key, KeyStyle);
                grid.Text(x + rightKey + 1, y, right[i].Text, Desc);
            }
        }
    }

    /// <summary>One column when the art is too narrow for two. False when even one column cannot show a key and a word.</summary>
    private static bool FitSingle(ArtGrid grid, ArtGeometry geo, IReadOnlyList<HelpRow> rows, int room, out List<HelpRow> fitted)
    {
        fitted = [];
        var shown = Math.Min(rows.Count, room);
        var hidden = rows.Count - shown;
        var keys = new List<HelpRow>();
        for (var i = 0; i < shown; i++)
            keys.Add(rows[i]);
        if (hidden > 0)
            keys[keys.Count - 1] = MoreNote(hidden, false);
        var keyW = KeyWidth(keys);
        var descW = geo.Width - 5 - keyW;
        if (descW < 8)
            return false;
        fitted = FitRows(keys, keyW, descW);
        return true;
    }

    private static void DrawSingle(ArtGeometry geo, ArtGrid grid, List<HelpRow> rows)
    {
        var boxW = Math.Min(geo.Width, KeyWidth(rows) + 1 + DescWidth(rows) + 4);
        var boxH = rows.Count + 2;
        var x0 = Math.Max(0, (geo.Width - boxW) / 2);
        var y0 = Math.Max(0, (geo.Height - boxH) / 2);
        var keyW = KeyWidth(rows);
        DrawBox(grid, x0, y0, boxW, boxH);
        for (var i = 0; i < rows.Count; i++)
        {
            grid.Text(x0 + 2, y0 + 1 + i, rows[i].Key, KeyStyle);
            grid.Text(x0 + 2 + keyW + 1, y0 + 1 + i, rows[i].Text, Desc);
        }
    }

    private static void DrawBox(ArtGrid grid, int x0, int y0, int w, int h)
    {
        grid.Clear(x0, x0 + w - 1, y0, y0 + h - 1);
        grid.Text(x0, y0, "┌" + new string('─', Math.Max(0, w - 2)) + "┐", Box);
        for (var y = y0 + 1; y < y0 + h - 1; y++)
        {
            grid.Text(x0, y, "│", Box);
            grid.Text(x0 + w - 1, y, "│", Box);
        }
        grid.Text(x0, y0 + h - 1, "└" + new string('─', Math.Max(0, w - 2)) + "┘", Box);
    }
}
