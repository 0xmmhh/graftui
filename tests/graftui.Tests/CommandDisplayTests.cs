using graftui.Git;
using graftui.Ui.Text;

namespace graftui.Tests;

public class CommandDisplayTests
{
    private const string H40 = "0123456789abcdef0123456789abcdef01234567";
    private const string H64 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string H7 = "0123456";

    /// <summary>A result the way GitClient builds it: Command is "git" plus the args, Args holds the args.</summary>
    private static GitResult R(params string[] args) => Make(0, args);

    private static GitResult RExit(int exit, params string[] args) => Make(exit, args);

    private static GitResult Make(int exit, string[] args) =>
        new("git " + string.Join(' ', args), exit, "", "") { Args = args };

    /// <summary>A result from one space-separated line, for the table rows whose arguments have no spaces.</summary>
    private static GitResult L(string line, int exit = 0) => Make(exit, line.Split(' '));

    /// <summary>A result run against a temporary index (GIT_INDEX_FILE), as RunWithIndex logs it.</summary>
    private static GitResult Indexed(string line, int exit = 0)
    {
        var args = line.Split(' ');
        return new GitResult("GIT_INDEX_FILE=.git/graftui-tmp-index git " + line, exit, "", "") { Args = args };
    }

    private static DisplayLine? Simplify(GitResult r, CommandHints? h = null) => CommandDisplay.Simplify(r, h ?? CommandHints.None);

    private static string Shown(GitResult r, CommandHints? h = null)
    {
        var line = Simplify(r, h);
        Assert.NotNull(line);
        Assert.Equal(DisplayKind.Command, line.Kind);
        return line.Text;
    }

    private static void AssertHidden(GitResult r)
    {
        Assert.Null(Simplify(r));
    }

    // ----- Table rows: how each kind of command is shown -----

    [Fact]
    public void Merge_drops_no_edit_and_keeps_the_branch()
    {
        var line = Simplify(L("merge --no-edit feature/fast-forward"));
        Assert.Equal(new DisplayLine(DisplayKind.Command, "git merge feature/fast-forward", Exact: false), line);
    }

    [Theory]
    [InlineData("merge --abort")]
    [InlineData("reset --merge")]
    public void Merge_rollback_is_unchanged(string line)
    {
        var expected = new DisplayLine(DisplayKind.Command, "git " + line, Exact: true);
        Assert.Equal(expected, Simplify(L(line)));
    }

    [Theory]
    [InlineData("symbolic-ref HEAD")]
    [InlineData("rev-parse --verify HEAD")]
    [InlineData("status --porcelain=v1 -z")]
    [InlineData("rev-list --parents -n 1 HEAD")]
    [InlineData("diff --name-only --diff-filter=U")]
    [InlineData("merge-tree --write-tree -z 0123456789abcdef0123456789abcdef01234567 89abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("version")]
    public void Merge_reads_are_hidden(string line)
    {
        AssertHidden(L(line));
    }

    [Fact]
    public void Stage_drops_the_separator_before_a_plain_path()
    {
        Assert.Equal("git add README.md", Shown(L("add -- README.md")));
    }

    [Fact]
    public void Stage_all_is_unchanged()
    {
        Assert.Equal(new DisplayLine(DisplayKind.Command, "git add -A", Exact: true), Simplify(L("add -A")));
    }

    [Theory]
    [InlineData("restore --staged -- notes.md", "git restore --staged notes.md")]
    [InlineData("restore --staged -- .", "git restore --staged .")]
    [InlineData("restore --staged -- new.txt old.txt", "git restore --staged new.txt old.txt")]
    [InlineData("rm --cached -q -f -- notes.md", "git rm --cached -f notes.md")]
    [InlineData("rm --cached -r -q -f -- .", "git rm --cached -r -f .")]
    public void Unstage_forms(string line, string expected)
    {
        Assert.Equal(expected, Shown(L(line)));
    }

    [Fact]
    public void Commit_with_a_subject_shows_the_message_not_the_file()
    {
        var r = L("commit --cleanup=whitespace -F .git/GRAFTUI_MSG");
        var hints = new CommandHints("Fix login redirect", null);
        Assert.Equal("git commit -m \"Fix login redirect\"", Shown(r, hints));
    }

    [Fact]
    public void Commit_with_a_one_line_body_adds_a_second_message()
    {
        var r = L("commit --cleanup=whitespace -F .git/GRAFTUI_MSG");
        var hints = new CommandHints("Fix login redirect", "Users landed on /");
        Assert.Equal("git commit -m \"Fix login redirect\" -m \"Users landed on /\"", Shown(r, hints));
    }

    [Fact]
    public void Commit_with_a_multi_line_body_shows_the_first_line_and_an_ellipsis()
    {
        var r = L("commit --cleanup=whitespace -F .git/GRAFTUI_MSG");
        var hints = new CommandHints("Fix login redirect", "Users landed on /\nsecond paragraph\nthird");
        Assert.Equal("git commit -m \"Fix login redirect\" -m \"Users landed on /…\"", Shown(r, hints));
    }

    [Fact]
    public void Commit_body_starts_at_the_first_non_blank_line()
    {
        var r = L("commit --cleanup=whitespace -F .git/GRAFTUI_MSG");
        var hints = new CommandHints("Subject", "\n\n  First line  \n\nmore");
        Assert.Equal("git commit -m \"Subject\" -m \"First line…\"", Shown(r, hints));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Commit_with_an_empty_body_has_no_second_message(string body)
    {
        var r = L("commit --cleanup=whitespace -F .git/GRAFTUI_MSG");
        Assert.Equal("git commit -m \"Subject\"", Shown(r, new CommandHints("Subject", body)));
    }

    [Fact]
    public void Amend_keeps_its_flag()
    {
        var r = L("commit --amend --cleanup=whitespace -F .git/GRAFTUI_MSG");
        Assert.Equal("git commit --amend -m \"Subject\" -m \"Body\"", Shown(r, new CommandHints("Subject", "Body")));
    }

    [Fact]
    public void Commit_without_hints_shows_the_bare_command()
    {
        Assert.Equal("git commit", Shown(L("commit --cleanup=whitespace -F .git/GRAFTUI_MSG")));
        Assert.Equal("git commit --amend", Shown(L("commit --amend --cleanup=whitespace -F .git/GRAFTUI_MSG")));
    }

    [Fact]
    public void Commit_subject_is_always_quoted_and_escaped()
    {
        var r = L("commit --cleanup=whitespace -F .git/GRAFTUI_MSG");
        Assert.Equal("git commit -m \"He said \\\"hi\\\" \\$x\"", Shown(r, new CommandHints("He said \"hi\" $x")));
    }

    [Theory]
    [InlineData("rev-parse --verify HEAD")]
    [InlineData("symbolic-ref -q HEAD")]
    [InlineData("rev-list --count HEAD")]
    [InlineData("log --format=%B -1 HEAD")]
    [InlineData("branch -r --contains HEAD")]
    public void Commit_reads_are_hidden(string line)
    {
        AssertHidden(L(line));
    }

    [Fact]
    public void Discard_backup_runs_on_a_temporary_index_and_is_hidden()
    {
        AssertHidden(Indexed("add -f -- notes.md"));
        AssertHidden(Indexed("write-tree"));
        AssertHidden(L("write-tree"));
        AssertHidden(L("log -1 --format=%H refs/graftui/undo/000001"));
        AssertHidden(L($"commit-tree {H40} -p HEAD -F .git/GRAFTUI_UNDO_MSG"));
    }

    [Fact]
    public void Discard_store_is_a_tidied_stash_store_with_a_short_hash()
    {
        var r = R("stash", "store", "-m", "graftui: discarded notes.md", H40);
        Assert.Equal($"git stash store -m \"graftui: discarded notes.md\" {H7}", Shown(r));
    }

    [Fact]
    public void Discard_put_drops_the_worktree_flag_and_separator()
    {
        Assert.Equal("git restore notes.md", Shown(L("restore -q --worktree -- notes.md")));
        Assert.Equal("git clean -f notes.md", Shown(L("clean -f -q -- notes.md")));
    }

    [Fact]
    public void Discard_rollback_drops_quiet()
    {
        Assert.Equal("git stash drop stash@{0}", Shown(L("stash drop -q stash@{0}")));
    }

    [Fact]
    public void Stash_push_quotes_its_message()
    {
        Assert.Equal("git stash push -u -m \"wip one\"", Shown(R("stash", "push", "-u", "-m", "wip one")));
    }

    [Fact]
    public void Stash_push_without_a_message_is_unchanged()
    {
        Assert.Equal(new DisplayLine(DisplayKind.Command, "git stash push -u", Exact: true), Simplify(L("stash push -u")));
    }

    [Fact]
    public void Pop_is_unchanged_and_its_rescue_ref_is_hidden()
    {
        Assert.Equal(new DisplayLine(DisplayKind.Command, "git stash pop", Exact: true), Simplify(L("stash pop")));
        AssertHidden(R("update-ref", "refs/graftui/rescue/pop", H40, ""));
    }

    [Theory]
    [InlineData("reset -q --hard HEAD", "git reset --hard HEAD")]
    [InlineData("restore -q --source=" + H40 + " --worktree -- a.txt b.txt", "git restore --source=" + H7 + " a.txt b.txt")]
    [InlineData("clean -f -q -- a.txt", "git clean -f a.txt")]
    [InlineData("read-tree " + H40, "git read-tree " + H7)]
    public void Work_state_rollback_forms(string line, string expected)
    {
        Assert.Equal(expected, Shown(L(line)));
    }

    [Fact]
    public void Work_state_index_refresh_is_hidden()
    {
        AssertHidden(L("update-index -q --refresh"));
    }

    [Theory]
    [InlineData("reset -q --soft " + H40, "git reset --soft " + H7)]
    [InlineData("reset -q --keep " + H40, "git reset --keep " + H7)]
    [InlineData("update-ref -d refs/heads/main " + H40, "git update-ref -d refs/heads/main " + H7)]
    public void Undo_commit_amend_and_merge_forms(string line, string expected)
    {
        Assert.Equal(expected, Shown(L(line)));
    }

    [Theory]
    [InlineData("restore -q --source=" + H40 + " --worktree -- a.txt", "git restore --source=" + H7 + " a.txt")]
    [InlineData("stash drop -q stash@{1}", "git stash drop stash@{1}")]
    public void Undo_discard_forms(string line, string expected)
    {
        Assert.Equal(expected, Shown(L(line)));
    }

    [Fact]
    public void Undo_stash_pops_with_index()
    {
        Assert.Equal("git stash pop --index", Shown(L("stash pop -q --index")));
    }

    [Fact]
    public void Undo_pop_restores_the_stash_with_its_message_quoted()
    {
        var r = R("stash", "store", "-m", "On main: graftui pop", H40);
        Assert.Equal($"git stash store -m \"On main: graftui pop\" {H7}", Shown(r));
    }

    [Fact]
    public void Undo_unstage_add_is_unchanged()
    {
        var line = "update-index --add --cacheinfo 100644," + H40 + ",notes.md";
        Assert.Equal(new DisplayLine(DisplayKind.Command, "git " + line, Exact: true), Simplify(L(line)));
    }

    [Fact]
    public void Undo_unstage_force_remove_drops_the_separator()
    {
        Assert.Equal("git update-index --force-remove notes.md", Shown(L("update-index --force-remove -- notes.md")));
    }

    [Theory]
    [InlineData("for-each-ref --format=%(refname) refs/graftui/undo/")]
    [InlineData("log -1 --format=%B " + H40)]
    [InlineData("update-ref -d refs/graftui/undo/000001 " + H40)]
    [InlineData("update-ref -d refs/graftui/rescue/undo-stash " + H40)]
    public void Undo_bookkeeping_is_hidden(string line)
    {
        AssertHidden(L(line));
    }

    [Fact]
    public void Undo_log_push_shows_only_the_note()
    {
        AssertHidden(L("rev-parse --git-dir"));
        AssertHidden(L("mktree"));
        AssertHidden(Indexed("update-index --add --cacheinfo 100644," + H40 + ",notes.md"));
        AssertHidden(L($"commit-tree {H40} -F .git/GRAFTUI_UNDO_MSG"));
        AssertHidden(L("for-each-ref refs/graftui/undo/"));

        var saved = R("update-ref", "refs/graftui/undo/000001", H40, "");
        Assert.Equal(new DisplayLine(DisplayKind.Note, CommandDisplay.UndoSavedNote, Exact: false), Simplify(saved));
        Assert.Equal("↶ saved an undo step (u)", CommandDisplay.UndoSavedNote);
    }

    // ----- Failures, answers and edge cases -----

    [Fact]
    public void A_failing_undo_ref_update_is_shown_exactly_and_is_not_the_note()
    {
        var r = RExit(128, "update-ref", "refs/graftui/undo/000001", H40, "");
        var line = Simplify(r);
        Assert.Equal(new DisplayLine(DisplayKind.Command, r.Command, Exact: true), line);
    }

    [Fact]
    public void A_failing_hidden_command_is_shown_exactly()
    {
        var r = RExit(128, "rev-parse", "--git-dir");
        Assert.Equal(new DisplayLine(DisplayKind.Command, "git rev-parse --git-dir", Exact: true), Simplify(r));
    }

    [Fact]
    public void A_failing_indexed_command_is_shown_with_its_index_prefix()
    {
        var r = new GitResult("GIT_INDEX_FILE=.git/graftui-tmp-index git write-tree", 128, "", "")
        {
            Args = ["write-tree"],
        };
        Assert.Equal(new DisplayLine(DisplayKind.Command, r.Command, Exact: true), Simplify(r));
    }

    [Fact]
    public void A_failing_merge_is_shown_exactly_as_it_ran()
    {
        var r = RExit(1, "merge", "--no-edit", "feature/conflict");
        Assert.True(CommandDisplay.IsFailure(r));
        Assert.Equal(new DisplayLine(DisplayKind.Command, "git merge --no-edit feature/conflict", Exact: true), Simplify(r));
    }

    [Theory]
    [InlineData("rev-parse -q --verify MERGE_HEAD")]
    [InlineData("symbolic-ref -q HEAD")]
    [InlineData("merge-base HEAD feature/x")]
    [InlineData("diff --quiet")]
    public void Exit_one_is_an_answer_and_not_a_failure(string line)
    {
        var r = L(line, exit: 1);
        Assert.False(CommandDisplay.IsFailure(r));
        AssertHidden(r);
    }

    [Theory]
    [InlineData("rev-parse -q --verify MERGE_HEAD")]
    [InlineData("diff --quiet")]
    public void Exit_two_or_more_is_a_failure_even_for_an_answer_query(string line)
    {
        Assert.True(CommandDisplay.IsFailure(L(line, exit: 128)));
    }

    [Fact]
    public void Exit_one_from_a_command_that_is_not_a_query_is_a_failure()
    {
        Assert.True(CommandDisplay.IsFailure(L("merge --abort", exit: 1)));
        Assert.True(CommandDisplay.IsFailure(L("rev-parse --git-dir", exit: 1)));
    }

    [Fact]
    public void Success_is_never_a_failure()
    {
        Assert.False(CommandDisplay.IsFailure(L("merge feature/x")));
    }

    [Fact]
    public void Quoting_wraps_spaces_and_escapes_quotes_dollars_backticks_and_backslashes()
    {
        Assert.Equal("\"a b\"", GitCommandLine.Quote("a b"));
        Assert.Equal("\"a\\\"b\"", GitCommandLine.Quote("a\"b"));
        Assert.Equal("\"\\$HOME\"", GitCommandLine.Quote("$HOME"));
        Assert.Equal("\"\\`x\\`\"", GitCommandLine.Quote("`x`"));
        Assert.Equal("\"back\\\\slash\"", GitCommandLine.Quote("back\\slash"));
    }

    [Fact]
    public void Quoting_leaves_plain_words_bare_and_quotes_empty_or_forced_arguments()
    {
        Assert.Equal("plain-1.2/x:y@z{}^~=,+_", GitCommandLine.Quote("plain-1.2/x:y@z{}^~=,+_"));
        Assert.Equal("\"\"", GitCommandLine.Quote(""));
        Assert.Equal("\"x\"", GitCommandLine.Quote("x", always: true));
    }

    [Fact]
    public void Quoting_is_used_for_the_simplified_command_line()
    {
        var r = R("add", "--", "my file.txt");
        Assert.Equal("git add \"my file.txt\"", Shown(r));
    }

    [Fact]
    public void Separator_is_kept_when_a_later_argument_starts_with_a_dash()
    {
        Assert.Equal("git add -- -odd.txt", Shown(L("add -- -odd.txt")));
        Assert.Equal("git add -- -odd.txt -other", Shown(L("add -- -odd.txt -other")));
    }

    [Fact]
    public void A_sixty_four_digit_hash_is_shortened_too()
    {
        Assert.Equal("git read-tree " + H7, Shown(R("read-tree", H64)));
        Assert.Equal("git restore --source=" + H7 + " a", Shown(R("restore", "--source=" + H64, "--worktree", "--", "a")));
    }

    [Fact]
    public void Shorter_or_non_hex_arguments_are_not_shortened()
    {
        Assert.Equal("git read-tree abc123", Shown(R("read-tree", "abc123")));
        Assert.Equal("git reset --hard " + H40[..39] + "g", Shown(R("reset", "--hard", H40[..39] + "g")));
    }

    [Fact]
    public void Shown_text_equal_to_the_exact_command_is_marked_exact()
    {
        Assert.False(Simplify(L("reset -q --hard HEAD"))!.Exact);
        Assert.True(Simplify(L("reset --hard HEAD"))!.Exact);
    }

    [Fact]
    public void A_result_without_args_is_shown_as_it_ran()
    {
        var r = new GitResult("git status", 0, "", "");
        Assert.Equal(new DisplayLine(DisplayKind.Command, "git status", Exact: true), Simplify(r));
    }

    [Fact]
    public void Commit_hints_default_to_none()
    {
        Assert.Null(CommandHints.None.Subject);
        Assert.Null(CommandHints.None.Body);
    }

    // ----- Log entries (one per git invocation, exact and simplified lines) -----

    [Fact]
    public void A_visible_command_has_its_exact_line_and_a_tidied_simple_line()
    {
        var r = L("merge --no-edit feature/fast-forward");

        var entry = CommandDisplay.Entry(r, CommandHints.None, showOutput: false);

        Assert.Equal([new LogLine(LogKind.Command, "git merge --no-edit feature/fast-forward", 0)], entry.Exact);
        Assert.Equal([new LogLine(LogKind.Command, "git merge feature/fast-forward", 0, Simple: true)], entry.Simple);
    }

    [Fact]
    public void Every_successful_simple_command_line_is_drawn_with_a_tick_even_when_it_is_not_tidied()
    {
        var entry = CommandDisplay.Entry(L("stash push -u"), CommandHints.None, showOutput: false);

        Assert.Equal([new LogLine(LogKind.Command, "git stash push -u", 0, Simple: true)], entry.Simple);
        Assert.Equal([new LogLine(LogKind.Command, "git stash push -u", 0)], entry.Exact);
    }

    [Fact]
    public void A_hidden_command_has_no_simple_lines()
    {
        var entry = CommandDisplay.Entry(L("rev-parse --verify HEAD"), CommandHints.None, showOutput: false);

        Assert.Single(entry.Exact);
        Assert.Empty(entry.Simple);
    }

    [Fact]
    public void A_failed_hidden_command_shows_its_exact_line_failed_in_both_lists()
    {
        var entry = CommandDisplay.Entry(L("write-tree", 128), CommandHints.None, showOutput: false);

        Assert.Equal([new LogLine(LogKind.Command, "git write-tree", 128, Failed: true)], entry.Exact);
        Assert.Equal([new LogLine(LogKind.Command, "git write-tree", 128, Simple: true, Failed: true)], entry.Simple);
    }

    [Fact]
    public void An_undo_step_is_one_note_on_the_simple_side_and_its_command_on_the_exact_side()
    {
        var r = Make(0, ["update-ref", "refs/graftui/undo/000001", H40, ""]);

        var entry = CommandDisplay.Entry(r, CommandHints.None, showOutput: false);

        Assert.Equal(new LogLine(LogKind.Message, CommandDisplay.UndoSavedNote, 0, new Style(Ink.Dim)), Assert.Single(entry.Simple));
        Assert.Equal(LogKind.Command, Assert.Single(entry.Exact).Kind);
    }

    [Fact]
    public void Output_lines_are_in_both_lists_when_they_are_shown()
    {
        var r = new GitResult("git merge feature/x", 1, "Auto-merging a.txt\n", "CONFLICT (content): Merge conflict in a.txt\n") { Args = ["merge", "feature/x"] };

        var entry = CommandDisplay.Entry(r, CommandHints.None, showOutput: true);

        var outputs = new[] { new LogLine(LogKind.Output, "Auto-merging a.txt"), new LogLine(LogKind.Output, "CONFLICT (content): Merge conflict in a.txt") };
        Assert.Equal(outputs, entry.Exact.Skip(1));
        Assert.Equal(outputs, entry.Simple.Skip(1));
        Assert.True(entry.Simple[0].Failed);
    }

    [Fact]
    public void A_commit_simple_line_uses_the_hints_and_the_exact_line_keeps_the_message_file()
    {
        var r = L("commit --cleanup=whitespace -F .git/GRAFTUI_MSG");

        var entry = CommandDisplay.Entry(r, new CommandHints("Fix login redirect", "Users landed on /"), showOutput: false);

        Assert.Equal("git commit --cleanup=whitespace -F .git/GRAFTUI_MSG", entry.Exact[0].Text);
        Assert.Equal("git commit -m \"Fix login redirect\" -m \"Users landed on /\"", entry.Simple[0].Text);
    }

    // ----- Branch commands (A3) -----

    [Fact]
    public void Switch_drops_no_guess_and_the_separator()
    {
        Assert.Equal(new DisplayLine(DisplayKind.Command, "git switch feature/x", Exact: false),
            Simplify(L("switch --no-guess -- feature/x")));
        Assert.Equal("git switch feature/x", Shown(L("switch --no-guess feature/x")));
    }

    [Theory]
    [InlineData("switch -c topic", "git switch -c topic")]
    [InlineData("branch -m old new", "git branch -m old new")]
    [InlineData("branch -d x", "git branch -d x")]
    [InlineData("branch -D x", "git branch -D x")]
    [InlineData("stash pop --index", "git stash pop --index")]
    [InlineData("merge feature/x", "git merge feature/x")]
    [InlineData("clean -f notes.md", "git clean -f notes.md")]
    public void Branch_and_stash_commands_read_as_they_ran(string line, string expected)
    {
        Assert.Equal(expected, Shown(L(line)));
    }

    [Fact]
    public void A_branch_at_a_full_hash_shows_the_short_hash()
    {
        Assert.Equal("git branch x " + H7, Shown(L("branch x " + H40)));
    }

    [Fact]
    public void Check_ref_format_is_hidden_and_an_invalid_name_is_an_answer_not_a_failure()
    {
        var invalid = RExit(128, "check-ref-format", "--branch", "a b");
        Assert.False(CommandDisplay.IsFailure(invalid));
        AssertHidden(invalid);
        Assert.False(CommandDisplay.IsFailure(RExit(1, "check-ref-format", "--branch", "x")));
        AssertHidden(R("check-ref-format", "--branch", "feature/x"));
    }

    [Fact]
    public void The_stash_merge_rescue_ref_is_hidden()
    {
        AssertHidden(R("update-ref", "refs/graftui/rescue/stash-merge", H40, ""));
        AssertHidden(L("update-ref -d refs/graftui/rescue/stash-merge " + H40));
    }

    [Fact]
    public void A_stash_push_with_a_message_that_names_two_branches_is_quoted()
    {
        Assert.Equal("git stash push -u -m \"graftui: before merging feature/x into main\"",
            Shown(R("stash", "push", "-u", "-m", "graftui: before merging feature/x into main")));
    }

    // ----- The rebase: the line is what the user would type -----

    [Fact]
    public void Rebase_shows_as_a_plain_rebase_onto_the_branch()
    {
        var line = Shown(L("rebase -i --no-autosquash --empty=drop " + H40), new CommandHints(Onto: "main"));
        Assert.Equal("git rebase main", line);
    }

    [Fact]
    public void Interactive_rebase_shows_with_dash_i()
    {
        var line = Shown(L("rebase -i --no-autosquash --empty=drop " + H40), new CommandHints(Onto: "main", Interactive: true));
        Assert.Equal("git rebase -i main", line);
    }

    [Fact]
    public void Rebase_without_a_hint_keeps_the_tidied_arguments()
    {
        Assert.Equal("git rebase -i --no-autosquash --empty=drop 0123456", Shown(L("rebase -i --no-autosquash --empty=drop " + H40)));
    }

    [Fact]
    public void A_failed_rebase_is_shown_exactly_as_it_ran()
    {
        var line = Simplify(L("rebase -i --no-autosquash --empty=drop " + H40, exit: 1), new CommandHints(Onto: "main"));
        Assert.Equal(new DisplayLine(DisplayKind.Command, "git rebase -i --no-autosquash --empty=drop " + H40, Exact: true), line);
    }

    [Fact]
    public void Rebase_abort_is_shown_as_it_ran()
    {
        Assert.Equal("git rebase --abort", Shown(L("rebase --abort"), new CommandHints(Onto: "main")));
    }
}
