using graftui.Model;
using graftui.Operations;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>The interactive rebase's todo list: the cursor, the moves, the actions and the start refusals.</summary>
public class TodoStateTests
{
    private static RebaseRow Row(string name, RebaseAction action = RebaseAction.Pick) => new(name + "0000000", name, "subject " + name, action);

    private static TodoState State(params RebaseAction[] actions) =>
        new(actions.Select((a, i) => Row(((char)('a' + i)).ToString(), a)).ToList(), "feature");

    private static string Names(TodoState s) => string.Concat(s.Rows.Select(r => r.ShortHash));

    [Fact]
    public void The_cursor_wraps_from_the_top_to_the_bottom_and_back()
    {
        var s = State(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);

        s.Up();
        Assert.Equal(2, s.Cursor);
        s.Down();
        Assert.Equal(0, s.Cursor);
        s.Down();
        Assert.Equal(1, s.Cursor);
    }

    [Fact]
    public void A_move_at_either_end_does_nothing()
    {
        var top = State(RebaseAction.Pick, RebaseAction.Pick);
        top.MoveUp();
        Assert.Equal("ab", Names(top));
        Assert.Equal(0, top.Cursor);

        var bottom = State(RebaseAction.Pick, RebaseAction.Pick);
        bottom.Down();
        bottom.MoveDown();
        Assert.Equal("ab", Names(bottom));
        Assert.Equal(1, bottom.Cursor);
    }

    [Fact]
    public void A_move_down_swaps_the_row_and_the_cursor_follows_it()
    {
        var s = State(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);

        s.MoveDown();

        Assert.Equal("bac", Names(s));
        Assert.Equal(1, s.Cursor);
    }

    [Fact]
    public void Moving_down_then_up_restores_the_order()
    {
        var s = State(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);

        s.Down();
        s.MoveDown();
        s.MoveUp();

        Assert.Equal("abc", Names(s));
        Assert.Equal(1, s.Cursor);
        Assert.Equal("abc", string.Concat(s.Original.Select(r => r.ShortHash)));
    }

    [Fact]
    public void Set_changes_the_action_of_the_cursor_row_only()
    {
        var s = State(RebaseAction.Pick, RebaseAction.Pick);
        s.Down();

        s.Set(RebaseAction.Drop);

        Assert.Equal(new[] { RebaseAction.Pick, RebaseAction.Drop }, s.Rows.Select(r => r.Action));
    }

    [Fact]
    public void A_plan_where_every_row_is_dropped_is_refused_to_start()
    {
        var s = State(RebaseAction.Drop, RebaseAction.Drop);

        Assert.Equal(RebaseText.KeepOne, s.StartRefusal());
    }

    [Fact]
    public void A_squash_as_the_first_kept_row_is_refused_to_start()
    {
        var s = State(RebaseAction.Squash, RebaseAction.Pick);

        Assert.Equal(RebaseText.FirstSquash, s.StartRefusal());
    }

    [Fact]
    public void A_fixup_as_the_first_kept_row_is_refused_to_start()
    {
        var s = State(RebaseAction.Fixup, RebaseAction.Pick);

        Assert.Equal(RebaseText.FirstFixup, s.StartRefusal());
    }

    [Fact]
    public void A_drop_then_a_squash_is_refused_because_the_squash_is_the_first_kept_row()
    {
        var s = State(RebaseAction.Drop, RebaseAction.Squash, RebaseAction.Pick);

        Assert.Equal(RebaseText.FirstSquash, s.StartRefusal());
    }

    [Fact]
    public void A_plan_that_starts_with_a_pick_may_start()
    {
        var s = State(RebaseAction.Pick, RebaseAction.Squash, RebaseAction.Drop);

        Assert.Null(s.StartRefusal());
    }

    [Theory]
    [InlineData("dd")]
    [InlineData("sp")]
    [InlineData("fp")]
    [InlineData("dfp")]
    [InlineData("ps")]
    [InlineData("pdr")]
    [InlineData("pppp")]
    public void The_start_refusal_agrees_with_the_engines_plan_check(string codes)
    {
        var actions = codes.Select(c => RebasePlan.ActionForKey(c)!.Value).ToArray();
        var s = State(actions);

        Assert.Equal(s.StartRefusal() is null, s.Analysis.Error.Length == 0);
    }
}
