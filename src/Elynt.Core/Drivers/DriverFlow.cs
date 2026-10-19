using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;

namespace Elynt.Core.Drivers;

/// <summary>Everything the driver flow may touch.</summary>
/// <param name="Inventory">Reads the display adapters and their installed drivers.</param>
/// <param name="Sources">One latest-driver lookup per vendor. A vendor without one falls back to its download page.</param>
/// <param name="DownloadFolder">Where installers are downloaded to; only the service can write there.</param>
/// <param name="Extractor">Unpacks NVIDIA's package for a lean install. Without it, lean installs are unavailable.</param>
public sealed record DriverAdapters(
    IDriverInventory Inventory,
    IReadOnlyList<IDriverSource> Sources,
    string DownloadFolder,
    IDownloader Downloader,
    ISignatureVerifier Signatures,
    IInstallerRunner Installer,
    IPackageExtractor? Extractor = null);

/// <summary>
/// Detects GPUs and the chipset driver, looks up their latest drivers, and downloads, verifies and installs them.
/// </summary>
internal sealed class DriverFlow(SystemAdapters system, ILogger log)
{
    private readonly DriverAdapters? _adapters = system.Drivers;

    public DriverReport Report()
    {
        if (_adapters is null)
            return new DriverReport([], "Driver detection isn't available.");
        try
        {
            return new DriverReport(_adapters.Inventory.DisplayAdapters().Select(Describe).ToList(), Chipset: Chipset());
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

        var (gpu, source) = (target.Gpu, target.Source);
        var query = new DriverQuery(target.Name, system.System.Hardware.HasFlag(HardwareTraits.Laptop), request.Branch);
        DriverOffer? offer;
        try
        {
            offer = source.FindLatest(query);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Looking up the latest driver for {Device} failed", target.Name);
            return new DriverCheck(false, $"Couldn't look up the latest driver: {ex.Message} Get it from the vendor's page instead.", gpu, null, false, source.VendorPage);
        }

        // Only NVIDIA GPUs have branches, so only their messages name one.
        var branch = source is { Vendor: GpuVendor.Nvidia, Kind: DriverKind.Graphics } ? $"{Describe(request.Branch)} " : "";
        if (offer is null)
            return new DriverCheck(false, $"The lookup found no {branch}driver for the {target.Name}. Get it from the vendor's page instead.", gpu, null, false, source.VendorPage);

        var (newer, message) = target.InstalledVersion switch
        {
            null when source.Kind == DriverKind.Chipset =>
                (true, $"The {target.Name} isn't installed. Driver {offer.Version} is available."),
            null =>
                (true, $"Driver {offer.Version} is available. The app can't tell which version you have (Windows reports {target.WindowsVersion}), so check before reinstalling."),
            var installed when IsNewer(offer.Version, installed) =>
                (true, $"Driver {offer.Version} is available; you have {installed}."),
            var installed =>
                (false, $"You have the latest {branch}driver ({installed})."),
        };
        return new DriverCheck(true, message, gpu, offer, newer, source.VendorPage);
    }

    /// <summary>
    /// Downloads the latest driver, refuses it unless it is signed by the vendor (and matches the vendor's hash when
    /// one is published), then runs the vendor's silent installer. The download is always deleted afterwards.
    /// </summary>
    public DriverInstallResult Install(DriverRequest request, IProgress<DriverInstallProgress>? progress = null)
    {
        progress?.Report(new(DriverInstallStage.LookingUp, "Looking up the latest driver…"));
        var check = Check(request);
        if (!check.Found || check.Latest is not { } offer || Find(request.DeviceId, out _) is not { } target)
            return new DriverInstallResult(false, false, check.Message, VendorPage: check.VendorPage);

        var source = target.Source;
        var vendorPage = source.VendorPage;
        var profile = source.Installer;
        if (offer.DownloadUrl.Scheme != Uri.UriSchemeHttps)
            return Rejected($"The lookup gave a download that isn't HTTPS ({offer.DownloadUrl}), so it wasn't downloaded.");

        var adapters = _adapters!;
        Directory.CreateDirectory(adapters.DownloadFolder);
        var path = Path.Combine(adapters.DownloadFolder, $"{source.Vendor}-{source.Kind}-{offer.Version}-{Guid.NewGuid():N}.exe");
        string? leanFolder = null;
        try
        {
            try
            {
                progress?.Report(new(DriverInstallStage.Downloading, $"Downloading driver {offer.Version}…"));
                log.LogInformation("Downloading driver {Version} for {Device} from {Url}", offer.Version, target.Name, offer.DownloadUrl);
                adapters.Downloader.Download(offer.DownloadUrl, path, offer.Referer);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Downloading {Url} failed", offer.DownloadUrl);
                return Rejected($"Couldn't download driver {offer.Version}: {ex.Message} Nothing was installed.");
            }

            progress?.Report(new(DriverInstallStage.Verifying, "Checking the download's signature…"));
            if (Verify(path, offer, profile) is { } refusal)
            {
                log.LogWarning("Refused driver download {Url}: {Refusal}", offer.DownloadUrl, refusal);
                return Rejected($"{refusal} The download was deleted and nothing was installed.");
            }

            var installer = path;
            if (request.Lean && source.Vendor == GpuVendor.Nvidia && source.Kind == DriverKind.Graphics)
            {
                progress?.Report(new(DriverInstallStage.Slimming, "Unpacking the driver and removing everything but the display driver…"));
                leanFolder = Path.ChangeExtension(path, null);
                var laptop = system.System.Hardware.HasFlag(HardwareTraits.Laptop);
                if (LeanInstall.Prepare(adapters, path, leanFolder, laptop, profile, log) is { } leanRefusal)
                    return Rejected($"{leanRefusal} Nothing was installed; untick lean install to install the full package.");
                installer = Path.Combine(leanFolder, "setup.exe");
            }

            var arguments = request.CleanInstall ? [.. profile.Arguments, .. profile.CleanInstallArguments] : profile.Arguments;
            int exitCode;
            try
            {
                progress?.Report(new(DriverInstallStage.Installing, $"Installing driver {offer.Version}. The screen may flicker…"));
                log.LogInformation("Installing driver {Version} for {Device} with {Arguments}", offer.Version, target.Name, arguments);
                exitCode = adapters.Installer.RunInstaller(installer, arguments);
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
            if (leanFolder is not null)
                LeanInstall.TryDeleteFolder(leanFolder, log);
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

        if (offer.Sha512 is { } sha512 && !Matches(path, sha512, System.Security.Cryptography.SHA512.HashData))
            return "The download doesn't match the SHA-512 hash the vendor published.";
        if (offer.Sha256 is { } sha256 && !Matches(path, sha256, System.Security.Cryptography.SHA256.HashData))
            return "The download doesn't match the SHA-256 hash the vendor published.";

        return null;
    }

    private static bool Matches(string path, string expected, Func<Stream, byte[]> hash)
    {
        using var file = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(hash(file)), expected.Trim(), StringComparison.OrdinalIgnoreCase);
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

    /// <summary>A device whose driver the flow can look up.</summary>
    /// <param name="InstalledVersion">The installed version in the lookup's format, or null when it can't be told or isn't installed.</param>
    /// <param name="Gpu">The GPU, or null for the chipset.</param>
    private sealed record Target(string Name, string? InstalledVersion, string WindowsVersion, InstalledGpu? Gpu, IDriverSource Source);

    private sealed record Problem(string Message, InstalledGpu? Gpu = null);

    /// <summary>The detected GPU (or the chipset) with this device ID and the lookup for it, or why there isn't one.</summary>
    private Target? Find(string deviceId, out Problem problem)
    {
        problem = new Problem("");
        if (_adapters is null)
        {
            problem = new Problem("Driver detection isn't available.");
            return null;
        }

        if (string.Equals(deviceId, DriverRequest.Chipset, StringComparison.OrdinalIgnoreCase))
        {
            if (Chipset() is not { } chipset)
            {
                problem = new Problem("The app can't tell who made this PC's CPU, so it can't pick a chipset driver.");
                return null;
            }

            if (SourceFor(chipset.Vendor, DriverKind.Chipset) is not { } chipsetSource)
            {
                problem = new Problem($"The app can't look up the {chipset.Name} yet.");
                return null;
            }

            return new Target(chipset.Name, chipset.DriverVersion, chipset.DriverVersion ?? "none", null, chipsetSource);
        }

        IReadOnlyList<DisplayAdapter> adapters;
        try
        {
            adapters = _adapters.Inventory.DisplayAdapters();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't read the display adapters");
            problem = new Problem($"Couldn't read the display adapters: {ex.Message}");
            return null;
        }

        var adapter = adapters.FirstOrDefault(a => string.Equals(a.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
        {
            problem = new Problem("That GPU isn't in this PC any more.");
            return null;
        }

        var gpu = Describe(adapter);
        if (SourceFor(gpu.Vendor, DriverKind.Graphics) is not { } source)
        {
            problem = new Problem($"The app can't look up drivers for the {gpu.Name} yet.", gpu);
            return null;
        }

        var installed = DriverVersions.Vendor(adapter.Vendor, adapter.DriverVersion, adapter.VendorVersion);
        return new Target(gpu.Name, installed, adapter.DriverVersion, gpu, source);
    }

    private IDriverSource? SourceFor(GpuVendor vendor, DriverKind kind) =>
        _adapters!.Sources.FirstOrDefault(s => s.Vendor == vendor && s.Kind == kind);

    /// <summary>The chipset driver package for the CPU's maker, or null when the maker isn't AMD or Intel.</summary>
    private InstalledChipset? Chipset()
    {
        var hardware = system.System.Hardware;
        var (vendor, name) =
            hardware.HasFlag(HardwareTraits.AmdCpu) ? (GpuVendor.Amd, "AMD chipset driver") :
            hardware.HasFlag(HardwareTraits.IntelCpu) ? (GpuVendor.Intel, "Intel chipset driver") :
            (GpuVendor.Unknown, "");
        if (vendor == GpuVendor.Unknown)
            return null;

        string? version = null;
        try
        {
            version = _adapters!.Inventory.ChipsetVersion(vendor);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't read the installed {Vendor} chipset driver version", vendor);
        }

        return new InstalledChipset(name, vendor, version);
    }

    private static InstalledGpu Describe(DisplayAdapter adapter) => new(
        adapter.Name,
        adapter.DeviceId,
        adapter.Vendor,
        DriverVersions.Display(adapter.Vendor, adapter.DriverVersion, adapter.VendorVersion),
        adapter.DriverVersion);

    private static bool IsNewer(string offered, string installed) =>
        Version.TryParse(offered, out var a) && Version.TryParse(installed, out var b)
            ? a > b
            : !string.Equals(offered, installed, StringComparison.OrdinalIgnoreCase);

    private static string Describe(DriverBranch branch) => branch == DriverBranch.Studio ? "Studio" : "Game Ready";
}
