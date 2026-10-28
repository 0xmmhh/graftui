using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace graftui.Ui.Views;

/// <summary>Which keys the bar lists: the branches, the changes, a commit message form, a yes/no question, or a one-line message box.</summary>
public enum KeyBarMode { Branches, Changes, Input, Ask, Message }

/// <summary>Bottom row: the keys that work right now. While a merge or another git call runs, only quit is offered.</summary>
public sealed class KeyBarView : View
{
    private const string SandboxNote = "practice sandbox · rebuilt on every --sandbox launch";

    private static readonly Style Fg = new(Ink.Fg), Dim = new(Ink.Dim);

    public bool Busy { get; set; }

    public bool SandboxMode { get; set; }

    public KeyBarMode Mode { get; set; } = KeyBarMode.Branches;

    /// <summary>What Space does on the Changes tab: "stage" or "unstage", by the selected row.</summary>
    public string SpaceLabel { get; set; } = "stage";

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

        if (SandboxMode)
        {
            var cols = TextFit.Columns(SandboxNote);
            var start = Math.Max(x + 2, w - 2 - cols);
            if (start < w - 1)
                Paint.Text(this, start, 0, SandboxNote, w - 1 - start, Dim);
        }
        return true;
    }

    /// <summary>The (key, label, priority) items for the current mode. Labels start with a space.</summary>
    private IReadOnlyList<(string Key, string Label, int Priority)> Items() => Mode switch
    {
        KeyBarMode.Changes => ChangesItems(SpaceLabel),
        KeyBarMode.Input =>
        [
            ("Enter", " save", 4),
            ("Tab", " next field", 2),
            ("Ctrl+Enter", " save from body", 3),
            ("Esc", " cancel", 5),
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
        _ => BranchesItems(),
    };

    /// <summary>
    /// The Changes tab's keys, in the order they are listed. Priority decides what is dropped first when the bar is narrow
    /// (higher is kept): Esc, Space, c, x, z, Z, u, A, a, U, Tab, ↑↓.
    /// </summary>
    internal static IReadOnlyList<(string Key, string Label, int Priority)> ChangesItems(string spaceLabel) =>
    [
        ("Tab", " branches", 2),
        ("↑↓", " file", 1),
        ("Space", " " + spaceLabel, 11),
        ("a", " stage all", 4),
        ("U", " unstage all", 3),
        ("c", " commit", 10),
        ("A", " amend", 5),
        ("x", " discard", 9),
        ("z", " stash", 8),
        ("Z", " pop", 7),
        ("u", " undo", 6),
        ("Esc", " quit", 12),
    ];

    /// <summary>The Branches tab's keys. Priority: Esc, m, u, ↑↓, Tab.</summary>
    internal static IReadOnlyList<(string Key, string Label, int Priority)> BranchesItems() =>
    [
        ("Tab", " changes", 1),
        ("↑↓", " select", 2),
        ("m", " merge", 4),
        ("u", " undo", 3),
        ("Esc", " quit", 5),
    ];

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
