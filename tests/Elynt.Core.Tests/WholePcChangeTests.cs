using Elynt.Core.Journal;
using Elynt.Core.Power;
using Elynt.Core.Profiles;
using Elynt.Core.Registry;
using Elynt.Core.Tests.Profiles;
using Elynt.Core.Tests.Services;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>
/// Whole-PC changes belong to the PC (ADR 0007): two administrator accounts on one PC share one journal, and each one's
/// Undo history holds its own changes plus every whole-PC change, whichever account made it.
/// </summary>
public class WholePcChangeTests
{
    private const string Aden = "S-1-5-21-aden";
    private const string Friend = "S-1-5-21-friend";

    private static readonly RegistryLocation MachineValue = new(RegistryRoot.LocalMachine, @"SOFTWARE\ElyntTest", "MachineWide");
    private static readonly RegistryLocation OwnValue = new(RegistryRoot.CurrentUser, @"Software\ElyntTest", "OwnSettings");

    private static readonly TweakDefinition MachineWide =
        LabelTests.TestTweak("test.machine-wide", new RegistryValueStep(MachineValue, RegistryValue.DWord(1))) with
        {
            Name = "Turn on the machine-wide setting",
        };

    private static readonly TweakDefinition OwnSettings =
        LabelTests.TestTweak("test.own-settings", new RegistryValueStep(OwnValue, RegistryValue.DWord(1))) with
        {
            Name = "Turn on the account's own setting",
        };

    private readonly FakeRegistry _machine = new();
    private readonly Dictionary<string, FakeRegistry> _accounts = [];
    private readonly FakeSystemInfo _system = new();
    private readonly FakeServiceManager _services = new();
    private readonly FakeBootConfig _boot = new();
    private readonly FakePowerPlans _power = new();
    private readonly FakeRemovableApps _apps = new();
    private readonly FakeProcesses _processes = new();
    private readonly InMemoryProfileStore _profiles = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());
    private Catalogue _catalogue = new([MachineWide, OwnSettings, PlaceboTweaks.DynamicTickOff, GamingTweaks.HighPerformancePlan]);

    [Fact]
    public void A_whole_PC_change_made_from_one_account_is_in_the_other_accounts_undo_history_and_can_be_undone_there()
    {
        Applied(EngineFor(Aden), MachineWide);
        var friend = EngineFor(Friend);

        var change = Assert.Single(friend.ListHistory());
        var result = friend.UndoChange(change.Id);

        Assert.Equal((MachineWide.Id, ChangeState.Applied, true), (change.TweakId, change.State, change.CanUndo));
        Assert.True(result.Succeeded, result.Message);
        Assert.Null(_machine.GetValue(MachineValue));
        Assert.Equal(ChangeState.Undone, Assert.Single(EngineFor(Aden).ListHistory()).State);
    }

    [Fact]
    public void A_whole_PC_change_from_another_account_is_on_with_an_undo_not_already_set_and_its_switch_turns_it_off()
    {
        Applied(EngineFor(Aden), MachineWide);
        var friend = EngineFor(Friend);

        var view = Listed(friend, MachineWide);
        var off = friend.Revert(MachineWide.Id);

        Assert.Equal((TweakState.On, false, true), (view.State, view.AlreadySet, view.CanUndo));
        Assert.True(off.Succeeded, off.Message);
        Assert.Null(_machine.GetValue(MachineValue));
    }

    [Fact]
    public void Restore_everything_in_the_other_account_undoes_whole_PC_changes_and_leaves_the_first_accounts_own_changes()
    {
        var aden = EngineFor(Aden);
        Applied(aden, MachineWide);
        Applied(aden, OwnSettings);
        var friend = EngineFor(Friend);

        var listed = friend.ListHistory().Select(c => c.TweakId).ToList();
        var result = friend.RestoreEverything();

        Assert.Equal([MachineWide.Id], listed);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal([MachineWide.Name], result.Changes.Select(c => c.TweakName));
        Assert.Null(_machine.GetValue(MachineValue));
        Assert.Equal(RegistryValue.DWord(1), Own(Aden).GetValue(OwnValue));
        Assert.True(aden.ListHistory().Single(c => c.TweakId == OwnSettings.Id).CanUndo);
    }

    [Fact]
    public void An_account_change_is_not_in_the_other_accounts_undo_history_and_cannot_be_undone_from_there()
    {
        Applied(EngineFor(Aden), OwnSettings);
        var friend = EngineFor(Friend);
        var change = Assert.Single(EngineFor(Aden).ListHistory());

        Assert.Empty(friend.ListHistory());
        Assert.False(friend.UndoChange(change.Id).Succeeded);
        Assert.False(Listed(friend, OwnSettings).CanUndo);
        Assert.Equal(RegistryValue.DWord(1), Own(Aden).GetValue(OwnValue));
    }

    [Fact]
    public void A_tweak_with_an_account_part_and_a_whole_PC_part_is_an_account_change()
    {
        var both = LabelTests.TestTweak("test.both",
            new RegistryValueStep(MachineValue, RegistryValue.DWord(1)), new RegistryValueStep(OwnValue, RegistryValue.DWord(1)));
        _catalogue = new Catalogue([both]);
        Applied(EngineFor(Aden), both);
        var friend = EngineFor(Friend);

        Assert.Empty(friend.ListHistory());
        Assert.False(Listed(friend, both).CanUndo);
        Assert.False(friend.Revert(both.Id).Succeeded);
        Assert.Equal(RegistryValue.DWord(1), _machine.GetValue(MachineValue));
    }

    [Fact]
    public void Undo_from_the_other_account_still_refuses_when_something_else_changed_the_setting_since()
    {
        Applied(EngineFor(Aden), MachineWide);
        _machine.SetValue(MachineValue, RegistryValue.DWord(5));
        var friend = EngineFor(Friend);
        var change = Assert.Single(friend.ListHistory());

        var result = friend.UndoChange(change.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Something else changed", result.Message);
        Assert.Contains("nothing was changed", result.Message);
        Assert.Equal(RegistryValue.DWord(5), _machine.GetValue(MachineValue));
    }

    [Fact]
    public void Only_the_newest_open_whole_PC_change_to_a_tweak_can_be_undone_whichever_account_made_it()
    {
        Applied(EngineFor(Aden), MachineWide);
        _machine.SetValue(MachineValue, RegistryValue.DWord(7)); // Windows put its own value back.
        Applied(EngineFor(Friend), MachineWide);
        var aden = EngineFor(Aden);
        var (older, newer) = (aden.ListHistory()[1], aden.ListHistory()[0]);

        Assert.All([Aden, Friend], account => Assert.Equal(
            [(newer.Id, true), (older.Id, false)],
            EngineFor(account).ListHistory().Select(c => (c.Id, c.CanUndo))));
        Assert.False(aden.UndoChange(older.Id).Succeeded);

        var undone = aden.UndoChange(newer.Id);

        Assert.True(undone.Succeeded, undone.Message);
        Assert.Equal(RegistryValue.DWord(7), _machine.GetValue(MachineValue));
        Assert.All([Aden, Friend], account =>
        {
            Assert.All(EngineFor(account).ListHistory(), c => Assert.Equal((ChangeState.Undone, false), (c.State, c.CanUndo)));
            Assert.False(Listed(EngineFor(account), MachineWide).CanUndo);
        });
    }

    [Fact]
    public void Service_boot_setting_and_power_plan_changes_are_whole_PC_changes_the_other_account_can_restore()
    {
        _services.Add("Spooler", ServiceStart.Automatic);
        var aden = EngineFor(Aden);
        Assert.True(aden.SetServiceStart("Spooler", ServiceStart.Manual).Succeeded);
        Applied(aden, PlaceboTweaks.DynamicTickOff);
        Applied(aden, GamingTweaks.HighPerformancePlan);
        var friend = EngineFor(Friend);

        var listed = friend.ListHistory().Where(c => c.CanUndo).Select(c => c.TweakId).ToList();
        var result = friend.RestoreEverything();

        Assert.Equal([GamingTweaks.HighPerformancePlan.Id, PlaceboTweaks.DynamicTickOff.Id, "service:Spooler"], listed);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(PowerPlans.Balanced, _power.Active);
        Assert.Null(_boot.Get("disabledynamictick"));
        Assert.Equal(ServiceStart.Automatic, _services.Find("Spooler")!.Config.Start);
    }

    [Fact]
    public void Removing_an_app_is_an_account_change_and_stopping_Windows_installing_it_for_new_accounts_is_whole_PC()
    {
        _catalogue = new Catalogue([AppRemovalTweaks.RemoveCopilot, AppRemovalTweaks.DeprovisionPreinstalled]);
        _apps.Install(AppRemovalTweaks.Copilot.PackageName);
        _apps.Provision(AppRemovalTweaks.Copilot.PackageName);
        var aden = EngineFor(Aden);
        Applied(aden, AppRemovalTweaks.RemoveCopilot);
        Applied(aden, AppRemovalTweaks.DeprovisionPreinstalled, new ApplyOptions(ConfirmDestructive: true));

        var listed = EngineFor(Friend).ListHistory().Select(c => c.TweakId);

        Assert.Equal([AppRemovalTweaks.DeprovisionPreinstalled.Id], listed);
    }

    [Fact]
    public void A_journal_written_by_an_earlier_version_loads_unchanged_and_each_change_in_it_is_classified_correctly()
    {
        var file = Path.Combine(Path.GetTempPath(), $"elynt-journal-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, EarlierJournal.Json);
        try
        {
            var journal = new ChangeJournal(new JsonFileJournalStore(file));
            var aden = EngineFor(Aden, journal);
            var friend = EngineFor(Friend, journal);

            var adensHistory = aden.ListHistory();
            var friendsHistory = friend.ListHistory().Select(c => c.TweakId).ToList();

            Assert.Equal(17, adensHistory.Count);
            Assert.Equal(
                [
                    @"security.game-folder-scan-exclusion|C:\Games\Valorant\VALORANT.exe",
                    "gaming.placebo.hpet-off",
                    "gaming.placebo.core-parking-off",
                    "privacy.remove-recall",
                    "debloat.deprovision-preinstalled",
                    "gaming.high-performance-plan",
                    "gaming.placebo.dynamic-tick-off",
                    "gaming.placebo.nagle-off",
                    @"startup:task:\Vendor\Updater",
                    "service:Spooler",
                    "test.machine-wide",
                ],
                friendsHistory);
            Assert.Equal(EarlierJournal.Json, File.ReadAllText(file));

            // And it still works: the other account undoes the earlier version's machine-wide change exactly.
            _machine.SetValue(MachineValue, RegistryValue.DWord(1));
            var undone = friend.UndoChange(friend.ListHistory().Single(c => c.TweakId == MachineWide.Id).Id);
            Assert.True(undone.Succeeded, undone.Message);
            Assert.Null(_machine.GetValue(MachineValue));
            Assert.False(_machine.KeyExists(RegistryRoot.LocalMachine, MachineValue.KeyPath));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_game_profiles_temporary_change_stays_with_its_account_where_only_the_newest_change_can_be_undone()
    {
        const string game = @"C:\Games\Valorant\VALORANT.exe";
        Applied(EngineFor(Friend), GamingTweaks.HighPerformancePlan);
        _power.Active = PowerPlans.Balanced; // Windows switched back since.
        var aden = EngineFor(Aden);
        Assert.True(aden.SaveProfile(new GameProfile(game) { TweakIds = [GamingTweaks.HighPerformancePlan.Id] }).Succeeded);
        _processes.Run(game, 4242);
        Assert.True(aden.StartProfile(game, 4242).Succeeded);

        var permanent = Assert.Single(EngineFor(Friend).ListHistory());
        var adens = EngineFor(Aden).ListHistory();

        Assert.Equal([(false, true), (true, false)], adens.Select(c => (c.Id == permanent.Id, c.CanUndo)));
    }

    // ---- helpers ----

    /// <summary>The Engine the service builds for one account: the PC's adapters, that account's HKCU, the shared journal.</summary>
    private Engine EngineFor(string account, ChangeJournal? journal = null) => new(
        _catalogue,
        new SystemAdapters(new FakeAccountRegistry(_machine, Own(account)), _system)
        {
            Services = _services,
            Boot = _boot,
            Power = _power,
            Apps = _apps,
            Processes = _processes,
        },
        journal ?? _journal,
        account,
        profiles: _profiles);

    private FakeRegistry Own(string account) =>
        _accounts.TryGetValue(account, out var registry) ? registry : _accounts[account] = new FakeRegistry();

    private static TweakView Listed(Engine engine, TweakDefinition tweak) => engine.ListTweaks().Single(t => t.Id == tweak.Id);

    private static void Applied(Engine engine, TweakDefinition tweak, ApplyOptions? options = null)
    {
        var result = engine.Apply(tweak.Id, options);
        Assert.True(result.Succeeded, result.Message);
    }
}
