using graftui.Operations;
using graftui.Ui.Text;
using graftui.Ui.Views;

namespace graftui.Tests;

/// <summary>
/// A refusal with a git command in it is shown in two places: the first line in the caption and the overlay, and the whole
/// reason in the Commands pane, wrapped. These tests pin both, at the sizes the screen is laid out for.
/// </summary>
public class RefusalTextTests
{
    private const string Step = "refs/graftui/undo/000001";
    private const string Pop = "refs/graftui/rescue/pop";

    private static readonly string[] Samples =
    [
        ChangeRefusals.WithUndoHint(ChangeRefusals.UndoDirty, Step),
        ChangeRefusals.WithUndoHint(ChangeRefusals.UndoWrongBranch("main"), Step),
        ChangeRefusals.WithUndoHint(ChangeRefusals.UndoStashGone, Step),
        ChangeRefusals.RescueSaved("your state from before an earlier pop", Pop),
        ChangeRefusals.RescueSaved("the state from before a stash undo that failed", "refs/graftui/rescue/undo-stash"),
        ChangeRefusals.UndoUnreadable(Step),
    ];

    [Fact]
    public void The_undo_hint_is_the_detail_and_the_guard_refusal_is_the_first_line()
    {
        var refusal = ChangeRefusals.WithUndoHint(ChangeRefusals.UndoDirty, Step);

        Assert.Equal(ChangeRefusals.UndoDirty, ChangeRefusals.Short(refusal));
        Assert.Equal([$"the step is {Step}: git update-ref -d {Step} drops it so older steps become reachable"],
            ChangeRefusals.Details(refusal));
    }

    [Fact]
    public void The_rescue_refusal_keeps_its_first_sentence_and_puts_the_three_ways_out_in_the_detail()
    {
        var refusal = ChangeRefusals.RescueSaved("your state from before an earlier pop", Pop);

        Assert.Equal($"your state from before an earlier pop is saved in {Pop}", ChangeRefusals.Short(refusal));
        var detail = Assert.Single(ChangeRefusals.Details(refusal));
        Assert.Contains($"inspect it with git show {Pop},", detail, StringComparison.Ordinal);
        Assert.Contains($"restore it with git restore --source={Pop} --worktree -- .,", detail, StringComparison.Ordinal);
        Assert.Contains($"or delete it with git update-ref -d {Pop}", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_without_detail_is_unchanged_and_logs_one_line()
    {
        Assert.Equal(ChangeRefusals.NothingStaged, ChangeRefusals.Short(ChangeRefusals.NothingStaged));
        Assert.Empty(ChangeRefusals.Details(ChangeRefusals.NothingStaged));
        Assert.Equal(["Refused: " + ChangeRefusals.NothingStaged], RefusalText.LogLines(ChangeRefusals.NothingStaged));
        Assert.Equal("Refused: " + ChangeRefusals.NothingStaged, RefusalText.Caption(ChangeRefusals.NothingStaged));
    }

    [Fact]
    public void The_log_lines_give_the_reason_first_and_the_detail_indented_under_it()
    {
        var lines = RefusalText.LogLines(ChangeRefusals.RescueSaved("your state from before an earlier pop", Pop));

        Assert.Equal(2, lines.Count);
        Assert.Equal($"Refused: your state from before an earlier pop is saved in {Pop}", lines[0]);
        Assert.StartsWith($"    inspect it with git show {Pop},", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_caption_shows_the_first_line_whole_in_two_rows_at_80_by_24()
    {
        // The art pane is the screen minus the widest the branches pane gets; the caption is 4 columns narrower than that.
        // The failed stash undo's rescue sentence is longer than two such rows and is cut in the caption; its whole text is
        // in the Commands pane (the test below), so it is left out here.
        var artWidth = 80 - ScreenLayout.Compute(80, 24, wantedBranchWidth: 1000).BranchWidth;
        var captionWidth = artWidth - 4;

        foreach (var sample in Samples.Where(s => !s.Contains("refs/graftui/rescue/undo-stash", StringComparison.Ordinal)))
        {
            var (first, second) = TextFit.Wrap2(RefusalText.Caption(sample), captionWidth);
            Assert.DoesNotContain("…", first + second, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(60)]
    [InlineData(76)]
    [InlineData(96)]
    [InlineData(106)]
    public void The_whole_reason_reads_in_the_commands_pane_at_every_width(int paneColumns)
    {
        foreach (var sample in Samples)
        {
            var rows = RefusalText.LogLines(sample)
                .SelectMany(l => LogView.Rows(new LogLine(LogKind.Message, l), paneColumns))
                .ToList();

            Assert.All(rows, r => Assert.True(TextFit.Columns(r.Text) <= paneColumns, r.Text));
            var joined = string.Join(" ", rows.Select(r => r.Text.Trim()));
            Assert.Contains(string.Join(" ", ChangeRefusals.Details(sample).Select(d => d.Trim())), joined, StringComparison.Ordinal);
            Assert.Contains(ChangeRefusals.Short(sample), joined, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void At_80_by_24_the_detail_fits_the_commands_pane_without_scrolling()
    {
        var paneRows = ScreenLayout.Compute(80, 24, wantedBranchWidth: 30).LogHeight - 2;
        var paneColumns = 80 - 4;

        foreach (var sample in Samples)
        {
            var detailRows = ChangeRefusals.Details(sample)
                .Sum(d => LogView.Rows(new LogLine(LogKind.Message, "    " + d), paneColumns).Count);
            Assert.True(detailRows <= paneRows, $"{detailRows} rows for {sample}");
        }
    }
}
