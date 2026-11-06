using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>The todo list under the art: its lines, colours and window.</summary>
public class TodoListTests
{
    private static readonly Ink Cur = Ink.Lane0;

    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

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
        var lines = SequenceLists.Todo(State(2), 80, 9, Cur, Now);

        Assert.Equal("▸ pick   a1234567 Subject a", Text(lines[0]));
        Assert.Equal("  pick   b1234567 Subject b", Text(lines[1]));
    }

    [Fact]
    public void The_action_takes_its_colour_and_a_drop_has_a_dim_subject()
    {
        var rows = new[] { Row("a", RebaseAction.Pick), Row("b", RebaseAction.Reword), Row("c", RebaseAction.Squash), Row("d", RebaseAction.Drop) };
        var lines = SequenceLists.Todo(new TodoState(rows), 80, 9, Cur, Now);

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
        var lines = SequenceLists.Todo(State(10, 7), 80, 4, Cur, Now);

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
        var line = SequenceLists.Todo(new TodoState(rows), 30, 9, Cur, Now)[0];

        Assert.True(Text(line).Length <= 30, Text(line));
        Assert.EndsWith("…", Text(line), StringComparison.Ordinal);
    }

    [Fact]
    public void The_rows_are_listed_in_the_plans_order_after_a_move()
    {
        var s = State(3);
        s.MoveDown();

        var lines = SequenceLists.Todo(s, 80, 9, Cur, Now);

        Assert.Contains("Subject b", Text(lines[0]), StringComparison.Ordinal);
        Assert.StartsWith("▸ ", Text(lines[1]), StringComparison.Ordinal);
    }

    // A row whose commit is the given age before Now (a negative age is in the future).
    private static RebaseRow Dated(string subject, TimeSpan age, RebaseAction action = RebaseAction.Pick) =>
        new("a123456" + new string('0', 33), "a123456", subject, action, Now - age);

    private static RebaseRow Undated(string subject) => new("a123456" + new string('0', 33), "a123456", subject, RebaseAction.Pick);

    private static TodoState One(RebaseRow row) => new([row]);

    [Fact]
    public void A_row_with_a_date_ends_with_its_age_after_a_gap_and_the_line_is_the_pane_width()
    {
        var line = SequenceLists.Todo(One(Dated("Fix typo in README", TimeSpan.FromDays(3))), 60, 9, Cur, Now)[0];

        // 60 columns: the 16-column prefix, a space, the 18-column subject, a gap of 19, then "3d ago" (6 columns).
        Assert.Equal("▸ pick   a123456 Fix typo in README" + new string(' ', 19) + "3d ago", Text(line));
        Assert.Equal(new Style(Ink.Dim), line[^1].Style);
    }

    [Fact]
    public void A_row_without_a_date_shows_no_age_and_keeps_its_subject_whole()
    {
        var line = SequenceLists.Todo(One(Undated("Fix typo in README")), 60, 9, Cur, Now)[0];

        Assert.Equal("▸ pick   a123456 Fix typo in README", Text(line));
    }

    [Fact]
    public void A_long_subject_is_cut_to_make_room_for_the_age()
    {
        // 50 columns: the prefix and its space take 17, so 33 are left. The age takes 6 and the gap 2, so the subject keeps 25: 24 letters and the ellipsis.
        var line = SequenceLists.Todo(One(Dated(new string('x', 60), TimeSpan.FromHours(5))), 50, 9, Cur, Now)[0];

        Assert.Equal("▸ pick   a123456 " + new string('x', 24) + "…  5h ago", Text(line));
        Assert.Equal(50, TextFit.Columns(Text(line)));
    }

    [Fact]
    public void A_narrow_pane_drops_the_age_once_the_subject_would_keep_fewer_than_eight_columns()
    {
        // The prefix "▸ pick   a123456" is 16 columns, then a space. The subject needs 8 columns, a gap of 2 and the age "3d ago" (6).
        var row = Dated("Fix typo in README", TimeSpan.FromDays(3));

        // 33 columns: the subject keeps 8 ("Fix typ…"), the gap is 2, and the age ends the line.
        var atLimit = SequenceLists.Todo(One(row), 33, 9, Cur, Now)[0];
        Assert.Equal("▸ pick   a123456 Fix typ…  3d ago", Text(atLimit));

        // 32 columns: one short, so the age is dropped and the subject is cut to the whole width.
        var belowLimit = SequenceLists.Todo(One(row), 32, 9, Cur, Now)[0];
        Assert.Equal("▸ pick   a123456 Fix typo in RE…", Text(belowLimit));
    }

    [Fact]
    public void A_short_subject_keeps_its_age_when_the_whole_subject_fits()
    {
        // The subject is 3 columns, so 3 + 2 + 6 is the least room it needs: 28 columns is enough, and the gap is the 2 spaces.
        var line = SequenceLists.Todo(One(Dated("Fix", TimeSpan.FromDays(3))), 28, 9, Cur, Now)[0];

        Assert.Equal("▸ pick   a123456 Fix  3d ago", Text(line));
    }

    [Fact]
    public void A_drop_row_keeps_its_subject_and_its_age_dim()
    {
        var line = SequenceLists.Todo(One(Dated("Add logging", TimeSpan.FromMinutes(12), RebaseAction.Drop)), 60, 9, Cur, Now)[0];

        Assert.Equal(new Style(Ink.Dim), line[3].Style);
        Assert.Equal(new Style(Ink.Dim), line[^1].Style);
        Assert.EndsWith("12m ago", Text(line), StringComparison.Ordinal);
    }

    [Fact]
    public void A_date_in_the_future_reads_just_now()
    {
        var line = SequenceLists.Todo(One(Dated("Add logging", -TimeSpan.FromMinutes(5))), 60, 9, Cur, Now)[0];

        Assert.EndsWith("just now", Text(line), StringComparison.Ordinal);
    }
}
