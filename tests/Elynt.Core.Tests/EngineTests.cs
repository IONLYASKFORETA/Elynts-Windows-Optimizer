using Elynt.Core.Journal;
using Elynt.Core.Power;
using Elynt.Core.Tweaks;
using Elynt.Core.Registry;
using Elynt.Tests.Shared;

namespace Elynt.Core.Tests;

public class EngineTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeBootConfig _boot = new();
    private readonly FakePowerPlans _power = new();
    private readonly FakeDevices _devices = new();
    private readonly InMemoryJournalStore _store = new();

    private Engine NewEngine() => NewEngineWith(_store);

    public static TheoryData<string, StartingState> EveryTweakFromEveryStartingState()
    {
        var data = new TheoryData<string, StartingState>();
        foreach (var id in CatalogueRoundTrip.ApplicableIds(Catalogue.Default))
            foreach (var state in Enum.GetValues<StartingState>())
                data.Add(id, state);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryTweakFromEveryStartingState))]
    public void Every_catalogue_tweak_reverts_to_the_exact_starting_state(string tweakId, StartingState start)
    {
        var tweak = Catalogue.Default.Get(tweakId);
        Seed(tweak, start);

        CatalogueRoundTrip.Run(NewEngine(), Adapters(), tweak);
    }

    [Fact]
    public void A_tweak_already_in_place_shows_as_on_and_has_nothing_to_undo()
    {
        var tweak = GamingTweaks.EnhancePointerPrecisionOff;
        foreach (var step in tweak.Steps.OfType<RegistryValueStep>())
            _registry.SetValue(step.Location, step.Desired);
        var before = RegistrySnapshot.Take(_registry, tweak);
        var engine = NewEngine();

        var view = engine.ListTweaks().Single(t => t.Id == tweak.Id);
        Assert.Equal(TweakState.On, view.State);
        Assert.False(view.CanUndo);

        Assert.True(engine.Apply(tweak.Id).Succeeded);
        var revert = engine.Revert(tweak.Id);

        Assert.False(revert.Succeeded);
        Assert.Equal(TweakState.On, revert.Tweak!.State);
        Assert.Equal(before, RegistrySnapshot.Take(_registry, tweak));
    }

    [Fact]
    public void A_change_that_fails_part_way_is_put_back()
    {
        var tweak = GamingTweaks.EnhancePointerPrecisionOff;
        Seed(tweak, StartingState.OtherValue);
        var before = RegistrySnapshot.Take(_registry, tweak);
        var engine = NewEngine();

        _registry.FailOnceAfter(1);
        var result = engine.Apply(tweak.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(before, RegistrySnapshot.Take(_registry, tweak));
        Assert.Equal(TweakState.Off, engine.ListTweaks().Single(t => t.Id == tweak.Id).State);
        Assert.False(engine.ListTweaks().Single(t => t.Id == tweak.Id).CanUndo);
    }

    [Fact]
    public void A_change_interrupted_by_a_crash_can_be_undone_after_restart()
    {
        var tweak = GamingTweaks.EnhancePointerPrecisionOff;
        Seed(tweak, StartingState.OtherValue);
        var before = RegistrySnapshot.Take(_registry, tweak);
        var journalFile = Path.Combine(Path.GetTempPath(), $"elynt-journal-{Guid.NewGuid():N}.json");
        try
        {
            // The service dies after the first write: nothing after that point reaches the registry.
            _registry.DieAfter(1);
            NewEngineWith(new JsonFileJournalStore(journalFile)).Apply(tweak.Id);
            Assert.NotEqual(before, RegistrySnapshot.Take(_registry, tweak));

            _registry.Revive();
            var restarted = NewEngineWith(new JsonFileJournalStore(journalFile));
            var revert = restarted.Revert(tweak.Id);

            Assert.True(revert.Succeeded, revert.Message);
            Assert.Equal(before, RegistrySnapshot.Take(_registry, tweak));
        }
        finally
        {
            File.Delete(journalFile);
        }
    }

    [Fact]
    public void Undo_after_reapplying_leaves_nothing_older_to_undo()
    {
        var tweak = GamingTweaks.EnhancePointerPrecisionOff;
        Seed(tweak, StartingState.OtherValue);
        var engine = NewEngine();
        engine.Apply(tweak.Id);

        // Something else turns acceleration back on, then the user switches the tweak on again and off.
        Seed(tweak, StartingState.DifferentType);
        var userValues = RegistrySnapshot.Take(_registry, tweak);
        engine.Apply(tweak.Id);
        engine.Revert(tweak.Id);

        Assert.Equal(userValues, RegistrySnapshot.Take(_registry, tweak));
        Assert.False(engine.ListTweaks().Single(t => t.Id == tweak.Id).CanUndo);
        Assert.False(engine.Revert(tweak.Id).Succeeded);
        Assert.Equal(userValues, RegistrySnapshot.Take(_registry, tweak));
    }

    [Fact]
    public void Nothing_is_changed_when_the_prior_state_cannot_be_saved()
    {
        var tweak = GamingTweaks.EnhancePointerPrecisionOff;
        Seed(tweak, StartingState.OtherValue);
        var before = RegistrySnapshot.Take(_registry, tweak);
        var engine = NewEngineWith(new FailingJournalStore());

        var result = engine.Apply(tweak.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Nothing was changed", result.Message);
        Assert.Equal(before, RegistrySnapshot.Take(_registry, tweak));
        Assert.False(engine.ListTweaks().Single(t => t.Id == tweak.Id).CanUndo);
    }

    private sealed class FailingJournalStore : IJournalStore
    {
        public IReadOnlyList<JournalEntry> Load() => [];
        public void Save(IReadOnlyList<JournalEntry> entries) => throw new IOException("Disk full.");
    }

    [Fact]
    public void Undo_only_reverts_the_users_own_changes()
    {
        var tweak = GamingTweaks.EnhancePointerPrecisionOff;
        Seed(tweak, StartingState.OtherValue);
        var journal = new ChangeJournal(_store);
        new Engine(Catalogue.Default, Adapters(), journal, "S-1-5-21-someone-else").Apply(tweak.Id);

        var mine = new Engine(Catalogue.Default, Adapters(), journal, "S-1-5-21-test");

        Assert.False(mine.ListTweaks().Single(t => t.Id == tweak.Id).CanUndo);
        Assert.False(mine.Revert(tweak.Id).Succeeded);
    }

    [Fact]
    public void An_unknown_tweak_is_refused_without_changing_anything()
    {
        var engine = NewEngine();

        Assert.False(engine.Apply("no.such-tweak").Succeeded);
        Assert.False(engine.Revert("no.such-tweak").Succeeded);
    }

    [Fact]
    public void The_detail_pane_shows_the_exact_change()
    {
        var view = NewEngine().ListTweaks().Single(t => t.Id == GamingTweaks.EnhancePointerPrecisionOff.Id);

        Assert.Equal(
            [
                @"Set HKCU\Control Panel\Mouse\MouseSpeed to ""0"" (REG_SZ)",
                @"Set HKCU\Control Panel\Mouse\MouseThreshold1 to ""0"" (REG_SZ)",
                @"Set HKCU\Control Panel\Mouse\MouseThreshold2 to ""0"" (REG_SZ)",
            ],
            view.ExactChange);
        Assert.Equal(EvidenceTier.Proven, view.Tier);
        Assert.NotEmpty(view.Sources);
        Assert.Equal(Reversibility.Reversible, view.Reversibility);
        Assert.Equal(RiskFlags.None, view.Risks);
        Assert.True(view.Applicable);
    }

    [Fact]
    public void A_tweak_that_needs_no_reboot_is_never_pending_one_and_fits_any_Windows_11_PC()
    {
        var engine = NewEngine();
        var tweak = GamingTweaks.EnhancePointerPrecisionOff;

        var applied = engine.Apply(tweak.Id);

        Assert.False(applied.Tweak!.PendingReboot);
        Assert.True(applied.Tweak.Applicable);
    }

    private SystemAdapters Adapters() => new(_registry, new FakeSystemInfo()) { Boot = _boot, Power = _power, Devices = _devices };

    private Engine NewEngineWith(IJournalStore store) => new(Catalogue.Default, Adapters(), new ChangeJournal(store), "S-1-5-21-test");

    public enum StartingState
    {
        /// <summary>Each touched value exists with some other value of the same type.</summary>
        OtherValue,
        /// <summary>The keys exist but the values do not.</summary>
        ValuesAbsent,
        /// <summary>The keys themselves do not exist.</summary>
        KeysAbsent,
        /// <summary>The values exist with a different type than the tweak writes.</summary>
        DifferentType,
    }

    private void Seed(TweakDefinition tweak, StartingState start)
    {
        // Boot settings and power plans start away from what the tweak wants, so there is something to undo.
        foreach (var target in tweak.Steps.Select(s => s.Target))
        {
            if (target is BootSettingTarget boot)
                _boot.Set(boot.Element, boot.Value is null ? "Yes" : "Other");
            if (target is DeviceTarget device)
                _devices.Add($@"{device.HardwareId}\0", device.HardwareId, enabled: !device.Enabled);
            if (target is PowerSettingTarget setting && start == StartingState.OtherValue)
            {
                _power.SetSetting(PowerPlans.Balanced, setting.Subgroup, setting.Setting, PowerSource.Ac, 50);
                _power.SetSetting(PowerPlans.Balanced, setting.Subgroup, setting.Setting, PowerSource.Dc, 25);
            }
            if (target is PowerPlanTarget)
                _power.Active = start == StartingState.OtherValue ? PowerPlans.PowerSaver : PowerPlans.Balanced;
        }

        // Per-device and per-interface steps get one matching key, set up like a plain value below.
        var targets = tweak.Steps.Select(s => s.Target).OfType<RegistryTarget>().ToList();
        foreach (var step in tweak.Steps.OfType<RegistryEachKeyStep>())
        {
            var key = step.KeyPattern.Replace("*", "Seeded");
            if (step.Filter is { } filter)
                _registry.SetValue(new(step.Root, key, filter.ValueName), RegistryValue.String(filter.EqualsText ?? "1"));
            else
                _registry.CreateKey(step.Root, key);
            var location = new RegistryLocation(step.Root, step.SubPath is null ? key : $@"{key}\{step.SubPath}", step.ValueName);
            if (start is StartingState.OtherValue or StartingState.DifferentType)
                targets.Add(new RegistryTarget(location, step.Desired));
        }

        foreach (var step in targets)
        {
            var location = step.Location;
            switch (start)
            {
                case StartingState.OtherValue:
                    // A leading "1" makes it a different value (also inside a list of settings); the extra bytes after
                    // the terminator check that raw bytes survive, not a parsed string.
                    _registry.SetValue(location, new RegistryValue(step.Desired.Kind, [0x31, 0x00, .. step.Desired.Data, 0x31, 0x00]));
                    break;
                case StartingState.ValuesAbsent:
                    _registry.CreateKey(location.Root, location.KeyPath);
                    break;
                case StartingState.KeysAbsent:
                    break;
                case StartingState.DifferentType:
                    _registry.SetValue(location, new RegistryValue(RegistryValueKind.Binary, [0xAB, 0xCD]));
                    break;
            }
        }
    }
}
