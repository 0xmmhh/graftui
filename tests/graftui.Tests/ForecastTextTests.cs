using graftui.Git;
using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

public class ForecastTextTests
{
    private static MergeForecast Of(ForecastKind kind, string? note = null, params ForecastFile[] files) =>
        new(kind, files, note, []);

    private static string Text(MergeForecast f, GraphLayout L, bool clean = true)
    {
        var caption = ForecastText.Caption(f, L, clean);
        Assert.NotNull(caption);
        return caption.Text;
    }

    [Fact]
    public void A_clean_merge_says_it_makes_a_merge_commit()
    {
        Assert.Equal("✔ m merges cleanly: a merge commit", Text(Of(ForecastKind.Clean), Layouts.Diverged()));
    }

    [Fact]
    public void A_clean_merge_made_by_merge_ff_false_says_so()
    {
        var f = Of(ForecastKind.Clean, MergeForecast.FfOffNote);

        Assert.Equal("✔ m makes a merge commit (merge.ff=false)", Text(f, Layouts.FastForward()));
    }

    [Fact]
    public void A_fast_forward_counts_its_commits()
    {
        Assert.Equal("✔ m fast-forwards: 1 commit", Text(Of(ForecastKind.FastForward), Layouts.FastForward()));
        Assert.Equal("✔ m fast-forwards: 3 commits", Text(Of(ForecastKind.FastForward), Layouts.FastForward() with { Ahead = 3 }));
    }

    [Fact]
    public void An_up_to_date_branch_says_there_is_nothing_to_merge()
    {
        Assert.Equal("✔ already up to date: nothing to merge", Text(Of(ForecastKind.UpToDate), Layouts.UpToDate()));
    }

    [Fact]
    public void A_conflict_names_one_file()
    {
        var f = Of(ForecastKind.Conflict, null, new ForecastFile("app.txt", ForecastConflict.Content));

        Assert.Equal("✕ m would conflict in app.txt: the merge would be aborted", Text(f, Layouts.Diverged()));
    }

    [Fact]
    public void A_conflict_names_the_first_file_and_counts_the_rest()
    {
        var f = Of(ForecastKind.Conflict, null,
            new ForecastFile("app.txt", ForecastConflict.Content),
            new ForecastFile("b.txt", ForecastConflict.Binary));

        Assert.Equal("✕ m would conflict in app.txt +1 more: the merge would be aborted", Text(f, Layouts.Diverged()));
    }

    [Fact]
    public void A_conflict_with_no_file_entries_still_says_it_would_conflict()
    {
        Assert.Equal("✕ m would conflict: the merge would be aborted", Text(Of(ForecastKind.Conflict), Layouts.Diverged()));
    }

    [Fact]
    public void A_refused_fast_forward_says_the_branches_diverged()
    {
        var f = Of(ForecastKind.Refused, MergeForecast.FfOnlyNote);

        Assert.Equal("✕ m can't fast-forward: the branches diverged (merge.ff=only)", Text(f, Layouts.Diverged()));
    }

    [Fact]
    public void An_unavailable_forecast_has_no_caption()
    {
        Assert.Null(ForecastText.Caption(MergeForecast.Unavailable([]), Layouts.Diverged(), clean: true));
        Assert.Null(ForecastText.Caption(MergeForecast.Unavailable([]), Layouts.Diverged(), clean: false));
    }

    [Fact]
    public void Each_conflicting_file_gets_a_footer_row_with_its_kind()
    {
        var idle = CommitLists.Idle(Layouts.Diverged());
        var f = Of(ForecastKind.Conflict, null,
            new ForecastFile("app.txt", ForecastConflict.Content),
            new ForecastFile("del.txt", ForecastConflict.DeletedCurrent));

        var panel = ForecastText.WithFiles(idle, f, Layouts.Diverged());

        Assert.Equal(2, panel.Footer.Count);
        Assert.Equal("app.txt", panel.Footer[0].Hash);
        Assert.Equal("would conflict: content", panel.Footer[0].Subject);
        Assert.Equal("del.txt", panel.Footer[1].Hash);
        Assert.Equal("would conflict: deleted on main, changed on feature", panel.Footer[1].Subject);
    }

    [Fact]
    public void A_deleted_on_the_selected_side_names_the_selected_branch_first()
    {
        var text = ForecastText.KindText(ForecastConflict.DeletedSelected, Layouts.Diverged());

        Assert.Equal("deleted on feature, changed on main", text);
    }

    [Fact]
    public void The_other_kinds_have_their_words()
    {
        var L = Layouts.Diverged();

        Assert.Equal("content", ForecastText.KindText(ForecastConflict.Content, L));
        Assert.Equal("added on both", ForecastText.KindText(ForecastConflict.BothAdded, L));
        Assert.Equal("binary", ForecastText.KindText(ForecastConflict.Binary, L));
        Assert.Equal("renamed", ForecastText.KindText(ForecastConflict.Rename, L));
        Assert.Equal("conflict", ForecastText.KindText(ForecastConflict.Other, L));
    }

    [Fact]
    public void A_clean_forecast_leaves_the_panel_as_it_was()
    {
        var idle = CommitLists.Idle(Layouts.Diverged());

        var panel = ForecastText.WithFiles(idle, Of(ForecastKind.Clean), Layouts.Diverged());

        Assert.Same(idle, panel);
    }

    [Fact]
    public void On_a_dirty_tree_a_clean_merge_says_the_merge_is_clean_and_m_asks_to_stash_first()
    {
        Assert.Equal("✔ the merge itself is clean; m asks to stash your changes first",
            Text(Of(ForecastKind.Clean), Layouts.Diverged(), clean: false));
    }

    [Fact]
    public void On_a_dirty_tree_a_merge_commit_made_by_merge_ff_false_says_the_same()
    {
        var f = Of(ForecastKind.Clean, MergeForecast.FfOffNote);

        Assert.Equal("✔ the merge itself is clean; m asks to stash your changes first", Text(f, Layouts.FastForward(), clean: false));
    }

    [Fact]
    public void On_a_dirty_tree_a_fast_forward_says_the_fast_forward_is_clean_and_m_asks_to_stash_first()
    {
        Assert.Equal("✔ the fast-forward itself is clean; m asks to stash your changes first",
            Text(Of(ForecastKind.FastForward), Layouts.FastForward(), clean: false));
    }

    [Fact]
    public void On_a_dirty_tree_an_up_to_date_branch_still_says_m_asks_to_stash_first()
    {
        Assert.Equal("✔ already up to date: nothing to merge; m asks to stash your changes first",
            Text(Of(ForecastKind.UpToDate), Layouts.UpToDate(), clean: false));
    }

    [Fact]
    public void On_a_dirty_tree_a_conflict_names_the_files_and_m_asks_to_stash_first()
    {
        var f = Of(ForecastKind.Conflict, null,
            new ForecastFile("app.txt", ForecastConflict.Content),
            new ForecastFile("b.txt", ForecastConflict.Binary));

        Assert.Equal("✕ would conflict in app.txt +1 more; m asks to stash your changes first", Text(f, Layouts.Diverged(), clean: false));
    }

    [Fact]
    public void On_a_dirty_tree_a_conflict_with_no_file_entries_says_so_and_m_asks_to_stash_first()
    {
        Assert.Equal("✕ would conflict; m asks to stash your changes first",
            Text(Of(ForecastKind.Conflict), Layouts.Diverged(), clean: false));
    }

    [Fact]
    public void On_a_dirty_tree_a_refused_fast_forward_still_says_m_asks_to_stash_first()
    {
        var f = Of(ForecastKind.Refused, MergeForecast.FfOnlyNote);

        Assert.Equal("✕ m can't fast-forward: the branches diverged (merge.ff=only); m asks to stash your changes first",
            Text(f, Layouts.Diverged(), clean: false));
    }

    [Fact]
    public void A_clean_tree_keeps_the_plain_words_for_every_kind()
    {
        Assert.Equal("✔ m fast-forwards: 1 commit", Text(Of(ForecastKind.FastForward), Layouts.FastForward(), clean: true));
        Assert.Equal("✔ already up to date: nothing to merge", Text(Of(ForecastKind.UpToDate), Layouts.UpToDate(), clean: true));
    }
}
