using Graft.Git;

namespace Graft.Ui;

/// <summary>Pure string rendering of the merge animation. No Terminal.Gui dependency.</summary>
public static class MergeArt
{
    public const int Width = 44;
    public const int Steps = 20; // 20 x 50 ms ~ 1 s

    public static string Frame(int step, string into, string branch, string? caption = null)
    {
        var g = 19 * Math.Clamp(step, 0, Steps) / Steps;
        var gap = Width - 2 - 2 * g;
        var line = "●" + new string('━', g) + new string(' ', gap) + new string('━', g) + "●";
        return string.Join('\n', Header(into, branch), line, caption ?? $"Merging {branch} into {into}...");
    }

    public static string Final(MergeOutcome o)
    {
        var chars = new char[Width];
        Array.Fill(chars, '━');
        chars[0] = '●';
        chars[Width - 1] = '●';
        var mid = Width / 2;
        if (o.Kind is MergeKind.Conflict or MergeKind.Failed)
        {
            chars[mid - 2] = '╳';
            chars[mid - 1] = ' ';
            chars[mid] = ' ';
            chars[mid + 1] = '╳';
        }
        else
        {
            chars[mid] = '◆';
        }

        var head = o.NewHead is { Length: > 0 } h ? h[..Math.Min(7, h.Length)] : "";
        var caption = o.Kind switch
        {
            MergeKind.FastForward => $"Fast-forward: {o.Into} is now at {head}",
            MergeKind.MergeCommit => $"Merge commit {head} created on {o.Into}",
            MergeKind.UpToDate => "Already up to date",
            MergeKind.Conflict => $"CONFLICT: merge aborted, {o.Into} unchanged",
            _ => $"Merge failed: {o.Message}",
        };
        return string.Join('\n', Header(o.Into, o.Branch), new string(chars), caption);
    }

    public static string Refused(string reason) => "Refused: " + reason;

    private static string Header(string into, string branch)
    {
        const int max = Width / 2 - 1;
        into = Truncate(into, max);
        branch = Truncate(branch, max);
        return into + new string(' ', Math.Max(1, Width - into.Length - branch.Length)) + branch;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
