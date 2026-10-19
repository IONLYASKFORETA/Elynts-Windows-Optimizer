using System.Text.RegularExpressions;

namespace Elynt.Core.Drivers;

/// <summary>
/// AMD's latest Adrenalin driver. AMD has no lookup API, so the version comes from the file name of the auto-detect
/// installer linked on AMD's drivers page, and the download is the combined ("-c") full package for that version
/// (research/driver-install-flow.md). The scrape is fragile, so anything unexpected throws and the app links to AMD's
/// page instead of guessing.
/// </summary>
public sealed partial class AmdGpuDriverSource(HttpClient http) : IDriverSource
{
    private const string DriversPage = "https://www.amd.com/en/support/download/drivers.html";

    public GpuVendor Vendor => GpuVendor.Amd;

    public string VendorPage => DriversPage;

    /// <summary>
    /// -INSTALL installs silently with no screen output, per AMD's command-line installation guide. The guide has no
    /// clean-install switch, so a clean install is left to a driver clean beforehand.
    /// </summary>
    public InstallerProfile Installer { get; } = new(
        "Advanced Micro Devices, Inc.",
        ["-INSTALL"],
        [],
        InstallerProfile.StandardRebootCodes);

    public DriverOffer? FindLatest(DriverQuery query)
    {
        if (NotInCombinedPackage().IsMatch(query.GpuName))
            return null;

        var page = VendorWeb.Fetch(http, DriversPage);
        var versions = MinimalSetup().Matches(page).Select(m => m.Groups["version"].Value).Distinct().ToList();
        var version = versions.Count switch
        {
            0 => throw new InvalidDataException("AMD's drivers page no longer links an installer the app recognises."),
            1 => versions[0],
            _ => throw new InvalidDataException($"AMD's drivers page links more than one driver version ({string.Join(", ", versions)})."),
        };

        return new DriverOffer(
            version,
            DriverBranch.GameReady,
            new Uri($"https://drivers.amd.com/drivers/whql-amd-software-adrenalin-edition-{version}-win11-c.exe"),
            Referer: new Uri(DriversPage));
    }

    /// <summary>
    /// GPUs the combined Adrenalin package doesn't cover: Polaris (RX 400/500), Vega and Radeon VII, which AMD moved to
    /// a separate maintenance driver, older GCN cards (R5/R7/R9, HD), and Radeon PRO cards, which take PRO drivers.
    /// Integrated "Radeon Graphics" on Ryzen APUs and RDNA cards (RX 5000 and later) stay in.
    /// </summary>
    [GeneratedRegex(@"\bRX\s*[45]\d{2}\b|\bVega\b|Radeon\s*VII|\bR[579]\s|\bHD\s*\d|\bPRO\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotInCombinedPackage();

    [GeneratedRegex(@"https://drivers\.amd\.com/drivers/[\w./-]*?amd-software-adrenalin-edition-(?<version>\d{2}\.\d{1,2}\.\d{1,2})-minimalsetup", RegexOptions.IgnoreCase)]
    private static partial Regex MinimalSetup();
}
