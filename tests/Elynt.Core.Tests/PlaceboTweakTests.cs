using Elynt.Core.Journal;
using Elynt.Core.Power;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The Gaming section's possible-placebo group, driven through the Engine.</summary>
public class PlaceboTweakTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakeBootConfig _boot = new();
    private readonly FakePowerPlans _power = new();
    private readonly FakeDevices _devices = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    private SystemAdapters Adapters() => new(_registry, _system) { Boot = _boot, Power = _power, Devices = _devices };

    private Engine NewEngine() => new(Catalogue.Default, Adapters(), _journal, "S-1-5-21-test");

    private TweakView View(string id) => NewEngine().ListTweaks().Single(t => t.Id == id);

    private const string SystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    public static TheoryData<string> PlaceboIds() => new(PlaceboTweaks.All.Select(t => t.Id));

    [Fact]
    public void The_group_holds_every_researched_possible_placebo_tweak()
    {
        Assert.Equal(
            [
                "gaming.placebo.core-parking-off", "gaming.placebo.msi-mode-gpu", "gaming.placebo.nagle-off",
                "gaming.placebo.network-throttling-off", "gaming.placebo.nic-interrupt-moderation-off",
                "gaming.placebo.mmcss-games", "gaming.placebo.win32-priority-separation",
                "gaming.placebo.dynamic-tick-off", "gaming.placebo.hpet-off", "gaming.placebo.standby-list-cleaner",
                "gaming.placebo.visual-effects-performance",
            ],
            PlaceboTweaks.All.Select(t => t.Id));
    }

    [Theory]
    [MemberData(nameof(PlaceboIds))]
    public void Each_placebo_tweak_is_listed_in_Gaming_with_why_it_is_likely_placebo_and_sources(string id)
    {
        var view = View(id);

        Assert.Equal(Sections.Gaming, view.Section);
        Assert.Equal(EvidenceTier.PossiblePlacebo, view.Tier);
        Assert.False(string.IsNullOrWhiteSpace(view.Evidence));
        Assert.NotEmpty(view.Sources);
        Assert.All(view.Sources, s => Assert.StartsWith("https://", s.Url));
    }

    [Fact]
    public void Network_throttling_is_turned_off_with_the_all_ones_index()
    {
        var result = NewEngine().Apply(PlaceboTweaks.NetworkThrottlingOff.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.DWord(0xFFFFFFFF),
            _registry.GetValue(new(RegistryRoot.LocalMachine, SystemProfile, "NetworkThrottlingIndex")));
    }

    [Fact]
    public void MMCSS_reserves_10_percent_not_the_popular_0_which_Windows_clamps_to_20()
    {
        var result = NewEngine().Apply(PlaceboTweaks.MmcssGames.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.DWord(10), _registry.GetValue(new(RegistryRoot.LocalMachine, SystemProfile, "SystemResponsiveness")));
        Assert.Equal(RegistryValue.String("High"),
            _registry.GetValue(new(RegistryRoot.LocalMachine, SystemProfile + @"\Tasks\Games", "Scheduling Category")));
        Assert.Contains("clamped to 20", result.Tweak!.Evidence);
    }

    [Fact]
    public void Dynamic_tick_is_turned_off_in_the_boot_settings_and_needs_a_reboot()
    {
        var result = NewEngine().Apply(PlaceboTweaks.DynamicTickOff.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Yes", _boot.Get("disabledynamictick"));
        Assert.True(result.Tweak!.PendingReboot);

        Assert.True(NewEngine().Revert(PlaceboTweaks.DynamicTickOff.Id).Succeeded);
        Assert.Null(_boot.Get("disabledynamictick"));
    }

    private const string Interfaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    [Fact]
    public void Nagle_is_turned_off_on_every_network_interface_and_undo_puts_each_back()
    {
        var wifi = new RegistryLocation(RegistryRoot.LocalMachine, Interfaces + @"\{11111111-1111-1111-1111-111111111111}", "TcpAckFrequency");
        var ethernet = new RegistryLocation(RegistryRoot.LocalMachine, Interfaces + @"\{22222222-2222-2222-2222-222222222222}", "TcpAckFrequency");
        _registry.SetValue(wifi, RegistryValue.DWord(2));
        _registry.CreateKey(RegistryRoot.LocalMachine, ethernet.KeyPath);
        var engine = NewEngine();

        var result = engine.Apply(PlaceboTweaks.NagleOff.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(TweakState.On, result.Tweak!.State);
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(wifi));
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(ethernet));
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(ethernet with { ValueName = "TCPNoDelay" }));

        Assert.True(engine.Revert(PlaceboTweaks.NagleOff.Id).Succeeded);
        Assert.Equal(RegistryValue.DWord(2), _registry.GetValue(wifi));
        Assert.Null(_registry.GetValue(ethernet));
        Assert.True(_registry.KeyExists(RegistryRoot.LocalMachine, ethernet.KeyPath));
    }

    [Fact]
    public void The_Nagle_tweak_says_whether_Windows_still_reads_TCPNoDelay()
    {
        Assert.Contains("TCPNoDelay", View(PlaceboTweaks.NagleOff.Id).Evidence);
        Assert.Contains("not documented", View(PlaceboTweaks.NagleOff.Id).Evidence);
    }

    private const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string GpuKey = @"SYSTEM\CurrentControlSet\Enum\PCI\VEN_10DE&DEV_2684&SUBSYS_00000000&REV_A1\4&1a2b3c4d&0&0008";
    private const string SoundKey = @"SYSTEM\CurrentControlSet\Enum\PCI\VEN_8086&DEV_7AD0&SUBSYS_00000000&REV_11\3&11583659&0&FB";
    private const string MsiProperties = @"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";

    [Fact]
    public void MSI_mode_is_forced_only_on_the_graphics_card_and_is_flagged_as_able_to_stop_boot()
    {
        _registry.SetValue(new(RegistryRoot.LocalMachine, GpuKey, "ClassGUID"), RegistryValue.String(DisplayClass));
        _registry.SetValue(new(RegistryRoot.LocalMachine, SoundKey, "ClassGUID"), RegistryValue.String("{4d36e96c-e325-11ce-bfc1-08002be10318}"));
        var engine = NewEngine();

        var result = engine.Apply(PlaceboTweaks.MsiModeGpu.Id, new ApplyOptions(ConfirmDestructive: true));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(new(RegistryRoot.LocalMachine, GpuKey + "\\" + MsiProperties, "MSISupported")));
        Assert.False(_registry.KeyExists(RegistryRoot.LocalMachine, SoundKey + @"\Device Parameters"));
        Assert.True(result.Tweak!.Risks.HasFlag(RiskFlags.CanStopBoot));
        Assert.Contains(result.Tweak.ExactChange, c => c.Contains("MSISupported"));

        Assert.True(engine.Revert(PlaceboTweaks.MsiModeGpu.Id).Succeeded);
        Assert.False(_registry.KeyExists(RegistryRoot.LocalMachine, GpuKey + @"\Device Parameters"));
    }

    private const string NetClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    [Fact]
    public void Interrupt_moderation_is_turned_off_only_on_adapters_that_offer_it()
    {
        var nic = new RegistryLocation(RegistryRoot.LocalMachine, NetClass + @"\0001", "*InterruptModeration");
        var virtualAdapter = new RegistryLocation(RegistryRoot.LocalMachine, NetClass + @"\0002", "DriverDesc");
        _registry.SetValue(nic, RegistryValue.String("1"));
        _registry.SetValue(virtualAdapter, RegistryValue.String("WAN Miniport"));
        var engine = NewEngine();

        var result = engine.Apply(PlaceboTweaks.NicInterruptModerationOff.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.String("0"), _registry.GetValue(nic));
        Assert.Null(_registry.GetValue(virtualAdapter with { ValueName = "*InterruptModeration" }));

        Assert.True(engine.Revert(PlaceboTweaks.NicInterruptModerationOff.Id).Succeeded);
        Assert.Equal(RegistryValue.String("1"), _registry.GetValue(nic));
    }

    [Fact]
    public void Undo_refuses_when_something_else_changed_one_interface_since()
    {
        var wifi = new RegistryLocation(RegistryRoot.LocalMachine, Interfaces + @"\{11111111-1111-1111-1111-111111111111}", "TcpAckFrequency");
        _registry.CreateKey(RegistryRoot.LocalMachine, wifi.KeyPath);
        var engine = NewEngine();
        engine.Apply(PlaceboTweaks.NagleOff.Id);
        _registry.SetValue(wifi, RegistryValue.DWord(5));

        var result = engine.Revert(PlaceboTweaks.NagleOff.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(RegistryValue.DWord(5), _registry.GetValue(wifi));
    }

    [Fact]
    public void Core_parking_is_turned_off_on_the_active_plan_for_mains_and_battery_and_undo_puts_back_both()
    {
        _power.SetSetting(PowerPlans.Balanced, PowerSettings.ProcessorSubgroup, PowerSettings.CoreParkingMinCores, PowerSource.Ac, 10);
        _power.SetSetting(PowerPlans.Balanced, PowerSettings.ProcessorSubgroup, PowerSettings.CoreParkingMinCores, PowerSource.Dc, 5);
        var engine = NewEngine();

        var result = engine.Apply(PlaceboTweaks.CoreParkingOff.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(TweakState.On, result.Tweak!.State);
        Assert.Equal(100u, _power.GetSetting(PowerPlans.Balanced, PowerSettings.ProcessorSubgroup, PowerSettings.CoreParkingMinCores, PowerSource.Ac));
        Assert.Equal(100u, _power.GetSetting(PowerPlans.Balanced, PowerSettings.ProcessorSubgroup, PowerSettings.CoreParkingMinCores, PowerSource.Dc));

        Assert.True(engine.Revert(PlaceboTweaks.CoreParkingOff.Id).Succeeded);
        Assert.Equal(10u, _power.GetSetting(PowerPlans.Balanced, PowerSettings.ProcessorSubgroup, PowerSettings.CoreParkingMinCores, PowerSource.Ac));
        Assert.Equal(5u, _power.GetSetting(PowerPlans.Balanced, PowerSettings.ProcessorSubgroup, PowerSettings.CoreParkingMinCores, PowerSource.Dc));
    }

    [Fact]
    public void Undo_puts_core_parking_back_on_the_plan_it_changed_even_after_a_plan_switch()
    {
        var engine = NewEngine();
        engine.Apply(PlaceboTweaks.CoreParkingOff.Id);
        _power.SetActive(PowerPlans.HighPerformance);

        Assert.True(engine.Revert(PlaceboTweaks.CoreParkingOff.Id).Succeeded);
        Assert.NotEqual(100u, _power.GetSetting(PowerPlans.Balanced, PowerSettings.ProcessorSubgroup, PowerSettings.CoreParkingMinCores, PowerSource.Ac));
    }

    [Fact]
    public void A_dual_CCD_X3D_CPU_is_warned_that_core_parking_off_defeats_V_Cache_steering()
    {
        _system.Hardware = HardwareTraits.AmdCpu | HardwareTraits.DualCcdX3dCpu;

        Assert.Contains(GamingTweaks.X3dCoreParking.Text, View(PlaceboTweaks.CoreParkingOff.Id).Warnings);
    }

    [Theory]
    [InlineData("gaming.placebo.nagle-off")]
    [InlineData("gaming.placebo.core-parking-off")]
    [InlineData("gaming.placebo.hpet-off")]
    public void Undo_still_works_after_the_service_restarts(string id)
    {
        _registry.CreateKey(RegistryRoot.LocalMachine, Interfaces + @"\{11111111-1111-1111-1111-111111111111}");
        _devices.Add(Hpet, @"ACPI\PNP0103", enabled: true);
        var tweak = Catalogue.Default.Get(id);
        var before = Elynt.Tests.Shared.SystemSnapshot.Take(Adapters(), tweak);
        var file = Path.Combine(Path.GetTempPath(), $"elynt-journal-{Guid.NewGuid():N}.json");
        try
        {
            var applied = new Engine(Catalogue.Default, Adapters(), new ChangeJournal(new JsonFileJournalStore(file)), "S-1-5-21-test").Apply(id);
            Assert.True(applied.Succeeded, applied.Message);

            var restarted = new Engine(Catalogue.Default, Adapters(), new ChangeJournal(new JsonFileJournalStore(file)), "S-1-5-21-test");
            var reverted = restarted.Revert(id);

            Assert.True(reverted.Succeeded, reverted.Message);
            Assert.Equal(before, Elynt.Tests.Shared.SystemSnapshot.Take(Adapters(), tweak));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private const string Hpet = @"ACPI\PNP0103\0";

    [Fact]
    public void HPET_is_disabled_in_Device_Manager_and_undo_enables_it_again()
    {
        _devices.Add(Hpet, @"ACPI\PNP0103", enabled: true);
        var engine = NewEngine();

        var result = engine.Apply(PlaceboTweaks.HpetOff.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(_devices.IsEnabled(Hpet));
        Assert.True(result.Tweak!.PendingReboot);

        Assert.True(engine.Revert(PlaceboTweaks.HpetOff.Id).Succeeded);
        Assert.True(_devices.IsEnabled(Hpet));
    }

    [Fact]
    public void Undo_refuses_when_the_HPET_device_has_gone_since()
    {
        _devices.Add(Hpet, @"ACPI\PNP0103", enabled: true);
        var engine = NewEngine();
        engine.Apply(PlaceboTweaks.HpetOff.Id);
        _devices.Remove(Hpet);

        var result = engine.Revert(PlaceboTweaks.HpetOff.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("PNP0103", result.Message);
    }
}
