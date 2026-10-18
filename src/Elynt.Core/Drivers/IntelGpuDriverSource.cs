using System.Text.RegularExpressions;

namespace Elynt.Core.Drivers;

/// <summary>
/// Intel's latest Arc and Core Ultra graphics driver, from the meta fields on Intel's download page 785597
/// (research/driver-install-flow.md). Every offer carries the SHA-512 Intel publishes; a page without one fails the
/// lookup, so an Intel driver is never installed unchecked.
/// </summary>
public sealed partial class IntelGpuDriverSource(HttpClient http) : IDriverSource
{
    private const string DownloadPage = "https://www.intel.com/content/www/us/en/download/785597/intel-arc-graphics-windows.html";

    public GpuVendor Vendor => GpuVendor.Intel;

    public string VendorPage => DownloadPage;

    /// <summary>-s installs silently and --overwrite reinstalls over the same version, per Intel's support article.</summary>
    public InstallerProfile Installer { get; } = new(
        "Intel Corporation",
        ["-s", "--overwrite"],
        [],
        InstallerProfile.StandardRebootCodes);

    public DriverOffer? FindLatest(DriverQuery query)
    {
        if (!Covered().IsMatch(query.GpuName))
            return null;

        var page = VendorWeb.Fetch(http, DownloadPage);
        var url = IntelDownloadPage.Meta(page, "RecommendedDownloadUrl")
            ?? throw new InvalidDataException("Intel's download page no longer names the driver download.");
        return new DriverOffer(
            IntelDownloadPage.Version(page),
            DriverBranch.GameReady,
            IntelDownloadPage.Download(url),
            Sha512: IntelDownloadPage.Hash(page, "SHA512", 128));
    }

    /// <summary>
    /// The page covers Arc cards and Core Ultra graphics ("Intel(R) Arc(TM) …" and plain "Intel(R) Graphics"). Iris Xe,
    /// UHD and HD graphics take Intel's older driver, so the app links to Intel's page for them.
    /// </summary>
    [GeneratedRegex(@"\bArc\b|^\s*Intel(\(R\))?\s+Graphics\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Covered();
}
