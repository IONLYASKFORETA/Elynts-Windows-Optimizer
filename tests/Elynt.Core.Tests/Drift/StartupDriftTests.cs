using Elynt.Core.Drift;
using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Startup;
using Elynt.Core.Tests.Startup;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>
/// Startup items the app switched that something else switched back (usually the app's own update) are drift
/// (ticket 77), driven through the Engine with fake adapters.
/// </summary>
public class StartupDriftTests
{
    private const string User = "S-1-5-21-test";
    private const string DiscordExe = @"C:\Users\aden\AppData\Local\Discord\Update.exe";
    private const string Discord = "run:HKCU:Discord";
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly RegistryLocation DiscordApproved = new(
        RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Discord");

    /// <summary>Task Manager's "on" for a Run item, as an app's update writes it when it switches itself back on.</summary>
    private static readonly RegistryValue SwitchedOn = new(RegistryValueKind.Binary, [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

    private readonly FakeRegistry _registry = new();
    private readonly FakeStartupSources _sources = new();
    private readonly FakeScheduledTasks _tasks = new();
    private readonly FakeClock _clock = new(Start);
    private readonly InMemoryJournalStore _journal = new();
    private readonly InMemoryDriftStore _drift = new();

    private Engine NewEngine(Catalogue? catalogue = null) => new(
        catalogue ?? new Catalogue([]),
        new SystemAdapters(_registry, new FakeSystemInfo())
        {
            Startup = new StartupAdapters(_sources, _tasks, new FakeFileSignatures(), new FakeStartupImpactLog()),
        },
        new ChangeJournal(_journal),
        User,
        clock: _clock,
        driftStore: _drift);

    private void AddDiscord() =>
        _sources.Run.Add(new RunValue(RunKey.CurrentUser, "Discord", $"\"{DiscordExe}\" --processStart Discord.exe", DiscordExe));

    [Fact]
    public void A_startup_item_switched_off_through_the_app_and_back_on_outside_it_is_drift()
    {
        AddDiscord();
        Assert.True(NewEngine().SetStartupItemEnabled(Discord, enabled: false).Succeeded);
        Assert.Empty(NewEngine().CheckDrift().Drifted);

        _registry.SetValue(DiscordApproved, SwitchedOn);
        var item = Assert.Single(NewEngine().CheckDrift().Drifted);

        Assert.Equal($"startup:{Discord}", item.TweakId);
        Assert.Equal("Startup item: Discord", item.Name);
        Assert.Equal(DriftKind.StartupItemSwitchedBack, item.Kind);
        Assert.False(item.NeedsConfirmation);
        Assert.False(item.NeedsReboot);
    }

    [Fact]
    public void Reapplying_switches_it_off_again_like_task_manager_and_undo_returns_to_what_the_update_set()
    {
        AddDiscord();
        Assert.True(NewEngine().SetStartupItemEnabled(Discord, enabled: false).Succeeded);
        _registry.SetValue(DiscordApproved, SwitchedOn);
        _clock.Advance(TimeSpan.FromDays(3));
        var engine = NewEngine();
        Assert.Single(engine.CheckDrift().Drifted);

        var result = engine.ReapplyDrift(null);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal($"startup:{Discord}", Assert.Single(result.Changes).TweakId);
        // 03 = off, then the FILETIME it was turned off, as Task Manager writes it.
        var off = new byte[12];
        off[0] = 0x03;
        BitConverter.GetBytes(Start.AddDays(3).ToFileTime()).CopyTo(off, 4);
        Assert.Equal(new RegistryValue(RegistryValueKind.Binary, off), _registry.GetValue(DiscordApproved));
        Assert.False(engine.ListStartupItems().Single().Enabled);
        Assert.Empty(engine.CheckDrift().Drifted);

        // Journaled like any startup switch: the newest change is the only one to undo, and it puts back the update's value.
        var history = engine.ListHistory();
        Assert.Equal(2, history.Count);
        Assert.Equal("Startup item: Discord", history[0].TweakName);
        Assert.True(history[0].CanUndo);
        Assert.Equal(ChangeState.Applied, history[0].State);
        Assert.False(history[1].CanUndo);
        Assert.Equal(ChangeState.Undone, history[1].State);
        Assert.True(engine.UndoChange(history[0].Id).Succeeded);
        Assert.Equal(SwitchedOn, _registry.GetValue(DiscordApproved));
    }

    [Fact]
    public void Automatic_reapply_switches_it_back_off_during_the_check_since_startup_switches_are_instant()
    {
        AddDiscord();
        var engine = NewEngine();
        Assert.True(engine.SetAutoReapply(true).AutoReapply);
        Assert.True(engine.SetStartupItemEnabled(Discord, enabled: false).Succeeded);
        _registry.SetValue(DiscordApproved, SwitchedOn);

        var report = NewEngine().CheckDrift();

        Assert.Empty(report.Drifted);
        Assert.Equal($"startup:{Discord}", Assert.Single(report.Reapplied).TweakId);
        Assert.Contains("Put back automatically: Startup item: Discord.", report.Message);
        Assert.False(NewEngine().ListStartupItems().Single().Enabled);
    }

    [Fact]
    public void An_item_whose_app_was_uninstalled_is_not_drift_and_is_dropped_quietly()
    {
        AddDiscord();
        Assert.True(NewEngine().SetStartupItemEnabled(Discord, enabled: false).Succeeded);
        // The uninstaller removes the Run value and switches nothing back.
        _sources.Run.Clear();

        var report = NewEngine().CheckDrift();

        Assert.Empty(report.Drifted);
        Assert.Empty(report.NewlyUnsupported);
        Assert.Equal("Everything the app changed is still in place.", report.Message);
        Assert.Empty(NewEngine().ReapplyDrift(null).Changes);
    }

    [Fact]
    public void A_task_and_a_store_app_switched_back_on_by_their_updates_are_drift_and_reapplying_turns_them_off()
    {
        const string Updater = @"\GoogleSystem\GoogleUpdater\GoogleUpdaterTaskSystem";
        const string SpotifyPfn = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0";
        var spotifyState = new RegistryLocation(
            RegistryRoot.CurrentUser,
            $@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\{SpotifyPfn}\SpotifyStartupTask",
            "State");
        _tasks.Tasks.Add(new ScheduledTaskInfo(Updater, true, RunsAtSignIn: false, "Google", null, null));
        _sources.Store.Add(new StoreStartupTask(SpotifyPfn, "SpotifyStartupTask", "Spotify", "Spotify AB", EnabledByDefault: true, null));
        var engine = NewEngine();
        Assert.True(engine.SetStartupItemEnabled($"task:{Updater}", enabled: false).Succeeded);
        Assert.True(engine.SetStartupItemEnabled($"store:{SpotifyPfn}!SpotifyStartupTask", enabled: false).Succeeded);

        _tasks.SetEnabled(Updater, true);
        _registry.SetValue(spotifyState, RegistryValue.DWord(2));
        var drifted = NewEngine().CheckDrift().Drifted;

        Assert.Equal(["Startup item: GoogleUpdaterTaskSystem", "Startup item: Spotify"], drifted.Select(d => d.Name).Order(StringComparer.Ordinal));
        Assert.True(NewEngine().ReapplyDrift(null).Succeeded);
        Assert.False(_tasks.IsEnabled(Updater));
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(spotifyState));
    }

    [Fact]
    public void The_report_names_it_alone_and_counts_it_with_tweaks_windows_undid_without_blaming_windows()
    {
        const string StartRecommendations = "debloat.start-recommendations-off";
        AddDiscord();
        var engine = NewEngine(Catalogue.Default);
        Assert.True(engine.Apply(StartRecommendations).Succeeded);
        Assert.True(engine.SetStartupItemEnabled(Discord, enabled: false).Succeeded);
        _registry.SetValue(DiscordApproved, SwitchedOn);

        Assert.Equal("1 change the app made was undone since, usually by an update: Startup item: Discord. Re-apply it?",
            NewEngine(Catalogue.Default).CheckDrift().Message);

        // What a Windows update does: writes its own value over each of the tweak's registry values.
        foreach (var step in Catalogue.Default.Get(StartRecommendations).Steps.OfType<RegistryValueStep>())
            _registry.SetValue(step.Location, RegistryValue.DWord(1));
        var report = NewEngine(Catalogue.Default).CheckDrift();

        Assert.Equal([StartRecommendations, $"startup:{Discord}"], report.Drifted.Select(d => d.TweakId));
        Assert.StartsWith("2 changes the app made were undone since, usually by an update:", report.Message);
    }

    [Fact]
    public void When_startup_items_cant_be_read_the_check_still_reports_the_tweaks()
    {
        const string StartRecommendations = "debloat.start-recommendations-off";
        AddDiscord();
        Assert.True(NewEngine(Catalogue.Default).Apply(StartRecommendations).Succeeded);
        Assert.True(NewEngine().SetStartupItemEnabled(Discord, enabled: false).Succeeded);
        foreach (var step in Catalogue.Default.Get(StartRecommendations).Steps.OfType<RegistryValueStep>())
            _registry.SetValue(step.Location, RegistryValue.DWord(1));
        var broken = new Engine(
            Catalogue.Default,
            new SystemAdapters(_registry, new FakeSystemInfo())
            {
                Startup = new StartupAdapters(new UnreadableStartupSources(), _tasks, new FakeFileSignatures(), new FakeStartupImpactLog()),
            },
            new ChangeJournal(_journal),
            User,
            driftStore: _drift);

        Assert.Equal(StartRecommendations, Assert.Single(broken.CheckDrift().Drifted).TweakId);
        Assert.True(broken.ReapplyDrift(null).Succeeded);
    }

    /// <summary>Startup sources Windows won't read, e.g. while the registry hive is locked.</summary>
    private sealed class UnreadableStartupSources : IStartupSources
    {
        public IReadOnlyList<RunValue> RunValues() => throw new UnauthorizedAccessException("Access is denied.");
        public IReadOnlyList<StartupFile> StartupFiles() => [];
        public IReadOnlyList<StoreStartupTask> StoreStartupTasks() => [];
        public void DeleteRunValue(RunKey key, string name) => throw new NotSupportedException();
        public void DeleteStartupFile(StartupFolder folder, string fileName) => throw new NotSupportedException();
    }

    [Fact]
    public void An_item_turned_off_again_outside_the_app_is_still_off_so_it_is_not_drift()
    {
        AddDiscord();
        Assert.True(NewEngine().SetStartupItemEnabled(Discord, enabled: false).Succeeded);
        // Switched on and off again in Task Manager, which writes its own date.
        _registry.SetValue(DiscordApproved, new RegistryValue(RegistryValueKind.Binary, [0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8]));

        Assert.Empty(NewEngine().CheckDrift().Drifted);
    }

    [Fact]
    public void Switching_it_off_on_the_startup_page_after_its_update_cleared_the_switch_turns_it_off()
    {
        AddDiscord();
        var engine = NewEngine();
        Assert.True(engine.SetStartupItemEnabled(Discord, enabled: false).Succeeded);
        // No value at all also means on, as it was before the app's switch.
        _registry.DeleteValue(DiscordApproved);

        Assert.True(engine.SetStartupItemEnabled(Discord, enabled: false).Succeeded);

        Assert.False(engine.ListStartupItems().Single().Enabled);
        Assert.Empty(engine.CheckDrift().Drifted);
        Assert.Single(engine.ListHistory(), c => c.CanUndo);
    }
}
