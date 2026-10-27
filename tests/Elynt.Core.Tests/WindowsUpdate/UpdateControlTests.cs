using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests.WindowsUpdate;

/// <summary>The Windows Update section's controls, driven through the Engine.</summary>
public class UpdateControlTests
{
    private const string UxSettings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new() { OsBuild = "26100.4652", Edition = WindowsEdition.Pro };
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 19, 15, 30, 0, TimeSpan.Zero));

    private Engine NewEngine() => new(
        Catalogue.Default, new SystemAdapters(_registry, _system) { Clock = _clock }, _journal, "S-1-5-21-test", clock: _clock);

    private static RegistryLocation Machine(string key, string name) => new(RegistryRoot.LocalMachine, key, name);

    [Fact]
    public void Pausing_updates_pauses_for_at_most_35_days_from_today_and_is_undone_exactly()
    {
        var engine = NewEngine();
        var qualityEnd = Machine(UxSettings, "PauseQualityUpdatesEndTime");

        var applied = engine.Apply("update.pause");

        Assert.True(applied.Succeeded, applied.Message);
        Assert.Equal(RegistryValue.String("2026-10-19T00:00:00Z"), _registry.GetValue(Machine(UxSettings, "PauseQualityUpdatesStartTime")));
        Assert.Equal(RegistryValue.String("2026-11-23T00:00:00Z"), _registry.GetValue(qualityEnd));
        Assert.Equal(RegistryValue.String("2026-11-23T00:00:00Z"), _registry.GetValue(Machine(UxSettings, "PauseUpdatesExpiryTime")));
        Assert.Null(_registry.GetValue(Machine(UxSettings, "FlightSettingsMaxPauseDays")));
        Assert.Equal(TweakState.On, engine.ListTweaks().Single(t => t.Id == "update.pause").State);
        Assert.Contains(engine.ListHistory(), c => c.TweakId == "update.pause" && c.State == ChangeState.Applied);

        Assert.True(engine.UndoTweak("update.pause").Succeeded);
        Assert.Null(_registry.GetValue(qualityEnd));
        Assert.False(_registry.KeyExists(RegistryRoot.LocalMachine, UxSettings));
    }

    [Fact]
    public void A_pause_that_ran_out_reads_as_off_and_is_never_re_applied_as_drift()
    {
        var engine = NewEngine();
        engine.SetAutoReapply(true);
        Assert.True(engine.Apply("update.pause").Succeeded);

        _clock.Advance(TimeSpan.FromDays(36));
        var drift = engine.CheckDrift();

        Assert.Equal(TweakState.Off, engine.ListTweaks().Single(t => t.Id == "update.pause").State);
        Assert.DoesNotContain(drift.Drifted, d => d.TweakId == "update.pause");
        Assert.DoesNotContain(drift.Reapplied, d => d.TweakId == "update.pause");
        Assert.Equal(RegistryValue.String("2026-11-23T00:00:00Z"), _registry.GetValue(Machine(UxSettings, "PauseQualityUpdatesEndTime")));
        Assert.Empty(engine.ReapplyDrift(["update.pause"]).Changes);
    }

    [Theory]
    [InlineData("26100.4652", "update.pin-24h2", "24H2")]
    [InlineData("26200.6899", "update.pin-25h2", "25H2")]
    public void Pinning_offers_only_this_PCs_own_version_and_sets_the_target_release_policy(string build, string id, string version)
    {
        _system.OsBuild = build;
        var engine = NewEngine();

        var pins = engine.ListTweaks().Where(t => t.Id.StartsWith("update.pin-")).ToList();
        Assert.Equal([id], pins.Select(p => p.Id));

        Assert.True(engine.Apply(id).Succeeded);
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(Machine(Policies, "TargetReleaseVersion")));
        Assert.Equal(RegistryValue.String(version), _registry.GetValue(Machine(Policies, "TargetReleaseVersionInfo")));
        Assert.Equal(RegistryValue.String("Windows 11"), _registry.GetValue(Machine(Policies, "ProductVersion")));
    }

    [Fact]
    public void A_build_with_no_known_version_offers_no_pin()
    {
        _system.OsBuild = "27000.1";

        Assert.DoesNotContain(NewEngine().ListTweaks(), t => t.Id.StartsWith("update.pin-"));
    }

    /// <summary>
    /// Microsoft's Policy CSP lists the Windows Update for Business policies (target version, quality deferral) for
    /// Pro, Enterprise and Education only.
    /// </summary>
    [Theory]
    [InlineData("update.defer-quality-7")]
    [InlineData("update.defer-quality-30")]
    public void Business_policies_are_not_applicable_on_Home_and_are_refused(string id)
    {
        _system.Edition = WindowsEdition.Home;
        var engine = NewEngine();

        var view = engine.ListTweaks().Single(t => t.Id == id);
        Assert.False(view.Applicable);
        Assert.Contains("Home", view.NotApplicableReason);
        Assert.False(engine.Apply(id).Succeeded);
    }

    [Fact]
    public void Pinning_is_not_offered_on_Home_and_is_refused()
    {
        _system.Edition = WindowsEdition.Home;
        var engine = NewEngine();

        Assert.DoesNotContain(engine.ListTweaks(), t => t.Id.StartsWith("update.pin-"));
        var result = engine.Apply("update.pin-24h2");
        Assert.False(result.Succeeded);
        Assert.Contains("Home", result.Message);
    }

    [Theory]
    [InlineData("update.pause")]
    [InlineData("update.active-hours")]
    [InlineData("update.no-auto-restart")]
    public void Settings_app_controls_work_on_Home(string id)
    {
        _system.Edition = WindowsEdition.Home;

        Assert.True(NewEngine().Apply(id).Succeeded);
    }

    /// <summary>
    /// Driver exclusion and Delivery Optimization also write the setting Home honours: the Device Installation
    /// Settings switch and Delivery Optimization's own download mode, besides the Pro-only policy.
    /// </summary>
    [Theory]
    [InlineData("update.exclude-drivers", @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0u)]
    [InlineData("update.delivery-optimization-sharing-off", @"SOFTWARE\Microsoft\Windows\DeliveryOptimization\Config", "DODownloadMode", 0u)]
    public void Driver_exclusion_and_update_sharing_work_on_Home_through_a_setting_Home_honours(string id, string key, string name, uint value)
    {
        _system.Edition = WindowsEdition.Home;
        var engine = NewEngine();

        Assert.True(engine.ListTweaks().Single(t => t.Id == id).Applicable);
        Assert.True(engine.Apply(id).Succeeded);
        Assert.Equal(RegistryValue.DWord(value), _registry.GetValue(Machine(key, name)));
    }

    /// <summary>Values from Microsoft's Policy CSP and "Manage device restarts after updates" pages.</summary>
    public static TheoryData<string, string, string, uint> PolicyValues() => new()
    {
        { "update.defer-quality-7", Policies, "DeferQualityUpdates", 1 },
        { "update.defer-quality-7", Policies, "DeferQualityUpdatesPeriodInDays", 7 },
        { "update.defer-quality-30", Policies, "DeferQualityUpdates", 1 },
        { "update.defer-quality-30", Policies, "DeferQualityUpdatesPeriodInDays", 30 },
        { "update.exclude-drivers", Policies, "ExcludeWUDriversInQualityUpdate", 1 },
        { "update.active-hours", UxSettings, "ActiveHoursStart", 8 },
        { "update.active-hours", UxSettings, "ActiveHoursEnd", 2 },
        { "update.active-hours", UxSettings, "SmartActiveHoursState", 0 },
        { "update.no-auto-restart", Policies + @"\AU", "NoAutoRebootWithLoggedOnUsers", 1 },
        { "update.no-auto-restart", Policies + @"\AU", "AUOptions", 4 },
        { "update.delivery-optimization-sharing-off", @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0 },
    };

    [Theory]
    [MemberData(nameof(PolicyValues))]
    public void Each_control_writes_its_documented_value(string id, string key, string name, uint value)
    {
        var engine = NewEngine();

        Assert.True(engine.Apply(id).Succeeded);

        Assert.Equal(RegistryValue.DWord(value), _registry.GetValue(Machine(key, name)));
        Assert.Equal(TweakState.On, engine.ListTweaks().Single(t => t.Id == id).State);
    }

    [Fact]
    public void Every_Windows_Update_control_is_a_reversible_journaled_tweak_that_never_blocks_quality_updates()
    {
        var controls = Catalogue.Default.Tweaks.Where(t => t.Section == Sections.WindowsUpdate).ToList();

        Assert.Equal(
            ["update.pause", "update.pin-24h2", "update.pin-25h2", "update.defer-quality-7", "update.defer-quality-30",
             "update.exclude-drivers", "update.active-hours", "update.no-auto-restart", "update.delivery-optimization-sharing-off"],
            controls.Select(t => t.Id));
        Assert.All(controls, t => Assert.Equal(Reversibility.Reversible, t.Reversibility));
        Assert.All(controls.SelectMany(t => t.Steps), step => Assert.Null(HardRules.Violation(step.Target)));
    }

    /// <summary>Changes that block updates or disable the update service. The spec rules all of them out.</summary>
    public static TheoryData<string, string, string, uint> UpdateBlockingValues() => new()
    {
        { "automatic updates off", Policies + @"\AU", "NoAutoUpdate", 1 },
        { "never check for updates", Policies + @"\AU", "AUOptions", 1 },
        { "redirected to a fake update server", Policies + @"\AU", "UseWUServer", 1 },
        { "Windows Update access removed", Policies, "DisableWindowsUpdateAccess", 1 },
        { "Microsoft's update servers cut off", Policies, "DoNotConnectToWindowsUpdateInternetLocations", 1 },
        { "quality updates deferred 31 days", Policies, "DeferQualityUpdatesPeriodInDays", 31 },
        { "update service disabled", @"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", 4 },
        { "update orchestrator disabled", @"SYSTEM\CurrentControlSet\Services\UsoSvc", "Start", 4 },
        { "pause limit raised past Windows' 35 days", UxSettings, "FlightSettingsMaxPauseDays", 18627 },
    };

    /// <summary>Fixed pause end dates in the Settings app's own key: a far-off date blocks quality updates.</summary>
    [Theory]
    [InlineData("PauseQualityUpdatesEndTime")]
    [InlineData("PauseUpdatesExpiryTime")]
    public void A_fixed_quality_update_pause_date_is_refused_even_in_Advanced(string name)
    {
        var tweak = new TweakDefinition(
            "test.pause-forever", Sections.WindowsUpdate, "Pause forever", "Test only.", "None.", [], EvidenceTier.Proven, "Puts it back.",
            [new RegistryValueStep(Machine(UxSettings, name), RegistryValue.String("2077-01-01T00:00:00Z"))]);
        var engine = new Engine(new Catalogue([tweak]), new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test");

        var result = engine.Apply(tweak.Id, new ApplyOptions(ConfirmDestructive: true, Advanced: true));

        Assert.False(result.Succeeded);
        Assert.Contains("is never allowed", result.Message);
        Assert.Null(_registry.GetValue(Machine(UxSettings, name)));
    }

    [Fact]
    public void A_pause_longer_than_35_days_is_refused()
    {
        Assert.NotNull(HardRules.Violation(new UpdatePauseTarget("PauseQualityUpdatesEndTime", 36)));
        Assert.Null(HardRules.Violation(new UpdatePauseTarget("PauseQualityUpdatesEndTime", 35)));
    }

    [Theory]
    [MemberData(nameof(UpdateBlockingValues))]
    public void Blocking_updates_is_refused_even_in_Advanced(string what, string key, string name, uint value)
    {
        var tweak = new TweakDefinition(
            "test.block-updates", Sections.WindowsUpdate, what, "Test only.", "None.", [], EvidenceTier.Proven, "Puts it back.",
            [new RegistryValueStep(Machine(key, name), RegistryValue.DWord(value))]);
        var engine = new Engine(new Catalogue([tweak]), new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test");

        var result = engine.Apply(tweak.Id, new ApplyOptions(ConfirmDestructive: true, Advanced: true));

        Assert.False(result.Succeeded, what);
        Assert.Contains("is never allowed", result.Message);
        Assert.Null(_registry.GetValue(Machine(key, name)));
    }

    [Fact]
    public void Turning_a_blocking_policy_back_off_is_allowed()
    {
        Assert.Null(HardRules.Violation(new RegistryTarget(Machine(Policies + @"\AU", "NoAutoUpdate"), RegistryValue.DWord(0))));
    }

    private const string Policies = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
}
