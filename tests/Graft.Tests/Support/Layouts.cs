using Graft.Model;

namespace Graft.Tests;

/// <summary>Hand-made graph layouts, so scene tests do not need a repo.</summary>
internal static class Layouts
{
    public const string Shared = "b000000000";
    public const string Cur = "c111111111";
    public const string Sel = "d222222222";

    public static CommitInfo C(string hash, int parents = 1, string subject = "s") =>
        new(hash, hash[..7], parents, subject);

    /// <summary>main and feature each have one commit of their own, on top of one shared commit.</summary>
    public static GraphLayout Diverged() =>
        new("main", false, "feature", Cur, Sel, Shared,
            [C(Cur)], false, [C(Sel)], false, [C(Shared)], false,
            C(Cur), C(Sel), 1, 1, GraphKind.Diverged);

    /// <summary>feature is one commit ahead of main, and main is its ancestor.</summary>
    public static GraphLayout FastForward() =>
        new("main", false, "feature", Cur, Sel, Cur,
            [], false, [C(Sel)], false, [C(Cur)], false,
            C(Cur), C(Sel), 1, 0, GraphKind.FastForward);

    /// <summary>feature is already part of main: its tip is a shared ancestor and main has one commit of its own.</summary>
    public static GraphLayout UpToDate() =>
        new("main", false, "feature", Cur, Shared, Shared,
            [C(Cur)], false, [], false, [C(Shared)], false,
            C(Cur), C(Shared), 0, 1, GraphKind.UpToDate);
}
