using Graft.Ui;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Graft.Ui.Views;

/// <summary>Bottom row: the keys that work right now. While a merge runs, only quit is offered.</summary>
public sealed class KeyBarView : View
{
    private const string SandboxNote = "practice sandbox · rebuilt on every --sandbox launch";

    private static readonly Style Fg = new(Ink.Fg), Dim = new(Ink.Dim);

    public bool Busy { get; set; }

    public bool SandboxMode { get; set; }

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
            x += Chip(x, " ↑↓ ", chip, max);
            x += Paint.Text(this, x, 0, " select", max - x, Fg);
            x += Paint.Text(this, x, 0, "  ", max - x, Fg);
            x += Chip(x, " m ", chip, max);
            x += Paint.Text(this, x, 0, " merge", max - x, Fg);
            x += Paint.Text(this, x, 0, "  ", max - x, Fg);
            x += Chip(x, " Esc ", chip, max);
            Paint.Text(this, x, 0, " quit", max - x, Fg);
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

    private int Chip(int x, string text, Attribute attr, int max) => Paint.Text(this, x, 0, text, max - x, attr);
}
