using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>The todo list under the art: its lines, colours and window.</summary>
public class TodoListTests
{
    private static readonly Ink Cur = Ink.Lane0;

    private static RebaseRow Row(string name, RebaseAction action = RebaseAction.Pick) =>
        new(name + "1234567", name + "1234567", "Subject " + name, action);

    private static TodoState State(int count, int cursor = 0)
    {
        var rows = Enumerable.Range(0, count).Select(i => Row(((char)('a' + i)).ToString())).ToList();
        var s = new TodoState(rows);
        for (var i = 0; i < cursor; i++)
            s.Down();
        return s;
    }

    private static string Text(IReadOnlyList<Seg> line) => string.Concat(line.Select(s => s.Text));

    [Fact]
    public void The_cursor_row_has_a_marker_and_every_row_shows_its_action_hash_and_subject()
    {
        var lines = SequenceLists.Todo(State(2), 80, 9, Cur);

        Assert.Equal("▸ pick   a1234567 Subject a", Text(lines[0]));
        Assert.Equal("  pick   b1234567 Subject b", Text(lines[1]));
    }

    [Fact]
    public void The_action_takes_its_colour_and_a_drop_has_a_dim_subject()
    {
        var rows = new[] { Row("a", RebaseAction.Pick), Row("b", RebaseAction.Reword), Row("c", RebaseAction.Squash), Row("d", RebaseAction.Drop) };
        var lines = SequenceLists.Todo(new TodoState(rows), 80, 9, Cur);

        Assert.Equal(new Style(Cur, true), lines[0][1].Style);
        Assert.Equal(new Style(Ink.Warn, true), lines[1][1].Style);
        Assert.Equal(new Style(Ink.Mrg, true), lines[2][1].Style);
        Assert.Equal(new Style(Ink.Bad, true), lines[3][1].Style);
        Assert.Equal(new Style(Ink.Dim), lines[3][3].Style);
        Assert.Equal(new Style(Ink.Fg), lines[0][3].Style);
    }

    [Fact]
    public void The_list_keeps_the_cursor_in_view_when_it_is_longer_than_the_pane()
    {
        // Ten rows, four visible, the cursor on row 7: rows 5 to 8 are listed, with the cursor on the third line.
        var lines = SequenceLists.Todo(State(10, 7), 80, 4, Cur);

        Assert.Equal(4, lines.Count);
        Assert.StartsWith("▸ ", Text(lines[2]), StringComparison.Ordinal);
        Assert.Contains("Subject f", Text(lines[0]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10, 0, 4, 0)]
    [InlineData(10, 9, 4, 6)]
    [InlineData(10, 5, 4, 3)]
    [InlineData(3, 2, 9, 0)]
    [InlineData(10, 9, 0, 9)]
    public void The_window_starts_so_that_the_cursor_is_in_the_middle_and_never_past_the_end(int count, int cursor, int visible, int first)
    {
        Assert.Equal(first, SequenceLists.Window(count, cursor, visible));
    }

    [Fact]
    public void A_subject_is_cut_to_the_width_of_the_pane()
    {
        var rows = new[] { new RebaseRow("a1234567", "a1234567", new string('x', 60), RebaseAction.Pick) };
        var line = SequenceLists.Todo(new TodoState(rows), 30, 9, Cur)[0];

        Assert.True(Text(line).Length <= 30, Text(line));
        Assert.EndsWith("…", Text(line), StringComparison.Ordinal);
    }

    [Fact]
    public void The_rows_are_listed_in_the_plans_order_after_a_move()
    {
        var s = State(3);
        s.MoveDown();

        var lines = SequenceLists.Todo(s, 80, 9, Cur);

        Assert.Contains("Subject b", Text(lines[0]), StringComparison.Ordinal);
        Assert.StartsWith("▸ ", Text(lines[1]), StringComparison.Ordinal);
    }
}
