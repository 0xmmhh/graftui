using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.App;
using graftui.Ui.Scenes;
using graftui.Ui.Text;
using graftui.Ui.Views;
using Terminal.Gui.Input;

namespace graftui.Tests;

/// <summary>
/// The decisions of the rebase on the Branches tab, as static functions: the worker's early refusal of a fast-forward, the keys of the
/// confirm screen, the confirm art, the running screen, and the small lookups the finish and the labels use.
/// </summary>
public class RebaseDecisionTests
{
    private static RepoSnapshot Snap(string current, params string[] names) =>
        new(current, "a111111", true, names.Select(n => new Branch(n, "a111111", "a111111", n == current)).ToList(),
            new Dictionary<string, (int, int)>());

    [Fact]
    public void A_fast_forward_is_refused_before_git_runs_so_its_check_has_no_commands()
    {
        using var repo = SequenceRepos.Feature4();

        var check = BranchesTab.CheckRebase(new RebaseOperation(repo.Git), "feature", "main", Layouts.FastForward());

        Assert.Equal(SequenceRefusals.NoOwnCommits("feature", "main"), check.Refusal);
        Assert.Empty(check.Commands);
        Assert.Equal("feature", check.Branch);
        Assert.Equal("main", check.Onto);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_diverged_branch_or_an_unread_layout_goes_on_to_the_operation_check(bool diverged)
    {
        using var repo = SequenceRepos.Feature4();
        var layout = diverged ? Layouts.Diverged() : null;

        var check = BranchesTab.CheckRebase(new RebaseOperation(repo.Git), "feature", "main", layout);

        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(4, check.Rows.Count);
        Assert.NotEmpty(check.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keys_on_the_confirm_screen_other_than_enter_and_esc_do_nothing(bool hasCheck)
    {
        Assert.Equal(ConfirmKeyAction.Ignore, BranchesTab.ConfirmKeyFor(Key.Tab, hasCheck));
        Assert.Equal(ConfirmKeyAction.Ignore, BranchesTab.ConfirmKeyFor(Key.A, hasCheck));
    }

    [Fact]
    public void Enter_runs_the_rebase_only_when_there_is_a_check_and_esc_goes_back_either_way()
    {
        Assert.Equal(ConfirmKeyAction.Run, BranchesTab.ConfirmKeyFor(Key.Enter, hasCheck: true));
        Assert.Equal(ConfirmKeyAction.Ignore, BranchesTab.ConfirmKeyFor(Key.Enter, hasCheck: false));
        Assert.Equal(ConfirmKeyAction.Back, BranchesTab.ConfirmKeyFor(Key.Esc, hasCheck: true));
        Assert.Equal(ConfirmKeyAction.Back, BranchesTab.ConfirmKeyFor(Key.Esc, hasCheck: false));
    }

    [Fact]
    public void The_confirm_art_is_titled_captions_the_count_and_lists_the_rewritten_commits_above_the_new_base()
    {
        using var repo = SequenceRepos.Feature4();
        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));
        var art = new ArtPaneView();

        BranchesTab.ShowConfirmArt(art, check, Layouts.Diverged(), BranchesTab.DirectionFor("feature", "main", _ => Ink.Fg));

        Assert.Equal(RebaseText.ConfirmTitle, art.Title);
        Assert.Null(art.Below);
        Assert.Null(art.FallbackCaption);
        var rows = art.Panel!.Left.Rows;
        Assert.Equal(5, rows.Count);
        Assert.Equal(4, rows.Count(r => r.Subject.Contains(RebaseText.WillBeRewritten, StringComparison.Ordinal)));
        Assert.Contains(rows, r => r.Subject.Contains(RebaseText.NewBase, StringComparison.Ordinal));
        Assert.NotEmpty(art.Direction(80));

        var geo = ArtGeometry.For(80, 11)!;
        var frame = art.Scene!(geo);
        Assert.Equal(new Caption(RebaseText.Confirm(4), new Style(Ink.Warn)), frame.Caption);
    }

    [Fact]
    public void The_running_screen_lists_each_commit_with_its_action_and_plays_a_rebase_animation()
    {
        using var repo = SequenceRepos.Feature4();
        var reader = new RepoReader(repo.Git);
        var check = new RebaseOperation(repo.Git).Check(new RebaseRequest("main"));

        var start = BranchesTab.RebaseStart(Layouts.Diverged(), reader.LoadSnapshot(withCounts: true, CancellationToken.None), check,
            _ => Ink.Fg, interactive: false);

        Assert.IsType<RebaseAnimation>(start.Anim);
        Assert.Equal(4, start.Commits!.Left.Rows.Count);
        Assert.All(start.Commits.Left.Rows, row => Assert.EndsWith("pick", row.Subject, StringComparison.Ordinal));
        Assert.Equal(DirectionLine.Rebase("feature", Ink.Fg, "main", Ink.Fg, 60), start.Direction(60));
    }

    [Fact]
    public void The_direction_line_takes_the_ink_of_each_branch_from_the_lookup()
    {
        var direction = BranchesTab.DirectionFor("feature", "main", name => name == "main" ? Ink.Lane0 : Ink.Lane1);

        Assert.Equal(DirectionLine.Rebase("feature", Ink.Lane1, "main", Ink.Lane0, 60), direction(60));
    }

    [Fact]
    public void The_current_name_is_HEAD_when_HEAD_is_detached()
    {
        Assert.Equal("main", BranchesTab.CurrentName(Snap("main", "main", "feature")));
        Assert.Equal("HEAD", BranchesTab.CurrentName(Snap("", "feature")));
    }

    [Fact]
    public void The_app_builds_the_rebase_and_its_resume_operation_on_the_root_repo()
    {
        using var repo = SequenceRepos.Feature4();
        var snapshot = new RepoReader(repo.Git).LoadSnapshot(withCounts: true, CancellationToken.None);

        // The app's constructor only builds its parts, so no terminal is needed.
        var app = new AppController(null!, repo.Git, repo.Git, snapshot, "repo");

        Assert.NotNull(app.RebaseOp);
        Assert.NotNull(app.ResumeOp);
    }

    [Fact]
    public void A_layout_is_read_for_the_listed_branch_only_and_none_is_read_without_a_snapshot()
    {
        var snap = Snap("main", "main", "feature");
        var layout = Layouts.Diverged();

        Assert.Null(BranchesTab.LayoutAfter(null, "feature", (s, b) => layout));
        Assert.Null(BranchesTab.LayoutAfter(snap, "gone", (s, b) => layout));
        Assert.Same(layout, BranchesTab.LayoutAfter(snap, "feature", (s, b) => layout));
    }
}
