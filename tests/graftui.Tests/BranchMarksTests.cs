using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>The mark in column 2 of each branch row (the new look): what it says, how it draws, and the order its forecasts run in.</summary>
public class BranchMarksTests
{
    private static readonly Style NameStyle = new(Ink.Lane2, true);

    private static GraphLayout LayoutOf(GraphKind kind) =>
        GraphLayout.Minimal("main", false, "feature", "aaaaaaa", "bbbbbbb") with { Kind = kind };

    private static MergeForecast Forecast(ForecastKind kind) => new(kind, [], null, []);

    [Theory]
    [InlineData(GraphKind.Same, ForecastKind.Unavailable, BranchMark.Nothing)]
    [InlineData(GraphKind.UpToDate, ForecastKind.UpToDate, BranchMark.Nothing)]
    [InlineData(GraphKind.Unrelated, ForecastKind.Unavailable, BranchMark.Refused)]
    [InlineData(GraphKind.Diverged, ForecastKind.Clean, BranchMark.Merge)]
    [InlineData(GraphKind.FastForward, ForecastKind.Clean, BranchMark.Merge)]
    [InlineData(GraphKind.FastForward, ForecastKind.FastForward, BranchMark.FastForward)]
    [InlineData(GraphKind.Diverged, ForecastKind.Conflict, BranchMark.Conflict)]
    [InlineData(GraphKind.Diverged, ForecastKind.UpToDate, BranchMark.Nothing)]
    [InlineData(GraphKind.Diverged, ForecastKind.Refused, BranchMark.Refused)]
    [InlineData(GraphKind.Diverged, ForecastKind.Unavailable, BranchMark.Unknown)]
    public void Of_gives_the_mark_for_each_layout_kind_and_forecast(GraphKind kind, ForecastKind forecast, BranchMark expected)
    {
        Assert.Equal(expected, BranchMarks.Of(LayoutOf(kind), Forecast(forecast)));
    }

    [Fact]
    public void The_current_branch_and_a_paused_branch_get_the_star_in_the_name_style()
    {
        var marks = new Dictionary<string, BranchMark>();

        Assert.Equal(("*", NameStyle), BranchMarks.Cell("main", true, false, NameStyle, marks, Glyphs.Unicode));
        Assert.Equal(("*", NameStyle), BranchMarks.Cell("feature", true, true, NameStyle, marks, Glyphs.Unicode));
    }

    [Fact]
    public void Every_other_row_is_blank_when_head_is_detached()
    {
        var marks = new Dictionary<string, BranchMark> { ["feature"] = BranchMark.Conflict };

        var cell = BranchMarks.Cell("feature", false, true, NameStyle, marks, Glyphs.Unicode);

        Assert.Equal(" ", cell.Text);
    }

    [Fact]
    public void A_name_with_no_mark_yet_is_the_dim_unknown_dot()
    {
        var marks = new Dictionary<string, BranchMark>();

        Assert.Equal(("·", new Style(Ink.Dim)), BranchMarks.Cell("feature", false, false, NameStyle, marks, Glyphs.Unicode));
    }

    [Theory]
    [InlineData(BranchMark.Merge, "⑂", Ink.Mrg)]
    [InlineData(BranchMark.FastForward, "⇢", Ink.Ok)]
    [InlineData(BranchMark.Conflict, "✕", Ink.Bad)]
    [InlineData(BranchMark.Nothing, "=", Ink.Dim)]
    [InlineData(BranchMark.Refused, "!", Ink.Warn)]
    [InlineData(BranchMark.Unknown, "·", Ink.Dim)]
    public void Each_mark_has_its_glyph_and_ink(BranchMark mark, string glyph, Ink ink)
    {
        var marks = new Dictionary<string, BranchMark> { ["feature"] = mark };

        var cell = BranchMarks.Cell("feature", false, false, NameStyle, marks, Glyphs.Unicode);

        Assert.Equal((glyph, new Style(ink)), cell);
    }

    [Fact]
    public void The_basic_glyphs_replace_the_merge_fast_forward_and_conflict_marks()
    {
        var marks = new Dictionary<string, BranchMark>
        {
            ["merge"] = BranchMark.Merge,
            ["ff"] = BranchMark.FastForward,
            ["conflict"] = BranchMark.Conflict,
        };

        Assert.Equal("Y", BranchMarks.Cell("merge", false, false, NameStyle, marks, Glyphs.Basic).Text);
        Assert.Equal(">", BranchMarks.Cell("ff", false, false, NameStyle, marks, Glyphs.Basic).Text);
        Assert.Equal("x", BranchMarks.Cell("conflict", false, false, NameStyle, marks, Glyphs.Basic).Text);
    }

    [Fact]
    public void Order_starts_at_the_selection_then_goes_up_and_down_and_skips_the_current_branch()
    {
        var branches = Branches(6, current: 5);

        var order = BranchMarks.Order(branches, selected: 2, max: 100);

        Assert.Equal(new[] { "b2", "b1", "b3", "b0", "b4" }, order.Select(b => b.Name));
    }

    [Fact]
    public void Order_from_the_top_goes_down_in_list_order()
    {
        var branches = Branches(6, current: 5);

        var order = BranchMarks.Order(branches, selected: 0, max: 100);

        Assert.Equal(new[] { "b0", "b1", "b2", "b3", "b4" }, order.Select(b => b.Name));
    }

    [Fact]
    public void Order_keeps_at_most_max_branches_and_the_selected_one_first()
    {
        var branches = Branches(151, current: 0);

        var order = BranchMarks.Order(branches, selected: 80, max: 100);

        Assert.Equal(100, order.Count);
        Assert.Equal("b80", order[0].Name);
        Assert.DoesNotContain(order, b => b.IsCurrent);
    }

    [Fact]
    public void Order_of_a_list_with_only_the_current_branch_is_empty()
    {
        var branches = Branches(1, current: 0);

        Assert.Empty(BranchMarks.Order(branches, selected: 0, max: 100));
    }

    // Branches b0..b(count-1); the one at index `current` is the current branch.
    private static List<Branch> Branches(int count, int current)
    {
        var list = new List<Branch>();
        for (var i = 0; i < count; i++)
            list.Add(new Branch("b" + i, "h" + i, "h" + i, i == current));
        return list;
    }
}
