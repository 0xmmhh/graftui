using graftui.Git;

namespace graftui.Tests;

public class GitVersionTests
{
    [Theory]
    [InlineData("git version 2.56.0\n", 2, 56)]
    [InlineData("git version 2.39.5 (Apple Git-154)", 2, 39)]
    [InlineData("git version 2.45.1.windows.1", 2, 45)]
    [InlineData("git version 2.38.0rc1", 2, 38)]
    public void The_major_and_minor_numbers_are_read(string output, int major, int minor)
    {
        Assert.Equal(new Version(major, minor), GitClient.ParseVersion(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("git version")]
    [InlineData("git version 2")]
    [InlineData("git version x.y.z")]
    public void Anything_else_is_not_a_version(string output)
    {
        Assert.Null(GitClient.ParseVersion(output));
    }

    [Fact]
    public void Git_2_37_is_older_than_the_forecast_needs()
    {
        var version = GitClient.ParseVersion("git version 2.37.9");

        Assert.NotNull(version);
        Assert.True(version < MergeForecaster.Minimum);
    }

    [Fact]
    public void The_installed_git_reports_a_version_the_forecast_accepts()
    {
        var version = new GitClient(Path.GetTempPath()).GetVersion();

        Assert.NotNull(version);
        Assert.True(version >= MergeForecaster.Minimum, version?.ToString());
    }
}
