using System.Text;
using Terminal.Gui.Text;

namespace Graft.Ui.Text;

/// <summary>Column-aware truncation. Every piece of text that can be wider than its pane goes through here.</summary>
public static class TextFit
{
    public const string Ellipsis = "…";

    /// <summary>Terminal columns the text occupies. Non-printable runes count as zero.</summary>
    public static int Columns(string s)
    {
        var total = 0;
        foreach (var rune in s.EnumerateRunes())
            total += Math.Max(0, rune.GetColumns());
        return total;
    }

    /// <summary>Replaces control characters (non-printable runes) with spaces, so nothing from git can move the cursor.</summary>
    public static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var rune in s.EnumerateRunes())
            sb.Append(rune.GetColumns() < 0 ? " " : rune.ToString());
        return sb.ToString();
    }

    /// <summary>
    /// Fits the text into <paramref name="width"/> columns. Text that does not fit is cut and ends in "…".
    /// Returns "" when width is 0 or less.
    /// </summary>
    public static string Fit(string s, int width)
    {
        if (width <= 0)
            return "";

        var clean = Sanitize(s);
        if (Columns(clean) <= width)
            return clean;

        var sb = new StringBuilder();
        var used = 0;
        foreach (var rune in clean.EnumerateRunes())
        {
            var cols = Math.Max(0, rune.GetColumns());
            if (used + cols > width - 1)
                break;
            sb.Append(rune.ToString());
            used += cols;
        }
        return sb.Append(Ellipsis).ToString();
    }
}
