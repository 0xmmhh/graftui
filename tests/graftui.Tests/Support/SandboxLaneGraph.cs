using graftui.Model;

namespace graftui.Tests;

/// <summary>
/// The sandbox's lane graph, built in memory through <see cref="LaneGraphBuilder"/>: the same commits and branches as the sandbox
/// repo, with the short hashes as ids. No git, so the fit tests need no repo. The branches are in git's order (refs sort by name).
/// </summary>
internal static class SandboxLaneGraph
{
    public static IReadOnlyList<Branch> Branches { get; } =
    [
        Br("feature/conflict", "30cc9f2"),
        Br("feature/fast-forward", "8f0068a"),
        Br("feature/ff-to-merge", "810cc55"),
        Br("feature/merge-commit", "acf4dee"),
        Br("feature/picks", "f6d6964"),
        Br("main", "be0126e"),
    ];

    /// <summary>
    /// The sandbox graph's rows as text, "name: hash@column ...". LaneGraphReaderTests.The_sandbox_graph reads the real sandbox repo
    /// and checks this same list, so the fixture and the repo cannot drift apart.
    /// </summary>
    public static string[] Rows { get; } =
    [
        "main: 34e4a2e@0 8c5ceee@1 be0126e@2",
        "feature/fast-forward: 8f0068a@3",
        "feature/ff-to-merge: 2b99ab2@3 810cc55@5",
        "side-for-ffm: df42801@4",
        "feature/conflict: 30cc9f2@2",
        "feature/merge-commit: acf4dee@2",
        "feature/picks: 089eae5@2 52b7690@3 f6d6964@4",
    ];

    /// <summary>The sandbox graph: base main, six lanes.</summary>
    public static LaneGraph Build() =>
        LaneGraphBuilder.Build(new LaneGraphInput("main", BaseChain(), false, Branches, Others(), false));

    // The base's first-parent history, newest first. main's tip is be0126e.
    private static List<GraphCommit> BaseChain() =>
    [
        C("be0126e", "8c5ceee"),
        C("8c5ceee", "34e4a2e"),
        C("34e4a2e"),
    ];

    // The commits of the other branches that the base does not have.
    private static List<GraphCommit> Others() =>
    [
        C("8f0068a", "be0126e"),
        C("2b99ab2", "be0126e"),
        C("df42801", "2b99ab2"),
        Commit("810cc55", "Merge branch 'side-for-ffm' into feature/ff-to-merge", "2b99ab2", "df42801"),
        C("30cc9f2", "8c5ceee"),
        C("acf4dee", "8c5ceee"),
        C("089eae5", "8c5ceee"),
        C("52b7690", "089eae5"),
        C("f6d6964", "52b7690"),
    ];

    // A commit whose subject is "s <id>".
    private static GraphCommit C(string id, params string[] parents) => Commit(id, "s " + id, parents);

    private static GraphCommit Commit(string id, string subject, params string[] parents) => new(id, id, parents, subject);

    private static Branch Br(string name, string tip) => new(name, tip, tip, name == "main");
}
