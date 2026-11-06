using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>The todo list's plan edits (a move, an action) and the key that names an action.</summary>
public class RebasePlanTests
{
    private static RebaseRow Row(string name, RebaseAction action = RebaseAction.Pick) => new(name + "0000000", name, "subject " + name, action);

    private static IReadOnlyList<RebaseRow> Plan(params RebaseAction[] actions) =>
        actions.Select((a, i) => Row(((char)('a' + i)).ToString(), a)).ToList();

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
