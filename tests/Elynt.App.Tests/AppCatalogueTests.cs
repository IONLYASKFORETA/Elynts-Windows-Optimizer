using Elynt.App.ViewModels;
using Elynt.Core;
using Elynt.Core.Apps;
using Elynt.Core.Journal;
using Elynt.Core.Tests;
using Elynt.Core.Tweaks;

namespace Elynt.App.Tests;

/// <summary>
/// The Apps page's Catalogue tab, fed by the engine's app commands over an in-memory winget: which rows say they are
/// installed or have an update waiting, the filter over them, and what the install button says it will do.
/// </summary>
public class AppCatalogueTests
{
    private readonly FakePackageManager _winget = new();

    private static readonly AppCatalogue Reviewed = new(
    [
        new("Mozilla.Firefox", "Firefox", AppGroups.Browsers, "Mozilla's open-source browser."),
        new("Valve.Steam", "Steam", AppGroups.GameLaunchers, "Valve's PC game store and launcher."),
        new("Discord.Discord", "Discord", AppGroups.Chat, "Voice, video and text chat for gaming communities."),
        new("7zip.7zip", "7-Zip", AppGroups.Utilities, "Opens and creates zip, 7z, rar and other archives."),
        new("Microsoft.VCRedist.2015+.x64", "Visual C++ 2015–2022 (64-bit)", AppGroups.Runtimes, "Runtime most current games need.", InAllRuntimes: true),
        new("Microsoft.DirectX", "DirectX End-User Runtime", AppGroups.Runtimes, "Legacy DirectX files older games still load.", InAllRuntimes: true),
        new("Microsoft.XNARedist", "XNA Framework 4.0", AppGroups.Runtimes, "Runtime for older indie games made with XNA."),
    ]);

    private Engine NewEngine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), Packages: _winget),
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test",
        apps: Reviewed);

    /// <summary>The tab as the window fills it: the engine's catalogue, then what winget lists as installed.</summary>
    private static AppCatalogueViewModel Tab(Engine engine)
    {
        var catalogue = new AppCatalogueViewModel();
        catalogue.Show(engine.ListApps());
        catalogue.MarkInstalled(engine.ListInstalledApps().Apps);
        return catalogue;
    }

    private static AppItemViewModel Row(AppCatalogueViewModel catalogue, string id) => catalogue.Apps.Single(a => a.View.Id == id);

    [Fact]
    public void Rows_say_which_apps_are_installed_and_which_have_an_update_waiting()
    {
        _winget.Installed.Add(new("Valve.Steam", "Steam", "2.10.91.91", null, "winget"));
        _winget.Installed.Add(new("Discord.Discord", "Discord", "1.0.9170", "1.0.9175", "winget"));

        var catalogue = Tab(NewEngine());

        Assert.Equal(
            [("Steam", "Installed", false), ("Discord", "Update available", true), ("Firefox", null, false)],
            new[] { "Valve.Steam", "Discord.Discord", "Mozilla.Firefox" }
                .Select(id => Row(catalogue, id))
                .Select(r => (r.Name, r.InstalledText, r.HasUpdate)));
    }

    [Fact]
    public void An_app_with_an_update_only_outside_wingets_community_source_says_installed()
    {
        // The app updates only from the community source, so another source's newer version isn't one it can install.
        _winget.Installed.Add(new("Mozilla.Firefox", "Mozilla Firefox", "131.0", "132.0.1", "msstore"));

        var row = Row(Tab(NewEngine()), "Mozilla.Firefox");

        Assert.Equal(("Installed", false), (row.InstalledText, row.HasUpdate));
    }

    [Fact]
    public void An_installed_id_that_differs_only_in_case_still_marks_its_row()
    {
        _winget.Installed.Add(new("7ZIP.7ZIP", "7-Zip", "24.08", null, "winget"));

        Assert.Equal("Installed", Row(Tab(NewEngine()), "7zip.7zip").InstalledText);
    }

    [Fact]
    public void A_new_installed_list_marks_the_rows_again()
    {
        _winget.Installed.Add(new("Valve.Steam", "Steam", "2.10.91.91", null, "winget"));
        _winget.Installed.Add(new("Discord.Discord", "Discord", "1.0.9170", "1.0.9175", "winget"));
        var engine = NewEngine();
        var catalogue = Tab(engine);

        engine.UninstallApps(["Valve.Steam"], confirmed: true);
        engine.UpdateApps(["Discord.Discord"]);
        catalogue.MarkInstalled(engine.ListInstalledApps().Apps);

        Assert.Equal(
            ((string?)null, "Installed"),
            (Row(catalogue, "Valve.Steam").InstalledText, Row(catalogue, "Discord.Discord").InstalledText));
    }

    [Fact]
    public void The_filter_shows_the_apps_whose_name_or_description_matches_and_says_how_many()
    {
        var catalogue = Tab(NewEngine());
        Assert.Equal((7, "7 apps"), (catalogue.Shown.Count, catalogue.ResultCount));

        catalogue.Filter = "STEAM";
        Assert.Equal(["Steam"], catalogue.Shown.Select(a => a.Name));
        Assert.Equal("1 app", catalogue.ResultCount);

        catalogue.Filter = "archives";
        Assert.Equal(["7-Zip"], catalogue.Shown.Select(a => a.Name));
    }

    [Fact]
    public void A_filter_that_matches_nothing_says_so()
    {
        var catalogue = Tab(NewEngine());

        catalogue.Filter = " spotify ";

        Assert.Equal((0, "No apps match \"spotify\"."), (catalogue.Shown.Count, catalogue.ResultCount));
    }

    [Fact]
    public void The_install_button_says_how_many_apps_it_installs_and_is_off_with_none_ticked()
    {
        var catalogue = Tab(NewEngine());
        var none = (catalogue.InstallText, catalogue.HasAppsToInstall);

        Row(catalogue, "Valve.Steam").IsTicked = true;
        var one = (catalogue.InstallText, catalogue.HasAppsToInstall);
        Row(catalogue, "Discord.Discord").IsTicked = true;
        Row(catalogue, "7zip.7zip").IsTicked = true;

        Assert.Equal(("Install apps", false), none);
        Assert.Equal(("Install 1 app", true), one);
        Assert.Equal(("Install 3 apps", true), (catalogue.InstallText, catalogue.HasAppsToInstall));
    }

    [Fact]
    public void Install_all_runtimes_adds_each_runtime_to_the_count_once()
    {
        var catalogue = Tab(NewEngine());
        Row(catalogue, "Valve.Steam").IsTicked = true;
        Row(catalogue, "Microsoft.VCRedist.2015+.x64").IsTicked = true;

        catalogue.InstallAllRuntimes = true;

        // Steam, Visual C++ (ticked, and one of the runtimes) and DirectX. XNA isn't one of "all runtimes".
        Assert.Equal("Install 3 apps", catalogue.InstallText);
    }

    [Fact]
    public void Apps_ticked_and_then_hidden_by_the_filter_still_count()
    {
        var catalogue = Tab(NewEngine());
        Row(catalogue, "Valve.Steam").IsTicked = true;

        catalogue.Filter = "discord";

        Assert.Equal("Install 1 app", catalogue.InstallText);
    }

    [Fact]
    public void Ticking_an_app_tells_the_window_the_buttons_words_changed()
    {
        var catalogue = Tab(NewEngine());
        var changed = new List<string?>();
        catalogue.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Row(catalogue, "Valve.Steam").IsTicked = true;

        Assert.Contains(nameof(AppCatalogueViewModel.InstallText), changed);
        Assert.Contains(nameof(AppCatalogueViewModel.HasAppsToInstall), changed);
    }

    [Fact]
    public void Opening_an_app_the_filter_hides_clears_the_filter_and_selects_its_row()
    {
        var catalogue = Tab(NewEngine());
        catalogue.Filter = "firefox";

        catalogue.Select("Valve.Steam");

        Assert.Equal(("", "Valve.Steam"), (catalogue.Filter, catalogue.Selected?.View.Id));
        Assert.Contains(catalogue.Selected, catalogue.Shown);
    }

    [Fact]
    public void Opening_an_app_the_filter_shows_keeps_the_filter()
    {
        var catalogue = Tab(NewEngine());
        catalogue.Filter = "steam";

        catalogue.Select("Valve.Steam");

        Assert.Equal(("steam", "Valve.Steam"), (catalogue.Filter, catalogue.Selected?.View.Id));
    }
}
