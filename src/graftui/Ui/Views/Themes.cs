using Terminal.Gui.Drawing;

namespace graftui.Ui.Views;

/// <summary>
/// One colour set. Token order matches the layout lab (docs/design/layout-lab.html). Lanes are the branch colours in lane order:
/// lane 0 is the trunk's (see <see cref="Model.BranchLanes"/>).
/// </summary>
public sealed record Theme(
    string Name,
    Color Term,
    Color Frame,
    Color Fg,
    Color Dim,
    Color Chip,
    Color SelRow,
    Color Focus,
    Color Cur,
    Color Inc,
    Color Mrg,
    Color Ok,
    Color Bad,
    Color Warn,
    Color Hash,
    IReadOnlyList<Color> Lanes);

/// <summary>The five presets from the layout lab, chosen with <c>--theme</c>. The first one is the default.</summary>
public static class Themes
{
    private static readonly Theme[] All =
    [
        new Theme("default",
            Term: Hex("12161d"), Frame: Hex("2b3442"), Fg: Hex("d5dce5"), Dim: Hex("6c7a8c"), Chip: Hex("1a2029"),
            SelRow: Hex("22314a"), Focus: Hex("22314a"), Cur: Hex("4fd1c5"), Inc: Hex("c39bff"), Mrg: Hex("f5c451"), Ok: Hex("7ee787"),
            Bad: Hex("ff6b6b"), Warn: Hex("f0a35e"), Hash: Hex("b39b62"),
            Lanes: [Hex("5b9dff"), Hex("c39bff"), Hex("4fd1c5"), Hex("f78fb3"), Hex("b5e36b"), Hex("7dcfff")]),
        new Theme("lifted",
            Term: Hex("12161d"), Frame: Hex("36414f"), Fg: Hex("d5dce5"), Dim: Hex("8391a3"), Chip: Hex("252d3a"),
            SelRow: Hex("27406a"), Focus: Hex("27406a"), Cur: Hex("4fd1c5"), Inc: Hex("c39bff"), Mrg: Hex("f5c451"), Ok: Hex("7ee787"),
            Bad: Hex("ff6b6b"), Warn: Hex("f0a35e"), Hash: Hex("c2a96c"),
            Lanes: [Hex("6aa6ff"), Hex("c39bff"), Hex("4fd1c5"), Hex("f78fb3"), Hex("b5e36b"), Hex("7dcfff")]),
        new Theme("muted",
            Term: Hex("16191f"), Frame: Hex("2f3640"), Fg: Hex("cfd5dc"), Dim: Hex("7d8894"), Chip: Hex("242a33"),
            SelRow: Hex("2a3646"), Focus: Hex("2a3646"), Cur: Hex("7cc4bc"), Inc: Hex("b4a3d9"), Mrg: Hex("d9bc73"), Ok: Hex("8fc79a"),
            Bad: Hex("d98282"), Warn: Hex("d4a074"), Hash: Hex("a99572"),
            Lanes: [Hex("7b9fdb"), Hex("b9a0dc"), Hex("74c7bb"), Hex("e09aae"), Hex("a8cc84"), Hex("8cc9e0")]),
        // Focus keeps the old blue: the selected row is 262626 now, and too close to the chip to show focus.
        new Theme("high-contrast",
            Term: Hex("0a0c10"), Frame: Hex("4a5668"), Fg: Hex("f2f5f8"), Dim: Hex("a3afbd"), Chip: Hex("2a3240"),
            SelRow: Hex("262626"), Focus: Hex("2b4673"), Cur: Hex("5eead4"), Inc: Hex("d4b8ff"), Mrg: Hex("ffd666"), Ok: Hex("8ff59a"),
            Bad: Hex("ff7b7b"), Warn: Hex("ffb26b"), Hash: Hex("d6bd80"),
            Lanes: [Hex("4070ff"), Hex("fc03fc"), Hex("03fcfc"), Hex("fc0303"), Hex("03fc03"), Hex("ff8800")]),
        new Theme("light",
            Term: Hex("f7f8fa"), Frame: Hex("c9d1db"), Fg: Hex("1d2430"), Dim: Hex("5c6878"), Chip: Hex("e3e8ee"),
            SelRow: Hex("d6e3f7"), Focus: Hex("d6e3f7"), Cur: Hex("0b7a71"), Inc: Hex("6d3fd0"), Mrg: Hex("8a6100"), Ok: Hex("1f7a32"),
            Bad: Hex("c22f2f"), Warn: Hex("a5520c"), Hash: Hex("7a6230"),
            Lanes: [Hex("1f5fd1"), Hex("6d3fd0"), Hex("0b7a71"), Hex("b8326e"), Hex("4d7a0f"), Hex("0a6f9e")]),
    ];

    /// <summary>The names accepted by <c>--theme</c>, in the order the mock lists them.</summary>
    public static IReadOnlyList<string> Names { get; } = All.Select(t => t.Name).ToArray();

    /// <summary>The "default" preset: the colours the app has always used.</summary>
    public static Theme Default => All[0];

    /// <summary>Looks a preset up by name, ignoring case. On a miss, <paramref name="t"/> is the default and the result is false.</summary>
    public static bool TryGet(string name, out Theme t)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                t = candidate;
                return true;
            }
        }

        t = Default;
        return false;
    }

    private static Color Hex(string rrggbb) => new(
        Convert.ToInt32(rrggbb[..2], 16),
        Convert.ToInt32(rrggbb[2..4], 16),
        Convert.ToInt32(rrggbb[4..6], 16));
}
