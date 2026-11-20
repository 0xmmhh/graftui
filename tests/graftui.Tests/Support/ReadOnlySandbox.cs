using graftui.Git;

namespace graftui.Tests;

/// <summary>One sandbox per test class whose cases only read it.</summary>
public sealed class ReadOnlySandbox : IDisposable
{
    private readonly string _dir = TempRepo.NewSandboxDir();

    public ReadOnlySandbox()
    {
        try
        {
            Sandbox.RebuildAt(_dir, TextWriter.Null);
        }
        catch
        {
            TempRepo.DeleteSandboxRoot(_dir);
            throw;
        }
        Git = new GitClient(_dir);
        Reader = new RepoReader(Git);
    }

    public GitClient Git { get; }

    public RepoReader Reader { get; }

    public void Dispose() => TempRepo.DeleteSandboxRoot(_dir);
}
