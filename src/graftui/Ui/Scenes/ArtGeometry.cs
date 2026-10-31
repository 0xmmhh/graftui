namespace graftui.Ui.Scenes;

/// <summary>
/// Where things sit in the art grid. Step is the horizontal distance between commits; A and B are the two
/// branch rows; XB is the column of the shared base. Commit n of a line sits at TipX(n).
/// </summary>
public sealed record ArtGeometry(int Width, int Height, int Step, int A, int B, int XB)
{
    public int TipX(int n) => XB + Step * n;

    /// <summary>
    /// Geometry for a grid of the given size, or null when the size is not one the scenes support: 11 rows (the Changes tab),
    /// 9 rows (the Branches tab, without the two empty rows of 11), 8 rows (a short pane) or 5 rows (a straight graph's
    /// single-line art: its line is row 2, and row 4 holds the lower label's name; no row-B line is drawn).
    /// The widest step that fits is used (6, 4 or 3 columns between commits).
    /// </summary>
    public static ArtGeometry? For(int width, int height)
    {
        int a;
        int b;
        if (height == 11)
        {
            a = 3;
            b = 7;
        }
        else if (height == 9)
        {
            a = 2;
            b = 6;
        }
        else if (height == 8)
        {
            a = 2;
            b = 5;
        }
        else if (height == 5)
        {
            a = 2;
            b = 4;
        }
        else
        {
            return null;
        }

        foreach (var step in new[] { 6, 4, 3 })
        {
            // Two commits of shared history to the left of XB, up to three commits to the right, and room for the merge join.
            if ((2 * step + 4) + 4 * step + 1 <= width)
                return new ArtGeometry(width, height, step, a, b, 2 * step + 4);
        }
        return null;
    }
}
