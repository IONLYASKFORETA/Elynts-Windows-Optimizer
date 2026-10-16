using Elynt.Core.Apps;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>Search, updates, uninstalls and app lists, driven through the engine with an in-memory winget.</summary>
public class AppManagementTests
{
    private readonly FakePackageManager _winget = new();

    private Engine NewEngine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), Packages: _winget),
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test");

    [Fact]
    public void Search_covers_all_of_winget_and_marks_results_outside_the_catalogue_not_reviewed()
    {
        _winget.Community.Add(new("Valve.Steam", "Steam", "2.10.91.91"));
        _winget.Community.Add(new("Some.SteamTool", "Steam Tool", "1.0"));
        _winget.Community.Add(new("VideoLAN.VLC", "VLC media player", "3.0.21"));

        var search = NewEngine().SearchApps("steam");

        Assert.True(search.Succeeded);
        Assert.Equal(
            [("Valve.Steam", true), ("Some.SteamTool", false)],
            search.Results.Select(r => (r.Id, r.Reviewed)));
        Assert.Equal("Game launchers", search.Results[0].Group);
        Assert.Null(search.Results[1].Group);
        Assert.Equal("1.0", search.Results[1].Version);
        Assert.Equal(["winget"], _winget.Searches.Select(s => s.Source).Distinct());
    }

    [Fact]
    public void Search_while_winget_is_missing_searches_the_catalogue_only_and_says_so()
    {
        _winget.Available = false;

        var search = NewEngine().SearchApps("steam");

        Assert.False(search.Succeeded);
        Assert.Contains("isn't set up", search.Message);
        Assert.Equal(["Valve.Steam"], search.Results.Select(r => r.Id));
        Assert.Equal(0, _winget.Bootstraps);
    }

    [Fact]
    public void Installed_apps_list_shows_available_updates_and_which_are_reviewed()
    {
        _winget.Installed.Add(new("Valve.Steam", "Steam", "2.10.91.91", "2.10.92.0", "winget"));
        _winget.Installed.Add(new("Some.Tool", "Some Tool", "1.0", null, "winget"));
        _winget.Installed.Add(new(@"ARP\Machine\X64\OldThing", "Old Thing", "3.1", null, null));

        var installed = NewEngine().ListInstalledApps();

        Assert.True(installed.Succeeded);
        Assert.Equal(
            [("Valve.Steam", "Steam", true, "2.10.92.0"), ("Some.Tool", "Some Tool", false, null), (@"ARP\Machine\X64\OldThing", "Old Thing", false, null)],
            installed.Apps.Select(a => (a.Id, a.Name, a.Reviewed, a.UpdateVersion)));
        Assert.Equal([true, false, false], installed.Apps.Select(a => a.CanUpdate));
        Assert.Equal("1 update available.", installed.Message);
    }

    [Fact]
    public void An_update_from_outside_the_community_source_is_not_offered()
    {
        _winget.Installed.Add(new("Some.StoreApp", "Store App", "1.0", "2.0", "msstore"));

        var app = NewEngine().ListInstalledApps().Apps.Single();

        Assert.False(app.CanUpdate);
        Assert.Null(app.UpdateVersion);
    }

    [Fact]
    public void Update_one_upgrades_just_that_app_from_the_community_source_with_progress()
    {
        _winget.Installed.Add(new("Valve.Steam", "Steam", "1", "2", "winget"));
        _winget.Installed.Add(new("VideoLAN.VLC", "VLC", "1", "2", "winget"));
        var progress = new ProgressLog();

        var report = NewEngine().UpdateApps(["Valve.Steam"], progress);

        Assert.True(report.Succeeded);
        Assert.Equal("1 of 1 updated.", report.Message);
        Assert.Equal([("Valve.Steam", "winget")], _winget.Upgrades);
        Assert.Equal(["1/1 Valve.Steam Updating", "1/1 Valve.Steam Updating 50%", "1/1 Valve.Steam Updated"], progress.Lines);
        Assert.Equal("2", _winget.Installed.Single(p => p.Id == "Valve.Steam").Version);
    }

    [Fact]
    public void Update_all_upgrades_every_app_with_an_update_one_at_a_time()
    {
        _winget.Installed.Add(new("Valve.Steam", "Steam", "1", "2", "winget"));
        _winget.Installed.Add(new("Some.Tool", "Some Tool", "1", "1.5", "winget"));
        _winget.Installed.Add(new("VideoLAN.VLC", "VLC", "3", null, "winget"));
        _winget.Installed.Add(new("Some.StoreApp", "Store App", "1.0", "2.0", "msstore"));

        var report = NewEngine().UpdateAllApps();

        Assert.True(report.Succeeded);
        Assert.Equal(["Valve.Steam", "Some.Tool"], _winget.Upgrades.Select(u => u.Id));
        Assert.Equal(1, _winget.MostAtOnce);
        Assert.Equal("2 of 2 updated.", report.Message);
    }

    [Fact]
    public void Update_all_with_nothing_to_update_says_everything_is_up_to_date()
    {
        _winget.Installed.Add(new("VideoLAN.VLC", "VLC", "3", null, "winget"));

        var report = NewEngine().UpdateAllApps();

        Assert.True(report.Succeeded);
        Assert.Equal("Every app is up to date.", report.Message);
        Assert.Empty(_winget.Upgrades);
    }

    [Fact]
    public void A_failed_update_is_reported_and_the_rest_still_run()
    {
        _winget.Installed.Add(new("Valve.Steam", "Steam", "1", "2", "winget"));
        _winget.Installed.Add(new("Some.Tool", "Some Tool", "1", "1.5", "winget"));
        _winget.Results["Valve.Steam"] = new(PackageInstallStatus.Failed, "Installer exited with code 1603.");

        var report = NewEngine().UpdateAllApps();

        Assert.False(report.Succeeded);
        Assert.Equal("1 of 2 updated. Steam didn't update.", report.Message);
        Assert.Equal([InstallStage.Failed, InstallStage.Updated], report.Apps.Select(a => a.Stage));
        Assert.Contains("1603", report.Apps[0].Message);
    }

    [Fact]
    public void Uninstall_removes_the_app_and_reports_it()
    {
        _winget.Installed.Add(new("Some.Tool", "Some Tool", "1", null, "winget"));
        _winget.Installed.Add(new("VideoLAN.VLC", "VLC", "3", null, "winget"));
        var progress = new ProgressLog();

        var report = NewEngine().UninstallApps(["Some.Tool"], progress);

        Assert.True(report.Succeeded);
        Assert.Equal("1 of 1 uninstalled.", report.Message);
        Assert.Equal(["VideoLAN.VLC"], NewEngine().ListInstalledApps().Apps.Select(a => a.Id));
        Assert.Equal(["1/1 Some.Tool Uninstalling", "1/1 Some.Tool Uninstalling 50%", "1/1 Some.Tool Uninstalled"], progress.Lines);
    }

    [Fact]
    public void Uninstalling_an_app_that_is_not_installed_is_refused_and_nothing_is_removed()
    {
        _winget.Installed.Add(new("VideoLAN.VLC", "VLC", "3", null, "winget"));

        var report = NewEngine().UninstallApps(["VideoLAN.VLC", "Not.There"]);

        Assert.False(report.Succeeded);
        Assert.Contains("Not.There", report.Message);
        Assert.Empty(_winget.Uninstalls);
    }

    [Fact]
    public void Uninstall_that_needs_a_restart_says_so()
    {
        _winget.Installed.Add(new("Some.Tool", "Some Tool", "1", null, "winget"));
        _winget.Results["Some.Tool"] = new(PackageInstallStatus.Installed, RebootRequired: true);

        var report = NewEngine().UninstallApps(["Some.Tool"]);

        Assert.Equal("1 of 1 uninstalled. Restart the PC to finish.", report.Message);
    }

    [Fact]
    public void A_not_reviewed_app_from_search_installs_only_once_the_user_accepts_it_is_not_reviewed()
    {
        var engine = NewEngine();

        var refused = engine.InstallApps(["Valve.Steam", "Some.SteamTool"], allRuntimes: false);
        var accepted = engine.InstallApps(["Valve.Steam", "Some.SteamTool"], allRuntimes: false, allowNotReviewed: true);

        Assert.False(refused.Succeeded);
        Assert.Contains("Some.SteamTool", refused.Message);
        Assert.True(accepted.Succeeded);
        Assert.Equal([("Valve.Steam", "winget"), ("Some.SteamTool", "winget")], _winget.Installs);
        Assert.Equal(["Steam", "Some.SteamTool"], accepted.Apps.Select(a => a.Name));
    }

    [Fact]
    public void A_saved_app_list_reads_back_and_reinstalls_on_another_PC()
    {
        var text = AppList.Write(
        [
            new("Valve.Steam", "Steam"),
            new("Some.SteamTool", "Steam Tool"),
            new("Valve.Steam", "Steam"),
        ]);

        var anotherPc = new FakePackageManager();
        var engine = new Engine(
            Catalogue.Default,
            new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), Packages: anotherPc),
            new ChangeJournal(new InMemoryJournalStore()),
            "S-1-5-21-other");
        var list = AppList.Read(text);
        var report = engine.InstallApps(list.Apps.Select(a => a.Id).ToList(), allRuntimes: false, allowNotReviewed: true);

        Assert.True(list.Succeeded);
        Assert.Equal([("Valve.Steam", "Steam"), ("Some.SteamTool", "Steam Tool")], list.Apps.Select(a => (a.Id, a.Name)));
        Assert.True(report.Succeeded);
        Assert.Equal(["Valve.Steam", "Some.SteamTool"], anotherPc.Installs.Select(i => i.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"format\":\"something-else\",\"version\":1,\"apps\":[]}")]
    [InlineData("{\"format\":\"elynt-app-list\",\"version\":99,\"apps\":[]}")]
    public void A_file_that_is_not_an_app_list_is_refused(string text)
    {
        var list = AppList.Read(text);

        Assert.False(list.Succeeded);
        Assert.Empty(list.Apps);
        Assert.False(string.IsNullOrWhiteSpace(list.Message));
    }

    [Fact]
    public void Entries_that_are_not_winget_ids_are_skipped_and_named()
    {
        var text = """
            {"format":"elynt-app-list","version":1,"apps":[
              {"id":"Valve.Steam","name":"Steam"},
              {"id":"bad id; rm -rf","name":"Evil"},
              {"id":"","name":"Empty"}
            ]}
            """;

        var list = AppList.Read(text);

        Assert.True(list.Succeeded);
        Assert.Equal(["Valve.Steam"], list.Apps.Select(a => a.Id));
        Assert.Contains("Evil", list.Message);
    }

    [Fact]
    public void Apps_winget_only_knows_from_installed_programs_are_left_out_of_a_saved_list()
    {
        var text = AppList.Write([new("Valve.Steam", "Steam"), new(@"ARP\Machine\X64\OldThing", "Old Thing")]);

        Assert.Equal(["Valve.Steam"], AppList.Read(text).Apps.Select(a => a.Id));
    }

    private sealed class ProgressLog : IProgress<InstallProgress>
    {
        public List<string> Lines { get; } = [];

        public void Report(InstallProgress value) => Lines.Add(
            $"{value.Position}/{value.Total} {value.AppId} {value.Stage}" +
            (value.Percent is { } fraction ? $" {(int)Math.Round(fraction * 100)}%" : ""));
    }
}
