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
    private readonly InMemoryJournalStore _store = new();
    private readonly FakeRemovableApps _apps = new();
    private readonly FakeOptionalFeatures _features = new();
    private FakeSystemInfo _system = new();

    private Engine NewEngine() => NewEngineWith(_store);

    public static TheoryData<string, StartingState> EveryTweakFromEveryStartingState()
    {
        var data = new TheoryData<string, StartingState>();
        // Removal for future accounts can't be put back by design; AppRemovalTests covers that undo refuses it.
        foreach (var id in CatalogueRoundTrip.ApplicableIds(Catalogue.Default)
                     .Where(id => !Catalogue.Default.Tweaks.Any(t => t.Id == id && t.Steps.Any(s => s is AppDeprovisionStep))))
            foreach (var state in Enum.GetValues<StartingState>())
                data.Add(id, state);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryTweakFromEveryStartingState))]
    public void Every_catalogue_tweak_reverts_to_the_exact_starting_state(string tweakId, StartingState start)
    {
        var tweak = Catalogue.Default.Get(tweakId);
        _system = FakeSystemInfo.Fitting(tweak.Applicability);
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

    private SystemAdapters Adapters() => new(_registry, _system, Packages: new FakePackageManager())
    {
        Boot = _boot,
        Power = _power,
        Apps = _apps,
        Features = _features,
    };

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
        // Apps a tweak removes start out installed, so there is something to remove and reinstall.
        foreach (var step in tweak.Steps.OfType<AppRemovalStep>())
            _apps.Install(step.App.PackageName);
        // Features a tweak switches start out the other way, on a PC that has them.
        foreach (var step in tweak.Steps.OfType<OptionalFeatureStep>())
            _features.Add(step.Feature, enabled: !step.Enabled);

        // Boot settings and power plans start away from what the tweak wants, so there is something to undo.
        foreach (var target in tweak.Steps.Select(s => s.Target))
        {
            if (target is BootSettingTarget boot)
                _boot.Set(boot.Element, boot.Value is null ? "Yes" : "Other");
            if (target is PowerPlanTarget)
                _power.Active = start == StartingState.OtherValue ? PowerPlans.PowerSaver : PowerPlans.Balanced;
        }

        foreach (var step in tweak.Steps.Select(s => s.Target).OfType<RegistryTarget>())
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
