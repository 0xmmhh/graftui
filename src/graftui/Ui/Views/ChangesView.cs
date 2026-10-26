using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;

namespace graftui.Ui.Views;

/// <summary>
/// The left pane on the Changes tab: the staged and unstaged groups, with the selected file marked and kept in view.
/// Row indices follow <see cref="WorkingStatus.Rows"/>; <see cref="Selected"/> is one of them.
/// </summary>
public sealed class ChangesView : PaneView
{
    private static readonly Style Fg = new(Ink.Fg), FgB = new(Ink.Fg, true), Dim = new(Ink.Dim);

    private int _top;

    public ChangesView()
    {
        TitleSegs = [new Seg("Branches │ ", Dim), new Seg("Changes", Fg)];
        Title = "Changes";
    }

    /// <summary>The rows to show. Null until the first read finishes.</summary>
    public WorkingStatus? Status { get; set; }

    public int Selected { get; set; }

    public bool Loading { get; set; }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        ClearAll();
        DrawFrame();

        var w = Viewport.Width;
        var h = Viewport.Height;
        var innerW = w - 2;
        var innerH = h - 2;
        if (innerW <= 0 || innerH <= 0)
            return true;

        if (Status is null)
        {
            if (Loading)
                DrawText(1, 1, "Loading…", innerW, Dim);
            return true;
        }

        var lines = ChangesList.Build(Status);
        var selLine = lines.ToList().FindIndex(l => l.Kind == ChangesLineKind.File && l.Row == Selected);
        if (selLine >= 0)
        {
            if (selLine < _top)
                _top = selLine;
            if (selLine >= _top + innerH)
                _top = selLine - innerH + 1;
        }
        _top = Math.Clamp(_top, 0, Math.Max(0, lines.Count - innerH));

        for (var i = _top; i < lines.Count && i < _top + innerH; i++)
        {
            var line = lines[i];
            var row = 1 + (i - _top);
            var isSel = line.Kind == ChangesLineKind.File && line.Row == Selected;

            Paint.Fill(this, 1, row, innerW, Palette.Get(Fg, isSel));
            switch (line.Kind)
            {
                case ChangesLineKind.File:
                    DrawText(1, row, isSel ? "▸" : " ", 1, FgB, isSel);
                    DrawText(3, row, line.File!.Letter.ToString(), 1, line.Style, isSel);
                    DrawText(5, row, line.Text, innerW - 4, Fg, isSel);
                    break;
                default:
                    DrawText(1, row, line.Text, innerW, line.Style);
                    break;
            }
        }
        return true;
    }
}
