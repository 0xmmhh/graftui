using graftui.Ui.Views;

namespace graftui.Tests;

public class ChangesViewTests
{
    [Fact]
    public void The_file_letter_sits_under_the_first_branch_name()
    {
        Assert.Equal(BranchesView.NameX, ChangesView.LetterX);
    }

    [Fact]
    public void The_path_starts_one_column_after_the_letter_and_runs_to_the_last_inner_column()
    {
        const int innerW = 20;

        Assert.Equal(ChangesView.LetterX + 2, ChangesView.PathX);
        Assert.Equal(innerW, ChangesView.PathX + ChangesView.PathWidth(innerW) - 1);
    }
}
