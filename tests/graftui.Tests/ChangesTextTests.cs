using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

public class ChangesTextTests
{
    private static readonly HeadLine Head = new("main", "abc1234def", [Layouts.C("abc1234def", 1, "Add app")], false);

    [Fact]
    public void Status_words_name_each_kind()
    {
        Assert.Equal("modified", ChangesText.StatusWord(new FileChange("a", null, ChangeSide.Unstaged, ChangeKind.Modified)));
        Assert.Equal("new, not tracked yet", ChangesText.StatusWord(new FileChange("a", null, ChangeSide.Unstaged, ChangeKind.Untracked)));
        Assert.Equal("renamed from old.txt", ChangesText.StatusWord(new FileChange("new.txt", "old.txt", ChangeSide.Staged, ChangeKind.Renamed)));
        Assert.Equal("conflicted", ChangesText.StatusWord(new FileChange("a", null, ChangeSide.Unstaged, ChangeKind.Conflicted)));
    }

    [Fact]
    public void Stage_direction_names_the_file_and_the_target()
    {
        var segs = ChangesText.OpDirection("stage", "README.md", [new Seg("staging area", new Style(Ink.Dim))], 200);

        Assert.Equal("stage        README.md  ──▶  staging area", string.Concat(segs.Select(s => s.Text)));
    }

    [Fact]
    public void What_is_the_path_for_one_file_and_a_count_for_all()
    {
        var files = new[] { new FileChange("a.txt", null, ChangeSide.Unstaged, ChangeKind.Modified), new FileChange("b.txt", null, ChangeSide.Unstaged, ChangeKind.Modified) };

        Assert.Equal("a.txt", ChangesText.What(files[..1], all: false));
        Assert.Equal("2 files", ChangesText.What(files, all: true));
    }

    [Fact]
    public void The_head_line_is_one_line_with_the_stash_count_before_the_subject()
    {
        var line = ChangesText.HeadStashLine(Head, new StashSummary(0, null, null));

        Assert.Equal("HEAD  main abc1234  ·  stash 0  ·  Add app", string.Concat(line.Select(s => s.Text)));
        Assert.Equal(Ink.Dim, line.First(s => s.Text == "stash 0").Style.Ink);
    }

    [Fact]
    public void The_stash_count_is_coloured_when_there_are_entries_and_is_left_out_without_a_stash()
    {
        var line = ChangesText.HeadStashLine(Head, new StashSummary(3, "a1", "x"));
        Assert.Equal("HEAD  main abc1234  ·  stash 3  ·  Add app", string.Concat(line.Select(s => s.Text)));
        Assert.Equal(Ink.Inc, line.First(s => s.Text == "stash 3").Style.Ink);

        Assert.Equal("HEAD  main abc1234  ·  Add app", string.Concat(ChangesText.HeadStashLine(Head, null).Select(s => s.Text)));
    }

    [Fact]
    public void The_head_line_says_detached_and_no_commits_when_there_is_nothing_to_name()
    {
        var detached = new HeadLine("", "abc1234def", [Layouts.C("abc1234def", 1, "Add app")], false);
        var unborn = new HeadLine("main", null, [], false);

        Assert.Equal("HEAD  HEAD (detached) abc1234  ·  stash 0  ·  Add app",
            string.Concat(ChangesText.HeadStashLine(detached, new StashSummary(0, null, null)).Select(s => s.Text)));
        Assert.Equal("HEAD  main no commits  ·  stash 0", string.Concat(ChangesText.HeadStashLine(unborn, new StashSummary(0, null, null)).Select(s => s.Text)));
    }

    [Fact]
    public void The_plain_direction_has_the_verb_and_the_subject_and_no_arrow()
    {
        var segs = ChangesText.PlainDirection("stash pop", "wip test", 200);

        Assert.Equal("stash pop    wip test", string.Concat(segs.Select(s => s.Text)));
    }
}
