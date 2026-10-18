namespace Elynt.Core.Apps;

/// <summary>
/// winget, as the engine sees it. The real adapter drives the Windows Package Manager; the fake holds in-memory
/// state. Calls block until done and are made one at a time.
/// </summary>
public interface IPackageManager
{
    /// <summary>Whether winget (App Installer) is present and usable.</summary>
    bool IsAvailable();

    /// <summary>Installs or repairs App Installer so winget works. Throws when it can't.</summary>
    void Bootstrap();

    /// <summary>Installs one package silently by its exact id from the named source.</summary>
    /// <param name="progress">Receives 0–1 as the download and install advance, when the adapter can tell.</param>
    PackageInstallResult Install(string packageId, string source, IProgress<double> progress);

    /// <summary>Searches one source by name, id or tag. Every match, reviewed or not.</summary>
    IReadOnlyList<PackageInfo> Search(string query, string source);

    /// <summary>Every app installed on the PC that winget can see, for the machine and for the signed-in user.</summary>
    IReadOnlyList<InstalledPackage> ListInstalled();

    /// <summary>Updates one installed package silently to the newest version in the named source.</summary>
    /// <returns><see cref="PackageInstallStatus.Installed"/> once updated; <see cref="PackageInstallStatus.AlreadyInstalled"/> when it was already up to date.</returns>
    PackageInstallResult Upgrade(string packageId, string source, IProgress<double> progress);

    /// <summary>Uninstalls one installed package silently.</summary>
    /// <returns><see cref="PackageInstallStatus.Installed"/> once removed; <see cref="PackageInstallStatus.AlreadyInstalled"/> when it was already gone.</returns>
    PackageInstallResult Uninstall(string packageId, IProgress<double> progress);
}

/// <summary>
/// An app winget sees on the PC. <paramref name="Source"/> is the source winget matched it to ("winget" for the
/// community source), or null when it only knows it from Windows' installed-programs list.
/// <paramref name="AvailableVersion"/> is the newer version that source offers, or null when it is up to date.
/// </summary>
public sealed record InstalledPackage(string Id, string Name, string Version, string? AvailableVersion, string? Source);

/// <summary>A package a source offers.</summary>
public sealed record PackageInfo(string Id, string Name, string Version);

public enum PackageInstallStatus
{
    Installed,
    AlreadyInstalled,
    Failed,
}

public sealed record PackageInstallResult(PackageInstallStatus Status, string? Error = null, bool RebootRequired = false);

/// <summary>Stands in when no package manager is wired up: every install reports winget as missing.</summary>
public sealed class NoPackageManager : IPackageManager
{
    public bool IsAvailable() => false;

    public void Bootstrap() => throw new NotSupportedException("App installs aren't available here.");

    public PackageInstallResult Install(string packageId, string source, IProgress<double> progress) =>
        new(PackageInstallStatus.Failed, "App installs aren't available here.");

    public IReadOnlyList<PackageInfo> Search(string query, string source) =>
        throw new NotSupportedException("App search isn't available here.");

    public IReadOnlyList<InstalledPackage> ListInstalled() =>
        throw new NotSupportedException("Listing installed apps isn't available here.");

    public PackageInstallResult Upgrade(string packageId, string source, IProgress<double> progress) =>
        new(PackageInstallStatus.Failed, "App updates aren't available here.");

    public PackageInstallResult Uninstall(string packageId, IProgress<double> progress) =>
        new(PackageInstallStatus.Failed, "App uninstalls aren't available here.");
}
