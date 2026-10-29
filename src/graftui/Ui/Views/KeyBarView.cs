using graftui.Model;
using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace graftui.Ui.Views;

/// <summary>Which keys the bar lists: the branches, the changes, a commit message form, a yes/no question, or a one-line message box.</summary>
public enum KeyBarMode { Branches, Changes, Input, Ask, Message }

/// <summary>
/// Bottom row: the keys that work right now. While a merge or another git call runs, only quit is offered.
/// The keys follow <see cref="ChangesState"/> and <see cref="BranchesState"/>, which the tabs keep up to date.
/// </summary>
public sealed class KeyBarView : View
{
    private static readonly Style Fg = new(Ink.Fg);

    public bool Busy { get; set; }

    public KeyBarMode Mode { get; set; } = KeyBarMode.Branches;

    /// <summary>What the Changes tab's keys depend on. Empty until the first status read.</summary>
    public ChangesKeyState ChangesState { get; set; } = ChangesKeyState.Empty;

    /// <summary>What the Branches tab's keys depend on.</summary>
    public BranchesKeyState BranchesState { get; set; } = BranchesKeyState.Empty;

    /// <summary>The key that answers yes to the question on screen: "y", "1" or "2".</summary>
    public string AskKey { get; set; } = "y";

    /// <summary>The label after the yes key, with its leading space: " yes", or " discard" for the discard question.</summary>
    public string AskYesLabel { get; set; } = " yes";

    /// <summary>The label after the no key, with its leading space: " no", or " keep" for the discard question.</summary>
    public string AskNoLabel { get; set; } = " no";

    protected override bool OnDrawingContent(DrawContext? context)
    {
        Paint.ClearAll(this);
        var w = Viewport.Width;
        if (w <= 0)
            return true;

        var chip = Palette.KeyChip;
        var max = w - 1;
        var x = 1;
        if (Busy)
        {
            x += Chip(x, " Esc ", chip, max);
            Paint.Text(this, x, 0, " quit (waits for git)", max - x, Fg);
        }
        else
        {
            // Two spaces between items when they fit, one when they do not; lower-priority items are dropped first.
            var items = FitItems(Items(), w, out var separator);
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    x += Paint.Text(this, x, 0, new string(' ', separator), max - x, Fg);
                x += Chip(x, $" {items[i].Key} ", chip, max);
                x += Paint.Text(this, x, 0, items[i].Label, max - x, Fg);
            }
        }

        return true;
    }

    /// <summary>The (key, label, priority) items for the current mode. Labels start with a space.</summary>
    private IReadOnlyList<(string Key, string Label, int Priority)> Items() => Mode switch
    {
        KeyBarMode.Changes => ChangesItems(ChangesState),
        KeyBarMode.Input =>
        [
            ("Enter", " save", 5),
            ("Tab", " next field", 4),
            ("Ctrl+Enter", " save from body", 3),
            ("Esc", " cancel", 6),
        ],
        KeyBarMode.Message =>
        [
            ("Enter", " stash", 4),
            ("Esc", " cancel", 5),
        ],
        KeyBarMode.Ask =>
        [
            (AskKey, AskYesLabel, 4),
            ("n", AskNoLabel, 4),
            ("Esc", " cancel", 5),
        ],
        _ => BranchesItems(BranchesState),
    };

    /// <summary>
    /// The Changes tab's keys that act now, in the order they are listed. Priority decides what is dropped first when the bar is
    /// narrow (higher is kept): Esc, Tab, Space, c, x, z, Z, u, A, a, U, ↑↓. Space says stage or unstage by the selected row.
    /// </summary>
    internal static IReadOnlyList<(string Key, string Label, int Priority)> ChangesItems(ChangesKeyState s)
    {
        var items = new List<(string Key, string Label, int Priority)> { ("Tab", " branches", 11) };
        if (s.RowCount >= 2)
            items.Add(("↑↓", " file", 1));
        if (s.SelectedSide is { } side)
            items.Add(("Space", side == ChangeSide.Staged ? " unstage" : " stage", 10));
        if (s.HasUnstaged)
            items.Add(("a", " stage all", 3));
        if (s.HasStaged)
            items.Add(("U", " unstage all", 2));
        if (s.HasStaged)
            items.Add(("c", " commit", 9));
        if (s.HasHead)
            items.Add(("A", " amend", 4));
        if (s.SelectedSide == ChangeSide.Unstaged)
            items.Add(("x", " discard", 8));
        if (s.RowCount > 0)
            items.Add(("z", " stash", 7));
        if (s.HasStash)
            items.Add(("Z", " pop", 6));
        if (s.HasUndo)
            items.Add(("u", " undo", 5));
        items.Add(("Esc", " quit", 12));
        return items;
    }

    /// <summary>
    /// The Branches tab's keys that act now. Priority: Esc, Tab, m, u, ↑↓.
    /// </summary>
    internal static IReadOnlyList<(string Key, string Label, int Priority)> BranchesItems(BranchesKeyState s)
    {
        var items = new List<(string Key, string Label, int Priority)> { ("Tab", " changes", 4) };
        if (s.BranchCount >= 2)
            items.Add(("↑↓", " select", 1));
        if (s.CanMerge)
            items.Add(("m", " merge", 3));
        if (s.HasUndo)
            items.Add(("u", " undo", 2));
        items.Add(("Esc", " quit", 5));
        return items;
    }

    /// <summary>
    /// The items that fit in <paramref name="width"/> columns, in their original order. Two spaces between items when all of them
    /// fit that way; otherwise one space, and the lowest-priority items (the later one on a tie) are dropped until the rest fit.
    /// <paramref name="separator"/> is the space used between the kept items.
    /// </summary>
    internal static IReadOnlyList<(string Key, string Label)> FitItems(IReadOnlyList<(string Key, string Label, int Priority)> items, int width, out int separator)
    {
        separator = 2;
        if (ItemsWidth(items, separator) <= width)
            return Plain(items);

        separator = 1;
        var kept = items.ToList();
        while (kept.Count > 1 && ItemsWidth(kept, separator) > width)
        {
            var drop = kept
                .Select((item, index) => (item, index))
                .OrderBy(x => x.item.Priority)
                .ThenByDescending(x => x.index)
                .First().index;
            kept.RemoveAt(drop);
        }
        return Plain(kept);
    }

    private static IReadOnlyList<(string Key, string Label)> Plain(IReadOnlyList<(string Key, string Label, int Priority)> items) =>
        items.Select(i => (i.Key, i.Label)).ToList();

    /// <summary>
    /// The width the items take, with the leading column and one column kept free on the right: the bar draws from column 1
    /// and a label may not run into the last column.
    /// </summary>
    private static int ItemsWidth(IReadOnlyList<(string Key, string Label, int Priority)> items, int separator) =>
        items.Sum(i => TextFit.Columns(" " + i.Key + " ") + TextFit.Columns(i.Label)) + separator * Math.Max(0, items.Count - 1) + 2;

    private int Chip(int x, string text, Attribute attr, int max) => Paint.Text(this, x, 0, text, max - x, attr);
}
