using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The words of g's replay: the title, the step at each boundary of the three kinds, the spinner, the ink of row 0 and the names in a
/// conflict. The times are the animation's: a merge 700 / 1500 / 1900 ms, a fast-forward 600 / 1500 / 1900, a stop 500 / 1300 / 2000.
/// </summary>
public sealed class ReplayTextTests
{
    private const string Hash = "1234567890abcdef";

    private static MergeCard Merged(int ahead = 1, string branch = "feature") =>
        new(DigestKind.Merged, branch, "main", Hash, "be0126e", ahead, [], "merge commit created", null, true, CanPlay: true, CanUndo: true);

    private static MergeCard FastForwarded() =>
        new(DigestKind.FastForwarded, "feature", "main", "8f0068a1234567890abcdef", "be0126e", 1, [], "fast-forward", null, true,
            CanPlay: true, CanUndo: true);

    private static MergeCard Stopped(string? rollback, IReadOnlyList<string> files) =>
        new(DigestKind.Stopped, "feature", "main", null, "be0126e", 1, files, "merge aborted", rollback, true, CanPlay: true, CanUndo: false);

    private static string Text(IReadOnlyList<Seg> segs) => string.Concat(segs.Select(seg => seg.Text));

    [Fact]
    public void The_title_names_the_branch_and_the_branch_it_goes_into()
    {
        Assert.Equal("Merging feature → main", ReplayText.Title(Merged()));
    }

    // Reading, joining, creating the merge commit, then checking main: each change at its boundary.
    [Fact]
    public void A_merge_steps_at_its_boundaries()
    {
        var card = Merged();
        Assert.Equal("reading 1 commit from feature", ReplayText.Step(card, 0));
        Assert.Equal("reading 1 commit from feature", ReplayText.Step(card, 699));
        Assert.Equal("joining feature into main", ReplayText.Step(card, 700));
        Assert.Equal("joining feature into main", ReplayText.Step(card, 1499));
        Assert.Equal("creating the merge commit", ReplayText.Step(card, 1500));
        Assert.Equal("creating the merge commit", ReplayText.Step(card, 1899));
        Assert.Equal("checking main", ReplayText.Step(card, 1900));
    }

    [Fact]
    public void A_fast_forward_steps_at_its_boundaries()
    {
        var card = FastForwarded();
        Assert.Equal("reading 1 commit from feature", ReplayText.Step(card, 599));
        Assert.Equal("moving main forward", ReplayText.Step(card, 600));
        Assert.Equal("moving main forward", ReplayText.Step(card, 1499));
        Assert.Equal("main now points at 8f0068a", ReplayText.Step(card, 1500));
        Assert.Equal("main now points at 8f0068a", ReplayText.Step(card, 1899));
        Assert.Equal("checking main", ReplayText.Step(card, 1900));
    }

    [Fact]
    public void A_stop_steps_at_its_boundaries()
    {
        var card = Stopped("git merge --abort", ["app.txt"]);
        Assert.Equal("reading 1 commit from feature", ReplayText.Step(card, 499));
        Assert.Equal("joining feature into main", ReplayText.Step(card, 500));
        Assert.Equal("joining feature into main", ReplayText.Step(card, 1299));
        Assert.Equal("conflict in app.txt", ReplayText.Step(card, 1300));
        Assert.Equal("conflict in app.txt", ReplayText.Step(card, 1999));
        Assert.Equal("git merge --abort: putting main back", ReplayText.Step(card, 2000));
    }

    // Without a rollback command the last step only checks main.
    [Fact]
    public void A_stop_without_a_rollback_checks_main_at_the_end()
    {
        Assert.Equal("checking main", ReplayText.Step(Stopped(null, ["app.txt"]), 2000));
    }

    // The conflict step names the files; with none it says only "conflict".
    [Fact]
    public void The_conflict_step_names_the_files()
    {
        Assert.Equal("conflict in a.txt and b.txt", ReplayText.Step(Stopped("git merge --abort", ["a.txt", "b.txt"]), 1300));
        Assert.Equal("conflict in a.txt and 2 more files", ReplayText.Step(Stopped("git merge --abort", ["a.txt", "b.txt", "c.txt"]), 1300));
        Assert.Equal("conflict", ReplayText.Step(Stopped("git merge --abort", []), 1300));
    }

    // "reading 3 commits from feature/picks", or "reading feature/x" when the branch brings no commit.
    [Fact]
    public void Reading_names_the_commits_the_branch_brings()
    {
        Assert.Equal("reading 3 commits from feature/picks", ReplayText.Step(Merged(3, "feature/picks"), 0));
        Assert.Equal("reading feature/x", ReplayText.Step(Merged(0, "feature/x"), 0));
    }

    [Fact]
    public void The_file_names_are_listed_one_two_or_many()
    {
        Assert.Equal("", ReplayText.FilesPhrase([]));
        Assert.Equal("app.txt", ReplayText.FilesPhrase(["app.txt"]));
        Assert.Equal("a.txt and b.txt", ReplayText.FilesPhrase(["a.txt", "b.txt"]));
        Assert.Equal("a.txt and 2 more files", ReplayText.FilesPhrase(["a.txt", "b.txt", "c.txt"]));
    }

    // The spinner turns every 80 ms and starts again after ten glyphs, then row 0 is the spinner, a space, and the step.
    [Fact]
    public void The_spinner_turns_every_80_milliseconds()
    {
        var card = Merged();
        Assert.Equal("⠋ reading 1 commit from feature", Text(ReplayText.Line(card, 0, Glyphs.Unicode)));
        Assert.StartsWith("⠙ ", Text(ReplayText.Line(card, 80, Glyphs.Unicode)));
        Assert.StartsWith("⠋ ", Text(ReplayText.Line(card, 800, Glyphs.Unicode)));
    }

    [Fact]
    public void Basic_glyphs_use_the_ascii_spinner()
    {
        Assert.StartsWith("| ", Text(ReplayText.Line(Merged(), 0, Glyphs.Basic)));
    }

    // Row 0 is one segment: green for a fast-forward, red for a stop, the merge ink otherwise.
    [Fact]
    public void Row_0_is_in_the_ink_of_its_kind()
    {
        Assert.Equal(Ink.Mrg, Assert.Single(ReplayText.Line(Merged(), 900, Glyphs.Unicode)).Style.Ink);
        Assert.Equal(Ink.Ok, Assert.Single(ReplayText.Line(FastForwarded(), 900, Glyphs.Unicode)).Style.Ink);
        var stopped = Stopped("git merge --abort", ["app.txt"]);
        Assert.Equal(Ink.Bad, Assert.Single(ReplayText.Line(stopped, 900, Glyphs.Unicode)).Style.Ink);
    }
}
