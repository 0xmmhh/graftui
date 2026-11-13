using graftui.Ui.Text;
using graftui.Ui.Views;

namespace graftui.Tests;

public class LogViewTests
{
    private static int[] Ones(int count) => Enumerable.Repeat(1, count).ToArray();

    [Fact]
    public void No_anchor_shows_the_newest_lines()
    {
        Assert.Equal(7, LogView.TopLine(Ones(10), rows: 3, anchor: -1));
    }

    [Fact]
    public void No_anchor_with_fewer_lines_than_rows_starts_at_the_top()
    {
        Assert.Equal(0, LogView.TopLine(Ones(2), rows: 3, anchor: -1));
    }

    [Fact]
    public void Anchor_above_the_end_is_the_top_line()
    {
        Assert.Equal(4, LogView.TopLine(Ones(10), rows: 3, anchor: 4));
    }

    [Fact]
    public void Anchor_near_the_end_is_capped_at_the_newest_lines()
    {
        Assert.Equal(7, LogView.TopLine(Ones(10), rows: 3, anchor: 9));
    }

    [Fact]
    public void Everything_fits_with_an_anchor_starts_at_the_top()
    {
        Assert.Equal(0, LogView.TopLine(Ones(3), rows: 5, anchor: 2));
    }

    [Fact]
    public void Zero_rows_keeps_the_anchor()
    {
        Assert.Equal(4, LogView.TopLine(Ones(10), rows: 0, anchor: 4));
    }

    [Fact]
    public void A_tall_newest_line_is_the_bottom_line_even_when_it_is_taller_than_the_pane()
    {
        Assert.Equal(1, LogView.TopLine([1, 5], rows: 3, anchor: -1));
    }

    [Fact]
    public void Taller_lines_count_their_rows_when_choosing_the_newest_lines_that_fit()
    {
        // 1 + 3 rows fit in 4; the 1-row line above them is not shown.
        Assert.Equal(1, LogView.TopLine([1, 1, 3], rows: 4, anchor: -1));
        Assert.Equal(0, LogView.TopLine([1, 1, 3], rows: 5, anchor: -1));
    }

    [Fact]
    public void A_short_message_is_one_row_unchanged()
    {
        var line = new LogLine(LogKind.Message, "Refused: working tree has uncommitted changes", 0, default);

        var rows = LogView.Rows(line, 60);

        Assert.Equal([line], rows);
    }

    [Fact]
    public void A_long_message_wraps_at_words_and_every_row_fits()
    {
        var text = "the step is refs/graftui/undo/000001: git update-ref -d refs/graftui/undo/000001 drops it so older steps become reachable";
        var line = new LogLine(LogKind.Message, "    " + text, 0, default);

        var rows = LogView.Rows(line, 40);

        Assert.True(rows.Count > 2);
        Assert.All(rows, r => Assert.True(TextFit.Columns(r.Text) <= 40, r.Text));
        Assert.All(rows, r => Assert.StartsWith("    ", r.Text));
        Assert.Equal(text, string.Join(" ", rows.Select(r => r.Text.Trim())));
    }

    [Fact]
    public void Command_and_output_lines_stay_one_row()
    {
        var line = new LogLine(LogKind.Command, "git update-ref -d refs/graftui/undo/000001", 0);

        Assert.Equal([line], LogView.Rows(line, 10));
    }

    [Fact]
    public void The_anchor_maps_to_the_first_line_of_its_entry_or_a_later_one()
    {
        // Entry 1 is left out of the simplified list, so the anchor on entry 1 lands on entry 2's first line.
        Assert.Equal(1, LogView.AnchorLine([0, 2, 2], anchorEntry: 1));
        Assert.Equal(0, LogView.AnchorLine([0, 2, 2], anchorEntry: 0));
    }

    [Fact]
    public void No_anchor_or_no_line_from_the_anchor_on_gives_no_anchor_line()
    {
        Assert.Equal(-1, LogView.AnchorLine([0, 1], anchorEntry: -1));
        Assert.Equal(-1, LogView.AnchorLine([0, 1], anchorEntry: 2));
        Assert.Equal(-1, LogView.AnchorLine([], anchorEntry: 0));
    }

    [Fact]
    public void A_failed_command_wraps_like_a_message_and_every_row_fits()
    {
        var line = new LogLine(LogKind.Command, "git merge --no-edit feature/a-branch-name then the rest of a long line", 1, Failed: true);

        var rows = LogView.Rows(line, 40);

        Assert.True(rows.Count > 1);
        Assert.All(rows, r => Assert.Equal(LogKind.Message, r.Kind));
        Assert.All(rows, r => Assert.Equal(new Style(Ink.Bad), r.Style));
        Assert.All(rows, r => Assert.True(TextFit.Columns(r.Text) <= 40, r.Text));
        Assert.StartsWith("$ git merge", rows[0].Text);
        Assert.EndsWith("[exit 1]", rows[^1].Text);
    }

    [Fact]
    public void A_failed_command_keeps_two_spaces_before_the_exit_code_when_it_fits_on_the_last_row()
    {
        var line = new LogLine(LogKind.Command, "git merge --no-edit feature/conflict", 1, Failed: true);

        Assert.Equal(["$ git merge --no-edit feature/conflict  [exit 1]"], LogView.Rows(line, 60).Select(r => r.Text));
        Assert.Equal(["$ git merge --no-edit feature/conflict", "[exit 1]"], LogView.Rows(line, 40).Select(r => r.Text));
    }

    [Fact]
    public void A_failed_command_with_a_long_path_keeps_every_character_of_it()
    {
        var path = "some/very/long/path/that/goes/on/and/on/and/on/file.txt";
        var line = new LogLine(LogKind.Command, "git restore -- " + path, 1, Failed: true);

        var rows = LogView.Rows(line, 24);

        Assert.All(rows, r => Assert.True(TextFit.Columns(r.Text) <= 24, r.Text));
        Assert.DoesNotContain(rows, r => r.Text.Contains(TextFit.Ellipsis));
        Assert.Contains(path, string.Concat(rows.Select(r => r.Text)));
        Assert.EndsWith("[exit 1]", rows[^1].Text);
    }

    [Fact]
    public void A_successful_simplified_command_is_cut_so_that_its_tick_still_fits()
    {
        var text = "git commit -m \"a subject that is much longer than the pane is wide\"";

        var shown = LogView.SimpleOkText(text, 30);

        Assert.EndsWith(TextFit.Ellipsis, shown);
        // The reserve covers "$ " before the text and "  ✔" after it: the row is at most the pane wide.
        Assert.True(TextFit.Columns(shown) + LogView.SimpleOkReserve <= 30);
        Assert.Equal(text, LogView.SimpleOkText(text, 200));
    }

    [Fact]
    public void A_successful_command_stays_one_row_however_long()
    {
        var line = new LogLine(LogKind.Command, "git merge feature/a-branch-with-a-long-name-that-goes-on", 0, Simple: true);

        Assert.Equal([line], LogView.Rows(line, 30));
    }

    [Fact]
    public void Toggling_the_list_keeps_the_entries_and_their_positions()
    {
        var log = new LogView();
        log.Add(new LogEntry([new LogLine(LogKind.Command, "git merge --no-edit feature/x", 0)], [new LogLine(LogKind.Command, "git merge feature/x", 0, Simple: true)]));
        log.Add(new LogEntry([new LogLine(LogKind.Command, "git rev-parse -q --verify MERGE_HEAD", 1)], []));
        var position = log.Position;

        Assert.False(log.ShowExact);
        log.ToggleExact();
        Assert.True(log.ShowExact);
        log.ToggleExact();
        Assert.False(log.ShowExact);

        Assert.Equal(2, log.Count);
        Assert.Equal(position, log.Position);
    }

    [Fact]
    public void Positions_count_entries_not_lines()
    {
        var log = new LogView();
        log.Add(new LogEntry(
            [new LogLine(LogKind.Command, "git commit", 0), new LogLine(LogKind.Output, "[main 1a2b3c4] Fix"), new LogLine(LogKind.Output, "1 file changed")],
            [new LogLine(LogKind.Command, "git commit -m \"Fix\"", 0, Simple: true), new LogLine(LogKind.Output, "[main 1a2b3c4] Fix")]));
        var second = log.Position;
        log.Add(new LogLine(LogKind.Message, "next"));

        Assert.Equal(1, second);
        Assert.Equal(2, log.Position);
        log.SetAnchor(second);
        log.ToggleExact();
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void An_empty_log_shows_the_hint_in_the_pane()
    {
        Assert.True(new LogView().ShowsEmptyHint);
        Assert.Equal("git commands graftui runs show up here", LogView.EmptyHint);
    }

    [Fact]
    public void A_message_line_takes_the_place_of_the_hint()
    {
        var log = new LogView();
        log.Add(new LogLine(LogKind.Message, "x"));

        Assert.False(log.ShowsEmptyHint);
    }

    [Fact]
    public void An_entry_with_no_simplified_lines_keeps_the_hint_until_the_exact_list_is_shown()
    {
        var log = new LogView();
        log.Add(new LogEntry([new LogLine(LogKind.Command, "git rev-parse -q --verify MERGE_HEAD", 1)], []));

        Assert.True(log.ShowsEmptyHint);
        log.ToggleExact();
        Assert.False(log.ShowsEmptyHint);
    }

    private static LogLine Merge(bool failed)
    {
        var exitCode = failed ? 1 : 0;
        return new LogLine(LogKind.Command, "git merge feature/x", exitCode, Simple: true, Failed: failed);
    }

    private static LogLine Abort() => new LogLine(LogKind.Command, "git merge --abort", 0, Simple: true);

    [Fact]
    public void The_commands_list_opens_and_closes_and_the_strip_is_the_default()
    {
        var log = new LogView();
        Assert.False(log.Open);

        log.Toggle();
        Assert.True(log.Open);
        log.Close();
        Assert.False(log.Open);
        log.Close();
        Assert.False(log.Open);
    }

    [Fact]
    public void An_empty_log_has_no_strip_line()
    {
        Assert.Null(new LogView().StripLineNow());
        Assert.Null(LogView.StripLine([], 0));
    }

    [Fact]
    public void The_strip_shows_the_newest_line_when_nothing_failed()
    {
        var older = new LogLine(LogKind.Command, "git add -A", 0, Simple: true);
        var newest = Abort();

        Assert.Equal(newest, LogView.StripLine([older, newest], 0));
    }

    [Fact]
    public void A_failed_merge_in_the_current_step_wins_over_the_newer_abort_after_a_conflict()
    {
        var log = new LogView();
        log.Add(new LogEntry([Merge(failed: true)], [Merge(failed: true)]));
        log.Add(new LogEntry([Abort()], [Abort()]));

        Assert.Equal(Merge(failed: true), log.StripLineNow());
    }

    [Fact]
    public void A_failure_from_an_earlier_step_does_not_win_after_a_key_press()
    {
        var log = new LogView();
        log.Add(new LogEntry([Merge(failed: true)], [Merge(failed: true)]));
        log.MarkStep();
        log.Add(new LogEntry([Abort()], [Abort()]));

        Assert.Equal(Abort(), log.StripLineNow());
    }

    [Fact]
    public void A_bad_message_in_the_current_step_wins_like_a_failed_command()
    {
        var bad = new LogLine(LogKind.Message, "Refused: working tree has uncommitted changes", 0, new Style(Ink.Bad));
        var lines = new List<LogLine> { bad, Abort() };

        Assert.Equal(bad, LogView.StripLine(lines, 0));
        Assert.Equal(Abort(), LogView.StripLine(lines, 1));
    }

    [Fact]
    public void A_step_with_no_lines_yet_shows_the_newest_line_of_the_log()
    {
        var log = new LogView();
        log.Add(new LogEntry([Abort()], [Abort()]));
        log.MarkStep();

        Assert.Equal(Abort(), log.StripLineNow());
    }

    [Fact]
    public void A_bold_red_message_is_a_failure_like_a_plain_red_one()
    {
        var bold = new LogLine(LogKind.Message, "Conflicting files: a.txt", 0, new Style(Ink.Bad, true));

        Assert.True(LogView.IsFailure(bold));
        Assert.Equal(bold, LogView.StripLine([Abort(), bold], 0));
    }

    [Fact]
    public void A_refusal_in_the_step_wins_over_its_indented_detail_lines()
    {
        var refused = new LogLine(LogKind.Message, "Refused: working tree has uncommitted changes", 0, new Style(Ink.Warn, true));
        var detail = new LogLine(LogKind.Message, "    commit or stash them first", 0);

        Assert.Equal(refused, LogView.StripLine([refused, detail], 0));
        Assert.True(LogView.HasDetails(refused));
        Assert.False(LogView.IsFailure(refused));
    }

    [Fact]
    public void A_failure_in_the_step_wins_over_a_newer_refusal()
    {
        var refused = new LogLine(LogKind.Message, "Refused: working tree has uncommitted changes", 0, new Style(Ink.Warn, true));

        Assert.Equal(Merge(failed: true), LogView.StripLine([Merge(failed: true), refused], 0));
    }

    [Fact]
    public void A_refusal_from_an_earlier_step_does_not_win()
    {
        var refused = new LogLine(LogKind.Message, "Refused: working tree has uncommitted changes", 0, new Style(Ink.Warn, true));

        Assert.Equal(Abort(), LogView.StripLine([refused, Abort()], 1));
    }

    [Fact]
    public void Git_output_is_never_the_strip_line_so_a_fast_forward_shows_its_command()
    {
        var merge = new LogLine(LogKind.Command, "git merge feature/x", 0, Simple: true);
        var chatter = new LogLine(LogKind.Output, "Updating 1a2b3c4..5d6e7f8");
        var stat = new LogLine(LogKind.Output, " file.txt | 2 +-");

        Assert.Equal(merge, LogView.StripLine([merge, chatter, stat], 0));
    }

    [Fact]
    public void An_indented_detail_line_is_not_the_strip_line_on_its_own()
    {
        var detail = new LogLine(LogKind.Message, "    a detail", 0);

        Assert.Equal(Abort(), LogView.StripLine([Abort(), detail], 0));
    }

    [Fact]
    public void A_step_of_only_git_output_has_no_strip_line()
    {
        Assert.Null(LogView.StripLine([new LogLine(LogKind.Output, "Updating 1a2b3c4")], 0));
    }

    [Fact]
    public void A_conflicting_files_message_after_the_failed_merge_is_the_newer_failure()
    {
        var conflict = new LogLine(LogKind.Message, "Conflicting files: a.txt", 0, new Style(Ink.Bad));

        Assert.Equal(conflict, LogView.StripLine([Merge(failed: true), Abort(), conflict], 0));
    }
}
