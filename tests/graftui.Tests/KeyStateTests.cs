using graftui.Model;
using graftui.Ui.Text;

namespace graftui.Tests;

public class KeyStateTests
{
    private static Branch Other(string name = "feature/picks") => new(name, "abc1234", "abc1234", IsCurrent: false);

    private static RepoSnapshot Snap(string current, params (string Name, int Ahead, int Behind)[] counts)
    {
        var dict = counts.ToDictionary(c => c.Name, c => (c.Ahead, c.Behind));
        return new RepoSnapshot(current, "head1234", true, [new Branch(current, "head1234", "head123", true), Other()], dict);
    }

    [Fact]
    public void Rebase_is_offered_when_the_two_branches_have_diverged()
    {
        var s = Snap("main", ("feature/picks", 3, 1));

        Assert.True(BranchesKeyState.RebaseOffered(Other(), s));
    }

    [Fact]
    public void Rebase_is_not_offered_when_the_current_branch_has_no_commits_of_its_own()
    {
        // Behind 0: HEAD has nothing the selected branch lacks, so there is nothing to rebase.
        var s = Snap("main", ("feature/picks", 2, 0));

        Assert.False(BranchesKeyState.RebaseOffered(Other(), s));
    }

    [Fact]
    public void Rebase_is_not_offered_when_the_selected_branch_is_already_on_top()
    {
        // Ahead 0: the selected branch is part of HEAD already.
        var s = Snap("main", ("feature/picks", 0, 2));

        Assert.False(BranchesKeyState.RebaseOffered(Other(), s));
    }

    [Fact]
    public void Rebase_is_offered_when_the_count_is_unknown()
    {
        var s = Snap("main");

        Assert.True(BranchesKeyState.RebaseOffered(Other(), s));
    }

    [Fact]
    public void Rebase_is_not_offered_for_the_current_branch()
    {
        var s = Snap("main");

        Assert.False(BranchesKeyState.RebaseOffered(new Branch("main", "x", "x", IsCurrent: true), s));
    }

    [Fact]
    public void Rebase_is_not_offered_on_a_detached_head()
    {
        var s = new RepoSnapshot("", "head1234", true, [Other()], new Dictionary<string, (int Ahead, int Behind)>());

        Assert.False(BranchesKeyState.RebaseOffered(Other(), s));
    }

    [Fact]
    public void Rebase_is_not_offered_without_a_selection()
    {
        Assert.False(BranchesKeyState.RebaseOffered(null, Snap("main")));
    }

    [Fact]
    public void For_sets_CanRebase_from_the_selected_branch()
    {
        var s = Snap("main", ("feature/picks", 3, 1));

        Assert.True(BranchesKeyState.For(s, Other()).CanRebase);
        Assert.False(BranchesKeyState.For(s, null).CanRebase);
    }
}
