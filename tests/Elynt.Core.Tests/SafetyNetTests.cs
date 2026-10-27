using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Safety;
using Elynt.Core.Tweaks;
using Elynt.Tests.Shared;

namespace Elynt.Core.Tests;

/// <summary>The safety net under the journal: a System Restore point and whole-store backups before changes.</summary>
public class SafetyNetTests
{
    private const string User = "S-1-5-21-test";
    private static readonly TweakDefinition Mouse = GamingTweaks.EnhancePointerPrecisionOff;

    private readonly FakeRegistry _registry = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeRestorePoints _restorePoints;
    private readonly FakeStoreBackups _backups;
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());
    private readonly SafetyNet _safetyNet;

    public SafetyNetTests()
    {
        _restorePoints = new(() => RegistrySnapshot.Take(_registry, Mouse));
        _backups = new(() => RegistrySnapshot.Take(_registry, Mouse));
        _safetyNet = new SafetyNet(_restorePoints, _backups, _clock);
    }

    [Fact]
    public void A_restore_point_is_made_before_the_first_change()
    {
        var before = RegistrySnapshot.Take(_registry, Mouse);

        var result = NewEngine().Apply(Mouse.Id);

        Assert.True(result.Succeeded, result.Message);
        var attempt = Assert.Single(_restorePoints.Attempts);
        Assert.Equal(before, attempt.RegistryAtTheTime);
        Assert.DoesNotContain("restore point", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_skipped_restore_point_is_reported_and_the_change_still_goes_ahead()
    {
        _restorePoints.Next = RestorePointResult.Skipped("Windows already made one in the last 24 hours.");

        var result = NewEngine().Apply(Mouse.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(TweakState.On, result.Tweak!.State);
        Assert.Contains("Windows didn't make a restore point", result.Message);
        Assert.Contains("already made one in the last 24 hours", result.Message);
    }

    [Fact]
    public void A_restore_point_that_fails_outright_is_reported_as_skipped()
    {
        _restorePoints.Throw = new InvalidOperationException("System Protection is turned off for C:.");

        var result = NewEngine().Apply(Mouse.Id);

        Assert.True(result.Succeeded);
        Assert.Contains("System Protection is turned off", result.Message);
    }

    [Fact]
    public void Only_one_restore_point_is_attempted_per_day()
    {
        var engine = NewEngine();
        engine.Apply(Mouse.Id);
        engine.UndoTweak(Mouse.Id);
        _clock.Advance(TimeSpan.FromHours(23));
        var second = engine.Apply(Mouse.Id);
        engine.UndoTweak(Mouse.Id);

        Assert.Single(_restorePoints.Attempts);
        Assert.DoesNotContain("restore point", second.Message, StringComparison.OrdinalIgnoreCase);

        _clock.Advance(TimeSpan.FromHours(2));
        engine.Apply(Mouse.Id);
        Assert.Equal(2, _restorePoints.Attempts.Count);
    }

    [Fact]
    public void A_skipped_restore_point_is_still_reported_for_later_changes_that_day()
    {
        _restorePoints.Next = RestorePointResult.Skipped("System Protection is turned off for C:.");
        var engine = NewEngine();
        engine.Apply(Mouse.Id);
        engine.UndoTweak(Mouse.Id);
        _clock.Advance(TimeSpan.FromHours(3));

        var later = engine.Apply(Mouse.Id);

        Assert.Single(_restorePoints.Attempts);
        Assert.Contains("Windows didn't make a restore point", later.Message);
        Assert.Contains("System Protection is turned off", later.Message);
    }

    [Fact]
    public void No_restore_point_when_nothing_is_going_to_change()
    {
        foreach (var step in Mouse.Steps.OfType<RegistryValueStep>())
            _registry.SetValue(step.Location, step.Desired);

        NewEngine().Apply(Mouse.Id);

        Assert.Empty(_restorePoints.Attempts);
    }

    [Fact]
    public void Whole_store_backups_exist_before_the_first_change_of_each_kind()
    {
        var before = RegistrySnapshot.Take(_registry, Mouse);
        var bootAndHosts = TweakTouching("test.boot-and-hosts", 0, BackupStore.BootConfiguration, BackupStore.Hosts);

        var result = NewEngine(new Catalogue([bootAndHosts])).Apply(bootAndHosts.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal([BackupStore.BootConfiguration, BackupStore.Hosts], _backups.Exports.Select(e => e.Store).Order());
        Assert.All(_backups.Exports, export => Assert.Equal(before, export.RegistryAtTheTime));
        Assert.Equal(2, _safetyNet.Backups.Count);
    }

    [Fact]
    public void Each_kind_is_backed_up_once_and_registry_tweaks_need_no_store_backup()
    {
        var hostsA = TweakTouching("test.hosts-a", 0, BackupStore.Hosts);
        var hostsB = TweakTouching("test.hosts-b", 1, BackupStore.Hosts);
        var power = TweakTouching("test.power", 2, BackupStore.PowerPlans);
        var engine = NewEngine(new Catalogue([Mouse, hostsA, hostsB, power]));

        engine.Apply(Mouse.Id);
        Assert.Empty(_backups.Exports);

        engine.Apply(hostsA.Id);
        engine.Apply(hostsB.Id);
        engine.Apply(power.Id);

        Assert.Equal([BackupStore.Hosts, BackupStore.PowerPlans], _backups.Exports.Select(e => e.Store));
    }

    [Fact]
    public void Boot_and_power_plan_tweaks_back_up_their_whole_store_first()
    {
        var boot = new FakeBootConfig();
        boot.Set("useplatformclock", "Yes");
        var power = new FakePowerPlans();
        var engine = new Engine(Catalogue.Default,
            new SystemAdapters(_registry, new FakeSystemInfo()) { Boot = boot, Power = power }, _journal, User, _safetyNet, _clock);

        Assert.True(engine.Apply(GamingTweaks.UsePlatformClockRemoved.Id).Succeeded);
        Assert.True(engine.Apply(GamingTweaks.HighPerformancePlan.Id).Succeeded);

        Assert.Equal([BackupStore.BootConfiguration, BackupStore.PowerPlans], _backups.Exports.Select(e => e.Store));
    }

    [Fact]
    public void A_power_setting_backs_up_the_power_plans()
    {
        Assert.Equal(BackupStore.PowerPlans, new Power.PowerSettingStep(Guid.NewGuid(), Guid.NewGuid(), "x", 1).Store);
        Assert.Equal(BackupStore.PowerPlans, new Power.PowerPlanStep(Guid.NewGuid(), "x").Store);
        Assert.Equal(BackupStore.BootConfiguration, new Boot.BootSettingStep("x", null).Store);
    }

    [Fact]
    public void Nothing_is_changed_when_a_needed_backup_cannot_be_made()
    {
        var boot = TweakTouching("test.boot", 0, BackupStore.BootConfiguration);
        var engine = NewEngine(new Catalogue([boot]));
        var before = RegistrySnapshot.Take(_registry, Mouse);
        _backups.FailFor = BackupStore.BootConfiguration;

        var result = engine.Apply(boot.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("boot configuration", result.Message);
        Assert.Contains("Nothing was changed", result.Message);
        Assert.Equal(before, RegistrySnapshot.Take(_registry, Mouse));
        Assert.Empty(engine.ListHistory());

        _backups.FailFor = null;
        Assert.True(engine.Apply(boot.Id).Succeeded);
        Assert.Single(_backups.Exports);
    }

    [Fact]
    public void A_refused_destructive_apply_makes_no_restore_point_or_backup()
    {
        var boot = TweakTouching("test.destructive-boot", 0, BackupStore.BootConfiguration) with
        {
            Reversibility = Reversibility.Destructive,
            DestructiveWarning = "Gone for good.",
        };
        var engine = NewEngine(new Catalogue([boot]));
        var before = RegistrySnapshot.Take(_registry, Mouse);

        var refused = engine.Apply(boot.Id);

        Assert.False(refused.Succeeded);
        Assert.Contains("separate confirmation", refused.Message);
        Assert.Empty(_restorePoints.Attempts);
        Assert.Empty(_backups.Exports);
        Assert.Equal(before, RegistrySnapshot.Take(_registry, Mouse));

        var confirmed = engine.Apply(boot.Id, new ApplyOptions(ConfirmDestructive: true));

        Assert.True(confirmed.Succeeded, confirmed.Message);
        Assert.Single(_restorePoints.Attempts);
        Assert.Equal([BackupStore.BootConfiguration], _backups.Exports.Select(e => e.Store));
    }

    [Fact]
    public void A_hard_rule_refusal_makes_no_restore_point_or_backup()
    {
        var forbidden = TweakTouching("test.forbidden-hosts", 0, BackupStore.Hosts) with
        {
            Steps =
            [
                ((RegistryValueStep)Mouse.Steps[0]) with { Desired = RegistryValue.String("x") },
                new ForbiddenStep(),
            ],
        };
        var engine = NewEngine(new Catalogue([forbidden]));

        var result = engine.Apply(forbidden.Id, new ApplyOptions(ConfirmDestructive: true, Advanced: true));

        Assert.False(result.Succeeded);
        Assert.Contains("is never allowed", result.Message);
        Assert.Empty(_restorePoints.Attempts);
        Assert.Empty(_backups.Exports);
    }

    /// <summary>A step on a whole store whose declared target breaks a hard rule. It must never run.</summary>
    private sealed record ForbiddenStep : TweakStep
    {
        public override ChangeTarget Target => new PackageTarget("Microsoft.MicrosoftEdge.Stable");
        public override BackupStore? Store => BackupStore.Hosts;
        public override string DescribeChange() => "Removes Edge.";
        public override bool IsApplied(SystemAdapters system) => false;
        public override StepRecord Capture(SystemAdapters system) => throw new InvalidOperationException("Ran a refused step.");
        public override void Apply(SystemAdapters system) => throw new InvalidOperationException("Ran a refused step.");
    }

    /// <summary>
    /// A test tweak whose steps stand in for steps on whole stores (BCD, hosts and so on). Each writes one of the
    /// mouse values so the test can see whether anything changed yet.
    /// </summary>
    private static TweakDefinition TweakTouching(string id, int mouseValue, params BackupStore[] stores) => new(
        id, Sections.Gaming, id, "Test only.", "None.", [], EvidenceTier.Proven, "Puts it back.",
        stores.Select(store => (TweakStep)new StoreStep(
            new RegistryValueStep(((RegistryValueStep)Mouse.Steps[mouseValue]).Location, RegistryValue.String($"{id}-{store}")),
            store)).ToList());

    private sealed record StoreStep(RegistryValueStep Inner, BackupStore Kind) : TweakStep
    {
        public override BackupStore? Store => Kind;
        public override ChangeTarget Target => Inner.Target;
        public override string DescribeChange() => Inner.DescribeChange();
        public override bool IsApplied(SystemAdapters system) => Inner.IsApplied(system);
        public override StepRecord Capture(SystemAdapters system) => Inner.Capture(system);
        public override void Apply(SystemAdapters system) => Inner.Apply(system);
    }

    private Engine NewEngine(Catalogue? catalogue = null) =>
        new(catalogue ?? Catalogue.Default, new SystemAdapters(_registry, new FakeSystemInfo()), _journal, User,
            _safetyNet, _clock);

    private sealed class FakeStoreBackups(Func<IReadOnlyList<string>> registryNow) : IStoreBackups
    {
        public List<(BackupStore Store, IReadOnlyList<string> RegistryAtTheTime)> Exports { get; } = [];
        public BackupStore? FailFor { get; set; }

        public string Export(BackupStore store)
        {
            if (store == FailFor)
                throw new IOException("bcdedit /export failed: access denied.");
            Exports.Add((store, registryNow()));
            return $@"C:\ProgramData\ElyntOptimizer\backups\{store}";
        }
    }

    private sealed class FakeRestorePoints(Func<IReadOnlyList<string>> registryNow) : IRestorePoints
    {
        public List<(string Description, IReadOnlyList<string> RegistryAtTheTime)> Attempts { get; } = [];
        public RestorePointResult Next { get; set; } = RestorePointResult.Created();
        public Exception? Throw { get; set; }

        public RestorePointResult TryCreate(string description)
        {
            Attempts.Add((description, registryNow()));
            if (Throw is not null)
                throw Throw;
            return Next;
        }
    }
}
