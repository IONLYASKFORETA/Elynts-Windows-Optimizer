namespace Elynt.Windows.Tests;

/// <summary>The real winget adapter's read-only commands, where winget exists (CI's Windows runner, a dev PC).</summary>
public sealed class RealWingetSearchTests
{
    [Fact]
    public void Search_finds_a_catalogue_app_in_the_community_source()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows.");
        var winget = new WingetPackageManager(user: null);
        Assert.SkipUnless(winget.IsAvailable(), "Needs winget.");

        var results = RetryingInternalError(() => winget.Search("Valve.Steam", "winget"));

        Assert.Contains(results, p => p.Id == "Valve.Steam" && p.Name.Length > 0 && p.Version.Length > 0);
    }

    [Fact]
    public void Installed_apps_list_without_error()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows.");
        var winget = new WingetPackageManager(user: null);
        Assert.SkipUnless(winget.IsAvailable(), "Needs winget.");

        var installed = RetryingInternalError(winget.ListInstalled);

        Assert.All(installed, p => Assert.False(string.IsNullOrEmpty(p.Id)));
    }

    /// <summary>
    /// winget failing inside itself (APPINSTALLER_CLI_ERROR_INTERNAL_ERROR). It hits GitHub's runners now and then and
    /// the next run passes, so it's tried once more and then skipped rather than failing the build.
    /// </summary>
    private const string InternalError = "0x8A150001";

    private static T RetryingInternalError<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(InternalError, StringComparison.Ordinal))
        {
            try
            {
                return call();
            }
            catch (InvalidOperationException again) when (again.Message.Contains(InternalError, StringComparison.Ordinal))
            {
                Assert.Skip($"winget failed inside itself twice on this machine: {again.Message}");
                return default!;
            }
        }
    }
}
