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
}
