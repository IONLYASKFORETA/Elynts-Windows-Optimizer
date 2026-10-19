namespace Elynt.Core.Drivers;

/// <summary>A driver package in Windows' driver store, as <c>pnputil /enum-drivers</c> lists it.</summary>
/// <param name="PublishedName">The store's name for it, e.g. oem12.inf.</param>
/// <param name="OriginalName">The vendor's INF name, e.g. nv_dispi.inf.</param>
/// <param name="Provider">The provider, e.g. NVIDIA or Advanced Micro Devices, Inc.</param>
/// <param name="ClassGuid">The device setup class, e.g. the Display class {4d36e968-…}. Not localised, unlike the class name.</param>
public sealed record DriverPackage(string PublishedName, string OriginalName, string Provider, string ClassGuid, string Version);

/// <summary>Windows' driver store, through pnputil.</summary>
public interface IDriverStore
{
    IReadOnlyList<DriverPackage> Packages();

    /// <summary>Copies the package out of the store into <paramref name="folder"/> (<c>pnputil /export-driver</c>). Throws on failure.</summary>
    void Export(string publishedName, string folder);

    /// <summary>Uninstalls the package from its devices and removes it from the store (<c>/delete-driver /uninstall /force</c>).</summary>
    void Delete(string publishedName);

    /// <summary>Adds and installs every exported package under <paramref name="folder"/> (<c>/add-driver *.inf /subdirs /install</c>).</summary>
    void Restore(string folder);
}

/// <summary>A kernel driver Windows is set to load.</summary>
/// <param name="Name">The driver file's name without ".sys", e.g. iastorvd.</param>
/// <param name="FileVersion">The file's version, or null when it can't be read.</param>
public sealed record KernelDriver(string Name, string? FileVersion);

/// <summary>Reads the kernel drivers that are installed and not disabled. It never changes anything.</summary>
public interface IKernelDrivers
{
    IReadOnlyList<KernelDriver> Installed();
}

/// <summary>The latest Display Driver Uninstaller (DDU) setup on Wagnardsoft's site.</summary>
/// <param name="Sha256">The setup's SHA-256 (hex) from an independent listing such as winget's manifest, when known.</param>
public sealed record DduOffer(string Version, Uri DownloadUrl, string? Sha256 = null);

public static class Ddu
{
    /// <summary>DDU's program, in the folder its setup installs to (unverified against a live install).</summary>
    public const string Exe = "Display Driver Uninstaller.exe";
}

/// <summary>Finds the latest DDU release. DDU is never bundled; it is downloaded only when the user opts in.</summary>
public interface IDduSource
{
    /// <summary>
    /// The organisation DDU's setup must be Authenticode-signed by, or null when it isn't known to be signed, in which
    /// case the offer's SHA-256 must match instead. With neither, the download is refused.
    /// </summary>
    string? Signer { get; }

    /// <summary>Wagnardsoft's DDU page, offered when the lookup or download fails.</summary>
    string ProjectPage { get; }

    /// <summary>Throws when the lookup fails.</summary>
    DduOffer FindLatest();
}

/// <summary>Everything driver cleaning may touch.</summary>
/// <param name="BackupFolder">Where pnputil exports go before anything is deleted, one folder per clean.</param>
/// <param name="DduFolder">Where DDU is downloaded and installed; only the service can write there.</param>
public sealed record DriverCleaningAdapters(
    IDriverStore Store,
    IKernelDrivers KernelDrivers,
    string BackupFolder,
    IDduSource Ddu,
    string DduFolder,
    IDownloader Downloader,
    ISignatureVerifier Signatures,
    IInstallerRunner Runner);
