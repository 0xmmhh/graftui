using graftui.Git;

namespace graftui.Tests;

public class GitCommandLineTests
{
    [Fact]
    public void Render_quotes_the_arguments_that_need_it_and_leaves_the_rest_bare()
    {
        Assert.Equal("git stash store -m \"graftui: discarded README.md\" 0123456", GitCommandLine.Render(["stash", "store", "-m", "graftui: discarded README.md", "0123456"]));
        Assert.Equal("git restore --staged -- \"new name.txt\" old.txt", GitCommandLine.Render(["restore", "--staged", "--", "new name.txt", "old.txt"]));
        Assert.Equal("git update-ref -m \"\" refs/graftui/undo/000001", GitCommandLine.Render(["update-ref", "-m", "", "refs/graftui/undo/000001"]));
    }

    [Fact]
    public void Render_of_no_arguments_is_just_git()
    {
        Assert.Equal("git", GitCommandLine.Render([]));
    }
}
