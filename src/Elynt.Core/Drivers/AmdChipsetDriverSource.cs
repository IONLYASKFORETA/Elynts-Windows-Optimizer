using System.Text.RegularExpressions;

namespace Elynt.Core.Drivers;

/// <summary>
/// AMD's latest Ryzen chipset driver, from the version file AMD's own auto-detect installer reads
/// (research/driver-install-flow.md). AMD's server only answers with an AMD Referer. Anything unexpected throws, and
/// the app links to AMD's page instead.
/// </summary>
public sealed partial class AmdChipsetDriverSource(HttpClient http) : IDriverSource
{
    private const string DriversPage = "https://www.amd.com/en/support/download/drivers.html";
    private const string VersionFeed = "https://drivers.amd.com/drivers/installer/chipset/version.txt";

    public GpuVendor Vendor => GpuVendor.Amd;

    public DriverKind Kind => DriverKind.Chipset;

    public string VendorPage => DriversPage;

    /// <summary>/S installs silently, per AMD's chipset release notes. The installer has no clean-install option.</summary>
    public InstallerProfile Installer { get; } = new(
        "Advanced Micro Devices, Inc.",
        ["/S"],
        [],
        InstallerProfile.StandardRebootCodes);

    public DriverOffer? FindLatest(DriverQuery query)
    {
        var feed = VendorWeb.Fetch(http, VersionFeed, DriversPage);
        var match = ChipsetVersion().Match(feed);
        if (!match.Success)
            throw new InvalidDataException("AMD's chipset version file didn't contain a version.");

        var version = match.Groups["version"].Value;
        return new DriverOffer(
            version,
            DriverBranch.GameReady,
            new Uri($"https://drivers.amd.com/drivers/AMD_Chipset_Software_{version}.exe"),
            Referer: new Uri(DriversPage));
    }

    /// <summary>AMD's chipset versions have four parts, e.g. 8.08.12.551.</summary>
    [GeneratedRegex(@"^\s*(?<version>\d+\.\d+\.\d+\.\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex ChipsetVersion();
}
