using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// Repro tests from the git-safety audit of 2026-11 (hooks, signing, submodules, worktrees, sparse checkout, line endings, odd
/// names, large files, two instances, user config). Each test states what graftui should do, and each fails today, so each is
/// skipped until its bug is fixed: remove the Skip together with the fix. The report with the details is kept outside the repo.
/// </summary>
public class SafetyAuditTests
{
    // ---- helpers ----

    private static string Text(string dir, string path)
    {
        var full = Path.Combine(dir, path);
        if (!File.Exists(full))
            return "<missing>";
        return File.ReadAllText(full);
    }

    private static WorkingStatus Read(GitClient git) =>
        new RepoReader(git).LoadChanges(CancellationToken.None).Status;

    private static FileChange UnstagedRow(GitClient git, string path)
    {
        foreach (var row in Read(git).Unstaged)
        {
            if (row.Path == path)
                return row;
        }
        throw new InvalidOperationException("no unstaged row for " + path);
    }

    private static PopOutcome Pop(GitClient git)
    {
        var op = new PopOperation(git);
        var check = op.Check(new PopRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new PopRequest(null), check);
    }

    private static StashOutcome Stash(GitClient git)
    {
        var op = new StashOperation(git);
        var check = op.Check(new StashRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new StashRequest(null), check);
    }

    private static DiscardOutcome Discard(GitClient git, FileChange row)
    {
        var op = new DiscardOperation(git);
        var check = op.Check(new DiscardRequest(row));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new DiscardRequest(row), check);
    }

    private static UndoCheck UndoCheckOnly(GitClient git) => new UndoOperation(git).Check(new UndoRequest(null));

    private static UndoOutcome Undo(GitClient git)
    {
        var op = new UndoOperation(git);
        var check = op.Check(new UndoRequest(null));
        Assert.True(check.CanRun, check.Refusal);
        return op.Run(new UndoRequest(check.StepRef), check);
    }

    /// <summary>
    /// HEAD tracks secret.env, but the user has stopped tracking it (git rm --cached) and ignored it, and keeps a local version
    /// in the work tree. That local file is ignored and untracked, so a snapshot made with `git add -A` does not hold it.
    /// </summary>
    private static void StopTrackingSecret(TempRepo repo)
    {
        repo.Run("rm", "-q", "--cached", "secret.env");
        repo.Write(".gitignore", "secret.env\n");
        repo.Run("add", ".gitignore");
        repo.Write("secret.env", "MY LOCAL SECRET\n");
    }

    /// <summary>A second repo with one commit, added to <paramref name="repo"/> as the submodule "sub" and committed.</summary>
    private static void AddSubmodule(TempRepo repo, TempRepo sub)
    {
        sub.Commit("s.txt", "s1\n", "Add s");
        repo.Run("-c", "protocol.file.allow=always", "submodule", "add", "-q", sub.Dir, "sub");
        repo.Run("commit", "-q", "-m", "Add sub");
    }

    /// <summary>
    /// A linked worktree of <paramref name="repo"/> at a new path beside it: on a new branch when <paramref name="newBranch"/> is set,
    /// otherwise on <paramref name="existing"/>. The caller deletes the folder.
    /// </summary>
    private static string AddWorktree(TempRepo repo, string? newBranch, string? existing)
    {
        var path = repo.Dir + "-wt";
        if (newBranch is not null)
            repo.Run("worktree", "add", "-q", "-b", newBranch, path);
        else
            repo.Run("worktree", "add", "-q", path, existing!);
        return path;
    }

    private static void DeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Left behind; the system cleans the temp folder.
        }
    }

    // ---- 1. an ignored local file at a path that HEAD tracks (WorkState.RestoreTo) ----

    [Fact(Skip = "safety-audit: undo of a pop deletes an ignored local file at a path HEAD tracks (WorkState.RestoreTo)")]
    public void Undo_of_a_pop_keeps_an_ignored_local_file_at_a_path_that_HEAD_tracks()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Commit("secret.env", "committed\n", "Add secret");
        repo.Write("other.txt", "stashed\n");
        repo.Run("stash", "push", "-q", "-u");
        StopTrackingSecret(repo);

        Assert.Equal(PopKind.Popped, Pop(repo.Git).Kind);
        Assert.Equal(UndoOutcomeKind.Undone, Undo(repo.Git).Kind);

        // RestoreTo's `reset --hard HEAD` writes HEAD's secret.env over the local one, then removes it because the snapshot lacks it.
        Assert.Equal("MY LOCAL SECRET\n", Text(repo.Dir, "secret.env"));
    }

    [Fact(Skip = "safety-audit: a pop that conflicts and is rolled back deletes an ignored local file at a path HEAD tracks")]
    public void A_rolled_back_pop_keeps_an_ignored_local_file_at_a_path_that_HEAD_tracks()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Commit("secret.env", "committed\n", "Add secret");
        repo.Write("app.txt", "stashed\n");
        repo.Run("stash", "push", "-q");
        repo.Commit("app.txt", "committed later\n", "Change app");
        StopTrackingSecret(repo);

        var outcome = Pop(repo.Git);

        Assert.Equal(PopKind.Conflict, outcome.Kind);
        Assert.Equal("MY LOCAL SECRET\n", Text(repo.Dir, "secret.env"));
    }

    // ---- 2. submodules ----

    [Fact(Skip = "safety-audit: with submodule.recurse=true, undo of a pop resets uncommitted work inside a submodule")]
    public void Undo_of_a_pop_with_submodule_recurse_keeps_uncommitted_work_inside_the_submodule()
    {
        using var sub = TempRepo.Create();
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        AddSubmodule(repo, sub);
        repo.Run("config", "submodule.recurse", "true");
        repo.Write("other.txt", "o\n");
        repo.Run("stash", "push", "-q", "-u");
        Assert.Equal(PopKind.Popped, Pop(repo.Git).Kind);
        File.WriteAllText(Path.Combine(repo.Dir, "sub", "s.txt"), "work in the submodule\n");

        // The pop guard compares snapshots, which hold only the submodule's commit, so the undo runs; its reset --hard recurses.
        var check = UndoCheckOnly(repo.Git);
        if (check.CanRun)
            new UndoOperation(repo.Git).Run(new UndoRequest(check.StepRef), check);

        Assert.Equal("work in the submodule\n", Text(repo.Dir, "sub/s.txt"));
    }

    [Fact(Skip = "safety-audit: a merge that moves a submodule pointer cannot be undone: refused as 'uncommitted changes'")]
    public void A_merge_that_moves_a_submodule_pointer_can_be_undone()
    {
        using var sub = TempRepo.Create();
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        AddSubmodule(repo, sub);
        repo.Run("switch", "-q", "-c", "feature");
        File.WriteAllText(Path.Combine(repo.Dir, "sub", "s.txt"), "s2\n");
        var subGit = new GitClient(Path.Combine(repo.Dir, "sub"));
        Assert.True(subGit.Run("commit", "-q", "-a", "-m", "s2").Success);
        repo.Run("add", "sub");
        repo.Run("commit", "-q", "-m", "Bump sub");
        repo.Run("switch", "-q", "main");
        repo.Run("submodule", "update", "-q");
        Assert.True(repo.IsClean);

        var op = new MergeOperation(repo.Git);
        var merged = op.Run(new MergeRequest("feature"), op.Check(new MergeRequest("feature")));
        Assert.Equal(MergeKind.FastForward, merged.Kind);

        // git merge moves the pointer but leaves the submodule's checkout behind, so status shows " M sub" though the user changed nothing.
        var check = UndoCheckOnly(repo.Git);
        Assert.True(check.CanRun, check.Refusal);
    }

    // ---- 3. linked worktrees: refs/graftui/undo is shared by every worktree of a repo ----

    [Fact(Skip = "safety-audit: u in one worktree brings back a discard made in another worktree, into the wrong one")]
    public void Undo_in_one_worktree_does_not_bring_back_a_discard_made_in_another()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        var wt = AddWorktree(repo, "feat", null);
        try
        {
            var other = new GitClient(wt);
            File.WriteAllText(Path.Combine(wt, "app.txt"), "edit in the linked worktree\n");
            Assert.Equal(DiscardKind.Discarded, Discard(other, UnstagedRow(other, "app.txt")).Kind);

            var check = UndoCheckOnly(repo.Git);
            if (check.CanRun)
                new UndoOperation(repo.Git).Run(new UndoRequest(check.StepRef), check);

            Assert.Equal("base\n", Text(repo.Dir, "app.txt"));
        }
        finally
        {
            DeleteFolder(wt);
        }
    }

    [Fact(Skip = "safety-audit: u in one worktree unstashes a stash made in another worktree into the wrong one")]
    public void Undo_in_one_worktree_does_not_unstash_a_stash_made_in_another()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        var wt = AddWorktree(repo, "feat", null);
        try
        {
            var other = new GitClient(wt);
            File.WriteAllText(Path.Combine(wt, "app.txt"), "edit in the linked worktree\n");
            Assert.Equal(StashKind.Stashed, Stash(other).Kind);

            var check = UndoCheckOnly(repo.Git);
            if (check.CanRun)
                new UndoOperation(repo.Git).Run(new UndoRequest(check.StepRef), check);

            Assert.Equal("base\n", Text(repo.Dir, "app.txt"));
        }
        finally
        {
            DeleteFolder(wt);
        }
    }

    [Fact(Skip = "safety-audit: u in one worktree deletes a new branch that another worktree has checked out")]
    public void Undo_in_one_worktree_does_not_delete_a_branch_another_worktree_has_checked_out()
    {
        using var repo = TempRepo.Create();
        repo.Commit("app.txt", "base\n", "Add app");
        repo.Run("switch", "-q", "-c", "side");
        var wt = AddWorktree(repo, null, "main");
        try
        {
            var other = new GitClient(wt);
            var create = new CreateBranchOperation(other);
            var created = create.Run(new CreateBranchRequest("nb"), create.Check(new CreateBranchRequest("nb")));
            Assert.Equal(BranchChangeKind.Done, created.Kind);

            var check = UndoCheckOnly(repo.Git);
            if (check.CanRun)
                new UndoOperation(repo.Git).Run(new UndoRequest(check.StepRef), check);

            // The linked worktree must still have a commit at HEAD; otherwise every file shows as a new staged file there.
            Assert.True(other.Run("rev-parse", "-q", "--verify", "HEAD").Success);
        }
        finally
        {
            DeleteFolder(wt);
        }
    }

    // ---- 4. sparse checkout ----

    [Fact(Skip = "safety-audit: sparse checkout: pop of an untracked file outside the cone leaves a rescue ref and no undo step")]
    public void A_pop_in_a_sparse_checkout_with_an_untracked_file_outside_the_cone_saves_its_undo_step()
    {
        using var repo = TempRepo.Create();
        Directory.CreateDirectory(Path.Combine(repo.Dir, "in"));
        Directory.CreateDirectory(Path.Combine(repo.Dir, "out"));
        repo.Commit("in/a.txt", "a\n", "Add a");
        repo.Commit("out/b.txt", "b\n", "Add b");
        repo.Run("sparse-checkout", "set", "in");
        repo.Write("in/a.txt", "edited\n");
        Directory.CreateDirectory(Path.Combine(repo.Dir, "out"));
        repo.Write("out/new.txt", "new, outside the cone\n");
        Assert.Equal(StashKind.Stashed, Stash(repo.Git).Kind);

        var popped = Pop(repo.Git);

        // WorkState.Take runs `git add -A`, which git refuses for a path outside the sparse cone.
        Assert.Equal(PopKind.Popped, popped.Kind);
        Assert.NotNull(popped.Undo);
        Assert.Equal("", repo.Run("for-each-ref", "refs/graftui/rescue/"));
    }

    // ---- 5. commit signing ----

    [Fact(Skip = "safety-audit: a rebase whose commit cannot be signed stops mid-rebase and graftui refuses continue, skip and abort")]
    public void A_rebase_whose_commits_cannot_be_signed_can_be_aborted()
    {
        using var repo = SequenceRepos.Feature4();
        repo.Run("config", "commit.gpgsign", "true");
        repo.Run("config", "gpg.program", "false");
        var op = new RebaseOperation(repo.Git);
        var check = op.Check(new RebaseRequest("main"));
        Assert.True(check.CanRun, check.Refusal);

        op.Run(new RebaseRequest("main"), check);

        // git leaves CHERRY_PICK_HEAD inside its rebase, and RepoState.InProgress reports "a cherry-pick is in progress".
        var abortCheck = new ResumeSequenceOperation(repo.Git).Check(new ResumeRequest(ResumeAction.Abort));
        var stuck = Directory.Exists(Path.Combine(repo.Dir, ".git", "rebase-merge"));
        Assert.True(!stuck || abortCheck.CanRun, abortCheck.Refusal);
    }

    // ---- 6. hooks ----

    // This test fails only when the tests run with a controlling terminal, as graftui always does (for example under
    // `python3 -c "import pty; pty.spawn(['dotnet', 'test'])"`). Without one, /dev/tty cannot be opened and it passes.
    [Fact(Skip = "safety-audit: a hook (or a gpg/ssh passphrase prompt) can open /dev/tty, the TUI's own terminal")]
    public void A_hook_cannot_open_the_terminal()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        var marker = Path.Combine(repo.Dir, "tty-opened");
        repo.WriteHook("pre-commit", "#!/bin/sh\nif (exec 3</dev/tty) 2>/dev/null; then echo yes > '" + marker + "'; fi\nexit 0\n");
        repo.Write("a.txt", "b\n");
        repo.Run("add", "a.txt");
        var op = new CommitOperation(repo.Git);
        var request = new CommitRequest(false, "Change a", "", null, false);

        var outcome = op.Run(request, op.Check(request));

        Assert.Equal(CommitKind.Committed, outcome.Kind);
        Assert.False(File.Exists(marker), "the hook opened /dev/tty");
    }

    [Fact(Skip = "safety-audit: a switch whose post-checkout hook fails is reported as failed although HEAD moved")]
    public void A_switch_whose_post_checkout_hook_fails_is_reported_as_switched()
    {
        using var repo = TempRepo.FastForwardable();
        repo.WriteHook("post-checkout", "#!/bin/sh\nexit 1\n");
        var op = new SwitchOperation(repo.Git);
        var check = op.Check(new SwitchRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);

        var outcome = op.Run(new SwitchRequest("feature"), check);

        Assert.Equal("refs/heads/feature", repo.Run("symbolic-ref", "HEAD"));
        Assert.Equal(SwitchKind.Switched, outcome.Kind);
    }

    [Fact(Skip = "safety-audit: a new branch whose post-checkout hook fails is made and checked out, but gets no undo step")]
    public void A_new_branch_whose_post_checkout_hook_fails_gets_its_undo_step()
    {
        using var repo = TempRepo.Create();
        repo.Commit("a.txt", "a\n", "Add a");
        repo.WriteHook("post-checkout", "#!/bin/sh\nexit 1\n");
        var op = new CreateBranchOperation(repo.Git);
        var check = op.Check(new CreateBranchRequest("nb"));
        Assert.True(check.CanRun, check.Refusal);

        var outcome = op.Run(new CreateBranchRequest("nb"), check);

        Assert.Equal("refs/heads/nb", repo.Run("symbolic-ref", "HEAD"));
        Assert.Equal(BranchChangeKind.Done, outcome.Kind);
        Assert.NotNull(outcome.Undo);
    }

    // ---- 7. user config and odd names ----

    [Fact(Skip = "safety-audit: with status.showUntrackedFiles=no, merge does not see untracked files and does not refuse")]
    public void Merge_refuses_an_untracked_file_when_status_hides_untracked_files()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Run("config", "status.showUntrackedFiles", "no");
        repo.Write("untracked.txt", "u\n");

        var check = new MergeOperation(repo.Git).Check(new MergeRequest("feature"));

        Assert.False(check.CanMerge);
        Assert.True(check.Dirty);
    }

    [Fact(Skip = "safety-audit: merge conflict file names come back C-quoted (caf\\303\\251.txt) for non-ASCII names")]
    public void Merge_conflict_file_names_are_not_quoted()
    {
        using var repo = TempRepo.Create();
        repo.Commit("café.txt", "1\n", "Add café");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("café.txt", "feature\n", "Change on feature");
        repo.Run("switch", "-q", "main");
        repo.Commit("café.txt", "main\n", "Change on main");
        var op = new MergeOperation(repo.Git);

        var outcome = op.Run(new MergeRequest("feature"), op.Check(new MergeRequest("feature")));

        Assert.Equal(MergeKind.Conflict, outcome.Kind);
        Assert.Equal(new[] { "café.txt" }, outcome.ConflictFiles);
    }
}
