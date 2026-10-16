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
}

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
}
