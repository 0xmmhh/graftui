using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The words of a paused sequence: the caption and its colour, the status bar, the exit line, which pause counts as graftui's, and the
/// resolved flags a poll compares.
/// </summary>
public class SequenceStatusTests
{
    private static readonly Style BadB = new(Ink.Bad, true), WarnB = new(Ink.Warn, true);

    private static SequenceStop Stop(PauseKind pause, SequenceKind kind = SequenceKind.Rebase, bool ours = true, int step = 1, int total = 1) =>
        new(kind, pause, "feature/conflict", "main", "c0ffee1234abc", "c0ffee1", "Change line 1 on branch",
            [new ConflictFile("app.txt", false)], step, total, "old message", [], ours);

    private static string Text(IEnumerable<Seg> segs) => string.Concat(segs.Select(s => s.Text));

    [Fact]
    public void The_status_bar_names_the_branch_its_progress_and_what_it_waits_for()
    {
        var bar = SequenceStatus.Segs(Stop(PauseKind.Conflict), new Style(Ink.Cur, true));

        Assert.Equal("feature/conflict (rebasing 1/1) · conflict", Text(bar));
        Assert.Equal(new Style(Ink.Cur, true), bar[0].Style);
    }

    [Theory]
    [InlineData(PauseKind.Edit, "editing")]
    [InlineData(PauseKind.Reword, "waiting for a message")]
    [InlineData(PauseKind.Squash, "waiting for a message")]
    public void The_status_bar_says_what_the_other_stops_wait_for(PauseKind pause, string word) =>
        Assert.EndsWith("· " + word, Text(SequenceStatus.Segs(Stop(pause, step: 2, total: 3), new Style(Ink.Cur))));

    [Fact]
    public void A_cherry_pick_shows_its_progress_as_picking()
    {
        var bar = SequenceStatus.Segs(Stop(PauseKind.Conflict, SequenceKind.CherryPick, step: 1, total: 2), new Style(Ink.Cur));

        Assert.Equal("feature/conflict (picking 1/2) · conflict", Text(bar));
    }

    [Fact]
    public void The_exit_line_says_what_is_paused_and_what_to_run_next()
    {
        Assert.Equal("graftui: rebase of feature/conflict onto main is paused at c0ffee1 (1/1): run graftui again to continue, skip or abort",
            SequenceStatus.ExitLine(Stop(PauseKind.Conflict)));
        Assert.Equal("graftui: cherry-pick onto feature/conflict is paused at c0ffee1 (1/2): run graftui again to continue, skip or abort",
            SequenceStatus.ExitLine(Stop(PauseKind.Conflict, SequenceKind.CherryPick, step: 1, total: 2)));
    }

    [Fact]
    public void A_conflict_caption_names_the_file_and_the_keys_and_is_bad()
    {
        var caption = SequenceStatus.Caption(Stop(PauseKind.Conflict));

        Assert.Equal("✕ CONFLICT in app.txt: resolve it in your editor, then Enter continues, or press X to abort", caption.Text);
        Assert.Equal(BadB, caption.Style);
    }

    [Fact]
    public void An_edit_or_message_caption_is_a_warning_and_names_its_commit()
    {
        var edit = SequenceStatus.Caption(Stop(PauseKind.Edit));
        var message = SequenceStatus.Caption(Stop(PauseKind.Reword));

        Assert.Equal("Stopped at c0ffee1 to edit · change files, stage them, then Amend & continue", edit.Text);
        Assert.Equal(WarnB, edit.Style);
        Assert.Equal("Paused at c0ffee1 for its message · Continue opens it again", message.Text);
        Assert.Equal(WarnB, message.Style);
    }

    [Fact]
    public void Only_a_pause_graftui_started_counts_as_a_pause_on_the_snapshot()
    {
        var ours = Snap(Stop(PauseKind.Conflict));
        var foreign = Snap(Stop(PauseKind.Conflict, ours: false));

        Assert.True(SequenceStatus.IsPaused(ours));
        Assert.False(SequenceStatus.IsPaused(foreign));
        Assert.False(SequenceStatus.IsPaused(Snap(null)));
        Assert.Equal<PauseKind?>(PauseKind.Edit, SequenceStatus.KindOf(Snap(Stop(PauseKind.Edit))));
        Assert.Null(SequenceStatus.KindOf(foreign));
        Assert.Equal("feature/conflict", SequenceStatus.PausedBranch(ours));
        Assert.Equal("", SequenceStatus.PausedBranch(foreign));
    }

    [Fact]
    public void A_resolved_flag_that_changed_or_a_different_list_counts_as_a_change()
    {
        var open = new[] { new ConflictFile("app.txt", false) };
        var done = new[] { new ConflictFile("app.txt", true) };

        Assert.False(SequenceStatus.ResolvedChanged(open, [new ConflictFile("app.txt", false)]));
        Assert.True(SequenceStatus.ResolvedChanged(open, done));
        Assert.True(SequenceStatus.ResolvedChanged(open, []));
    }

    private static RepoSnapshot Snap(SequenceStop? stop) =>
        new("feature/conflict", "a111111", false, [new Branch("feature/conflict", "a111111", "a111111", false)],
            new Dictionary<string, (int, int)>())
        {
            Paused = stop,
        };
}
