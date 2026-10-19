namespace Graft.Ui;

/// <summary>
/// Where things sit in the art grid. Step is the horizontal distance between commits; A and B are the two
/// branch rows; XB is the column of the shared base. Commit n of a line sits at TipX(n).
/// </summary>
public sealed record ArtGeometry(int Width, int Height, int Step, int A, int B, int XB)
{
    public int TipX(int n) => XB + Step * n;

    /// <summary>
    /// Geometry for a grid of the given size, or null when the size is not one the scenes support.
    /// The widest step that fits is used (6, 4 or 3 columns between commits).
    /// </summary>
    public static ArtGeometry? For(int width, int height)
    {
        var (a, b) = height switch
        {
            11 => (3, 7),
            8 => (2, 5),
            _ => (-1, -1),
        };
        if (a < 0)
            return null;

        foreach (var step in new[] { 6, 4, 3 })
        {
            // Two commits of shared history to the left of XB, up to three commits to the right, and room for the merge join.
            if ((2 * step + 4) + 4 * step + 1 <= width)
                return new ArtGeometry(width, height, step, a, b, 2 * step + 4);
        }
        return null;
    }
}
