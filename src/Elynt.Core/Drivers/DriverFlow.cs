using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;

namespace Elynt.Core.Drivers;

/// <summary>Everything the driver flow may touch.</summary>
/// <param name="Inventory">Reads the display adapters and their installed drivers.</param>
/// <param name="Sources">One latest-driver lookup per vendor. A vendor without one falls back to its download page.</param>
/// <param name="DownloadFolder">Where installers are downloaded to; only the service can write there.</param>
public sealed record DriverAdapters(
    IDriverInventory Inventory,
    IReadOnlyList<IDriverSource> Sources,
    string DownloadFolder,
    IDownloader Downloader,
    ISignatureVerifier Signatures,
    IInstallerRunner Installer);

/// <summary>Detects GPUs, looks up their latest drivers, and downloads, verifies and installs them.</summary>
internal sealed class DriverFlow(SystemAdapters system, ILogger log)
{
    private readonly DriverAdapters? _adapters = system.Drivers;

    public DriverReport Report()
    {
        if (_adapters is null)
            return new DriverReport([], "Driver detection isn't available.");
        try
        {
            return new DriverReport(_adapters.Inventory.DisplayAdapters().Select(Describe).ToList());
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't read the display adapters");
            return new DriverReport([], $"Couldn't read the display adapters: {ex.Message}");
        }
    }

    public DriverCheck Check(DriverRequest request)
    {
        if (Find(request.DeviceId, out var problem) is not { } target)
            return new DriverCheck(false, problem.Message, problem.Gpu, null, false, null);

        var (gpu, source) = target;
        var query = new DriverQuery(gpu.Name, system.System.Hardware.HasFlag(HardwareTraits.Laptop), request.Branch);
        DriverOffer? offer;
        try
        {
            offer = source.FindLatest(query);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Looking up the latest driver for {Gpu} failed", gpu.Name);
            return new DriverCheck(false, $"Couldn't look up the latest driver: {ex.Message} Get it from the vendor's page instead.", gpu, null, false, source.VendorPage);
        }

        if (offer is null)
            return new DriverCheck(false, $"The lookup found no {Describe(request.Branch)} driver for the {gpu.Name}. Get it from the vendor's page instead.", gpu, null, false, source.VendorPage);

        var newer = IsNewer(offer.Version, gpu.DriverVersion);
        var message = newer
            ? $"Driver {offer.Version} is available; you have {gpu.DriverVersion}."
            : $"You have the latest {Describe(request.Branch)} driver ({gpu.DriverVersion}).";
        return new DriverCheck(true, message, gpu, offer, newer, source.VendorPage);
    }

    /// <summary>
    /// Downloads the latest driver, refuses it unless it is signed by the vendor (and matches the vendor's hash when
    /// one is published), then runs the vendor's silent installer. The download is always deleted afterwards.
    /// </summary>
    public DriverInstallResult Install(DriverRequest request)
    {
        var check = Check(request);
        if (!check.Found || check.Latest is not { } offer || Find(request.DeviceId, out _) is not { } target)
            return new DriverInstallResult(false, false, check.Message, VendorPage: check.VendorPage);

        var (gpu, source) = target;
        var vendorPage = source.VendorPage;
        var profile = source.Installer;
        if (offer.DownloadUrl.Scheme != Uri.UriSchemeHttps)
            return Rejected($"The lookup gave a download that isn't HTTPS ({offer.DownloadUrl}), so it wasn't downloaded.");

        var adapters = _adapters!;
        Directory.CreateDirectory(adapters.DownloadFolder);
        var path = Path.Combine(adapters.DownloadFolder, $"{gpu.Vendor}-{offer.Version}-{Guid.NewGuid():N}.exe");
        try
        {
            try
            {
                log.LogInformation("Downloading driver {Version} for {Gpu} from {Url}", offer.Version, gpu.Name, offer.DownloadUrl);
                adapters.Downloader.Download(offer.DownloadUrl, path);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Downloading {Url} failed", offer.DownloadUrl);
                return Rejected($"Couldn't download driver {offer.Version}: {ex.Message} Nothing was installed.");
            }

            if (Verify(path, offer, profile) is { } refusal)
            {
                log.LogWarning("Refused driver download {Url}: {Refusal}", offer.DownloadUrl, refusal);
                return Rejected($"{refusal} The download was deleted and nothing was installed.");
            }

            var arguments = request.CleanInstall ? [.. profile.Arguments, .. profile.CleanInstallArguments] : profile.Arguments;
            int exitCode;
            try
            {
                log.LogInformation("Installing driver {Version} for {Gpu} with {Arguments}", offer.Version, gpu.Name, arguments);
                exitCode = adapters.Installer.RunInstaller(path, arguments);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Running the driver installer failed");
                return Rejected($"Couldn't run the driver installer: {ex.Message}");
            }

            log.LogInformation("Driver installer for {Version} exited with {ExitCode}", offer.Version, exitCode);
            if (exitCode == 0)
                return new DriverInstallResult(true, false, $"Installed driver {offer.Version}.", offer.Version);
            if (profile.RebootCodes.Contains(exitCode))
                return new DriverInstallResult(true, true, $"Installed driver {offer.Version}. Restart the PC to finish.", offer.Version);
            return Rejected($"The driver installer failed with exit code {exitCode}. Your current driver should still be in place.");
        }
        finally
        {
            TryDelete(path);
        }

        DriverInstallResult Rejected(string message) => new(false, false, message, VendorPage: vendorPage);
    }

    /// <summary>Why a downloaded installer must not run, or null when it is the vendor's own, untampered file.</summary>
    private string? Verify(string path, DriverOffer offer, InstallerProfile profile)
    {
        SignatureCheck signature;
        try
        {
            signature = _adapters!.Signatures.Check(path);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Checking the signature of {Path} failed", path);
            return $"Couldn't check the download's signature: {ex.Message}";
        }

        if (!signature.Trusted)
            return "The download isn't validly signed, so it may have been tampered with.";
        if (!string.Equals(signature.Signer?.Trim(), profile.Signer, StringComparison.OrdinalIgnoreCase))
            return $"The download is signed by \"{signature.Signer}\", not {profile.Signer}.";

        if (offer.Sha512 is { } expected)
        {
            using var file = File.OpenRead(path);
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(file));
            if (!string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase))
                return "The download doesn't match the SHA-512 hash the vendor published.";
        }

        return null;
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't delete the downloaded installer {Path}", path);
        }
    }

    private sealed record Target(InstalledGpu Gpu, IDriverSource Source);

    private sealed record Problem(string Message, InstalledGpu? Gpu = null);

    /// <summary>The detected GPU with this device ID and the lookup for its vendor, or why there isn't one.</summary>
    private Target? Find(string deviceId, out Problem problem)
    {
        problem = new Problem("");
        var report = Report();
        if (report.Problem is not null)
        {
            problem = new Problem(report.Problem);
            return null;
        }

        var gpu = report.Gpus.FirstOrDefault(g => string.Equals(g.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        if (gpu is null)
        {
            problem = new Problem("That GPU isn't in this PC any more.");
            return null;
        }

        var source = _adapters!.Sources.FirstOrDefault(s => s.Vendor == gpu.Vendor);
        if (source is null)
        {
            problem = new Problem($"The app can't look up drivers for the {gpu.Name} yet.", gpu);
            return null;
        }

        return new Target(gpu, source);
    }

    private static InstalledGpu Describe(DisplayAdapter adapter) => new(
        adapter.Name,
        adapter.DeviceId,
        adapter.Vendor,
        DriverVersions.Display(adapter.Vendor, adapter.DriverVersion),
        adapter.DriverVersion);

    private static bool IsNewer(string offered, string installed) =>
        Version.TryParse(offered, out var a) && Version.TryParse(installed, out var b)
            ? a > b
            : !string.Equals(offered, installed, StringComparison.OrdinalIgnoreCase);

    private static string Describe(DriverBranch branch) => branch == DriverBranch.Studio ? "Studio" : "Game Ready";
}
