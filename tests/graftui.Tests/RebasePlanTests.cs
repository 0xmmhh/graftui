using graftui.Model;
using graftui.Operations;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>The todo list's plan reads and edits: the port of the desktop plan, which must refuse what the engine refuses.</summary>
public class RebasePlanTests
{
    private static RebaseRow Row(string name, RebaseAction action = RebaseAction.Pick) => new(name + "0000000", name, "subject " + name, action);

    private static IReadOnlyList<RebaseRow> Plan(params RebaseAction[] actions) =>
        actions.Select((a, i) => Row(((char)('a' + i)).ToString(), a)).ToList();

    [Fact]
    public void An_all_pick_plan_keeps_every_row_and_has_no_error()
    {
        var analysis = RebasePlan.Analyze(Plan(RebaseAction.Pick, RebaseAction.Pick), "feature");

        Assert.Equal(new[] { 0, 1 }, analysis.Kept);
        Assert.Empty(analysis.Dropped);
        Assert.All(analysis.Groups, g => Assert.Empty(g.Folds));
        Assert.Equal("", analysis.Error);
    }

    [Fact]
    public void A_fixup_folds_into_the_kept_row_above_it_and_a_squash_marks_the_group_as_a_message_pause()
    {
        var fixup = RebasePlan.Analyze(Plan(RebaseAction.Pick, RebaseAction.Fixup), "feature");
        var squash = RebasePlan.Analyze(Plan(RebaseAction.Pick, RebaseAction.Squash), "feature");

        Assert.Equal(new[] { 1 }, fixup.Groups[0].Folds);
        Assert.False(fixup.Groups[0].HasSquash);
        Assert.True(squash.Groups[0].HasSquash);
        Assert.Equal("", fixup.Error);
    }

    [Fact]
    public void A_dropped_row_is_listed_and_never_kept()
    {
        var analysis = RebasePlan.Analyze(Plan(RebaseAction.Pick, RebaseAction.Drop, RebaseAction.Reword), "feature");

        Assert.Equal(new[] { 0, 2 }, analysis.Kept);
        Assert.Equal(new[] { 1 }, analysis.Dropped);
    }

    [Fact]
    public void A_squash_with_nothing_above_it_is_refused_in_the_engine_words()
    {
        var analysis = RebasePlan.Analyze(Plan(RebaseAction.Squash, RebaseAction.Pick), "feature");

        Assert.Equal(SequenceRefusals.FoldFirst("squash"), analysis.Error);
        Assert.Equal("squash needs a commit above it to fold into", analysis.Error);
    }

    [Fact]
    public void A_fixup_after_only_drops_is_refused_as_a_fixup()
    {
        var analysis = RebasePlan.Analyze(Plan(RebaseAction.Drop, RebaseAction.Fixup, RebaseAction.Pick), "feature");

        Assert.Equal("fixup needs a commit above it to fold into", analysis.Error);
    }

    [Fact]
    public void A_plan_that_drops_every_row_is_refused_as_empty()
    {
        var analysis = RebasePlan.Analyze(Plan(RebaseAction.Drop, RebaseAction.Drop), "feature");

        Assert.Equal(SequenceRefusals.EmptyPlan("feature"), analysis.Error);
    }

    [Fact]
    public void Move_puts_the_row_at_the_new_index_and_leaves_the_original_list_alone()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick, RebaseAction.Pick);

        var moved = RebasePlan.Move(rows, 2, 0);

        Assert.Equal(new[] { "c", "a", "b" }, moved.Select(r => r.ShortHash));
        Assert.Equal(new[] { "a", "b", "c" }, rows.Select(r => r.ShortHash));
    }

    [Fact]
    public void Move_refuses_an_index_outside_the_rows()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick);

        Assert.Throws<ArgumentOutOfRangeException>(() => RebasePlan.Move(rows, 2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RebasePlan.Move(rows, 0, -1));
    }

    [Fact]
    public void WithAction_changes_only_the_action_of_that_row()
    {
        var rows = Plan(RebaseAction.Pick, RebaseAction.Pick);

        var changed = RebasePlan.WithAction(rows, 1, RebaseAction.Drop);

        Assert.Equal(new[] { RebaseAction.Pick, RebaseAction.Drop }, changed.Select(r => r.Action));
        Assert.Equal(RebaseAction.Pick, rows[1].Action);
    }

    [Fact]
    public void WithAction_refuses_an_index_outside_the_rows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RebasePlan.WithAction(Plan(RebaseAction.Pick), 1, RebaseAction.Drop));
    }

    [Theory]
    [InlineData('p', RebaseAction.Pick)]
    [InlineData('r', RebaseAction.Reword)]
    [InlineData('e', RebaseAction.Edit)]
    [InlineData('s', RebaseAction.Squash)]
    [InlineData('f', RebaseAction.Fixup)]
    [InlineData('d', RebaseAction.Drop)]
    [InlineData('D', RebaseAction.Drop)]
    public void A_letter_key_sets_its_action(char key, RebaseAction expected)
    {
        Assert.Equal(expected, RebasePlan.ActionForKey(key));
    }

    [Theory]
    [InlineData('x')]
    [InlineData('\0')]
    [InlineData('1')]
    public void Any_other_key_sets_no_action(char key)
    {
        Assert.Null(RebasePlan.ActionForKey(key));
    }
}
