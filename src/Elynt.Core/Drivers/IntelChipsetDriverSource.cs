using System.Text.RegularExpressions;

namespace Elynt.Core.Drivers;

/// <summary>
/// Intel's latest Chipset Device Software (the chipset INF utility), from Intel's download page 19347. The spec asks for
/// Intel's published SHA-512; when the page shows one the offer carries it, and otherwise it carries the SHA-256 the
/// page showed on 2026-10-16 (unverified.md). A page with neither fails the lookup, so it is never installed unchecked.
/// </summary>
public sealed partial class IntelChipsetDriverSource(HttpClient http) : IDriverSource
{
    private const string DownloadPage = "https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html";

    public GpuVendor Vendor => GpuVendor.Intel;

    public DriverKind Kind => DriverKind.Chipset;

    public string VendorPage => DownloadPage;

    /// <summary>
    /// /exenoui runs the setup without UI and passes /qn /norestart on to Windows Installer, per the readme of the same
    /// installer family (10.1.x, installer 3.1.7). Not yet confirmed against the client build on a real PC.
    /// </summary>
    public InstallerProfile Installer { get; } = new(
        "Intel Corporation",
        ["/exenoui", "/qn", "/norestart"],
        [],
        InstallerProfile.StandardRebootCodes);

    public DriverOffer? FindLatest(DriverQuery query)
    {
        var page = VendorWeb.Fetch(http, DownloadPage);
        var url = IntelDownloadPage.Meta(page, "RecommendedDownloadUrl")
            ?? SetupLinks().Matches(page).Select(m => m.Value).Distinct().SingleOrDefault()
            ?? throw new InvalidDataException("Intel's chipset page no longer links SetupChipset.exe.");
        var version = IntelDownloadPage.Version(page);
        var download = IntelDownloadPage.Download(url);
        return IntelDownloadPage.HasHash(page, "SHA512")
            ? new DriverOffer(version, DriverBranch.GameReady, download, Sha512: IntelDownloadPage.Hash(page, "SHA512", 128))
            : new DriverOffer(version, DriverBranch.GameReady, download, Sha256: IntelDownloadPage.Hash(page, "SHA256", 64));
    }

    [GeneratedRegex(@"https://downloadmirror\.intel\.com/\d+/SetupChipset\.exe", RegexOptions.IgnoreCase)]
    private static partial Regex SetupLinks();
}
