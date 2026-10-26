namespace graftui.Operations;

/// <summary>
/// Builds and takes apart commit messages: a subject line, a blank line, the body, and one final newline.
/// Line endings are normalised to LF, so a message written on Windows-style input comes out the same.
/// </summary>
public static class CommitMessage
{
    public static string Build(string subject, string body)
    {
        var s = Normalize(subject).Replace('\n', ' ').Trim();
        var b = TrimBody(Normalize(body));
        return s + (b.Length > 0 ? "\n\n" + b : "") + "\n";
    }

    /// <summary>The first line is the subject; the body is whatever follows the blank lines after it.</summary>
    public static (string Subject, string Body) Split(string fullMessage)
    {
        var text = Normalize(fullMessage);
        var newline = text.IndexOf('\n');
        var subject = (newline < 0 ? text : text[..newline]).Trim();
        var body = newline < 0 ? "" : TrimBody(text[(newline + 1)..]);
        return (subject, body);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    // Drops leading blank lines and trailing whitespace; blank lines inside the body are kept.
    private static string TrimBody(string s)
    {
        var lines = s.Split('\n');
        var first = 0;
        while (first < lines.Length && lines[first].Trim().Length == 0)
            first++;
        return string.Join('\n', lines[first..]).TrimEnd();
    }
}
