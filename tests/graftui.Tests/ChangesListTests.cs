using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

public class ChangesListTests
{
    private static FileChange Staged(string path, ChangeKind kind = ChangeKind.Modified, string? orig = null) =>
        new(path, orig, ChangeSide.Staged, kind);

    private static FileChange Unstaged(string path, ChangeKind kind = ChangeKind.Modified) =>
        new(path, null, ChangeSide.Unstaged, kind);

    [Fact]
    public void A_clean_tree_is_one_line()
    {
        var lines = ChangesList.Build(WorkingStatus.Empty);

        var line = Assert.Single(lines);
        Assert.Equal(ChangesLineKind.Clean, line.Kind);
        Assert.Equal("✔ working tree clean", line.Text);
        Assert.Equal(new Style(Ink.Ok), line.Style);
    }

    [Fact]
    public void Headers_count_each_group_and_say_when_it_is_empty()
    {
        var status = new WorkingStatus([], [Unstaged("a.txt"), Unstaged("b.txt"), Unstaged("c.txt")]);

        var texts = ChangesList.Build(status).Select(l => l.Text).ToArray();

        Assert.Equal(["Staged (0)", "  nothing staged", "Unstaged (3)", "a.txt", "b.txt", "c.txt"], texts);
    }

    [Fact]
    public void Row_indices_follow_the_status_rows_with_staged_first()
    {
        var status = new WorkingStatus([Staged("s.txt")], [Unstaged("u1.txt"), Unstaged("u2.txt")]);

        var files = ChangesList.Build(status).Where(l => l.Kind == ChangesLineKind.File).ToList();

        Assert.Equal([0, 1, 2], files.Select(f => f.Row).ToArray());
        Assert.Equal(status.Rows.Select(r => r.Path), files.Select(f => f.File!.Path));
    }

    [Fact]
    public void A_rename_shows_the_old_and_new_names()
    {
        var status = new WorkingStatus([Staged("new name.txt", ChangeKind.Renamed, "old.txt")], []);

        var file = ChangesList.Build(status).Single(l => l.Kind == ChangesLineKind.File);

        Assert.Equal("old.txt → new name.txt", file.Text);
    }

    [Fact]
    public void A_file_line_takes_the_letter_style_of_its_status()
    {
        var status = new WorkingStatus([Staged("s.txt")], [Unstaged("d.txt", ChangeKind.Deleted)]);

        var files = ChangesList.Build(status).Where(l => l.Kind == ChangesLineKind.File).ToList();

        Assert.Equal(new Style(Ink.Ok, true), files[0].Style);
        Assert.Equal(new Style(Ink.Bad, true), files[1].Style);
    }

    [Fact]
    public void A_stash_line_ends_the_list_with_the_count_and_the_newest_message()
    {
        var status = new WorkingStatus([Staged("s.txt")], []);

        var lines = ChangesList.Build(status, new StashSummary(1, "a1", "On main: wip"));

        Assert.Equal(ChangesLineKind.Stash, lines[^1].Kind);
        Assert.Equal("Stash (1) On main: wip", lines[^1].Text);
    }

    [Fact]
    public void A_stash_line_follows_the_clean_line_and_an_empty_stash_adds_nothing()
    {
        var clean = ChangesList.Build(WorkingStatus.Empty, new StashSummary(2, "a1", "wip"));

        Assert.Equal(["✔ working tree clean", "Stash (2) wip"], clean.Select(l => l.Text).ToArray());
        Assert.Single(ChangesList.Build(WorkingStatus.Empty, StashSummary.None));
    }
}
