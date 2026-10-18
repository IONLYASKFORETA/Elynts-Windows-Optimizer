using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Startup;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests.Startup;

/// <summary>The Startup page: every startup item, turning one off like Task Manager does, and deleting one.</summary>
public class StartupItemsTests
{
    private const string User = "S-1-5-21-test";
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private const string DiscordExe = @"C:\Users\aden\AppData\Local\Discord\Update.exe";

    private readonly FakeRegistry _registry = new();
    private readonly FakeStartupSources _sources = new();
    private readonly FakeScheduledTasks _tasks = new();
    private readonly FakeFileSignatures _signatures = new();
    private readonly FakeStartupImpactLog _impact = new();
    private readonly FakeClock _clock = new(Start);
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    private Engine NewEngine(ChangeJournal? journal = null) => new(
        new Catalogue([]),
        new SystemAdapters(_registry, new FakeSystemInfo())
        {
            Startup = new StartupAdapters(_sources, _tasks, _signatures, _impact),
        },
        journal ?? _journal,
        User,
        clock: _clock);

    [Fact]
    public void A_run_key_item_is_listed_with_its_publisher_signature_and_impact()
    {
        _sources.Run.Add(new RunValue(RunKey.CurrentUser, "Discord", $"\"{DiscordExe}\" --processStart Discord.exe", DiscordExe));
        _signatures.Files[DiscordExe] = new FileSignature(SignatureStatus.Signed, "Discord Inc.");
        _impact.Measurements.Add(new StartupMeasurement(DiscordExe, TimeSpan.FromMilliseconds(1500), 1_000_000));

        var item = Assert.Single(NewEngine().ListStartupItems());

        Assert.Equal("Discord", item.Name);
        Assert.Equal(StartupSource.RunKey, item.Source);
        Assert.Equal(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", item.Location);
        Assert.Equal("Discord Inc.", item.Publisher);
        Assert.Equal(SignatureStatus.Signed, item.Signature);
        Assert.Equal(StartupImpact.High, item.Impact);
        Assert.True(item.Enabled);
        Assert.True(item.CanSwitch);
    }

    private static readonly RegistryLocation DiscordApproved = new(
        RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Discord");

    private void AddDiscord() =>
        _sources.Run.Add(new RunValue(RunKey.CurrentUser, "Discord", $"\"{DiscordExe}\" --processStart Discord.exe", DiscordExe));

    [Fact]
    public void Turning_a_run_item_off_writes_the_same_switch_task_manager_does()
    {
        AddDiscord();
        var engine = NewEngine();

        var result = engine.SetStartupItemEnabled("run:HKCU:Discord", enabled: false);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(result.Item!.Enabled);
        // 03 = off, then the FILETIME it was turned off, as Task Manager writes it.
        var expected = new byte[12];
        expected[0] = 0x03;
        BitConverter.GetBytes(Start.ToFileTime()).CopyTo(expected, 4);
        Assert.Equal(new RegistryValue(RegistryValueKind.Binary, expected), _registry.GetValue(DiscordApproved));
        Assert.False(engine.ListStartupItems().Single().Enabled);
    }

    [Fact]
    public void Turning_it_back_on_puts_back_exactly_what_was_there()
    {
        AddDiscord();
        var engine = NewEngine();
        engine.SetStartupItemEnabled("run:HKCU:Discord", enabled: false);

        var result = engine.SetStartupItemEnabled("run:HKCU:Discord", enabled: true);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.Item!.Enabled);
        Assert.Null(_registry.GetValue(DiscordApproved));
        Assert.False(_registry.KeyExists(RegistryRoot.CurrentUser, DiscordApproved.KeyPath));
    }

    [Fact]
    public void An_item_task_manager_turned_off_shows_as_off_and_turns_on_like_task_manager()
    {
        AddDiscord();
        var turnedOffInTaskManager = new RegistryValue(RegistryValueKind.Binary, [0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8]);
        _registry.SetValue(DiscordApproved, turnedOffInTaskManager);
        var engine = NewEngine();
        Assert.False(engine.ListStartupItems().Single().Enabled);

        Assert.True(engine.SetStartupItemEnabled("run:HKCU:Discord", enabled: true).Succeeded);

        Assert.Equal(new RegistryValue(RegistryValueKind.Binary, [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]), _registry.GetValue(DiscordApproved));
        Assert.True(engine.ListStartupItems().Single().Enabled);

        // That too is a journaled change: switching it off again puts back Task Manager's own value.
        Assert.True(engine.SetStartupItemEnabled("run:HKCU:Discord", enabled: false).Succeeded);
        Assert.Equal(turnedOffInTaskManager, _registry.GetValue(DiscordApproved));
    }

    [Fact]
    public void Turning_an_item_off_shows_in_undo_history_and_can_be_undone_from_there()
    {
        AddDiscord();
        var engine = NewEngine();
        engine.SetStartupItemEnabled("run:HKCU:Discord", enabled: false);

        var change = Assert.Single(engine.ListHistory());
        Assert.Equal("Startup item: Discord", change.TweakName);
        Assert.True(change.CanUndo);

        var result = engine.UndoChange(change.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Null(_registry.GetValue(DiscordApproved));
        Assert.Equal(ChangeState.Undone, engine.ListHistory().Single().State);
        Assert.True(engine.ListStartupItems().Single().Enabled);
    }

    [Fact]
    public void Restore_everything_turns_startup_items_back_on()
    {
        AddDiscord();
        var engine = NewEngine();
        engine.SetStartupItemEnabled("run:HKCU:Discord", enabled: false);

        var result = engine.RestoreEverything();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Startup item: Discord", Assert.Single(result.Changes).TweakName);
        Assert.Null(_registry.GetValue(DiscordApproved));
    }

    [Fact]
    public void Undo_refuses_to_overwrite_a_switch_something_else_changed_since()
    {
        AddDiscord();
        var engine = NewEngine();
        engine.SetStartupItemEnabled("run:HKCU:Discord", enabled: false);
        var taskManagerValue = new RegistryValue(RegistryValueKind.Binary, [0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        _registry.SetValue(DiscordApproved, taskManagerValue);

        var result = engine.UndoChange(engine.ListHistory().Single().Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Something else changed", result.Message);
        Assert.Equal(taskManagerValue, _registry.GetValue(DiscordApproved));
    }

    private const string SteamExe = @"C:\Program Files (x86)\Steam\steam.exe";
    private const string SpotifyPfn = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0";
    private const string ChromeUpdater = @"\GoogleSystem\GoogleUpdater\GoogleUpdaterTaskSystem";

    private static readonly RegistryLocation SteamApproved = new(
        RegistryRoot.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", "Steam.lnk");

    private static readonly RegistryLocation SpotifyState = new(
        RegistryRoot.CurrentUser,
        $@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\{SpotifyPfn}\SpotifyStartupTask",
        "State");

    private void AddEverySource()
    {
        AddDiscord();
        _sources.Files.Add(new StartupFile(StartupFolder.AllUsers, "Steam.lnk", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp\Steam.lnk", SteamExe));
        _sources.Store.Add(new StoreStartupTask(SpotifyPfn, "SpotifyStartupTask", "Spotify", "Spotify AB", EnabledByDefault: true, null));
        _tasks.Tasks.Add(new ScheduledTaskInfo(@"\Logitech\LGHUB Launch", true, RunsAtSignIn: true, "Logitech", @"C:\Program Files\LGHUB\lghub.exe", @"C:\Program Files\LGHUB\lghub.exe"));
        _tasks.Tasks.Add(new ScheduledTaskInfo(ChromeUpdater, true, RunsAtSignIn: false, "Google", @"C:\Program Files\Google\updater.exe --wake", @"C:\Program Files\Google\updater.exe"));
        _tasks.Tasks.Add(new ScheduledTaskInfo(@"\Microsoft\Windows\Defrag\ScheduledDefrag", true, RunsAtSignIn: false, "Microsoft Corporation", null, null));
        _tasks.Tasks.Add(new ScheduledTaskInfo(@"\MicrosoftEdgeUpdateTaskMachineUA", true, RunsAtSignIn: false, "Microsoft Corporation", null, null));
    }

    [Fact]
    public void Every_startup_source_and_third_party_scheduled_tasks_are_listed()
    {
        AddEverySource();
        _signatures.Files[SteamExe] = new FileSignature(SignatureStatus.Invalid, "Valve");

        var items = NewEngine().ListStartupItems();

        Assert.Equal(
            ["Discord", "Steam", "LGHUB Launch", "Spotify", "GoogleUpdaterTaskSystem", "MicrosoftEdgeUpdateTaskMachineUA"],
            items.Select(i => i.Name));
        Assert.Equal(
            [StartupSource.RunKey, StartupSource.StartupFolder, StartupSource.SignInTask, StartupSource.StoreApp, StartupSource.ScheduledTask, StartupSource.ScheduledTask],
            items.Select(i => i.Source));

        var steam = items.Single(i => i.Name == "Steam");
        Assert.Equal(SignatureStatus.Invalid, steam.Signature);
        Assert.Equal("Valve", steam.Publisher);
        Assert.Equal(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp", steam.Location);
        var spotify = items.Single(i => i.Name == "Spotify");
        Assert.Equal("Spotify AB", spotify.Publisher);
        Assert.Equal(SignatureStatus.Signed, spotify.Signature);
        Assert.True(spotify.Enabled);
        Assert.False(spotify.CanDelete);
        var updater = items.Single(i => i.Name == "GoogleUpdaterTaskSystem");
        Assert.Equal(ChromeUpdater, updater.Location);
        Assert.Equal("Google", updater.Publisher);
    }

    [Fact]
    public void Startup_folder_items_use_the_startup_folder_switch()
    {
        AddEverySource();
        var engine = NewEngine();
        var steam = engine.ListStartupItems().Single(i => i.Name == "Steam");

        Assert.True(engine.SetStartupItemEnabled(steam.Id, false).Succeeded);

        Assert.Equal(0x03, _registry.GetValue(SteamApproved)!.Data[0]);
        Assert.True(engine.SetStartupItemEnabled(steam.Id, true).Succeeded);
        Assert.Null(_registry.GetValue(SteamApproved));
    }

    [Fact]
    public void Store_apps_are_turned_off_as_disabled_by_user_like_task_manager()
    {
        AddEverySource();
        var engine = NewEngine();
        var spotify = engine.ListStartupItems().Single(i => i.Name == "Spotify");

        Assert.True(engine.SetStartupItemEnabled(spotify.Id, false).Succeeded);

        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(SpotifyState));
        Assert.False(engine.ListStartupItems().Single(i => i.Name == "Spotify").Enabled);
        Assert.True(engine.SetStartupItemEnabled(spotify.Id, true).Succeeded);
        Assert.Null(_registry.GetValue(SpotifyState));
    }

    [Fact]
    public void A_store_app_a_policy_controls_cannot_be_switched()
    {
        AddEverySource();
        _registry.SetValue(SpotifyState, RegistryValue.DWord(4));
        var engine = NewEngine();
        var spotify = engine.ListStartupItems().Single(i => i.Name == "Spotify");
        Assert.True(spotify.Enabled);
        Assert.False(spotify.CanSwitch);

        Assert.False(engine.SetStartupItemEnabled(spotify.Id, false).Succeeded);
        Assert.Equal(RegistryValue.DWord(4), _registry.GetValue(SpotifyState));
    }

    [Fact]
    public void Third_party_scheduled_tasks_switch_off_and_back_through_task_scheduler()
    {
        AddEverySource();
        var engine = NewEngine();
        var updater = engine.ListStartupItems().Single(i => i.Name == "GoogleUpdaterTaskSystem");

        Assert.True(engine.SetStartupItemEnabled(updater.Id, false).Succeeded);
        Assert.False(_tasks.IsEnabled(ChromeUpdater));
        Assert.Equal("Startup item: GoogleUpdaterTaskSystem", engine.ListHistory().Single().TweakName);

        Assert.True(engine.RestoreEverything().Succeeded);
        Assert.True(_tasks.IsEnabled(ChromeUpdater));
    }

    [Fact]
    public void Microsoft_tasks_under_the_microsoft_folder_are_never_switched()
    {
        AddEverySource();

        var result = NewEngine().SetStartupItemEnabled(@"task:\Microsoft\Windows\Defrag\ScheduledDefrag", false);

        Assert.False(result.Succeeded);
        Assert.True(_tasks.IsEnabled(@"\Microsoft\Windows\Defrag\ScheduledDefrag"));
    }

    [Fact]
    public void A_task_switch_can_still_be_undone_after_the_service_restarts()
    {
        AddEverySource();
        var store = new InMemoryJournalStore();
        var before = NewEngine(new ChangeJournal(store));
        before.SetStartupItemEnabled($"task:{ChromeUpdater}", false);

        var after = NewEngine(new ChangeJournal(store));

        Assert.True(after.UndoChange(after.ListHistory().Single().Id).Succeeded);
        Assert.True(_tasks.IsEnabled(ChromeUpdater));
    }

    [Fact]
    public void Deleting_needs_its_own_confirmation()
    {
        AddEverySource();

        var result = NewEngine().DeleteStartupItem("run:HKCU:Discord", confirmDestructive: false);

        Assert.False(result.Succeeded);
        Assert.Contains("can't be undone", result.Message);
        Assert.Single(_sources.Run);
    }

    [Fact]
    public void Deleting_removes_the_item_and_its_switch_from_every_kind_of_source()
    {
        AddEverySource();
        var engine = NewEngine();
        engine.SetStartupItemEnabled("run:HKCU:Discord", false);
        var steam = engine.ListStartupItems().Single(i => i.Name == "Steam");

        Assert.True(engine.DeleteStartupItem("run:HKCU:Discord", confirmDestructive: true).Succeeded);
        Assert.True(engine.DeleteStartupItem(steam.Id, confirmDestructive: true).Succeeded);
        Assert.True(engine.DeleteStartupItem($"task:{ChromeUpdater}", confirmDestructive: true).Succeeded);

        Assert.Empty(_sources.Run);
        Assert.Null(_registry.GetValue(DiscordApproved));
        Assert.Empty(_sources.Files);
        Assert.DoesNotContain(_tasks.Tasks, t => t.Path == ChromeUpdater);
        Assert.DoesNotContain(engine.ListStartupItems(), i => i.Name is "Discord" or "Steam" or "GoogleUpdaterTaskSystem");
    }

    [Fact]
    public void Store_apps_and_microsoft_tasks_cannot_be_deleted()
    {
        AddEverySource();
        var engine = NewEngine();
        var spotify = engine.ListStartupItems().Single(i => i.Name == "Spotify");

        Assert.False(engine.DeleteStartupItem(spotify.Id, confirmDestructive: true).Succeeded);
        Assert.False(engine.DeleteStartupItem(@"task:\Microsoft\Windows\Defrag\ScheduledDefrag", confirmDestructive: true).Succeeded);
        Assert.Contains(_tasks.Tasks, t => t.Path == @"\Microsoft\Windows\Defrag\ScheduledDefrag");
    }

    [Fact]
    public void An_unknown_item_is_refused()
    {
        var result = NewEngine().SetStartupItemEnabled("run:HKCU:Nope", enabled: false);

        Assert.False(result.Succeeded);
        Assert.Null(result.Item);
    }
}
