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

        var results = winget.Search("Valve.Steam", "winget");

        Assert.Contains(results, p => p.Id == "Valve.Steam" && p.Name.Length > 0 && p.Version.Length > 0);
    }

    [Fact]
    public void Installed_apps_list_without_error()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows.");
        var winget = new WingetPackageManager(user: null);
        Assert.SkipUnless(winget.IsAvailable(), "Needs winget.");

        var installed = winget.ListInstalled();

        Assert.All(installed, p => Assert.False(string.IsNullOrEmpty(p.Id)));
    }
}
