namespace Elynt.Core.Drivers;

/// <summary>NVIDIA's two driver branches. Other vendors only have Game Ready-style releases.</summary>
public enum DriverBranch
{
    GameReady,
    Studio,
}

/// <summary>What a driver is for: a GPU, or the PC's chipset (the platform drivers that come with the CPU).</summary>
public enum DriverKind
{
    Graphics,
    Chipset,
}

/// <summary>What a lookup needs to know about the device.</summary>
/// <param name="GpuName">The device name as Windows reports it, e.g. "NVIDIA GeForce RTX 4070", or the chipset's name.</param>
/// <param name="Laptop">The PC is a laptop, so the notebook driver applies.</param>
public sealed record DriverQuery(string GpuName, bool Laptop, DriverBranch Branch);

/// <summary>The latest driver a vendor offers for a GPU.</summary>
/// <param name="Version">In the vendor's own format, e.g. 617.42.</param>
/// <param name="Sha512">The SHA-512 the vendor publishes for the download, when it publishes one (hex).</param>
/// <param name="Referer">The page the download must claim to come from; AMD's servers refuse downloads without one.</param>
/// <param name="Sha256">The SHA-256 the vendor publishes for the download, when it publishes that instead (hex).</param>
public sealed record DriverOffer(
    string Version,
    DriverBranch Branch,
    Uri DownloadUrl,
    DateTimeOffset? Released = null,
    string? Sha512 = null,
    Uri? Referer = null,
    string? Sha256 = null);

/// <summary>
/// Looks up one vendor's latest driver. Every vendor route is undocumented, so each one is a swappable source, and
/// a failed lookup falls back to <see cref="VendorPage"/> rather than guessing a download.
/// </summary>
public interface IDriverSource
{
    GpuVendor Vendor { get; }

    /// <summary>Whether this source looks up GPU drivers or chipset drivers.</summary>
    DriverKind Kind => DriverKind.Graphics;

    /// <summary>The vendor's own driver download page, offered whenever the lookup can't be trusted.</summary>
    string VendorPage { get; }

    /// <summary>How the vendor's installer runs silently and what it signs with.</summary>
    InstallerProfile Installer { get; }

    /// <summary>The latest driver for the GPU, or null when the vendor has none for it. Throws when the lookup fails.</summary>
    DriverOffer? FindLatest(DriverQuery query);
}

/// <summary>How to run one vendor's driver installer silently, and how to read its result.</summary>
/// <param name="Signer">The organisation the installer must be Authenticode-signed by, e.g. "NVIDIA Corporation".</param>
/// <param name="Arguments">The silent-install arguments.</param>
/// <param name="CleanInstallArguments">Extra arguments for a clean install, which resets the vendor's settings.</param>
/// <param name="RebootCodes">Exit codes meaning "installed, restart to finish".</param>
public sealed record InstallerProfile(
    string Signer,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> CleanInstallArguments,
    IReadOnlyList<int> RebootCodes)
{
    /// <summary>Windows Installer's "restart required" (3010) and "restart started" (1641).</summary>
    public static IReadOnlyList<int> StandardRebootCodes { get; } = [3010, 1641];
}
