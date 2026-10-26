namespace graftui.Operations;

/// <summary>
/// Builds and takes apart commit messages. A message is a subject line, then the body. The subject is separated from the body
/// by a blank line, or, when the old message had no blank line there (a subject that wraps onto a second line), by nothing.
/// Line endings are normalised to LF, so a message written on Windows-style input comes out the same.
/// </summary>
public static class CommitMessage
{
    /// <summary>
    /// The message to commit. Leading blank lines of the body are dropped and the subject's newlines become spaces.
    /// <paramref name="blankAfterSubject"/> false keeps a body that continues the subject's paragraph.
    /// </summary>
    public static string Build(string subject, string body, bool blankAfterSubject = true)
    {
        var s = Normalize(subject).Replace('\n', ' ').Trim();
        var b = TrimBody(Normalize(body));
        if (b.Length == 0)
            return s + "\n";
        return s + (blankAfterSubject ? "\n\n" : "\n") + b + "\n";
    }

    /// <summary>
    /// The first line is the subject. The body is everything after it, with exactly one blank line removed when there is one.
    /// <c>BlankAfterSubject</c> says whether that blank line was there, so <see cref="Build"/> can write the message back as it was.
    /// </summary>
    public static (string Subject, string Body, bool BlankAfterSubject) Split(string fullMessage)
    {
        var lines = Normalize(fullMessage).Split('\n');
        var subject = lines[0].Trim();
        var rest = lines.Skip(1).ToArray();
        if (rest.Length == 0)
            return (subject, "", true);
        if (rest[0].Trim().Length == 0)
            return (subject, TrimEnd(string.Join('\n', rest.Skip(1))), true);
        return (subject, TrimEnd(string.Join('\n', rest)), false);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string TrimEnd(string s) => s.TrimEnd();

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
