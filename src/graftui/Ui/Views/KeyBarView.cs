using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace graftui.Ui.Views;

/// <summary>Which keys the bar lists: the branches, the changes, a message form, or a yes/no question.</summary>
public enum KeyBarMode { Branches, Changes, Input, Ask }

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
            var items = Items();
            // Two spaces between items when they fit, one space when they do not.
            var separator = ItemsWidth(items, 2) <= w - 2 ? 2 : 1;
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

    /// <summary>The (key, label) pairs for the current mode. Labels start with a space.</summary>
    private IReadOnlyList<(string Key, string Label)> Items() => Mode switch
    {
        KeyBarMode.Changes =>
        [
            ("Tab", " branches"),
            ("↑↓", " file"),
            ("Space", " " + SpaceLabel),
            ("a", " stage all"),
            ("U", " unstage all"),
            ("c", " commit"),
            ("A", " amend"),
            ("Esc", " quit"),
        ],
        KeyBarMode.Input =>
        [
            ("Enter", " save"),
            ("Tab", " next field"),
            ("Ctrl+Enter", " save from body"),
            ("Esc", " cancel"),
        ],
        KeyBarMode.Ask =>
        [
            ("y", " yes"),
            ("n", " no"),
        ],
        _ =>
        [
            ("Tab", " changes"),
            ("↑↓", " select"),
            ("m", " merge"),
            ("Esc", " quit"),
        ],
    };

    private static int ItemsWidth(IReadOnlyList<(string Key, string Label)> items, int separator) =>
        items.Sum(i => TextFit.Columns(" " + i.Key + " ") + TextFit.Columns(i.Label)) + separator * (items.Count - 1) + 1;

    private int Chip(int x, string text, Attribute attr, int max) => Paint.Text(this, x, 0, text, max - x, attr);
}
