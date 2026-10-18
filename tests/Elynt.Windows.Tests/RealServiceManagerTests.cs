using System.Security.Principal;
using Elynt.Core;
using Elynt.Core.Journal;
using Elynt.Core.Services;
using Elynt.Core.Tweaks;

namespace Elynt.Windows.Tests;

/// <summary>The real Service Control Manager on a real Windows PC. Skipped elsewhere.</summary>
public sealed class RealServiceManagerTests
{
    private readonly WindowsServiceManager _services = new();

    public RealServiceManagerTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the real Service Control Manager.");

    [Fact]
    public void Lists_services_and_boot_drivers_with_their_config()
    {
        var all = _services.List();

        var update = Assert.Single(all, s => s.Name.Equals("wuauserv", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ServiceKind.Service, update.Kind);
        Assert.NotEmpty(update.Config.BinaryPath);
        Assert.Contains(all, s => s.Kind == ServiceKind.Driver && s.Config.Start == ServiceStart.Boot);
    }

    [Fact]
    public void Every_boot_driver_found_is_protected_through_the_engine()
    {
        var engine = new Engine(Catalogue.Default,
            new SystemAdapters(new WindowsRegistry(WindowsIdentity.GetCurrent().User!.Value), new WindowsSystemInfo()) { Services = _services },
            new ChangeJournal(new InMemoryJournalStore()), "test");

        var bootDrivers = engine.ListServices().Where(s => s.Start == ServiceStart.Boot).ToList();

        Assert.NotEmpty(bootDrivers);
        Assert.All(bootDrivers, s => Assert.Equal(ServiceAccess.Protected, s.Access));
    }

    [Fact]
    public void Device_detection_answers_without_throwing()
    {
        var devices = new WindowsDevicePresence();

        _ = devices.Has(DeviceKind.Printer);
        _ = devices.Has(DeviceKind.BluetoothAdapter);
    }

    /// <summary>Changes the Print Spooler's start type and puts its whole configuration back. Needs admin rights.</summary>
    [Fact]
    public void A_start_type_change_round_trips_the_full_config()
    {
        Assert.SkipUnless(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator), "Needs admin rights.");
        var before = (_services.Find("Spooler") ?? throw new InvalidOperationException("No Print Spooler here.")).Config;
        var other = before.Start == ServiceStart.Manual ? ServiceStart.Automatic : ServiceStart.Manual;
        try
        {
            _services.Configure("Spooler", before with { Start = other });
            Assert.Equal(other, _services.Find("Spooler")!.Config.Start);
        }
        finally
        {
            _services.Configure("Spooler", before);
        }

        Assert.True(before.SameAs(_services.Find("Spooler")!.Config));
    }
}
