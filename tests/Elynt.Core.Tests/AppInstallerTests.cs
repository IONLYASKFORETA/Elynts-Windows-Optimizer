using Elynt.Core.Apps;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

public class AppInstallerTests
{
    private readonly FakePackageManager _winget = new();

    private Engine NewEngine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), _winget),
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test");

    [Fact]
    public void The_catalogue_lists_about_100_apps_in_the_seven_groups_each_described()
    {
        var apps = NewEngine().ListApps();

        Assert.Equal(
            ["Browsers", "Game launchers", "Chat", "Media", "Utilities", "Developer tools", "Runtimes"],
            apps.Select(a => a.Group).Distinct());
        Assert.InRange(apps.Count, 90, 120);
        Assert.All(apps, app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Name));
            Assert.False(string.IsNullOrWhiteSpace(app.Description));
            Assert.DoesNotContain('\n', app.Description);
        });
        Assert.Equal(apps.Count, apps.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(apps, a => a.Id == "Valve.Steam" && a.Group == "Game launchers");
        Assert.Contains(apps, a => a.Id == "Microsoft.VCRedist.2015+.x64" && a.Group == "Runtimes");
    }

    [Fact]
    public void Ticked_apps_install_one_at_a_time_from_the_community_source_with_progress()
    {
        var progress = new ProgressLog();

        var report = NewEngine().InstallApps(["Discord.Discord", "Valve.Steam"], allRuntimes: false, progress);

        Assert.True(report.Succeeded);
        Assert.Equal([("Discord.Discord", "winget"), ("Valve.Steam", "winget")], _winget.Installs);
        Assert.Equal(1, _winget.MostAtOnce);
        Assert.Equal(
            [
                "1/2 Discord.Discord Installing",
                "1/2 Discord.Discord Installing 50%",
                "1/2 Discord.Discord Installed",
                "2/2 Valve.Steam Installing",
                "2/2 Valve.Steam Installing 50%",
                "2/2 Valve.Steam Installed",
            ],
            progress.Lines);
        Assert.Equal(
            [("Discord.Discord", InstallStage.Installed), ("Valve.Steam", InstallStage.Installed)],
            report.Apps.Select(a => (a.AppId, a.Stage)));
    }

    [Fact]
    public void Install_all_runtimes_queues_every_Visual_CPP_DirectX_and_dotNET_runtime_after_the_ticked_apps_once_each()
    {
        var report = NewEngine().InstallApps(["Valve.Steam", "Microsoft.DirectX"], allRuntimes: true);

        Assert.True(report.Succeeded);
        var installed = _winget.Installs.Select(i => i.Id).ToList();
        Assert.Equal(["Valve.Steam", "Microsoft.DirectX"], installed.Take(2));
        Assert.Equal(installed.Count, installed.Distinct().Count());
        Assert.Contains("Microsoft.VCRedist.2015+.x64", installed);
        Assert.Contains("Microsoft.VCRedist.2015+.x86", installed);
        Assert.Contains("Microsoft.VCRedist.2005.x86", installed);
        Assert.Contains("Microsoft.DotNet.DesktopRuntime.8", installed);
        Assert.DoesNotContain("EclipseAdoptium.Temurin.21.JRE", installed);
        Assert.Equal(2 + 15, installed.Count);
    }

    [Fact]
    public void An_app_outside_the_catalogue_is_refused_and_nothing_is_installed()
    {
        var report = NewEngine().InstallApps(["Valve.Steam", "Some.Unreviewed.App"], allRuntimes: false);

        Assert.False(report.Succeeded);
        Assert.Contains("Some.Unreviewed.App", report.Message);
        Assert.Empty(report.Apps);
        Assert.Empty(_winget.Installs);
    }

    [Fact]
    public void Nothing_ticked_installs_nothing()
    {
        var report = NewEngine().InstallApps([], allRuntimes: false);

        Assert.False(report.Succeeded);
        Assert.Equal("Nothing is ticked, so there is nothing to install.", report.Message);
        Assert.Empty(_winget.Installs);
    }

    [Fact]
    public void A_failed_install_is_reported_and_the_rest_of_the_queue_still_runs()
    {
        _winget.Results["Discord.Discord"] = new(PackageInstallStatus.Failed, "Installer exited with code 1603.");
        _winget.Crashes.Add("Spotify.Spotify");
        _winget.Results["Valve.Steam"] = new(PackageInstallStatus.AlreadyInstalled);
        var progress = new ProgressLog();

        var report = NewEngine().InstallApps(["Discord.Discord", "Spotify.Spotify", "Valve.Steam", "VideoLAN.VLC"], allRuntimes: false, progress);

        Assert.False(report.Succeeded);
        Assert.Equal("2 of 4 installed. Discord and Spotify didn't install.", report.Message);
        Assert.Equal(
            [
                ("Discord.Discord", InstallStage.Failed),
                ("Spotify.Spotify", InstallStage.Failed),
                ("Valve.Steam", InstallStage.AlreadyInstalled),
                ("VideoLAN.VLC", InstallStage.Installed),
            ],
            report.Apps.Select(a => (a.AppId, a.Stage)));
        Assert.Contains("1603", report.Apps[0].Message);
        Assert.Contains("1/4 Discord.Discord Failed", progress.Lines);
        Assert.Contains("3/4 Valve.Steam AlreadyInstalled", progress.Lines);
        Assert.Equal(4, _winget.Installs.Count);
    }

    [Fact]
    public void An_install_that_needs_a_reboot_says_so()
    {
        _winget.Results["Microsoft.DirectX"] = new(PackageInstallStatus.Installed, RebootRequired: true);

        var report = NewEngine().InstallApps(["Microsoft.DirectX"], allRuntimes: false);

        Assert.True(report.Succeeded);
        Assert.True(report.Apps.Single().RebootRequired);
        Assert.Equal("1 of 1 installed. Restart the PC to finish.", report.Message);
    }

    [Fact]
    public void Missing_winget_is_set_up_first_then_the_queue_runs()
    {
        _winget.Available = false;
        var progress = new ProgressLog();

        var report = NewEngine().InstallApps(["Valve.Steam"], allRuntimes: false, progress);

        Assert.True(report.Succeeded);
        Assert.Equal(1, _winget.Bootstraps);
        Assert.Equal("0/1  SettingUpWinget", progress.Lines[0]);
        Assert.Equal([("Valve.Steam", "winget")], _winget.Installs);
    }

    [Fact]
    public void Winget_already_present_is_not_set_up_again()
    {
        NewEngine().InstallApps(["Valve.Steam"], allRuntimes: false);

        Assert.Equal(0, _winget.Bootstraps);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void When_winget_cannot_be_set_up_nothing_is_installed(bool bootstrapThrows)
    {
        _winget.Available = false;
        _winget.BootstrapWorks = false;
        _winget.BootstrapThrows = bootstrapThrows;

        var report = NewEngine().InstallApps(["Valve.Steam"], allRuntimes: false);

        Assert.False(report.Succeeded);
        Assert.StartsWith("Couldn't set up winget (App Installer), so nothing was installed.", report.Message);
        Assert.Empty(report.Apps);
        Assert.Empty(_winget.Installs);
    }

    /// <summary>Records progress synchronously, in order, as the pipe server does.</summary>
    private sealed class ProgressLog : IProgress<InstallProgress>
    {
        public List<string> Lines { get; } = [];

        public void Report(InstallProgress value) => Lines.Add(
            $"{value.Position}/{value.Total} {value.AppId} {value.Stage}" +
            (value.Percent is { } fraction ? $" {(int)Math.Round(fraction * 100)}%" : ""));
    }
}
