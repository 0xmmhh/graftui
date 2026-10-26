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
    public void The_hint_says_what_space_does_to_the_selected_row()
    {
        Assert.Equal("Space stages this file · c commits what is staged",
            ChangesText.BrowseHint(new FileChange("a", null, ChangeSide.Unstaged, ChangeKind.Modified)));
        Assert.Equal("Space unstages this file · c commits what is staged",
            ChangesText.BrowseHint(new FileChange("a", null, ChangeSide.Staged, ChangeKind.Modified)));
        Assert.Equal("resolve the conflict outside graftui first",
            ChangesText.BrowseHint(new FileChange("a", null, ChangeSide.Unstaged, ChangeKind.Conflicted)));
        Assert.Equal("", ChangesText.BrowseHint(null));
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
    public void The_summary_has_three_lines_with_the_counts_and_the_head()
    {
        var status = new WorkingStatus([new FileChange("a", null, ChangeSide.Staged, ChangeKind.Modified)], []);

        var lines = ChangesText.Summary(status, Head);

        Assert.Equal(3, lines.Count);
        Assert.Equal("staged    1 file", string.Concat(lines[0].Select(s => s.Text)));
        Assert.Equal("unstaged  0 files", string.Concat(lines[1].Select(s => s.Text)));
        Assert.Equal("HEAD      main abc1234 Add app", string.Concat(lines[2].Select(s => s.Text)));
    }
}
