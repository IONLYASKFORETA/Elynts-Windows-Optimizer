using Elynt.Core.Drivers;

namespace Elynt.Windows.Tests;

/// <summary>Reading <c>pnputil /enum-drivers</c> output, and the real read-only driver adapters on Windows.</summary>
public sealed class PnputilDriverStoreTests
{
    private const string English = """
        Microsoft PnP Utility

        Published Name:     oem12.inf
        Original Name:      nv_dispi.inf
        Provider Name:      NVIDIA
        Class Name:         Display adapters
        Class GUID:         {4d36e968-e325-11ce-bfc1-08002be10318}
        Driver Version:     09/24/2026 32.0.15.9174
        Signer Name:        Microsoft Windows Hardware Compatibility Publisher

        Published Name:     oem3.inf
        Original Name:      iigd_ext.inf
        Provider Name:      Intel Corporation
        Class Name:         Extensions
        Class GUID:         {e2f84ce7-8efa-411c-aa69-97454ca4cb57}
        Extension ID:       {3b5b8f1b-1a43-4ee5-a1f2-0d8f1cc6a6a7}
        Driver Version:     03/14/2026 32.0.101.6078
        Signer Name:        Microsoft Windows Hardware Compatibility Publisher

        """;

    // German Windows: the labels and date are localised, but the values come in the same order.
    private const string German = """
        Microsoft-PnP-Dienstprogramm

        Veröffentlichter Name:  oem7.inf
        Ursprünglicher Name:    u0412345.inf
        Anbietername:           Advanced Micro Devices, Inc.
        Klassenname:            Grafikkarten
        Klassen-GUID:           {4d36e968-e325-11ce-bfc1-08002be10318}
        Treiberversion:         24.09.2026 32.0.21025.10016
        Signaturgebername:      Microsoft Windows Hardware Compatibility Publisher
        """;

    [Fact]
    public void Reads_each_package_from_english_output()
    {
        var packages = PnputilDriverStore.Parse(English);

        Assert.Equal(
            [
                new DriverPackage("oem12.inf", "nv_dispi.inf", "NVIDIA", "{4d36e968-e325-11ce-bfc1-08002be10318}", "32.0.15.9174"),
                new DriverPackage("oem3.inf", "iigd_ext.inf", "Intel Corporation", "{e2f84ce7-8efa-411c-aa69-97454ca4cb57}", "32.0.101.6078"),
            ],
            packages);
    }

    [Fact]
    public void Reads_packages_whatever_the_display_language()
    {
        var package = Assert.Single(PnputilDriverStore.Parse(German));

        Assert.Equal(new DriverPackage("oem7.inf", "u0412345.inf", "Advanced Micro Devices, Inc.", "{4d36e968-e325-11ce-bfc1-08002be10318}", "32.0.21025.10016"), package);
    }

    [Fact]
    public void Lists_the_real_driver_store_and_kernel_drivers_without_changing_anything()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs real Windows.");

        var packages = new PnputilDriverStore().Packages();
        var drivers = new WindowsKernelDrivers().Installed();

        Assert.NotEmpty(packages);
        Assert.All(packages, p => Assert.Matches(@"^oem\d+\.inf$", p.PublishedName));
        Assert.Contains(drivers, d => d.Name.Equals("disk", StringComparison.OrdinalIgnoreCase));
    }
}
