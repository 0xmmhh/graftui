using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>Commit and amend against throwaway repos, including the refusals and the pushed-commit detection.</summary>
public class CommitSafetyTests
{
    private static CommitRequest Commit(string subject, string body = "") =>
        new(false, subject, body, null, false);

    private static CommitOutcome Run(CommitOperation op, CommitRequest request)
    {
        var check = op.Check(request);
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(request, check);
    }

    private static void StageAll(TempRepo repo) => repo.Run("add", "-A");

    private static void AssertNoMessageFile(TempRepo repo) =>
        Assert.False(File.Exists(Path.Combine(repo.Dir, ".git", CommitOperation.MessageFileName)));

    [Fact]
    public void Commit_with_subject_and_body_writes_the_built_message_and_advances_head()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        var oldHead = repo.Head;
        repo.Write("app.txt", "line 1 (edited)\n");
        StageAll(repo);
        var op = new CommitOperation(repo.Git);

        var outcome = Run(op, Commit("Subj", "line one\nline two"));

        Assert.Equal(CommitKind.Committed, outcome.Kind);
        Assert.Equal(repo.Run("log", "-1", "--format=%B"), CommitMessage.Build("Subj", "line one\nline two").TrimEnd('\n'));
        Assert.Equal(oldHead, repo.Run("rev-parse", "HEAD^"));
        Assert.Equal(outcome.NewHead, repo.Head);
        Assert.NotNull(outcome.Undo);
        var change = Assert.Single(outcome.Undo!.Changes);
        Assert.Equal("refs/heads/main", change.Ref);
        Assert.Equal(oldHead, change.Before);
        Assert.Equal(repo.Head, change.After);
        Assert.Contains(outcome.Commands, c => c.Command == "git commit --cleanup=whitespace -F .git/GRAFTUI_MSG" && c.ExitCode == 0);
        AssertNoMessageFile(repo);
    }

    [Fact]
    public void A_hash_line_survives_even_when_the_repo_asks_git_to_strip_comments()
    {
        using var repo = TempRepo.Create();
        repo.Run("config", "commit.cleanup", "strip");
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");
        StageAll(repo);
        var op = new CommitOperation(repo.Git);

        var outcome = Run(op, Commit("# Heading that starts with a hash", "# and a body line"));
        Assert.Equal(CommitKind.Committed, outcome.Kind);
        Assert.Equal("# Heading that starts with a hash", repo.Run("log", "-1", "--format=%s"));
        Assert.Equal("# and a body line", repo.Run("log", "-1", "--format=%b"));
    }

    [Fact]
    public void Amend_keeps_a_hash_line_under_strip_too()
    {
        using var repo = TempRepo.Create();
        repo.Run("config", "commit.cleanup", "strip");
        repo.Commit("app.txt", "line 1\n", "Old");
        var oldHead = repo.Head;
        var op = new CommitOperation(repo.Git);
        var request = new CommitRequest(true, "# Kept", "", oldHead, false);

        var outcome = Run(op, request);

        Assert.Equal(CommitKind.Amended, outcome.Kind);
        Assert.Equal("# Kept", repo.Run("log", "-1", "--format=%s"));
        Assert.Contains(outcome.Commands, c => c.Command == "git commit --amend --cleanup=whitespace -F .git/GRAFTUI_MSG");
    }

    [Fact]
    public void A_pre_commit_hook_failure_leaves_head_the_index_and_the_work_tree_as_they_were()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        var oldHead = repo.Head;
        repo.Write("app.txt", "line 1 (edited)\n");
        StageAll(repo);
        repo.WriteHook("pre-commit", "#!/bin/sh\necho lint failed >&2\nexit 1\n");
        var cached = repo.Run("diff", "--cached", "--name-only");
        var worktree = File.ReadAllBytes(Path.Combine(repo.Dir, "app.txt"));

        var outcome = Run(new CommitOperation(repo.Git), Commit("Will fail"));

        Assert.Equal(CommitKind.Failed, outcome.Kind);
        Assert.Contains("lint failed", outcome.Message);
        Assert.Equal(oldHead, repo.Head);
        Assert.Equal(cached, repo.Run("diff", "--cached", "--name-only"));
        Assert.Equal(worktree, File.ReadAllBytes(Path.Combine(repo.Dir, "app.txt")));
        Assert.Contains(outcome.Commands, c => c.Command == "git commit --cleanup=whitespace -F .git/GRAFTUI_MSG" && c.ExitCode == 1);
        AssertNoMessageFile(repo);
    }

    [Fact]
    public void A_commit_msg_hook_failure_leaves_everything_as_it_was()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        var oldHead = repo.Head;
        repo.Write("app.txt", "line 1 (edited)\n");
        StageAll(repo);
        repo.WriteHook("commit-msg", "#!/bin/sh\necho lint failed in message >&2\nexit 1\n");
        var cached = repo.Run("diff", "--cached", "--name-only");

        var outcome = Run(new CommitOperation(repo.Git), Commit("Will fail too"));

        Assert.Equal(CommitKind.Failed, outcome.Kind);
        Assert.Contains("lint failed in message", outcome.Message);
        Assert.Equal(oldHead, repo.Head);
        Assert.Equal(cached, repo.Run("diff", "--cached", "--name-only"));
        AssertNoMessageFile(repo);
    }

    [Fact]
    public void Nothing_staged_is_refused()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "line 1 (edited)\n");

        var check = new CommitOperation(repo.Git).Check(Commit("Nope"));

        Assert.Equal(ChangeRefusals.NothingStaged, check.Refusal);
    }

    [Fact]
    public void An_empty_subject_is_refused()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "x\n");
        StageAll(repo);

        var check = new CommitOperation(repo.Git).Check(Commit("   "));

        Assert.Equal(ChangeRefusals.EmptySubject, check.Refusal);
    }

    [Fact]
    public void A_detached_head_is_refused()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "x\n");
        StageAll(repo);
        repo.Run("switch", "-q", "--detach");

        var check = new CommitOperation(repo.Git).CheckRepo(amend: false);

        Assert.Equal(ChangeRefusals.Detached, check.Refusal);
    }

    [Fact]
    public void A_conflicted_merge_in_progress_is_refused_as_a_merge()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        Assert.False(repo.Git.Run("merge", "feature").Success);
        try
        {
            var check = new CommitOperation(repo.Git).CheckRepo(amend: false);

            Assert.Equal(ChangeRefusals.InProgress("merge"), check.Refusal);
        }
        finally
        {
            repo.Git.Run("merge", "--abort");
        }
    }

    [Fact]
    public void A_conflicting_cherry_pick_in_progress_is_refused_as_a_cherry_pick()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        Assert.False(repo.Git.Run("cherry-pick", "feature").Success);
        try
        {
            var check = new CommitOperation(repo.Git).CheckRepo(amend: false);

            Assert.Equal(ChangeRefusals.InProgress("cherry-pick"), check.Refusal);
        }
        finally
        {
            repo.Git.Run("cherry-pick", "--abort");
        }
    }

    [Fact]
    public void A_rebase_directory_is_refused_as_a_rebase()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Write("app.txt", "x\n");
        StageAll(repo);
        var rebaseDir = Path.Combine(repo.Dir, ".git", "rebase-merge");
        Directory.CreateDirectory(rebaseDir);
        try
        {
            var check = new CommitOperation(repo.Git).Check(Commit("Blocked"));

            Assert.Equal(ChangeRefusals.InProgress("rebase"), check.Refusal);
        }
        finally
        {
            Directory.Delete(rebaseDir, recursive: true);
        }
    }

    [Fact]
    public void The_first_commit_in_an_empty_repo_has_no_parent_and_an_empty_before_ref()
    {
        using var repo = TempRepo.Create();
        repo.Write("a.txt", "a\n");
        StageAll(repo);

        var outcome = Run(new CommitOperation(repo.Git), Commit("First"));

        Assert.Equal(CommitKind.Committed, outcome.Kind);
        Assert.Equal(repo.Head, repo.Run("rev-list", "--parents", "-n", "1", "HEAD"));
        Assert.Single(repo.Run("rev-list", "HEAD").Split('\n'));
        var change = Assert.Single(outcome.Undo!.Changes);
        Assert.Equal("", change.Before);
        Assert.Equal(repo.Head, change.After);
    }

    [Fact]
    public void A_commit_saves_an_undo_step_that_names_the_branch_and_the_move()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        var oldHead = repo.Head;
        repo.Write("app.txt", "line 1 (edited)\n");
        StageAll(repo);

        var outcome = Run(new CommitOperation(repo.Git), Commit("Subj"));

        var top = new UndoLog(repo.Git).Top([]);
        Assert.NotNull(top?.Step);
        Assert.Equal(UndoKind.Commit, top.Step.Kind);
        Assert.Equal("commit", top.Step.What);
        Assert.Equal("main", top.Step.Branch);
        Assert.Equal(outcome.NewHead, top.Step.Head);
        Assert.Equal(new RefChange("refs/heads/main", oldHead, repo.Head), Assert.Single(top.Step.Changes));
    }

    [Fact]
    public void An_amend_saves_an_amend_step_with_the_old_and_the_new_commit()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        var oldHead = repo.Head;

        var outcome = Run(new CommitOperation(repo.Git), new CommitRequest(true, "Reworded", "", oldHead, false));

        var top = new UndoLog(repo.Git).Top([]);
        Assert.NotNull(top?.Step);
        Assert.Equal(UndoKind.Amend, top.Step.Kind);
        Assert.Equal("amend", top.Step.What);
        Assert.Equal(outcome.NewHead, top.Step.Head);
        Assert.Equal(new RefChange("refs/heads/main", oldHead, repo.Head), Assert.Single(top.Step.Changes));
    }

    [Fact]
    public void A_commit_whose_undo_step_cannot_be_saved_stands_and_says_so()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        var oldHead = repo.Head;
        repo.Write("b.txt", "b\n");
        StageAll(repo);
        // A folder where the message file goes makes the save fail after the commit has landed.
        Directory.CreateDirectory(Path.Combine(repo.Dir, ".git", UndoLog.MessageFile));

        var outcome = Run(new CommitOperation(repo.Git), Commit("Second"));

        Assert.Equal(CommitKind.Committed, outcome.Kind);
        Assert.NotEqual(oldHead, repo.Head);
        Assert.Contains("could not save the undo step", outcome.Message);
        Assert.Null(outcome.Undo);
        Assert.Null(new UndoLog(repo.Git).Top([]));
    }

    [Fact]
    public void Amend_replaces_the_message_keeps_the_parent_and_includes_staged_changes()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Commit("other.txt", "o\n", "Old subject");
        var oldHead = repo.Head;
        var parent = repo.Run("rev-parse", "HEAD^");
        var commitsBefore = repo.Run("rev-list", "--count", "HEAD");
        repo.Write("other.txt", "o changed\n");
        StageAll(repo);
        var op = new CommitOperation(repo.Git);

        var check = op.Check(new CommitRequest(true, "New subject", "", oldHead, false));
        Assert.True(check.CanRun, check.Refusal);
        var outcome = op.Run(new CommitRequest(true, "New subject", "", oldHead, false), check);

        Assert.Equal(CommitKind.Amended, outcome.Kind);
        Assert.Equal("New subject", repo.Run("log", "-1", "--format=%s"));
        Assert.Equal(parent, repo.Run("rev-parse", "HEAD^"));
        Assert.Equal(commitsBefore, repo.Run("rev-list", "--count", "HEAD"));
        Assert.Equal("o changed", File.ReadAllText(Path.Combine(repo.Dir, "other.txt")).Trim());
        Assert.Equal("other.txt", repo.Run("show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Equal(1, repo.Git.Run("merge-base", "--is-ancestor", oldHead, "HEAD").ExitCode);
        Assert.Equal("refs/heads/main", outcome.Undo!.Changes[0].Ref);
        Assert.Equal(oldHead, outcome.Undo!.Changes[0].Before);
    }

    [Fact]
    public void Amend_with_nothing_staged_is_allowed_and_only_changes_the_message()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Old");
        var oldHead = repo.Head;
        var op = new CommitOperation(repo.Git);

        var outcome = Run(op, new CommitRequest(true, "Reworded", "", oldHead, false));

        Assert.Equal(CommitKind.Amended, outcome.Kind);
        Assert.Equal("Reworded", repo.Run("log", "-1", "--format=%s"));
    }

    [Fact]
    public void Amend_in_an_empty_repo_is_refused()
    {
        using var repo = TempRepo.Create();

        var check = new CommitOperation(repo.Git).Check(new CommitRequest(true, "Nope", "", null, false));

        Assert.Equal(ChangeRefusals.NoCommitToAmend, check.Refusal);
    }

    [Fact]
    public void An_amend_with_a_mismatching_expected_head_is_refused_as_head_moved()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");

        var check = new CommitOperation(repo.Git).Check(new CommitRequest(true, "Subj", "", "0000000000000000000000000000000000000000", false));

        Assert.Equal(ChangeRefusals.HeadMoved, check.Refusal);
    }

    [Fact]
    public void Pushed_amend_is_detected_and_needs_confirmation_and_never_touches_the_remote()
    {
        var remoteDir = TempRepo.NewSandboxDir();
        try
        {
            Directory.CreateDirectory(remoteDir);
            var bare = new GitClient(remoteDir);
            Assert.True(bare.Run("init", "-q", "--bare").Success);

            using var repo = TempRepo.Create();
            repo.Commit("app.txt", "line 1\n", "Add app");
            repo.Run("remote", "add", "origin", remoteDir);
            repo.Run("push", "-q", "origin", "main");
            var remoteBefore = bare.Run("rev-parse", "refs/heads/main").StdOut.Trim();
            var op = new CommitOperation(repo.Git);

            var pushed = op.CheckRepo(amend: true);
            Assert.Equal(new[] { "origin/main" }, pushed.PushedTo);

            var unconfirmed = op.Check(new CommitRequest(true, "Changed", "", repo.Head, false));
            Assert.Equal(ChangeRefusals.PushedNotConfirmed("origin/main"), unconfirmed.Refusal);

            var request = new CommitRequest(true, "Changed", "", repo.Head, PushedConfirmed: true);
            var outcome = op.Run(request, op.Check(request));
            Assert.Equal(CommitKind.Amended, outcome.Kind);
            Assert.Equal(remoteBefore, bare.Run("rev-parse", "refs/heads/main").StdOut.Trim());
            Assert.NotEqual(remoteBefore, repo.Run("rev-parse", "main"));

            repo.Write("new.txt", "n\n");
            StageAll(repo);
            var local = new CommitRequest(false, "Local only", "", repo.Head, false);
            Assert.Equal(CommitKind.Committed, op.Run(local, op.Check(local)).Kind);
            Assert.Empty(op.CheckRepo(amend: true).PushedTo);
        }
        finally
        {
            TempRepo.DeleteSandboxRoot(remoteDir);
        }
    }

    [Fact]
    public void An_amend_during_a_conflicted_merge_is_refused_as_a_merge()
    {
        using var repo = TempRepo.Diverged(conflict: true);
        Assert.False(repo.Git.Run("merge", "feature").Success);
        try
        {
            var check = new CommitOperation(repo.Git).CheckRepo(amend: true);

            Assert.Equal(ChangeRefusals.InProgress("merge"), check.Refusal);
        }
        finally
        {
            repo.Git.Run("merge", "--abort");
        }
    }
}
