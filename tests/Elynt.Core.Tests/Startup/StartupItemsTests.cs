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

    private Engine NewEngine() => new(
        new Catalogue([]),
        new SystemAdapters(_registry, new FakeSystemInfo())
        {
            Startup = new StartupAdapters(_sources, _tasks, _signatures, _impact),
        },
        _journal,
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

    [Fact]
    public void An_unknown_item_is_refused()
    {
        var result = NewEngine().SetStartupItemEnabled("run:HKCU:Nope", enabled: false);

        Assert.False(result.Succeeded);
        Assert.Null(result.Item);
    }
}
