using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Services;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests.Services;

/// <summary>The services manager, driven through Engine commands with a fake Service Control Manager.</summary>
public class ServicesTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakeServiceManager _services = new();
    private readonly FakeDevicePresence _devices = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    private Engine NewEngine() =>
        new(Catalogue.Default, new SystemAdapters(_registry, _system) { Services = _services, Devices = _devices }, _journal, "S-1-5-21-test");

    private static ServiceView View(Engine engine, string name) =>
        engine.ListServices().Single(s => s.Name == name);

    [Fact]
    public void Every_service_is_listed_and_only_reviewed_ones_are_editable_in_the_normal_view()
    {
        _services.Add("Spooler", ServiceStart.Automatic, displayName: "Print Spooler")
            .Add("SomeVendorSvc", ServiceStart.Automatic)
            .Add("vgc", ServiceStart.Manual);

        var engine = NewEngine();

        var spooler = View(engine, "Spooler");
        Assert.Equal(ServiceAccess.Listed, spooler.Access);
        Assert.Contains("print", spooler.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([ServiceStart.Automatic, ServiceStart.Manual], spooler.NormalChoices);
        Assert.Equal([ServiceStart.Automatic, ServiceStart.Manual, ServiceStart.Disabled], spooler.AdvancedChoices);

        var vendor = View(engine, "SomeVendorSvc");
        Assert.Equal(ServiceAccess.AdvancedOnly, vendor.Access);
        Assert.Empty(vendor.NormalChoices);
        Assert.Equal([ServiceStart.Automatic, ServiceStart.Manual, ServiceStart.Disabled], vendor.AdvancedChoices);

        var vanguard = View(engine, "vgc");
        Assert.Equal(ServiceAccess.Protected, vanguard.Access);
        Assert.NotNull(vanguard.LockReason);
        Assert.Empty(vanguard.NormalChoices);
        Assert.Empty(vanguard.AdvancedChoices);
    }

    [Fact]
    public void Manual_is_recommended_for_services_whose_hardware_this_pc_lacks()
    {
        _services.Add("Spooler", ServiceStart.Automatic)
            .Add("bthserv", ServiceStart.Automatic)
            .Add("WSearch", ServiceStart.Automatic);
        _devices.Present.Add(DeviceKind.BluetoothAdapter);

        var engine = NewEngine();

        var spooler = View(engine, "Spooler").Recommendation;
        Assert.NotNull(spooler);
        Assert.Equal(ServiceStart.Manual, spooler.Start);
        Assert.Contains("printer", spooler.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(View(engine, "bthserv").Recommendation);
        Assert.Null(View(engine, "WSearch").Recommendation);
    }

    [Fact]
    public void A_service_already_on_manual_gets_no_recommendation()
    {
        _services.Add("bthserv", ServiceStart.Manual).Add("BTAGService", ServiceStart.Automatic);

        var engine = NewEngine();

        Assert.Null(View(engine, "bthserv").Recommendation);
        Assert.Contains("Bluetooth", View(engine, "BTAGService").Recommendation!.Reason);
    }

    [Fact]
    public void Without_device_detection_nothing_is_recommended()
    {
        _services.Add("Spooler", ServiceStart.Automatic);
        var engine = new Engine(Catalogue.Default, new SystemAdapters(_registry, _system) { Services = _services }, _journal, "S-1-5-21-test");

        Assert.Null(View(engine, "Spooler").Recommendation);
    }

    private static readonly ServiceConfig SpoolerConfig = new(
        ServiceStart.Automatic, DelayedAutoStart: true, ErrorControl: 2, BinaryPath: @"C:\Windows\System32\spoolsv.exe",
        Account: "LocalSystem", LoadOrderGroup: "SpoolerGroup")
    {
        Dependencies = ["RPCSS", "http"],
        TriggerStart = true,
    };

    [Fact]
    public void Changing_a_start_type_and_undoing_it_restores_the_full_service_config()
    {
        _services.Add(new ServiceInfo("Spooler", "Print Spooler", "", ServiceKind.Service, true, SpoolerConfig));
        var engine = NewEngine();

        var changed = engine.SetServiceStart("Spooler", ServiceStart.Manual);

        Assert.True(changed.Succeeded, changed.Message);
        Assert.Equal(ServiceStart.Manual, _services.ConfigOf("Spooler").Start);
        Assert.Equal(ServiceStart.Manual, changed.Service!.Start);
        Assert.True(changed.Service.CanUndo);

        var undone = engine.UndoServiceChange("Spooler");

        Assert.True(undone.Succeeded, undone.Message);
        AssertSameConfig(SpoolerConfig, _services.ConfigOf("Spooler"));
        Assert.False(undone.Service!.CanUndo);
    }

    [Fact]
    public void A_service_change_is_in_the_undo_history_and_restore_everything_puts_it_back()
    {
        _services.Add(new ServiceInfo("Spooler", "Print Spooler", "", ServiceKind.Service, true, SpoolerConfig));
        var engine = NewEngine();
        engine.SetServiceStart("Spooler", ServiceStart.Manual);

        var change = Assert.Single(engine.ListHistory());
        Assert.Contains("Print Spooler", change.TweakName);
        Assert.True(change.CanUndo);

        var restore = engine.RestoreEverything();

        Assert.True(restore.Succeeded, restore.Message);
        AssertSameConfig(SpoolerConfig, _services.ConfigOf("Spooler"));
    }

    [Fact]
    public void A_service_change_survives_a_service_restart_through_the_saved_journal()
    {
        _services.Add(new ServiceInfo("Spooler", "Print Spooler", "", ServiceKind.Service, true, SpoolerConfig));
        var store = new InMemoryJournalStore();
        new Engine(Catalogue.Default, new SystemAdapters(_registry, _system) { Services = _services }, new ChangeJournal(store), "S-1-5-21-test")
            .SetServiceStart("Spooler", ServiceStart.Manual);

        var restarted = new Engine(Catalogue.Default, new SystemAdapters(_registry, _system) { Services = _services }, new ChangeJournal(store), "S-1-5-21-test");
        var change = Assert.Single(restarted.ListHistory());
        var undone = restarted.UndoChange(change.Id);

        Assert.True(undone.Succeeded, undone.Message);
        AssertSameConfig(SpoolerConfig, _services.ConfigOf("Spooler"));
    }

    [Fact]
    public void Disabled_needs_advanced_even_for_a_reviewed_service()
    {
        _services.Add("Spooler", ServiceStart.Automatic);
        var engine = NewEngine();

        var refused = engine.SetServiceStart("Spooler", ServiceStart.Disabled);
        Assert.False(refused.Succeeded);
        Assert.Contains("Advanced", refused.Message);
        Assert.Equal(0, _services.Writes);

        var advanced = engine.SetServiceStart("Spooler", ServiceStart.Disabled, new ApplyOptions(Advanced: true));
        Assert.True(advanced.Succeeded, advanced.Message);
        Assert.Equal(ServiceStart.Disabled, _services.ConfigOf("Spooler").Start);
    }

    [Fact]
    public void A_service_off_the_reviewed_list_needs_advanced()
    {
        _services.Add("SomeVendorSvc", ServiceStart.Automatic);
        var engine = NewEngine();

        var refused = engine.SetServiceStart("SomeVendorSvc", ServiceStart.Manual);
        Assert.False(refused.Succeeded);
        Assert.Contains("Advanced", refused.Message);
        Assert.Equal(ServiceStart.Automatic, _services.ConfigOf("SomeVendorSvc").Start);

        Assert.True(engine.SetServiceStart("SomeVendorSvc", ServiceStart.Manual, new ApplyOptions(Advanced: true)).Succeeded);
        Assert.Equal(ServiceStart.Manual, _services.ConfigOf("SomeVendorSvc").Start);
    }

    [Theory]
    [InlineData("vgc", ServiceStart.Manual, ServiceKind.Service)]
    [InlineData("wuauserv", ServiceStart.Manual, ServiceKind.Service)]
    [InlineData("SomeRaidDriver", ServiceStart.Boot, ServiceKind.Driver)]
    public void Protected_services_and_boot_drivers_never_change_even_in_advanced(string name, ServiceStart start, ServiceKind kind)
    {
        _services.Add(name, start, kind);
        var engine = NewEngine();

        var result = engine.SetServiceStart(name, ServiceStart.Disabled, new ApplyOptions(Advanced: true));

        Assert.False(result.Succeeded);
        Assert.Contains("never", result.Message);
        Assert.Equal(0, _services.Writes);
        Assert.Empty(engine.ListHistory());
    }

    [Fact]
    public void When_windows_refuses_the_change_nothing_is_left_to_undo()
    {
        _services.Add("Spooler", ServiceStart.Automatic);
        _services.Denied.Add("Spooler");
        var engine = NewEngine();

        var result = engine.SetServiceStart("Spooler", ServiceStart.Manual);

        Assert.False(result.Succeeded);
        Assert.Contains("Access is denied", result.Message);
        Assert.Equal(ServiceStart.Automatic, _services.ConfigOf("Spooler").Start);
        Assert.False(View(engine, "Spooler").CanUndo);
    }

    [Fact]
    public void Undo_refuses_to_overwrite_a_start_type_something_else_changed()
    {
        _services.Add("Spooler", ServiceStart.Automatic);
        var engine = NewEngine();
        engine.SetServiceStart("Spooler", ServiceStart.Manual);
        _services.ChangeOutsideTheApp("Spooler", _services.ConfigOf("Spooler") with { Start = ServiceStart.Disabled });

        var result = engine.UndoServiceChange("Spooler");

        Assert.False(result.Succeeded);
        Assert.Contains("Something else changed", result.Message);
        Assert.Equal(ServiceStart.Disabled, _services.ConfigOf("Spooler").Start);
    }

    [Fact]
    public void Undo_with_nothing_changed_says_so()
    {
        _services.Add("Spooler", ServiceStart.Automatic);

        var result = NewEngine().UndoServiceChange("Spooler");

        Assert.False(result.Succeeded);
        Assert.Contains("Nothing to undo", result.Message);
    }

    private static void AssertSameConfig(ServiceConfig expected, ServiceConfig actual)
    {
        Assert.Equal(expected with { Dependencies = [] }, actual with { Dependencies = [] });
        Assert.Equal(expected.Dependencies, actual.Dependencies);
    }

    [Fact]
    public void A_driver_that_starts_at_boot_is_protected_whatever_its_name()
    {
        _services.Add("SomeRaidDriver", ServiceStart.Boot, ServiceKind.Driver)
            .Add("SomeUsbDriver", ServiceStart.Manual, ServiceKind.Driver);

        var engine = NewEngine();

        Assert.Equal(ServiceAccess.Protected, View(engine, "SomeRaidDriver").Access);
        Assert.Contains("boot", View(engine, "SomeRaidDriver").LockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ServiceAccess.AdvancedOnly, View(engine, "SomeUsbDriver").Access);
    }

    [Fact]
    public void A_tweak_that_edits_a_boot_drivers_registry_key_is_refused()
    {
        _services.Add("SomeRaidDriver", ServiceStart.Boot, ServiceKind.Driver);
        var location = new RegistryLocation(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Services\SomeRaidDriver", "Start");
        var tweak = new TweakDefinition("test.raid", Sections.StartupAndServices, "Delay the RAID driver", "Test only.", "None.", [],
            EvidenceTier.PossiblePlacebo, "Puts it back.", [new RegistryValueStep(location, RegistryValue.DWord(3))]);
        var engine = new Engine(new Catalogue([tweak]), new SystemAdapters(_registry, _system) { Services = _services }, _journal, "S-1-5-21-test");

        var result = engine.Apply(tweak.Id, new ApplyOptions(Advanced: true));

        Assert.False(result.Succeeded);
        Assert.Contains("boot", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(_registry.GetValue(location));
    }
}
