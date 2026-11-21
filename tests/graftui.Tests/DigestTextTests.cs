using graftui.Git;
using graftui.Model;
using graftui.Operations;
using graftui.Ui.Scenes;
using graftui.Ui.Text;

namespace graftui.Tests;

/// <summary>The merge card's words, one test per row of the table in the plan, plus the rollback, hint and hint placement rules.</summary>
public sealed class DigestTextTests
{
    private const string Before = "be0126eabcdef0123456789";
    private const string NewHead = "1234567890abcdef";

    private static string Text(IReadOnlyList<Seg> segs) => string.Concat(segs.Select(seg => seg.Text));

    private static MergeCard Merged(bool verified = true, string into = "main") =>
        new(DigestKind.Merged, "feature/merge-commit", into, NewHead, Before, 1, [], "merge commit created", null,
            verified, CanPlay: true, CanUndo: true);

    private static MergeCard FastForwarded(bool verified = true, int ahead = 1) =>
        new(DigestKind.FastForwarded, "feature/fast-forward", "main", NewHead, Before, ahead, [], "fast-forward", null,
            verified, CanPlay: true, CanUndo: true);

    private static MergeCard Stopped(bool verified = true, string? rollback = "git merge --abort") =>
        new(DigestKind.Stopped, "feature/conflict", "main", null, Before, 1, ["app.txt"], "merge aborted", rollback,
            verified, CanPlay: true, CanUndo: false);

    private static MergeCard UpToDate(bool verified = true) =>
        new(DigestKind.UpToDate, "feature", "main", null, Before, 0, [], "already up to date", null,
            verified, CanPlay: true, CanUndo: false);

    private static MergeCard Failed(string message, bool verified = true) =>
        new(DigestKind.Failed, "feature", "main", null, Before, 0, [], message, null, verified, CanPlay: true, CanUndo: false);

    private static GitResult Ran(string command, int exit, params string[] args) =>
        new(command, exit, "", "") { Args = args };

    [Fact]
    public void Merged_title_and_headline()
    {
        var card = Merged();

        Assert.Equal("Merged feature/merge-commit → main", DigestText.Title(card));
        Assert.Equal("✔ Merged feature/merge-commit into main", Text(DigestText.Headline(card)));
        Assert.Equal(Ink.Ok, DigestText.Headline(card)[0].Style.Ink);
    }

    [Fact]
    public void Merged_verified_detail_says_what_was_checked()
    {
        var detail = DigestText.Detail(Merged());

        Assert.Equal("new merge commit 1234567 · ✔ verified: main moved, tree clean", Text(detail));
        Assert.Equal(Ink.Hash, detail[1].Style.Ink);
        Assert.Equal(Ink.Ok, detail[3].Style.Ink);
    }

    [Fact]
    public void Merged_not_verified_detail_keeps_the_hash_and_warns()
    {
        var detail = DigestText.Detail(Merged(verified: false));

        Assert.Equal("new merge commit 1234567 · ! not verified: check git status", Text(detail));
        Assert.Equal(Ink.Warn, detail[detail.Count - 1].Style.Ink);
    }

    [Fact]
    public void Fast_forward_title_headline_and_detail()
    {
        var card = FastForwarded();

        Assert.Equal("Merged feature/fast-forward → main", DigestText.Title(card));
        Assert.Equal("✔ Fast-forwarded main to 1234567", Text(DigestText.Headline(card)));
        Assert.Equal("1 commit now on main, no merge commit · ✔ verified", Text(DigestText.Detail(card)));
    }

    [Fact]
    public void Fast_forward_with_nothing_ahead_says_where_the_branch_now_is()
    {
        var detail = DigestText.Detail(FastForwarded(ahead: 0));

        Assert.Equal("main now at 1234567, no merge commit · ✔ verified", Text(detail));
    }

    [Fact]
    public void Fast_forward_not_verified_has_the_warning()
    {
        Assert.Equal("1 commit now on main, no merge commit · ! not verified: check git status",
            Text(DigestText.Detail(FastForwarded(verified: false))));
    }

    [Fact]
    public void Conflict_title_headline_and_detail_name_the_rollback()
    {
        var card = Stopped();

        Assert.Equal("Tried feature/conflict → main", DigestText.Title(card));
        Assert.Equal("✕ Merge stopped: app.txt conflicts", Text(DigestText.Headline(card)));
        Assert.Equal(Ink.Bad, DigestText.Headline(card)[0].Style.Ink);
        Assert.Equal("graftui ran git merge --abort · main unchanged at be0126e · ✔ verified", Text(DigestText.Detail(card)));
    }

    [Fact]
    public void Conflict_not_verified_keeps_only_the_rollback_and_the_warning()
    {
        Assert.Equal("graftui ran git merge --abort · ! not verified: check git status",
            Text(DigestText.Detail(Stopped(verified: false))));
    }

    [Fact]
    public void Without_a_rollback_the_detail_starts_with_main_unchanged()
    {
        var detail = DigestText.Detail(Stopped(rollback: null));

        Assert.StartsWith("main unchanged at be0126e", Text(detail));
    }

    [Fact]
    public void Up_to_date_headline_and_detail()
    {
        var card = UpToDate();

        Assert.Equal("✔ Already up to date · nothing to merge", Text(DigestText.Headline(card)));
        Assert.Equal("main unchanged at be0126e · ✔ verified", Text(DigestText.Detail(card)));
        Assert.Equal("Tried feature → main", DigestText.Title(card));
    }

    [Fact]
    public void Up_to_date_not_verified_is_only_the_warning()
    {
        Assert.Equal("! not verified: check git status", Text(DigestText.Detail(UpToDate(verified: false))));
    }

    [Fact]
    public void Failed_headline_is_the_first_line_of_git_s_message()
    {
        var card = Failed("could not resolve branch x\nsecond line");

        Assert.Equal("✕ Merge failed: could not resolve branch x", Text(DigestText.Headline(card)));
        Assert.Equal("main unchanged at be0126e · ✔ verified", Text(DigestText.Detail(card)));
    }

    [Fact]
    public void Failed_not_verified_is_only_the_warning()
    {
        Assert.Equal("! not verified: check git status", Text(DigestText.Detail(Failed("boom", verified: false))));
    }

    // g plays a merge, a fast-forward and a stop; an up-to-date merge and a failure have nothing to play, so no hint and no g.
    [Fact]
    public void FromOutcome_lets_g_play_only_the_kinds_that_animate()
    {
        Assert.True(DigestText.FromOutcome(new MergeOutcome(MergeKind.MergeCommit, "feature", "main", NewHead, [], "", []), true, Before, 1).CanPlay);
        Assert.True(DigestText.FromOutcome(new MergeOutcome(MergeKind.FastForward, "feature", "main", NewHead, [], "", []), true, Before, 1).CanPlay);
        Assert.True(DigestText.FromOutcome(new MergeOutcome(MergeKind.Conflict, "feature", "main", null, ["app.txt"], "", []), true, Before, 1).CanPlay);

        var upToDate = DigestText.FromOutcome(new MergeOutcome(MergeKind.UpToDate, "feature", "main", null, [], "", []), true, Before, 0);
        Assert.False(upToDate.CanPlay);
        Assert.Equal("", DigestText.Hint(upToDate));

        var failed = DigestText.FromOutcome(new MergeOutcome(MergeKind.Failed, "feature", "main", null, [], "boom", []), true, Before, 0);
        Assert.False(failed.CanPlay);
        Assert.Equal("", DigestText.Hint(failed));
    }

    [Fact]
    public void A_refusal_says_nothing_was_changed()
    {
        var card = DigestText.Refused("feature", "", "HEAD is detached");

        Assert.Equal("Tried feature → HEAD", DigestText.Title(card));
        Assert.Equal("! Refused: HEAD is detached", Text(DigestText.Headline(card)));
        Assert.Equal("nothing was changed · switch to a branch first", Text(DigestText.Detail(card)));
        Assert.Equal("", DigestText.Hint(card));
    }

    [Fact]
    public void A_refusal_on_a_branch_says_nothing_was_changed_only()
    {
        var card = DigestText.Refused("feature", "main", "a merge is already in progress");

        Assert.Equal("nothing was changed", Text(DigestText.Detail(card)));
    }

    [Fact]
    public void A_detached_head_is_named_HEAD_in_the_title()
    {
        var card = Merged(into: "");

        Assert.Equal("Merged feature/merge-commit → HEAD", DigestText.Title(card));
    }

    [Theory]
    [InlineData(new[] { "app.txt" }, "app.txt conflicts")]
    [InlineData(new[] { "a.txt", "b.txt" }, "a.txt and b.txt conflict")]
    [InlineData(new[] { "a.txt", "b.txt", "c.txt" }, "a.txt and 2 more files conflict")]
    public void Conflict_phrase_names_the_files(string[] files, string expected)
    {
        Assert.Equal(expected, DigestText.ConflictPhrase(files));
    }

    [Fact]
    public void Rollback_is_merge_abort_when_that_ran_and_succeeded()
    {
        var commands = new List<GitResult> { Ran("git merge --abort", 0, "merge", "--abort") };

        Assert.Equal("git merge --abort", DigestText.RollbackCommand(commands));
    }

    [Fact]
    public void Rollback_is_reset_merge_when_the_abort_failed_and_the_reset_ran()
    {
        var commands = new List<GitResult>
        {
            Ran("git merge --abort", 1, "merge", "--abort"),
            Ran("git reset --merge", 0, "reset", "--merge"),
        };

        Assert.Equal("git reset --merge", DigestText.RollbackCommand(commands));
    }

    [Fact]
    public void No_rollback_is_named_when_none_ran()
    {
        var commands = new List<GitResult> { Ran("git merge", 1, "merge", "--no-edit") };

        Assert.Null(DigestText.RollbackCommand(commands));
    }

    [Theory]
    [InlineData(true, "g play · u undo")]
    [InlineData(false, "g play")]
    public void The_hint_offers_undo_only_for_a_merge_that_has_an_undo_step(bool canUndo, string expected)
    {
        var card = new MergeCard(DigestKind.Merged, "feature", "main", NewHead, Before, 1, [], "", null,
            true, CanPlay: true, CanUndo: canUndo);

        Assert.Equal(expected, DigestText.Hint(card));
    }

    [Fact]
    public void The_hint_is_empty_when_g_cannot_play()
    {
        Assert.Equal("", DigestText.Hint(DigestText.Refused("feature", "main", "x")));
    }

    [Fact]
    public void A_hint_is_placed_only_when_the_line_and_the_hint_leave_three_columns()
    {
        IReadOnlyList<Seg> line = [new Seg("abc", new Style(Ink.Fg))];

        var noRoom = DigestText.WithHint(line, "g play", 3 + 6 + 3);
        Assert.Single(noRoom);

        var placed = DigestText.WithHint(line, "g play", 3 + 6 + 4);
        Assert.Equal(3 + 4 + 6, TextFit.Columns(Text(placed)));
        Assert.EndsWith("g play", Text(placed));
        Assert.Equal(3, placed.Count);
    }

    [Fact]
    public void Running_names_the_branch_and_the_target()
    {
        Assert.Equal("merging feature into main…", Text(DigestText.Running("feature", "main")));
        Assert.Equal("merging feature into HEAD…", Text(DigestText.Running("feature", "")));
    }

    [Theory]
    [InlineData(DigestKind.Merged, GhostKind.Merged)]
    [InlineData(DigestKind.FastForwarded, GhostKind.FastForwarded)]
    [InlineData(DigestKind.Stopped, GhostKind.Stopped)]
    [InlineData(DigestKind.UpToDate, GhostKind.None)]
    [InlineData(DigestKind.Failed, GhostKind.None)]
    public void The_ghost_is_the_done_state_of_the_outcome(DigestKind kind, GhostKind expected)
    {
        var card = new MergeCard(kind, "feature", "main", null, Before, 0, [], "", null, true, true, false);

        Assert.Equal(expected, DigestText.Ghost(card));
    }

    [Fact]
    public void The_list_after_a_merge_says_the_commits_came_in()
    {
        Assert.Equal(DigestText.CameInTitle, DigestText.ListTitle(Merged()));
        Assert.Equal(PreviewText.IncomingTitle, DigestText.ListTitle(Stopped()));
    }
}
