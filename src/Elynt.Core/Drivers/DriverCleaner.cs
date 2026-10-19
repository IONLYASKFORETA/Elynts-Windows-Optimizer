using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Elynt.Core.Drivers;

/// <summary>Cleans old display drivers with pnputil (exported first, so restorable) or opt-in DDU, and spots problem drivers.</summary>
internal sealed class DriverCleaner(DriverCleaningAdapters? adapters, ILogger log)
{
    /// <summary>The Display device setup class. pnputil prints the class name localised, but the GUID never changes.</summary>
    private const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";

    private const string Manifest = "backup.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public DriverCleaningReport Report()
    {
        if (adapters is null)
            return new DriverCleaningReport([], [], DduLabel, Unavailable.Message);

        var backups = Backups()
            .Select(b => new DriverBackup(b.Id, b.Vendor, b.Created, b.Packages.Select(p => $"{p.OriginalName} {p.Version}").ToList()))
            .ToList();
        IReadOnlyList<ProblemDriver> problems = [];
        string? problem = null;
        try
        {
            problems = ProblemDrivers.Find(adapters.KernelDrivers.Installed());
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Reading the installed kernel drivers failed");
            problem = $"Couldn't check for drivers that stop anti-cheats: {ex.Message}";
        }

        return new DriverCleaningReport(backups, problems, DduLabel, problem);
    }

    public const string DduLabel =
        "Display Driver Uninstaller (DDU) is a free third-party tool by Wagnardsoft (MIT licence), not part of this app. " +
        "It is downloaded from wagnardsoft.com only when you use it. It removes a GPU vendor's drivers, services, folders " +
        "and settings: use it when switching GPU vendor or when a driver install is broken. Your drivers are backed up first, " +
        "but DDU's other removals can't be undone by the app.";

    public DriverCleanResult Clean(DriverCleanRequest request)
    {
        if (adapters is null)
            return Unavailable;
        if (request.Method == DriverCleanMethod.Ddu)
            return RunDdu(request);

        var backup = Backup(request.Vendor, out var failure);
        if (backup is null)
            return failure ?? new DriverCleanResult(false, false, $"There are no {Name(request.Vendor)} display drivers to clean.");

        if (Delete(backup) is { } problem)
            return new DriverCleanResult(false, false, $"{problem} Restore the backup to put them back.", backup.Id);

        var count = backup.Packages.Count;
        return new DriverCleanResult(true, false,
            $"Removed {count} {Name(request.Vendor)} driver package{(count == 1 ? "" : "s")} after backing them up. Windows uses its basic display driver until a new one is installed.",
            backup.Id);
    }

    /// <summary>
    /// Before a same-vendor reinstall: backs up and removes the vendor's old display drivers. Returns why it couldn't,
    /// having put back anything it removed, or null when ready (<paramref name="backupId"/> is null when there was nothing to remove).
    /// </summary>
    public string? RemoveBeforeReinstall(GpuVendor vendor, out string? backupId)
    {
        backupId = null;
        if (adapters is null)
            return "Removing old drivers isn't available.";

        var backup = Backup(vendor, out var failure);
        if (backup is null)
            return failure?.Message;

        if (Delete(backup) is { } problem)
            return problem + PutBack(backup.Id);

        backupId = backup.Id;
        return null;
    }

    /// <summary>Restores a backup after a failed reinstall, and says how that went (as a sentence to append).</summary>
    public string PutBack(string? backupId)
    {
        if (backupId is null)
            return "";
        var restore = Restore(backupId);
        return restore.Succeeded
            ? " The old drivers were put back; restart the PC to finish."
            : $" The old drivers couldn't be put back automatically ({restore.Message}); restore them from the Drivers page.";
    }

    /// <summary>Deletes every package in the backup, stopping at the first failure, which it describes.</summary>
    private string? Delete(BackupManifest backup)
    {
        var deleted = 0;
        foreach (var package in backup.Packages)
        {
            try
            {
                adapters!.Store.Delete(package.PublishedName);
                deleted++;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Deleting driver package {Package} failed", package.PublishedName);
                return $"Removed {deleted} of {backup.Packages.Count} {Name(backup.Vendor)} driver packages, then couldn't remove {package.OriginalName}: {ex.Message}";
            }
        }

        log.LogInformation("Removed {Count} {Vendor} driver packages", deleted, backup.Vendor);
        return null;
    }

    public DriverCleanResult Restore(string backupId)
    {
        if (adapters is null)
            return Unavailable;
        if (Backups().FirstOrDefault(b => b.Id == backupId) is not { } backup)
            return new DriverCleanResult(false, false, "That driver backup doesn't exist any more.");

        try
        {
            adapters.Store.Restore(Path.Combine(adapters.BackupFolder, backup.Id));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Restoring driver backup {Backup} failed", backup.Id);
            return new DriverCleanResult(false, false, $"Couldn't restore the {Name(backup.Vendor)} drivers: {ex.Message}", backup.Id);
        }

        log.LogInformation("Restored driver backup {Backup}", backup.Id);
        return new DriverCleanResult(true, true,
            $"Put back {backup.Packages.Count} {Name(backup.Vendor)} driver package{(backup.Packages.Count == 1 ? "" : "s")}. Restart the PC to finish.",
            backup.Id);
    }

    /// <summary>Every backup with a readable manifest, newest first. Anything else in the folder is ignored.</summary>
    private IReadOnlyList<BackupManifest> Backups()
    {
        if (!Directory.Exists(adapters!.BackupFolder))
            return [];

        var backups = new List<BackupManifest>();
        foreach (var folder in Directory.EnumerateDirectories(adapters.BackupFolder))
        {
            try
            {
                var file = Path.Combine(folder, Manifest);
                if (File.Exists(file) &&
                    JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(file)) is { } manifest &&
                    manifest.Id == Path.GetFileName(folder))
                    backups.Add(manifest);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Skipping unreadable driver backup {Folder}", folder);
            }
        }

        return backups.OrderByDescending(b => b.Created).ToList();
    }

    /// <summary>
    /// Backs up the vendor's display drivers, then downloads DDU's setup from Wagnardsoft, refuses it unless Wagnardsoft
    /// signed it, installs it into the service's own folder and runs DDU silently for the vendor. DDU is never bundled.
    /// </summary>
    private DriverCleanResult RunDdu(DriverCleanRequest request)
    {
        var a = adapters!;
        var page = a.Ddu.ProjectPage;
        if (!request.Confirmed)
            return new DriverCleanResult(false, false, $"DDU only runs once you accept this: {DduLabel}", Link: page);
        if (request.Vendor == GpuVendor.Unknown)
            return new DriverCleanResult(false, false, "Choose NVIDIA, AMD or Intel for DDU to clean.");

        // A vendor switch may have nothing left in the store, so DDU still runs when there is nothing to back up.
        string? backupId = null;
        if (Backup(request.Vendor, out var failure) is { } backup)
            backupId = backup.Id;
        else if (failure is not null)
            return failure;

        DduOffer offer;
        try
        {
            offer = a.Ddu.FindLatest();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Looking up the latest DDU failed");
            return Failed($"Couldn't find the latest DDU: {ex.Message} Nothing was removed; you can get DDU from Wagnardsoft's page.");
        }

        if (offer.DownloadUrl.Scheme != Uri.UriSchemeHttps)
            return Failed($"The DDU download isn't HTTPS ({offer.DownloadUrl}), so it wasn't downloaded. Nothing was removed.");

        Directory.CreateDirectory(a.DduFolder);
        var setup = Path.Combine(a.DduFolder, $"ddu-{offer.Version}-{Guid.NewGuid():N}-setup.exe");
        var installFolder = Path.Combine(a.DduFolder, "DDU");
        try
        {
            try
            {
                log.LogInformation("Downloading DDU {Version} from {Url}", offer.Version, offer.DownloadUrl);
                a.Downloader.Download(offer.DownloadUrl, setup);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Downloading DDU failed");
                return Failed($"Couldn't download DDU: {ex.Message} Nothing was removed.");
            }

            if (VerifyDdu(setup, offer) is { } refusal)
            {
                log.LogWarning("Refused DDU download {Url}: {Refusal}", offer.DownloadUrl, refusal);
                return Failed($"{refusal} It was deleted and nothing was removed.");
            }

            try
            {
                // NSIS: /D= must be the last argument and is taken verbatim.
                var setupExit = a.Runner.RunInstaller(setup, ["/S", "/D=" + installFolder]);
                if (setupExit != 0)
                    return Failed($"DDU's setup failed with exit code {setupExit}. Nothing was removed.");

                // No -Restart (the app asks), and no -PreventWinUpdate: it changes a Windows Update policy the
                // journal wouldn't know about.
                string[] arguments = ["-Silent", $"-Clean{request.Vendor}"];
                log.LogInformation("Running DDU {Version} with {Arguments}", offer.Version, arguments);
                var exit = a.Runner.RunInstaller(Path.Combine(installFolder, Ddu.Exe), arguments);
                if (exit != 0)
                    return Failed($"DDU failed with exit code {exit}.{(backupId is null ? "" : " Your drivers were backed up first and can be restored.")}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Running DDU failed");
                return Failed($"Couldn't run DDU: {ex.Message}");
            }
        }
        finally
        {
            TryDeleteFile(setup);
        }

        return new DriverCleanResult(true, true,
            $"DDU removed the {Name(request.Vendor)} drivers. Restart the PC, then install the new driver." +
            (backupId is null ? "" : " The old drivers were backed up first."),
            backupId, page);

        DriverCleanResult Failed(string message) => new(false, false, message, backupId, page);
    }

    /// <summary>Why DDU's setup must not run, or null when its signature and/or published hash check out.</summary>
    private string? VerifyDdu(string path, DduOffer offer)
    {
        var a = adapters!;
        if (a.Ddu.Signer is null && offer.Sha256 is null)
            return "There's no signature or published hash to check the DDU download against.";

        if (a.Ddu.Signer is { } signer)
        {
            SignatureCheck signature;
            try
            {
                signature = a.Signatures.Check(path);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Checking DDU's signature failed");
                return $"Couldn't check DDU's signature: {ex.Message}";
            }

            if (!signature.Trusted || !string.Equals(signature.Signer?.Trim(), signer, StringComparison.OrdinalIgnoreCase))
                return $"The DDU download isn't validly signed by {signer}, so it may have been tampered with.";
        }

        if (offer.Sha256 is { } expected)
        {
            using var file = File.OpenRead(path);
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));
            if (!string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase))
                return "The DDU download doesn't match its published SHA-256 hash, so it may have been tampered with.";
        }

        return null;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't delete {Path}", path);
        }
    }

    private sealed record BackupManifest(string Id, GpuVendor Vendor, DateTimeOffset Created, IReadOnlyList<DriverPackage> Packages);

    /// <summary>
    /// Exports every display driver package of the vendor into a new backup folder. If any export fails, the backup is
    /// discarded and <paramref name="failure"/> says why, so nothing gets deleted without a copy. Null with no failure
    /// means the vendor has no display drivers to back up.
    /// </summary>
    private BackupManifest? Backup(GpuVendor vendor, out DriverCleanResult? failure)
    {
        failure = null;
        var store = adapters!.Store;
        IReadOnlyList<DriverPackage> packages;
        try
        {
            packages = store.Packages().Where(p => IsDisplayDriverOf(p, vendor)).ToList();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Listing the driver store failed");
            failure = new DriverCleanResult(false, false, $"Couldn't list the installed drivers: {ex.Message}");
            return null;
        }

        if (packages.Count == 0)
            return null;

        var created = DateTimeOffset.UtcNow;
        var id = $"{created:yyyyMMdd-HHmmss}-{vendor}-{Guid.NewGuid():N}"[..32];
        var folder = Path.Combine(adapters.BackupFolder, id);
        try
        {
            foreach (var package in packages)
                store.Export(package.PublishedName, Path.Combine(folder, Path.GetFileNameWithoutExtension(package.PublishedName)));
            var manifest = new BackupManifest(id, vendor, created, packages);
            File.WriteAllText(Path.Combine(folder, Manifest), JsonSerializer.Serialize(manifest, Json));
            log.LogInformation("Backed up {Count} {Vendor} driver packages to {Folder}", packages.Count, vendor, folder);
            return manifest;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Backing up the {Vendor} drivers failed", vendor);
            TryDeleteFolder(folder);
            failure = new DriverCleanResult(false, false, $"Couldn't back up the {Name(vendor)} drivers, so nothing was removed: {ex.Message}");
            return null;
        }
    }

    private static bool IsDisplayDriverOf(DriverPackage package, GpuVendor vendor) =>
        string.Equals(package.ClassGuid, DisplayClass, StringComparison.OrdinalIgnoreCase) && VendorOf(package.Provider) == vendor;

    private static GpuVendor VendorOf(string provider)
    {
        if (provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Nvidia;
        if (provider.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) ||
            provider.StartsWith("AMD", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("ATI Technologies", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Amd;
        if (provider.StartsWith("Intel", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Intel;
        return GpuVendor.Unknown;
    }

    private static string Name(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => "NVIDIA",
        GpuVendor.Amd => "AMD",
        GpuVendor.Intel => "Intel",
        _ => "unknown",
    };

    private void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't delete {Folder}", folder);
        }
    }

    private static DriverCleanResult Unavailable { get; } = new(false, false, "Driver cleaning isn't available.");
}
