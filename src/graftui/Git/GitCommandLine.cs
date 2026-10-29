using System.Text;

namespace graftui.Git;

/// <summary>
/// How a git command reads on screen: "git" and its arguments, each quoted where a shell would need it, so the line can be pasted.
/// It only renders text. Git itself is started from the argument list, which quoting never touches.
/// </summary>
public static class GitCommandLine
{
    /// <summary>"git" and the arguments, separated by spaces, each one quoted by <see cref="Quote"/> when needed.</summary>
    public static string Render(IEnumerable<string> args) => string.Join(' ', args.Select(a => Quote(a)).Prepend("git"));

    /// <summary>Quotes an argument for display. Plain words stay bare; empty, spaced or special arguments go in double quotes.</summary>
    internal static string Quote(string arg, bool always = false)
    {
        if (!always && arg.Length > 0 && arg.All(IsPlain))
            return arg;

        var sb = new StringBuilder(arg.Length + 2).Append('"');
        foreach (var c in arg)
        {
            if (c is '\\' or '"' or '$' or '`')
                sb.Append('\\');
            sb.Append(c);
        }

        return sb.Append('"').ToString();
    }

    private static bool IsPlain(char c) => char.IsAsciiLetterOrDigit(c) || "_./:@{}^~=,+-".Contains(c);
}
