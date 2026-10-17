using System.Text;
using Terminal.Gui.Text;

namespace Graft.Ui;

public readonly record struct Cell(char Ch, Style Style);

/// <summary>
/// A character grid the scenes draw into. One cell per glyph; the view turns each cell into one terminal cell.
/// Writes outside the grid are ignored.
/// </summary>
public sealed class ArtGrid(int width, int height)
{
    private static readonly Cell Blank = new(' ', new Style(Ink.Fg));

    private readonly Cell[,] _cells = Filled(width, height);

    public int Width { get; } = width;
    public int Height { get; } = height;

    public Cell this[int x, int y]
    {
        get
        {
            if (!In(x, y))
                return Blank;
            return _cells[x, y];
        }
    }

    private static Cell[,] Filled(int width, int height)
    {
        var cells = new Cell[width, height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                cells[x, y] = Blank;
        return cells;
    }

    private bool In(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public void Put(int x, int y, char ch, Style s)
    {
        if (In(x, y))
            _cells[x, y] = new Cell(ch, s);
    }

    /// <summary>Writes one glyph per rune. Anything that is not exactly one column wide becomes '?', so columns stay aligned.</summary>
    public void Text(int x, int y, string s, Style st)
    {
        var i = 0;
        foreach (var rune in s.EnumerateRunes())
        {
            var ch = rune.Value <= 0xFFFF && rune.GetColumns() == 1 ? (char)rune.Value : '?';
            Put(x + i, y, ch, st);
            i++;
        }
    }

    public void HLine(int x1, int x2, int y, Style s, char ch = '─')
    {
        for (var x = x1; x <= x2; x++)
            Put(x, y, ch, s);
    }

    public void VLine(int x, int y1, int y2, Style s, char ch = '│')
    {
        for (var y = y1; y <= y2; y++)
            Put(x, y, ch, s);
    }

    /// <summary>Blanks the inclusive rectangle.</summary>
    public void Clear(int x1, int x2, int y1, int y2)
    {
        for (var y = Math.Max(0, y1); y <= Math.Min(Height - 1, y2); y++)
            for (var x = Math.Max(0, x1); x <= Math.Min(Width - 1, x2); x++)
                _cells[x, y] = Blank;
    }

    /// <summary>Every visible glyph becomes dim. Used behind the refused box.</summary>
    public void DimAll()
    {
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (_cells[x, y].Ch != ' ')
                    _cells[x, y] = _cells[x, y] with { Style = new Style(Ink.Dim) };
    }
}
