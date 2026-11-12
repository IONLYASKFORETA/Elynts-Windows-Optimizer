using System.IO.Pipes;
using Elynt.App.ViewModels;
using Elynt.Core.Ipc;
using Elynt.Core.Tests;

namespace Elynt.App.Tests;

/// <summary>
/// The Per-game profiles page against the screenshot tour's made-up PC, served over a real pipe as the app sees it: the
/// games found, the "Find a game" box, and the profile editor the detail pane shows for the selected game.
/// </summary>
public sealed class ProfilesPageTests : IAsyncLifetime
{
    private readonly string _pipeName = $"elynt-test-{Guid.NewGuid():N}";
    private readonly DirectoryInfo _scratch = Directory.CreateTempSubdirectory("elynt-profiles-page-");
    private readonly CancellationTokenSource _stop = new();
    private Task _server = Task.CompletedTask;
    private EnginePipeClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var engine = SamplePc.NewEngine(_scratch.FullName);
        var server = new EnginePipeServer(
            () => new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            _ => engine,
            new FakeAdministratorCheck());
        _server = server.RunAsync(_stop.Token);
        _client = await EnginePipeClient.ConnectAsync(_pipeName, cancellation: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _stop.CancelAsync();
        await _server;
        _scratch.Delete(recursive: true);
    }

    /// <summary>The page as the window opens it.</summary>
    private async Task<ProfilesPageViewModel> OpenAsync(CancellationToken cancellation)
    {
        var page = new ProfilesPageViewModel(() => _client);
        await page.LoadAsync().WaitAsync(cancellation);
        return page;
    }

    [Fact(Timeout = 10_000)]
    public async Task The_page_lists_every_game_found_with_its_launcher_its_anti_cheat_and_whether_it_has_a_profile()
    {
        var page = await OpenAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                ("Counter-Strike 2", "Steam · FACEIT", false),
                ("Fortnite", "Epic Games · Easy Anti-Cheat", true),
                ("The Witcher 3: Wild Hunt", "GOG", false),
                ("VALORANT", "Added by you · Riot Vanguard", false),
            ],
            page.Games.Select(g => (g.Name, g.Detail, g.HasProfile)));
    }

    [Fact(Timeout = 10_000)]
    public async Task Find_a_game_shows_only_the_games_that_match_what_was_typed_and_says_how_many()
    {
        var page = await OpenAsync(TestContext.Current.CancellationToken);
        var everything = (page.Shown.Count, page.ResultCount);

        page.Filter = "WIT";
        var witcher = string.Join(", ", page.Shown.Select(g => g.Name));
        var one = page.ResultCount;
        page.Filter = "steam";
        var steam = page.Shown.Select(g => g.Name).ToList();
        page.Filter = "halo ";

        Assert.Equal((4, "4 games"), everything);
        Assert.Equal(("The Witcher 3: Wild Hunt", "1 game"), (witcher, one));
        Assert.Equal(["Counter-Strike 2"], steam);
        Assert.Equal((0, "No games match \"halo\"."), (page.Shown.Count, page.ResultCount));
    }

    [Fact(Timeout = 10_000)]
    public async Task The_power_plan_is_one_pick_one_choice_and_the_profile_keeps_only_the_plan_chosen()
    {
        var page = await OpenAsync(TestContext.Current.CancellationToken);
        page.SelectedGame = page.Games.Single(g => g.Name == "Fortnite");
        var plan = Assert.Single(page.Choices);
        var shown = (plan.Name, Labels: string.Join(" / ", plan.Options.Select(o => o.Label)), Selected: Selected(plan));

        plan.Options.Single(o => o.Label == "No change").Choose();
        plan.Options.Single(o => o.Label == "Ultimate").Choose();
        var chosen = Selected(plan);
        await page.SaveAsync().WaitAsync(TestContext.Current.CancellationToken);

        // The sample PC's Fortnite profile holds High performance and Game Mode.
        Assert.Equal(("Power plan", "No change / High performance / Ultimate", "High performance"), shown);
        Assert.Equal("Ultimate", chosen);
        Assert.Equal(["Keep Game Mode on"], page.Tweaks.Select(t => t.Name));
        var saved = Assert.Single(await _client.ListProfilesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["gaming.ultimate-performance-plan", "gaming.game-mode-on"], saved.TweakIds);

        static string Selected(ProfileChoice choice) => string.Join(", ", choice.Options.Where(o => o.IsSelected).Select(o => o.Label));
    }

    [Fact(Timeout = 10_000)]
    public async Task Background_apps_to_close_show_by_name_with_their_path_and_each_comes_off_on_its_own()
    {
        const string Discord = @"C:\Users\aden\AppData\Local\Discord\app-1.0.9170\Discord.exe";
        const string Steam = @"C:\Program Files (x86)\Steam\steam.exe";
        var page = await OpenAsync(TestContext.Current.CancellationToken);
        page.SelectedGame = page.Games.Single(g => g.Name == "Fortnite");
        // The made-up PC has no such files, so no file description either: each shows by its file name.
        var shown = string.Join(" | ", page.AppsToClose.Select(a => $"{a.Name}: {a.Path}"));

        page.RemoveApp(page.AppsToClose.Single(a => a.Name == "Discord"));
        await page.SaveAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal($"Discord: {Discord} | steam: {Steam}", shown);
        var saved = Assert.Single(await _client.ListProfilesAsync(TestContext.Current.CancellationToken));
        Assert.Equal([Steam], saved.AppsToClose);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_game_its_launcher_gave_no_exe_for_gets_a_profile_once_its_exe_is_chosen_from_its_folder()
    {
        const string Folder = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive";
        const string Cs2 = $@"{Folder}\game\bin\win64\cs2.exe";
        var page = await OpenAsync(TestContext.Current.CancellationToken);
        string? openedIn = null;
        page.PickGameExe = folder => (openedIn = folder) is not null ? Cs2 : null;
        page.SelectedGame = page.Games.Single(g => g.Name == "Counter-Strike 2");
        var before = (page.NeedsExe, page.CanEdit);

        page.ChooseExe();
        var after = (page.NeedsExe, page.CanEdit, page.ExePath);
        await page.SaveAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal((true, false), before);
        Assert.Equal(Folder, openedIn);
        Assert.Equal((false, true, Cs2), after);
        Assert.Equal("Saved the profile for Counter-Strike 2.", page.Message);
        // The profile is the game's, on its row, not a row of its own.
        Assert.Equal(
            [("Counter-Strike 2", true), ("Fortnite", true), ("The Witcher 3: Wild Hunt", false), ("VALORANT", false)],
            page.Games.Select(g => (g.Name, g.HasProfile)));
        Assert.Equal(Cs2, page.Games[0].ExePath);
    }

    [Fact(Timeout = 10_000)]
    public async Task An_exe_from_outside_the_games_folder_is_refused_and_still_asked_for()
    {
        var page = await OpenAsync(TestContext.Current.CancellationToken);
        page.PickGameExe = _ => @"C:\Riot Games\VALORANT\live\VALORANT.exe";
        page.SelectedGame = page.Games.Single(g => g.Name == "Counter-Strike 2");

        page.ChooseExe();

        Assert.True(page.NeedsExe);
        Assert.Equal(
            @"VALORANT.exe isn't in Counter-Strike 2's folder (C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive). Choose the .exe inside it that starts the game.",
            page.Message);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_profile_whose_game_is_no_longer_found_is_still_listed_so_it_can_be_edited_or_deleted()
    {
        const string Gone = @"D:\Games\Old Game\oldgame.exe";
        Assert.True((await _client.SaveProfileAsync(new(Gone), TestContext.Current.CancellationToken)).Succeeded);

        var page = await OpenAsync(TestContext.Current.CancellationToken);
        var row = page.Games.Single(g => g.ExePath == Gone);

        Assert.Equal(("oldgame", "Not in your games any more", true), (row.Name, row.Detail, row.HasProfile));
    }
}
