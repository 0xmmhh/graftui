using System.Text;
using Terminal.Gui.Text;

namespace graftui.Ui.Text;

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

    /// <summary>
    /// Two names that share one line of <paramref name="budget"/> columns. When both fit, nothing is cut. Otherwise the shorter
    /// name is kept whole, and the longer one is cut, until the longer one is down to <paramref name="min"/> columns; only then
    /// is the shorter one cut too. Two names of equal width split the budget evenly. Each name gets at least
    /// <paramref name="min"/> columns, even when that makes the line longer than the budget.
    /// </summary>
    public static (string A, string B) FitPair(string a, string b, int budget, int min = 8)
    {
        var wa = Columns(a);
        var wb = Columns(b);
        if (wa + wb <= budget)
            return (Fit(a, wa), Fit(b, wb));

        if (wa == wb)
        {
            var half = budget / 2;
            return (Fit(a, Math.Max(min, half)), Fit(b, Math.Max(min, budget - half)));
        }

        // The longer name keeps at least min columns (or its whole width, when that is less); the shorter one gets the rest,
        // up to its own width.
        var aIsShorter = wa < wb;
        var shortWidth = aIsShorter ? wa : wb;
        var longWidth = aIsShorter ? wb : wa;
        var longFloor = Math.Min(longWidth, min);
        var shortLimit = Math.Min(shortWidth, budget - longFloor);
        var longLimit = budget - shortLimit;

        var shortText = aIsShorter ? Fit(a, Math.Max(min, shortLimit)) : Fit(b, Math.Max(min, shortLimit));
        var longText = aIsShorter ? Fit(b, Math.Max(min, longLimit)) : Fit(a, Math.Max(min, longLimit));
        return aIsShorter ? (shortText, longText) : (longText, shortText);
    }

    /// <summary>
    /// Splits the text over at most two lines of <paramref name="width"/> columns. Text that fits is (Fit(s), null).
    /// Otherwise the first line ends at the last space that fits, and the second line goes through <see cref="Fit"/>.
    /// With no usable space the first line is cut at <paramref name="width"/>, and the rest goes through Fit.
    /// Returns ("", null) when width is 0 or less.
    /// </summary>
    public static (string First, string? Second) Wrap2(string s, int width)
    {
        if (width <= 0)
            return ("", null);

        var clean = Sanitize(s);
        if (Columns(clean) <= width)
            return (clean, null);

        var fitEnd = FitEnd(clean, width);

        // A space can end the first line only after some text, and only where everything before it fits.
        var firstText = clean.AsSpan().IndexOfAnyExcept(' ');
        var space = -1;
        for (var i = Math.Min(fitEnd, clean.Length - 1); i > firstText; i--)
        {
            if (clean[i] == ' ')
            {
                space = i;
                break;
            }
        }

        if (space > 0)
        {
            var first = clean[..space].TrimEnd(' ');
            var rest = clean[(space + 1)..].TrimStart(' ');
            return rest.Length == 0 ? (first, null) : (first, Fit(rest, width));
        }

        return (clean[..fitEnd], Fit(clean[fitEnd..], width));
    }

    /// <summary>
    /// Greedy word wrap by columns: the text is split at spaces, control characters count as spaces, and each line holds as many
    /// words as fit. A word longer than the line is cut with "…". Empty when there are no words. width should be 1 or more.
    /// </summary>
    public static List<string> WrapWords(string s, int width)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var raw in Sanitize(s).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = Columns(raw) > width ? Fit(raw, width) : raw;
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (Columns(candidate) <= width)
            {
                current = candidate;
                continue;
            }
            lines.Add(current);
            current = word;
        }
        if (current.Length > 0)
            lines.Add(current);
        return lines;
    }

    /// <summary>Length in UTF-16 units of the longest prefix that fits in width columns, never splitting a rune.</summary>
    private static int FitEnd(string s, int width)
    {
        var used = 0;
        var index = 0;
        foreach (var rune in s.EnumerateRunes())
        {
            var cols = Math.Max(0, rune.GetColumns());
            if (used + cols > width)
                break;
            used += cols;
            index += rune.Utf16SequenceLength;
        }
        return index;
    }
}
