using System.Text;
using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;
using graftui.Ui.Text;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Time;

namespace graftui.Tests;

/// <summary>
/// --glyphs basic on the new look, read from the headless driver's drawn cells: the Branches tab at 120 × 40 and 80 × 24, the merge card,
/// the replay and the help popup draw no character outside ASCII. The default set still draws Unicode, so the check can fail.
/// The branch names in the test repo are ASCII, so every non-ASCII cell is a glyph. They share a headless Terminal.Gui session and
/// Glyphs.Current, so they run one at a time, and Dispose puts the default set back.
/// </summary>
[Collection("TerminalGui")]
public sealed class GlyphsScreenTests : IDisposable
{
    private readonly IApplication _ui;

    public GlyphsScreenTests()
    {
        _ui = Application.Create(new VirtualTimeProvider());
        _ui.Init("ansi");
    }

    public void Dispose()
    {
        Glyphs.Current = Glyphs.Unicode;
        _ui.Dispose();
    }

    [Fact]
    public void The_default_set_draws_unicode_so_the_check_can_fail()
    {
        Glyphs.Current = Glyphs.Unicode;
        using var repo = TempRepo.Diverged(false);
        Open(repo, 120, 40);

        Assert.NotEmpty(NonAsciiCells());
    }

    [Fact]
    public void The_branches_tab_is_plain_ascii_at_120_by_40()
    {
        Glyphs.Current = Glyphs.Basic;
        using var repo = TempRepo.Diverged(false);
        Open(repo, 120, 40);

        Assert.Contains("Branches", ScreenText());
        Assert.Empty(NonAsciiCells());
    }

    [Fact]
    public void The_branches_tab_is_plain_ascii_at_80_by_24()
    {
        Glyphs.Current = Glyphs.Basic;
        using var repo = TempRepo.Diverged(false);
        Open(repo, 80, 24);

        Assert.Contains("Branches", ScreenText());
        Assert.Empty(NonAsciiCells());
    }

    [Fact]
    public void The_merge_card_and_its_replay_are_plain_ascii()
    {
        Glyphs.Current = Glyphs.Basic;
        using var repo = TempRepo.Diverged(conflict: false);
        var snapshot = Snapshot(repo);
        var app = Open(repo, 120, 40);

        MergeDigestScreenTests.Merge(app, repo, snapshot, "feature");
        _ui.LayoutAndDraw();
        Assert.Contains("Merged", ScreenText());
        Assert.Empty(NonAsciiCells());

        _ui.Keyboard.RaiseKeyDownEvent(Key.G);
        _ui.LayoutAndDraw();
        Assert.True(app.Digest.Replaying);
        Assert.Empty(NonAsciiCells());
    }

    [Fact]
    public void The_help_popup_is_plain_ascii()
    {
        AssertHelpPopupIsPlainAscii(120, 40);
    }

    [Fact]
    public void The_help_popup_at_80_by_24_has_the_glyphs_row_and_is_plain_ascii()
    {
        AssertHelpPopupIsPlainAscii(80, 24);
    }

    [Fact]
    public void The_rebase_pause_screen_with_a_conflicted_file_is_plain_ascii()
    {
        Glyphs.Current = Glyphs.Basic;
        using var repo = SequenceRepos.FeatureConflict();
        var op = new RebaseOperation(repo.Git);
        var request = new RebaseRequest("main");
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(request, check).Kind);
        var app = Open(repo, 120, 40);

        Assert.True(app.IsPaused);
        Assert.Contains("app.txt", ScreenText());
        Assert.Empty(NonAsciiCells());
    }

    // Opens the help popup on the basic set at the given size: it must show the --glyphs row and draw no non-ASCII cell.
    private void AssertHelpPopupIsPlainAscii(int cols, int rows)
    {
        Glyphs.Current = Glyphs.Basic;
        using var repo = TempRepo.Diverged(false);
        var app = Open(repo, cols, rows);

        _ui.Keyboard.RaiseKeyDownEvent(new Key('?'));
        _ui.LayoutAndDraw();

        Assert.True(app.HelpOpen);
        Assert.Contains("--glyphs", ScreenText());
        Assert.Empty(NonAsciiCells());
    }

    private AppController Open(TempRepo repo, int cols, int rows)
    {
        _ui.Driver!.SetScreenSize(cols, rows);
        var app = new AppController(_ui, repo.Git, repo.Git, Snapshot(repo), "repo", newUi: true);
        var window = app.BuildWindow();
        _ui.Begin(window);
        _ui.LayoutAndDraw(true);
        return app;
    }

    private static RepoSnapshot Snapshot(TempRepo repo) => new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);

    // Every drawn cell that holds a character outside ASCII, as "row,col text".
    private List<string> NonAsciiCells()
    {
        var found = new List<string>();
        var cells = _ui.Driver!.Contents!;
        for (var row = 0; row < cells.GetLength(0); row++)
        {
            for (var col = 0; col < cells.GetLength(1); col++)
            {
                var text = cells[row, col].Grapheme;
                if (!IsAscii(text))
                    found.Add(row + "," + col + " " + text);
            }
        }
        return found;
    }

    // The screen as text, one line per row, for finding a word on it.
    private string ScreenText()
    {
        var text = new StringBuilder();
        var cells = _ui.Driver!.Contents!;
        for (var row = 0; row < cells.GetLength(0); row++)
        {
            for (var col = 0; col < cells.GetLength(1); col++)
            {
                var grapheme = cells[row, col].Grapheme;
                text.Append(string.IsNullOrEmpty(grapheme) ? " " : grapheme);
            }
            text.Append('\n');
        }
        return text.ToString();
    }

    private static bool IsAscii(string? text) => string.IsNullOrEmpty(text) || text.All(ch => ch < 128);
}
