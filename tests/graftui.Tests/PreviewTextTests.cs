using graftui.Model;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>
/// The preview sheet's words: the title, the forecast line for each case, the command line and the ghost kind. The layouts are
/// Layouts.Diverged (feature, one commit ahead of main, one behind) with a change each, and the forecasts are made by hand.
/// </summary>
public sealed class PreviewTextTests
{
    // feature is selected; main is the current branch.
    private static GraphLayout Diverged() => Layouts.Diverged();

    private static MergeForecast F(ForecastKind kind, string? note = null) => new(kind, [], note, []);

    private static MergeForecast Conflicted(params string[] paths)
    {
        var files = new List<ForecastFile>();
        foreach (var path in paths)
            files.Add(new ForecastFile(path, ForecastConflict.Content));
        return new MergeForecast(ForecastKind.Conflict, files, null, []);
    }

    // The segments' text, joined.
    private static string Text(IReadOnlyList<Seg> segs) => string.Concat(segs.Select(s => s.Text));

    [Fact]
    public void Title_of_a_branch_says_what_m_would_do()
    {
        Assert.Equal("If you press m: feature → main", PreviewText.Title(Diverged()));
    }

    [Fact]
    public void Title_of_the_current_branch_says_it_is_checked_out()
    {
        var layout = Diverged() with { Selected = "main", Kind = GraphKind.Same };

        Assert.Equal("main is checked out", PreviewText.Title(layout));
    }

    [Fact]
    public void Title_with_a_detached_head_says_so()
    {
        var layout = Diverged() with { Detached = true };

        Assert.Equal("HEAD is detached", PreviewText.Title(layout));
    }

    [Fact]
    public void Row_current_branch_is_checked_out_and_has_no_command()
    {
        var layout = Diverged() with { Selected = "main", Kind = GraphKind.Same };
        var segs = PreviewText.Forecast(layout, F(ForecastKind.Clean), true);

        Assert.Single(segs);
        Assert.Equal("main is checked out: pick a branch on the left to preview its merge.", Text(segs));
        Assert.Equal(Ink.Dim, segs[0].Style.Ink);
        Assert.Empty(PreviewText.Command(layout));
    }

    [Fact]
    public void Row_detached_head_warns_and_has_no_command()
    {
        var layout = Diverged() with { Detached = true };
        var segs = PreviewText.Forecast(layout, F(ForecastKind.Clean), true);

        Assert.Equal(2, segs.Count);
        Assert.Equal("! HEAD is detached · switch to a branch first, then m merges into it", Text(segs));
        Assert.Equal(Ink.Warn, segs[0].Style.Ink);
        Assert.Equal(Ink.Dim, segs[1].Style.Ink);
        Assert.Empty(PreviewText.Command(layout));
    }

    [Fact]
    public void Row_same_commit_says_nothing_to_merge()
    {
        var layout = Diverged() with { Kind = GraphKind.Same };
        var segs = PreviewText.Forecast(layout, F(ForecastKind.Clean), true);

        Assert.Equal("= same commit as main · nothing to merge", Text(segs));
        Assert.Equal(Ink.Dim, segs[0].Style.Ink);
        Assert.Equal(Ink.Dim, segs[1].Style.Ink);
        Assert.Equal("$ git merge feature", Text(PreviewText.Command(layout)));
    }

    [Fact]
    public void Row_unrelated_history_is_refused_by_git()
    {
        var layout = Diverged() with { Kind = GraphKind.Unrelated };
        var segs = PreviewText.Forecast(layout, F(ForecastKind.Refused), true);

        Assert.Equal("✕ no common history · git refuses to merge unrelated histories", Text(segs));
        Assert.Equal(Ink.Bad, segs[0].Style.Ink);
        Assert.Equal(Ink.Dim, segs[1].Style.Ink);
        Assert.Equal("$ git merge feature", Text(PreviewText.Command(layout)));
    }

    [Fact]
    public void Row_before_the_forecast_is_read_says_checking()
    {
        var segs = PreviewText.Forecast(Diverged(), null, true);

        Assert.Single(segs);
        Assert.Equal("checking what m would do…", Text(segs));
        Assert.Equal(Ink.Dim, segs[0].Style.Ink);
        Assert.Equal("$ git merge feature", Text(PreviewText.Command(Diverged())));
    }

    [Fact]
    public void Row_clean_merge_creates_a_merge_commit()
    {
        var segs = PreviewText.Forecast(Diverged(), F(ForecastKind.Clean), true);

        Assert.Equal("✔ merges cleanly · creates a merge commit on main", Text(segs));
        Assert.Equal(Ink.Ok, segs[0].Style.Ink);
        Assert.Equal(Ink.Dim, segs[1].Style.Ink);
    }

    [Fact]
    public void Row_clean_merge_with_ff_off_says_so()
    {
        var segs = PreviewText.Forecast(Diverged(), F(ForecastKind.Clean, MergeForecast.FfOffNote), true);

        Assert.Equal("✔ merges cleanly · creates a merge commit on main (merge.ff=false)", Text(segs));
    }

    [Fact]
    public void Row_fast_forward_counts_its_commits()
    {
        var layout = Diverged() with { Kind = GraphKind.FastForward, Ahead = 1 };
        var segs = PreviewText.Forecast(layout, F(ForecastKind.FastForward), true);

        Assert.Equal("⇢ fast-forwards · 1 commit, no merge commit", Text(segs));
        Assert.Equal(Ink.Ok, segs[0].Style.Ink);
        Assert.Equal(Ink.Dim, segs[1].Style.Ink);
    }

    [Fact]
    public void Row_up_to_date_has_nothing_to_merge()
    {
        var segs = PreviewText.Forecast(Diverged(), F(ForecastKind.UpToDate), true);

        Assert.Equal("✔ already up to date · nothing to merge", Text(segs));
        Assert.Equal(Ink.Ok, segs[0].Style.Ink);
    }

    [Fact]
    public void Row_conflict_names_the_file_and_says_main_is_unchanged()
    {
        var segs = PreviewText.Forecast(Diverged(), Conflicted("app.txt"), true);

        Assert.Equal("✕ would conflict in app.txt · the merge would be aborted, main unchanged", Text(segs));
        Assert.Equal(Ink.Bad, segs[0].Style.Ink);
        Assert.Equal(Ink.Dim, segs[1].Style.Ink);
        Assert.Equal("$ git merge feature", Text(PreviewText.Command(Diverged())));
    }

    [Fact]
    public void Row_conflict_without_a_file_name_says_would_conflict()
    {
        var segs = PreviewText.Forecast(Diverged(), F(ForecastKind.Conflict), true);

        Assert.Equal("✕ would conflict · the merge would be aborted, main unchanged", Text(segs));
    }

    [Fact]
    public void Row_refused_fast_forward_says_the_branches_diverged()
    {
        var segs = PreviewText.Forecast(Diverged(), F(ForecastKind.Refused, MergeForecast.FfOnlyNote), true);

        Assert.Equal("✕ can't fast-forward · the branches diverged (merge.ff=only)", Text(segs));
        Assert.Equal(Ink.Bad, segs[0].Style.Ink);
        Assert.Equal(Ink.Dim, segs[1].Style.Ink);
    }

    [Fact]
    public void Row_refused_for_another_reason_gives_the_note()
    {
        var segs = PreviewText.Forecast(Diverged(), F(ForecastKind.Refused, "some reason"), true);

        Assert.Equal("✕ can't merge · some reason", Text(segs));
        Assert.Equal(Ink.Bad, segs[0].Style.Ink);
    }

    [Fact]
    public void Row_unavailable_forecast_says_nothing()
    {
        var segs = PreviewText.Forecast(Diverged(), F(ForecastKind.Unavailable), true);

        Assert.Empty(segs);
    }

    [Fact]
    public void A_dirty_tree_asks_to_stash_first_on_the_four_merge_rows()
    {
        const string Stash = " · m asks to stash your changes first";

        var clean = PreviewText.Forecast(Diverged(), F(ForecastKind.Clean), false);
        var fastForward = PreviewText.Forecast(Diverged() with { Ahead = 3 }, F(ForecastKind.FastForward), false);
        var upToDate = PreviewText.Forecast(Diverged(), F(ForecastKind.UpToDate), false);
        var conflict = PreviewText.Forecast(Diverged(), Conflicted("app.txt"), false);

        Assert.Equal("✔ merges cleanly" + Stash, Text(clean));
        Assert.Equal("⇢ fast-forwards" + Stash, Text(fastForward));
        Assert.Equal("✔ already up to date" + Stash, Text(upToDate));
        Assert.Equal("✕ would conflict in app.txt" + Stash, Text(conflict));
    }

    [Fact]
    public void A_dirty_tree_keeps_the_refusal_text()
    {
        var segs = PreviewText.Forecast(Diverged(), F(ForecastKind.Refused, MergeForecast.FfOnlyNote), false);

        Assert.Equal("✕ can't fast-forward · the branches diverged (merge.ff=only)", Text(segs));
    }

    [Fact]
    public void The_lines_name_the_current_branch_and_never_main()
    {
        var onDevelop = Diverged() with { Current = "develop" };

        Assert.Equal("If you press m: feature → develop", PreviewText.Title(onDevelop));
        Assert.Equal("= same commit as develop · nothing to merge", Text(PreviewText.Forecast(onDevelop with { Kind = GraphKind.Same }, F(ForecastKind.Clean), true)));
        Assert.Equal("✔ merges cleanly · creates a merge commit on develop", Text(PreviewText.Forecast(onDevelop, F(ForecastKind.Clean), true)));
        Assert.Equal("✕ would conflict in app.txt · the merge would be aborted, develop unchanged", Text(PreviewText.Forecast(onDevelop, Conflicted("app.txt"), true)));

        var checkedOut = onDevelop with { Selected = "develop", Kind = GraphKind.Same };
        Assert.Equal("develop is checked out", PreviewText.Title(checkedOut));
        Assert.Equal("develop is checked out: pick a branch on the left to preview its merge.", Text(PreviewText.Forecast(checkedOut, null, true)));
    }

    [Fact]
    public void Command_of_a_branch_is_the_simplified_merge()
    {
        var segs = PreviewText.Command(Diverged());

        Assert.Single(segs);
        Assert.Equal("$ git merge feature", Text(segs));
        Assert.Equal(Ink.Dim, segs[0].Style.Ink);
    }

    [Fact]
    public void Command_is_empty_with_a_detached_head()
    {
        Assert.Empty(PreviewText.Command(Diverged() with { Detached = true }));
    }

    [Theory]
    [InlineData(ForecastKind.Clean, GhostKind.Merge)]
    [InlineData(ForecastKind.FastForward, GhostKind.FastForward)]
    [InlineData(ForecastKind.Conflict, GhostKind.Conflict)]
    [InlineData(ForecastKind.UpToDate, GhostKind.None)]
    [InlineData(ForecastKind.Refused, GhostKind.None)]
    [InlineData(ForecastKind.Unavailable, GhostKind.None)]
    public void Ghost_follows_the_forecast(ForecastKind kind, GhostKind expected)
    {
        Assert.Equal(expected, PreviewText.Ghost(Diverged(), F(kind)));
    }

    [Fact]
    public void Ghost_is_none_until_the_forecast_is_read()
    {
        Assert.Equal(GhostKind.None, PreviewText.Ghost(Diverged(), null));
    }

    [Fact]
    public void Ghost_is_none_for_the_current_branch_a_detached_head_the_same_commit_and_unrelated_history()
    {
        var clean = F(ForecastKind.Clean);

        Assert.Equal(GhostKind.None, PreviewText.Ghost(Diverged() with { Selected = "main", Kind = GraphKind.Same }, clean));
        Assert.Equal(GhostKind.None, PreviewText.Ghost(Diverged() with { Detached = true }, clean));
        Assert.Equal(GhostKind.None, PreviewText.Ghost(Diverged() with { Kind = GraphKind.Same }, clean));
        Assert.Equal(GhostKind.None, PreviewText.Ghost(Diverged() with { Kind = GraphKind.Unrelated }, clean));
    }

    [Fact]
    public void IsCurrent_needs_the_selected_branch_to_be_the_current_one_and_no_detached_head()
    {
        Assert.False(PreviewText.IsCurrent(Diverged()));
        Assert.True(PreviewText.IsCurrent(Diverged() with { Selected = "main" }));
        Assert.False(PreviewText.IsCurrent(Diverged() with { Selected = "main", Detached = true }));
    }
    [Theory]
    [InlineData(GraphKind.Same, "= same commit as main · nothing to merge")]
    [InlineData(GraphKind.Unrelated, "✕ no common history · git refuses to merge unrelated histories")]
    public void Layout_facts_take_precedence_over_pending_or_conflicting_forecasts(GraphKind kind, string expected)
    {
        var layout = Diverged() with { Kind = kind };

        foreach (var forecast in new MergeForecast?[] { null, F(ForecastKind.Clean), Conflicted("app.txt") })
        {
            Assert.Equal(expected, Text(PreviewText.Forecast(layout, forecast, false)));
            Assert.Equal(GhostKind.None, PreviewText.Ghost(layout, forecast));
        }
    }

    [Fact]
    public void Detached_head_takes_precedence_even_when_branch_names_match()
    {
        var layout = Diverged() with { Selected = "main", Detached = true, Kind = GraphKind.Same };

        Assert.False(PreviewText.IsCurrent(layout));
        Assert.Equal("HEAD is detached", PreviewText.Title(layout));
        Assert.Equal("! HEAD is detached · switch to a branch first, then m merges into it",
            Text(PreviewText.Forecast(layout, null, false)));
        Assert.Empty(PreviewText.Command(layout));
        Assert.Equal(GhostKind.None, PreviewText.Ghost(layout, F(ForecastKind.Conflict)));
    }

    [Theory]
    [InlineData(0, "0 commits")]
    [InlineData(2, "2 commits")]
    public void Fast_forward_uses_the_layout_count(int ahead, string count)
    {
        var layout = Diverged() with { Ahead = ahead };

        Assert.Equal("⇢ fast-forwards · " + count + ", no merge commit",
            Text(PreviewText.Forecast(layout, F(ForecastKind.FastForward), true)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Refusal_without_a_note_is_a_single_error_segment(bool clean)
    {
        var seg = Assert.Single(PreviewText.Forecast(Diverged(), F(ForecastKind.Refused), clean));

        Assert.Equal("✕ can't merge", seg.Text);
        Assert.Equal(Ink.Bad, seg.Style.Ink);
    }

    [Fact]
    public void Conflict_lists_the_first_file_and_counts_the_others()
    {
        var segs = PreviewText.Forecast(Diverged(), Conflicted("first.txt", "second.txt", "third.txt"), true);

        Assert.Equal("✕ would conflict in first.txt +2 more · the merge would be aborted, main unchanged", Text(segs));
        Assert.Equal(Ink.Bad, segs[0].Style.Ink);
    }

}
