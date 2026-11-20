using graftui.Git;
using graftui.Model;
using graftui.Operations;

namespace graftui.Tests;

/// <summary>
/// Repro tests from the git-safety audit of 2026-11 (hooks, signing, submodules, worktrees, sparse checkout, line endings, odd
/// names, large files, two instances, user config). Each test states what graftui should do, and each fails today, so each is
/// skipped until its bug is fixed: remove the Skip together with the fix. The report with the details is kept outside the repo.
/// One test is manual (A_hook_cannot_open_the_terminal): it passes when the tests run without a controlling terminal, as they
/// normally do, and fails only under one.
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
        catch (UnauthorizedAccessException)
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
        Assert.True(subGit.Run("config", "--local", "user.name", "t").Success);
        Assert.True(subGit.Run("config", "--local", "user.email", "t@example.invalid").Success);
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

    [Fact]
    public void A_rebase_whose_commits_cannot_be_signed_can_be_aborted()
    {
        using var repo = SequenceRepos.Feature4();
        repo.Run("config", "commit.gpgsign", "true");
        repo.Run("config", "gpg.program", "false");
        var oldTip = repo.Run("rev-parse", "HEAD");
        var op = new RebaseOperation(repo.Git);
        var check = op.Check(new RebaseRequest("main"));
        Assert.True(check.CanRun, check.Refusal);

        op.Run(new RebaseRequest("main"), check);

        // git leaves CHERRY_PICK_HEAD inside its rebase, and RepoState.InProgress reports "a cherry-pick is in progress".
        var resume = new ResumeSequenceOperation(repo.Git);
        var abortCheck = resume.Check(new ResumeRequest(ResumeAction.Abort));
        Assert.True(abortCheck.CanRun, abortCheck.Refusal);
        resume.Run(new ResumeRequest(ResumeAction.Abort), abortCheck);

        Assert.Equal(oldTip, repo.Run("rev-parse", "HEAD"));
        Assert.False(Directory.Exists(Path.Combine(repo.Dir, ".git", "rebase-merge")));
    }

    // ---- 6. hooks ----

    // This test fails only when the tests run with a controlling terminal, as graftui always does (for example under
    // `python3 -c "import pty; pty.spawn(['dotnet', 'test'])"`). Without one, /dev/tty cannot be opened and it passes.
    [Fact(Skip = "manual: needs a controlling terminal")]
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
        if (OperatingSystem.IsWindows())
            return;
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
        if (OperatingSystem.IsWindows())
            return;
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

    // ======== second pass (same audit, later on 2026-11-16): flags on index entries, ignored files, a branch's merge options,
    // a file and a folder that swap places, interactive-rebase stops, a branch's config ========

    /// <summary>
    /// config.json is tracked, and the user keeps a local version that git is told not to look at (the "local config" trick):
    /// `git update-index --assume-unchanged` or `--skip-worktree`. `git status` then shows nothing for it.
    /// </summary>
    private static void KeepLocalConfig(TempRepo repo, string flag)
    {
        repo.Run("update-index", flag, "config.json");
        repo.Write("config.json", "MY LOCAL CONFIG\n");
    }

    /// <summary>A repo with config.json and app.txt committed.</summary>
    private static TempRepo ConfigAndApp()
    {
        var repo = TempRepo.Create();
        repo.Commit("config.json", "committed\n", "Add config");
        repo.Commit("app.txt", "a\n", "Add app");
        return repo;
    }

    /// <summary>
    /// main tracks secret.env. feature stops tracking it and ignores it. HEAD is on main, and secret.env is not in the work tree
    /// of feature. Merging feature into main removes the file; the user then keeps a local, ignored one.
    /// </summary>
    private static TempRepo FeatureStopsTrackingSecret()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        repo.Commit("secret.env", "TEMPLATE\n", "Add secret template");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Run("rm", "-q", "secret.env");
        repo.Write(".gitignore", "secret.env\n");
        repo.Run("add", ".gitignore");
        repo.Run("commit", "-q", "-m", "Stop tracking secret.env");
        repo.Run("switch", "-q", "main");
        return repo;
    }

    /// <summary>feature adds local.cfg. On main the user has an ignored local.cfg of their own (ignored through .git/info/exclude).</summary>
    private static TempRepo IgnoredFileInTheWay()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "a\n", "Add app");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("local.cfg", "FROM FEATURE\n", "Add local.cfg");
        repo.Run("switch", "-q", "main");
        File.AppendAllText(Path.Combine(repo.Dir, ".git", "info", "exclude"), "local.cfg\n");
        repo.Write("local.cfg", "MY LOCAL\n");
        return repo;
    }

    /// <summary>
    /// feature has three commits: the first changes line 1 of app.txt (main changes it too, so it conflicts), then two that add files.
    /// HEAD is on feature.
    /// </summary>
    private static TempRepo FirstCommitConflicts()
    {
        var repo = TempRepo.Create();
        repo.Commit("app.txt", "line 1\n", "Add app");
        repo.Run("switch", "-q", "-c", "feature");
        repo.Commit("app.txt", "feature line 1\n", "Change line 1 on feature");
        repo.Commit("extra.txt", "extra\n", "Add extra");
        repo.Commit("last.txt", "last\n", "Add last");
        repo.Run("switch", "-q", "main");
        repo.Commit("app.txt", "main line 1\n", "Change line 1 on main");
        repo.Run("switch", "-q", "feature");
        return repo;
    }

    /// <summary>feature is merged into main (HEAD), but its upstream origin/feature is one commit behind it.</summary>
    private static TempRepo MergedButAheadOfUpstream()
    {
        var repo = TempRepo.FastForwardable();
        repo.Run("merge", "-q", "feature");
        repo.Run("update-ref", "refs/remotes/origin/feature", "HEAD~1");
        repo.Run("config", "remote.origin.url", "/nonexistent");
        repo.Run("config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*");
        repo.Run("config", "branch.feature.remote", "origin");
        repo.Run("config", "branch.feature.merge", "refs/heads/feature");
        return repo;
    }

    // ---- 8. a file and a folder that swap places ----

    [Fact]
    public void Discard_of_a_deleted_file_keeps_an_untracked_file_that_took_its_folders_place()
    {
        using var repo = TempRepo.Create();
        Directory.CreateDirectory(Path.Combine(repo.Dir, "docs"));
        repo.Commit("docs/a.txt", "tracked a\n", "Add docs/a");
        Directory.Delete(Path.Combine(repo.Dir, "docs"), recursive: true);
        repo.Write("docs", "MY NEW FILE NAMED docs\n");
        var row = UnstagedRow(repo.Git, "docs/a.txt");
        var op = new DiscardOperation(repo.Git);

        var check = op.Check(new DiscardRequest(row));
        if (check.CanRun)
            op.Run(new DiscardRequest(row), check);

        // `git restore --worktree -- docs/a.txt` removes the file "docs" to make the folder. The backup holds only docs/a.txt.
        Assert.Equal("MY NEW FILE NAMED docs\n", Text(repo.Dir, "docs"));
    }

    [Fact]
    public void Discard_of_a_deleted_file_leaves_the_untracked_files_in_a_folder_at_its_path()
    {
        using var repo = TempRepo.Create();
        repo.Commit("notes", "tracked notes\n", "Add notes");
        File.Delete(Path.Combine(repo.Dir, "notes"));
        Directory.CreateDirectory(Path.Combine(repo.Dir, "notes"));
        repo.Write("notes/draft.txt", "MY DRAFT\n");
        var row = UnstagedRow(repo.Git, "notes");
        var op = new DiscardOperation(repo.Git);

        var check = op.Check(new DiscardRequest(row));
        if (check.CanRun)
            op.Run(new DiscardRequest(row), check);

        // notes/draft.txt is another row of the list. The discard of "notes" removes it too (it is in the stash, and u brings it back).
        Assert.Equal("MY DRAFT\n", Text(repo.Dir, "notes/draft.txt"));
    }

    [Fact]
    public void Discard_refusing_a_file_that_took_a_folders_place_leaves_the_repo_unchanged()
    {
        using var repo = TempRepo.Create();
        Directory.CreateDirectory(Path.Combine(repo.Dir, "docs"));
        repo.Commit("docs/a.txt", "tracked a\n", "Add docs/a");
        Directory.Delete(Path.Combine(repo.Dir, "docs"), recursive: true);
        repo.Write("docs", "MY NEW FILE NAMED docs\n");
        var row = UnstagedRow(repo.Git, "docs/a.txt");
        var before = repo.Status();
        var stashList = repo.Run("stash", "list");
        var head = repo.Run("rev-parse", "HEAD");

        var check = new DiscardOperation(repo.Git).Check(new DiscardRequest(row));

        Assert.False(check.CanRun, "the discard ran although a file is in the way");
        Assert.Contains("docs", check.Refusal);
        Assert.Equal(before, repo.Status());
        Assert.Equal(stashList, repo.Run("stash", "list"));
        Assert.Equal(head, repo.Run("rev-parse", "HEAD"));
        Assert.Equal("MY NEW FILE NAMED docs\n", Text(repo.Dir, "docs"));
    }

    [Fact]
    public void Discard_refusing_a_folder_at_a_files_path_leaves_the_repo_unchanged()
    {
        using var repo = TempRepo.Create();
        repo.Commit("notes", "tracked notes\n", "Add notes");
        File.Delete(Path.Combine(repo.Dir, "notes"));
        Directory.CreateDirectory(Path.Combine(repo.Dir, "notes"));
        repo.Write("notes/draft.txt", "MY DRAFT\n");
        var row = UnstagedRow(repo.Git, "notes");
        var before = repo.Status();
        var stashList = repo.Run("stash", "list");
        var head = repo.Run("rev-parse", "HEAD");

        var check = new DiscardOperation(repo.Git).Check(new DiscardRequest(row));

        Assert.False(check.CanRun, "the discard ran although a folder is in the way");
        Assert.Contains("notes", check.Refusal);
        Assert.Equal(before, repo.Status());
        Assert.Equal(stashList, repo.Run("stash", "list"));
        Assert.Equal(head, repo.Run("rev-parse", "HEAD"));
        Assert.Equal("MY DRAFT\n", Text(repo.Dir, "notes/draft.txt"));
    }

    [Fact]
    public void Discard_of_a_deleted_file_in_a_folder_that_still_exists_discards_it()
    {
        using var repo = TempRepo.Create();
        Directory.CreateDirectory(Path.Combine(repo.Dir, "docs"));
        repo.Commit("docs/a.txt", "tracked a\n", "Add docs/a");
        File.Delete(Path.Combine(repo.Dir, "docs", "a.txt"));
        var row = UnstagedRow(repo.Git, "docs/a.txt");

        var outcome = Discard(repo.Git, row);

        Assert.Equal(DiscardKind.Discarded, outcome.Kind);
        Assert.Equal("tracked a\n", Text(repo.Dir, "docs/a.txt"));
    }

    [Fact]
    public void Discard_refuses_when_a_parent_is_a_symlink()
    {
        using var repo = TempRepo.Create();
        Directory.CreateDirectory(Path.Combine(repo.Dir, "docs"));
        repo.Commit("docs/a.txt", "tracked a\n", "Add docs/a");
        Directory.Delete(Path.Combine(repo.Dir, "docs"), recursive: true);
        Directory.CreateDirectory(Path.Combine(repo.Dir, "real"));
        Directory.CreateSymbolicLink(Path.Combine(repo.Dir, "docs"), "real");
        var row = UnstagedRow(repo.Git, "docs/a.txt");
        var before = repo.Status();

        var check = new DiscardOperation(repo.Git).Check(new DiscardRequest(row));

        Assert.False(check.CanRun, "the discard ran although a symlink is in the way");
        Assert.Equal(before, repo.Status());
    }

    [Fact(Skip = "safety-audit: undo of a pop fails when a tracked folder was replaced by an untracked file; the tree is left at HEAD")]
    public void Undo_of_a_pop_works_when_an_untracked_file_took_a_tracked_folders_place()
    {
        using var repo = TempRepo.Create();
        Directory.CreateDirectory(Path.Combine(repo.Dir, "docs"));
        repo.Commit("docs/a.txt", "tracked a\n", "Add docs/a");
        repo.Commit("app.txt", "a\n", "Add app");
        repo.Write("app.txt", "stashed\n");
        repo.Run("stash", "push", "-q");
        Directory.Delete(Path.Combine(repo.Dir, "docs"), recursive: true);
        repo.Write("docs", "MY NEW FILE NAMED docs\n");
        var before = repo.Status();
        Assert.Equal(PopKind.Popped, Pop(repo.Git).Kind);

        var outcome = Undo(repo.Git);

        // WorkState.RestoreTo passes both "docs" and "docs/a.txt" to `git restore`, which fails on the second.
        Assert.Equal(UndoOutcomeKind.Undone, outcome.Kind);
        Assert.Equal(before, repo.Status());
        Assert.Equal("MY NEW FILE NAMED docs\n", Text(repo.Dir, "docs"));
        Assert.Equal("", repo.Run("for-each-ref", "refs/graftui/rescue/"));
    }

    // ---- 9. assume-unchanged and skip-worktree files (a local edit that git status does not show) ----

    [Fact(Skip = "safety-audit: stash (z) destroys a local edit to an assume-unchanged file: git stash resets it and does not save it")]
    public void Stash_keeps_a_local_edit_to_an_assume_unchanged_file()
    {
        using var repo = ConfigAndApp();
        KeepLocalConfig(repo, "--assume-unchanged");
        repo.Write("app.txt", "edited\n");
        var op = new StashOperation(repo.Git);

        var check = op.Check(new StashRequest(null));
        if (check.CanRun)
            op.Run(new StashRequest(null), check);

        Assert.Equal("MY LOCAL CONFIG\n", Text(repo.Dir, "config.json"));
    }

    [Fact(Skip = "safety-audit: stash-merge-pop destroys a local edit to an assume-unchanged file (its git stash push resets it)")]
    public void Stash_merge_keeps_a_local_edit_to_an_assume_unchanged_file()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("update-index", "--assume-unchanged", "a.txt");
        repo.Write("a.txt", "MY LOCAL A\n");
        repo.Write("untracked.txt", "u\n");
        var op = new StashMergeOperation(repo.Git);

        var check = op.Check(new StashMergeRequest("feature"));
        if (check.CanRun)
            op.Run(new StashMergeRequest("feature"), check);

        Assert.Equal("MY LOCAL A\n", Text(repo.Dir, "a.txt"));
    }

    [Fact(Skip = "safety-audit: a pop that conflicts and is rolled back destroys a local edit to an assume-unchanged file")]
    public void A_rolled_back_pop_keeps_a_local_edit_to_an_assume_unchanged_file()
    {
        using var repo = ConfigAndApp();
        repo.Write("app.txt", "stashed edit\n");
        repo.Run("stash", "push", "-q");
        repo.Commit("app.txt", "other\n", "Change app");
        KeepLocalConfig(repo, "--assume-unchanged");
        var op = new PopOperation(repo.Git);

        var check = op.Check(new PopRequest(null));
        if (check.CanRun)
            op.Run(new PopRequest(null), check);

        // WorkState.RestoreTo's `reset --hard HEAD` writes HEAD's version over the file, and the snapshot never held the local one.
        Assert.Equal("MY LOCAL CONFIG\n", Text(repo.Dir, "config.json"));
    }

    [Fact(Skip = "safety-audit: undo of a pop destroys a local edit to an assume-unchanged file")]
    public void Undo_of_a_pop_keeps_a_local_edit_to_an_assume_unchanged_file()
    {
        using var repo = ConfigAndApp();
        repo.Write("app.txt", "edited\n");
        repo.Run("stash", "push", "-q");
        KeepLocalConfig(repo, "--assume-unchanged");
        var pop = new PopOperation(repo.Git);
        var popCheck = pop.Check(new PopRequest(null));
        if (popCheck.CanRun)
            pop.Run(new PopRequest(null), popCheck);

        var check = UndoCheckOnly(repo.Git);
        if (check.CanRun)
            new UndoOperation(repo.Git).Run(new UndoRequest(check.StepRef), check);

        Assert.Equal("MY LOCAL CONFIG\n", Text(repo.Dir, "config.json"));
    }

    [Fact(Skip = "safety-audit: undo of a pop fails with a skip-worktree file: read-tree drops the flag, so the state never matches")]
    public void Undo_of_a_pop_works_with_a_skip_worktree_file_and_keeps_its_flag()
    {
        using var repo = ConfigAndApp();
        repo.Write("app.txt", "edited\n");
        repo.Run("stash", "push", "-q");
        KeepLocalConfig(repo, "--skip-worktree");
        Assert.Equal(PopKind.Popped, Pop(repo.Git).Kind);

        var outcome = Undo(repo.Git);

        Assert.Equal(UndoOutcomeKind.Undone, outcome.Kind);
        Assert.Equal("MY LOCAL CONFIG\n", Text(repo.Dir, "config.json"));
        // `git ls-files -v` marks a skip-worktree entry with S.
        Assert.StartsWith("S ", repo.Run("ls-files", "-v", "--", "config.json"));
        Assert.Equal("", repo.Run("for-each-ref", "refs/graftui/rescue/"));
    }

    // ---- 10. an ignored local file in the way ----

    [Fact(Skip = "safety-audit: undo of a merge overwrites an ignored local file at a path the older commit tracks (reset --keep)")]
    public void Undo_of_a_merge_keeps_an_ignored_local_file_at_a_path_the_older_commit_tracks()
    {
        using var repo = FeatureStopsTrackingSecret();
        var op = new MergeOperation(repo.Git);
        Assert.Equal(MergeKind.FastForward, op.Run(new MergeRequest("feature"), op.Check(new MergeRequest("feature"))).Kind);
        repo.Write("secret.env", "MY LOCAL SECRET\n");
        Assert.True(repo.IsClean);

        var check = UndoCheckOnly(repo.Git);
        if (check.CanRun)
            new UndoOperation(repo.Git).Run(new UndoRequest(check.StepRef), check);

        Assert.Equal("MY LOCAL SECRET\n", Text(repo.Dir, "secret.env"));
    }

    [Fact(Skip = "safety-audit: undo of a rebase overwrites an ignored local file at a path the old tip tracks (reset --keep)")]
    public void Undo_of_a_rebase_keeps_an_ignored_local_file_at_a_path_the_old_tip_tracks()
    {
        using var repo = FeatureStopsTrackingSecret();
        repo.Run("switch", "-q", "-c", "topic", "main");
        repo.Commit("t.txt", "t\n", "Add t");
        var op = new RebaseOperation(repo.Git);
        var done = op.Run(new RebaseRequest("feature"), op.Check(new RebaseRequest("feature")));
        Assert.Equal(SequenceOutcomeKind.Done, done.Kind);
        repo.Write("secret.env", "MY LOCAL SECRET\n");
        Assert.True(repo.IsClean);

        var check = UndoCheckOnly(repo.Git);
        if (check.CanRun)
            new UndoOperation(repo.Git).Run(new UndoRequest(check.StepRef), check);

        Assert.Equal("MY LOCAL SECRET\n", Text(repo.Dir, "secret.env"));
    }

    [Fact(Skip = "safety-audit: a merge overwrites an ignored local file that the merged branch tracks (git's default; --no-overwrite-ignore refuses)")]
    public void Merge_keeps_an_ignored_local_file_that_the_merged_branch_tracks()
    {
        using var repo = IgnoredFileInTheWay();
        var op = new MergeOperation(repo.Git);

        var check = op.Check(new MergeRequest("feature"));
        if (check.CanMerge)
            op.Run(new MergeRequest("feature"), check);

        Assert.Equal("MY LOCAL\n", Text(repo.Dir, "local.cfg"));
        Assert.False(repo.Merging);
    }

    [Fact(Skip = "safety-audit: a switch overwrites an ignored local file that the target branch tracks (git's default; --no-overwrite-ignore refuses)")]
    public void Switch_keeps_an_ignored_local_file_that_the_target_branch_tracks()
    {
        using var repo = IgnoredFileInTheWay();
        var op = new SwitchOperation(repo.Git);

        var check = op.Check(new SwitchRequest("feature"));
        if (check.CanRun)
            op.Run(new SwitchRequest("feature"), check);

        Assert.Equal("MY LOCAL\n", Text(repo.Dir, "local.cfg"));
    }

    // ---- 11. branch.<name>.mergeoptions ----

    [Fact(Skip = "safety-audit: with --no-commit in branch.main.mergeoptions the merge is reported as 'already up to date' and the repo is left mid-merge")]
    public void Merge_with_no_commit_in_the_branchs_mergeoptions_is_not_left_mid_merge()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Run("config", "branch.main.mergeoptions", "--no-commit");
        var op = new MergeOperation(repo.Git);

        var outcome = op.Run(new MergeRequest("feature"), op.Check(new MergeRequest("feature")));

        // git exits 0 with MERGE_HEAD set and HEAD unmoved; Verify reads only HEAD.
        Assert.False(repo.Merging, "MERGE_HEAD is left behind");
        Assert.True(repo.IsClean, repo.Status());
        Assert.NotEqual(MergeKind.UpToDate, outcome.Kind);
    }

    [Fact(Skip = "safety-audit: with --squash in branch.main.mergeoptions the merge is reported as 'already up to date' and its changes are left staged")]
    public void Merge_with_squash_in_the_branchs_mergeoptions_leaves_nothing_staged()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("config", "branch.main.mergeoptions", "--squash");
        var op = new MergeOperation(repo.Git);

        var outcome = op.Run(new MergeRequest("feature"), op.Check(new MergeRequest("feature")));

        Assert.True(repo.IsClean, repo.Status());
        Assert.NotEqual(MergeKind.UpToDate, outcome.Kind);
    }

    [Fact(Skip = "safety-audit: with --no-commit in branch.main.mergeoptions a stash-merge-pop fails and leaves the repo mid-merge")]
    public void Stash_merge_with_no_commit_in_the_branchs_mergeoptions_is_not_left_mid_merge()
    {
        using var repo = TempRepo.Diverged(conflict: false);
        repo.Run("config", "branch.main.mergeoptions", "--no-commit");
        repo.Write("app.txt", "my edit\n");
        var op = new StashMergeOperation(repo.Git);
        var check = op.Check(new StashMergeRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);

        op.Run(new StashMergeRequest("feature"), check);

        Assert.False(repo.Merging, "MERGE_HEAD is left behind");
    }

    // ---- 12. interactive-rebase and cherry-pick stops ----

    [Fact]
    public void Skipping_a_commit_does_not_fold_its_fixup_into_the_base_branchs_commit()
    {
        using var repo = FirstCommitConflicts();
        var mainTip = repo.Run("rev-parse", "main");
        var op = new RebaseOperation(repo.Git);
        var rows = op.Check(new RebaseRequest("main")).Rows;
        var plan = new List<RebaseRow> { rows[0], rows[1] with { Action = RebaseAction.Fixup }, rows[2] };
        var check = op.Check(new RebaseRequest("main", plan));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(new RebaseRequest("main", plan), check).Kind);
        var resume = new ResumeSequenceOperation(repo.Git);

        var skipCheck = resume.Check(new ResumeRequest(ResumeAction.Skip));
        if (skipCheck.CanRun)
            resume.Run(new ResumeRequest(ResumeAction.Skip), skipCheck);

        // After the skip git folds "Add extra" into HEAD, which is main's own tip: feature then holds a rewritten copy of it.
        var stillRebasing = Directory.Exists(Path.Combine(repo.Dir, ".git", "rebase-merge"));
        var onMain = repo.Git.Run("merge-base", "--is-ancestor", mainTip, "HEAD").Success;
        Assert.True(stillRebasing || onMain, "feature is no longer on top of main: main's tip commit was rewritten into it");
    }

    [Fact]
    public void A_continue_that_git_refuses_says_why()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var op = new RebaseOperation(repo.Git);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(new RebaseRequest("main"), op.Check(new RebaseRequest("main"))).Kind);
        repo.Write("app.txt", "resolved\n");
        repo.Write("f1.txt", "another edit while resolving\n");
        var resume = new ResumeSequenceOperation(repo.Git);

        var check = resume.Check(new ResumeRequest(ResumeAction.Continue));
        var said = check.Refusal ?? "";
        if (check.CanRun)
            said = resume.Run(new ResumeRequest(ResumeAction.Continue), check).Message;

        // git refuses the continue (wording varies with the git version: "You must edit all merge conflicts…" on 2.56,
        // "You have unstaged changes" on others); the pause now repeats git's own reason instead of the bare hash.
        Assert.StartsWith("rebase paused at ", said);
        var parts = said.Split(';', 2);
        Assert.True(parts.Length == 2 && parts[1].Trim().Length > 0, $"the pause says nothing about why: {said}");
    }

    [Fact]
    public void A_cherry_pick_whose_commit_cannot_be_signed_says_why()
    {
        using var repo = SequenceRepos.Picks();
        repo.Run("config", "commit.gpgsign", "true");
        repo.Run("config", "gpg.program", "false");
        var picks = repo.Run("rev-list", "--reverse", "main..feature").Split('\n');
        var op = new CherryPickOperation(repo.Git);
        var check = op.Check(new CherryPickRequest(picks));
        Assert.True(check.CanRun, check.Refusal);

        var outcome = op.Run(new CherryPickRequest(picks), check);

        // Either the pick is aborted and reported as failed, or the pause says that signing failed.
        Assert.Contains("gpg", outcome.Message);
    }

    [Fact]
    public void A_real_cherry_pick_conflict_is_still_reported_as_a_cherry_pick()
    {
        using var repo = SequenceRepos.PickConflict();
        var picks = repo.Run("rev-list", "--reverse", "main..feature").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var op = new CherryPickOperation(repo.Git);
        var check = op.Check(new CherryPickRequest(picks));
        Assert.True(check.CanRun, check.Refusal);

        var outcome = op.Run(new CherryPickRequest(picks), check);

        // The pause holds CHERRY_PICK_HEAD and no rebase state, so it stays a cherry-pick whichever check reads first.
        Assert.Equal(SequenceOutcomeKind.Paused, outcome.Kind);
        Assert.StartsWith("cherry-pick paused at", outcome.Message);
        var gitDir = Path.Combine(repo.Dir, ".git");
        Assert.Equal("cherry-pick", RepoState.InProgress(args => repo.Git.Run(args), gitDir));
    }

    [Fact]
    public void Skipping_a_stopped_fixup_row_still_works()
    {
        using var repo = SequenceRepos.FeatureConflict();
        var op = new RebaseOperation(repo.Git);
        var rows = op.Check(new RebaseRequest("main")).Rows;
        var plan = new List<RebaseRow> { rows[0], rows[1], rows[2] with { Action = RebaseAction.Fixup }, rows[3] };
        var check = op.Check(new RebaseRequest("main", plan));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(new RebaseRequest("main", plan), check).Kind);
        var resume = new ResumeSequenceOperation(repo.Git);

        // The stop is the fixup row itself (its commit changes app.txt, which conflicts): the skip drops it, nothing folds.
        var skipCheck = resume.Check(new ResumeRequest(ResumeAction.Skip));
        Assert.True(skipCheck.CanRun, skipCheck.Refusal);
        var skipped = resume.Run(new ResumeRequest(ResumeAction.Skip), skipCheck);

        Assert.Equal(SequenceOutcomeKind.Done, skipped.Kind);
        Assert.DoesNotContain("Add password check", repo.Run("log", "--format=%s", "main..HEAD"));
    }

    [Fact]
    public void Skipping_is_refused_when_a_fold_follows_after_dropped_rows()
    {
        using var repo = FirstCommitConflicts();
        var op = new RebaseOperation(repo.Git);
        var rows = op.Check(new RebaseRequest("main")).Rows;
        var plan = new List<RebaseRow> { rows[0], rows[1] with { Action = RebaseAction.Drop }, rows[2] with { Action = RebaseAction.Fixup } };
        var check = op.Check(new RebaseRequest("main", plan));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(new RebaseRequest("main", plan), check).Kind);
        var resume = new ResumeSequenceOperation(repo.Git);

        // The dropped row makes no commit, so skipping the stop would fold the fixup into main's tip.
        var skipCheck = resume.Check(new ResumeRequest(ResumeAction.Skip));

        Assert.False(skipCheck.CanRun);
        Assert.Contains("folded into", skipCheck.Refusal);
    }

    [Fact]
    public void Skipping_a_pick_whose_next_row_is_also_a_pick_still_works()
    {
        using var repo = FirstCommitConflicts();
        var op = new RebaseOperation(repo.Git);
        var check = op.Check(new RebaseRequest("main"));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(SequenceOutcomeKind.Paused, op.Run(new RebaseRequest("main"), check).Kind);
        var resume = new ResumeSequenceOperation(repo.Git);

        var skipCheck = resume.Check(new ResumeRequest(ResumeAction.Skip));
        Assert.True(skipCheck.CanRun, skipCheck.Refusal);
        var skipped = resume.Run(new ResumeRequest(ResumeAction.Skip), skipCheck);

        // "Add extra" and "Add last" apply cleanly once the conflicting first commit is gone.
        Assert.Equal(SequenceOutcomeKind.Done, skipped.Kind);
    }

    // ---- 13. a branch's config ----

    [Fact(Skip = "safety-audit: undo of a branch deletion does not bring back the branch's upstream (branch.<name>.remote and .merge)")]
    public void Undo_of_a_branch_deletion_brings_back_its_upstream()
    {
        using var repo = TempRepo.FastForwardable();
        repo.Run("merge", "-q", "feature");
        repo.Run("config", "branch.feature.remote", "origin");
        repo.Run("config", "branch.feature.merge", "refs/heads/feature");
        var op = new DeleteBranchOperation(repo.Git);
        var check = op.Check(new DeleteBranchRequest("feature"));
        Assert.True(check.CanRun, check.Refusal);
        Assert.Equal(BranchChangeKind.Done, op.Run(new DeleteBranchRequest("feature", check.Tip), check).Kind);

        Assert.Equal(UndoOutcomeKind.Undone, Undo(repo.Git).Kind);

        Assert.Equal("origin", repo.Git.Run("config", "branch.feature.remote").StdOut.Trim());
        Assert.Equal("refs/heads/feature", repo.Git.Run("config", "branch.feature.merge").StdOut.Trim());
    }

    [Fact(Skip = "safety-audit: a branch merged into HEAD but ahead of its upstream cannot be deleted, even with Force (git branch -d checks the upstream)")]
    public void A_merged_branch_that_is_ahead_of_its_upstream_can_be_deleted_with_force()
    {
        using var repo = MergedButAheadOfUpstream();
        var op = new DeleteBranchOperation(repo.Git);
        var check = op.Check(new DeleteBranchRequest("feature", null, true));
        Assert.True(check.CanRun, check.Refusal);

        var outcome = op.Run(new DeleteBranchRequest("feature", check.Tip, true), check);

        Assert.Equal(BranchChangeKind.Done, outcome.Kind);
    }

    // ---- 14. a path that is staged as deleted and also untracked ----

    [Fact(Skip = "safety-audit: a file that is staged as deleted and kept as an untracked file is stashed, and then cannot be popped or un-stashed")]
    public void A_stash_of_a_file_that_is_staged_as_deleted_and_untracked_can_be_popped()
    {
        using var repo = TempRepo.Create();
        repo.Commit("conf.txt", "committed\n", "Add conf");
        repo.Run("rm", "-q", "--cached", "conf.txt");
        repo.Write("conf.txt", "MY LOCAL EDIT\n");
        var op = new StashOperation(repo.Git);
        var check = op.Check(new StashRequest(null));
        if (!check.CanRun)
            return;
        Assert.Equal(StashKind.Stashed, op.Run(new StashRequest(null), check).Kind);

        var popped = Pop(repo.Git);

        // git stash restores HEAD's conf.txt, so its own pop finds the file in the way ("already exists, no checkout").
        Assert.Equal(PopKind.Popped, popped.Kind);
        Assert.Equal("MY LOCAL EDIT\n", Text(repo.Dir, "conf.txt"));
    }
}
