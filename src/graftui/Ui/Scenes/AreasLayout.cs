namespace graftui.Ui.Scenes;

/// <summary>
/// Where things sit in the three-areas grid: the Working tree, Staging area and Repository boxes, the commit line under
/// the repository box, and the message form. Sizes follow the design mock. The boxes share the width, so they shrink with the pane.
/// </summary>
public sealed record AreasLayout(int Width, int Height, int BoxW, int ListRows, int LineX, int LineY, int Step)
{
    /// <summary>Column where the form's fields start, after the "subject  " label.</summary>
    public const int FieldX = 9;

    public static AreasLayout For(int w, int h)
    {
        const int step = 6;
        var boxW = (w - 6) / 3;
        var listRows = h == 11 ? 4 : 3;
        var lineY = h == 11 ? 9 : 7;
        var lineX = Math.Max(0, Math.Min(30, w - 3 * step - 2));
        return new AreasLayout(w, h, boxW, listRows, lineX, lineY, step);
    }

    public int BoxX(int i) => i switch
    {
        0 => 0,
        1 => BoxW + 3,
        _ => 2 * BoxW + 6,
    };

    public int BoxWidth(int i) => i switch
    {
        0 or 1 => BoxW,
        _ => Width - (2 * BoxW + 6),
    };

    /// <summary>Row of the bottom border; the lists sit between row 1 and ListRows.</summary>
    public int BoxBottom => ListRows + 1;

    /// <summary>Where the j-th working-tree file's letter sits. Rows past the last one are drawn on the last row.</summary>
    public (int X, int Y) WtPos(int j) => (BoxX(0) + 2, 1 + Math.Min(j, ListRows - 1));

    /// <summary>Where the j-th staged file's letter sits.</summary>
    public (int X, int Y) IxPos(int j) => (BoxX(1) + 2, 1 + Math.Min(j, ListRows - 1));

    /// <summary>Where a commit's new dot gathers before it drops onto the line: just inside the repository box.</summary>
    public (int X, int Y) FoldPos => (Math.Min(BoxX(2) + 10, Width - 2), ListRows);

    public int SlotX(int i) => LineX + Step * i;

    /// <summary>Row of the subject field. The tall layout puts the form one row higher, to make room for the rule.</summary>
    public int SubjectRow => Height == 11 ? 7 : Height - 3;

    /// <summary>Width of the stash box: the box's left edge is column 0 and it stops two columns short of the commit line.</summary>
    public int StashBoxW => Math.Min(21, LineX - 2);

    /// <summary>Too narrow for a box: the stash shows as one line, "stash n", on the commit line's row.</summary>
    public bool StashCompact => StashBoxW < 12;

    /// <summary>Row of the stash box's top border. It sits below the working-tree and staging boxes.</summary>
    public int StashTop => Height == 11 ? 7 : 5;

    /// <summary>Entry rows inside the stash box (the newest entry, and "+n more" when there is room).</summary>
    public int StashRows => Height == 11 ? 2 : 1;

    /// <summary>Where a stash entry's letter sits: the first entry row, or the commit line when compact.</summary>
    public (int X, int Y) StashPos => StashCompact ? (0, LineY) : (2, StashTop + 1);

    /// <summary>The body takes this row and the one below it.</summary>
    public int BodyRow => Height - 2;

    /// <summary>The form's label row. Only the tall layout has room for it.</summary>
    public int FormLabelRow => Height == 11 ? 6 : -1;

    /// <summary>The dotted rule between subject and body. Only the tall layout has a spare row for it.</summary>
    public int FormRuleRow => Height == 11 ? 8 : -1;

    public int FieldWidth => Width - 9 - 7;
}
