namespace Elynt.Core.Drivers;

/// <summary>How to clean a vendor's old display drivers.</summary>
public enum DriverCleanMethod
{
    /// <summary>Windows' pnputil: exports each package first, so the clean can be restored. For same-vendor reinstalls.</summary>
    Pnputil,

    /// <summary>
    /// Display Driver Uninstaller, a third-party tool downloaded from Wagnardsoft when used. A deep clean for vendor
    /// switches or broken installs. Opt-in: it needs the user's confirmation.
    /// </summary>
    Ddu,
}

/// <param name="Confirmed">The user accepted DDU's label. DDU refuses to run without it.</param>
public sealed record DriverCleanRequest(GpuVendor Vendor, DriverCleanMethod Method = DriverCleanMethod.Pnputil, bool Confirmed = false);

/// <param name="BackupId">The backup the clean exported to, which <c>RestoreDrivers</c> puts back.</param>
public sealed record DriverCleanResult(bool Succeeded, bool RebootNeeded, string Message, string? BackupId = null, string? Link = null);

/// <summary>Drivers exported before a clean.</summary>
/// <param name="Packages">The packages' vendor INF names and versions, e.g. "nv_dispi.inf 32.0.15.9174".</param>
public sealed record DriverBackup(string Id, GpuVendor Vendor, DateTimeOffset Created, IReadOnlyList<string> Packages);

/// <summary>An installed driver known to stop an anti-cheat from launching its games.</summary>
/// <param name="Driver">The driver file, e.g. iastorvd.sys.</param>
/// <param name="Warning">Names the games it stops and what to do about it.</param>
public sealed record ProblemDriver(string Name, string Driver, string? Version, string Warning);

/// <summary>What the Drivers page shows about cleaning: backups to restore, problem drivers, and DDU's label.</summary>
/// <param name="DduLabel">Says DDU is a third-party tool, downloaded when used, and what it does.</param>
public sealed record DriverCleaningReport(
    IReadOnlyList<DriverBackup> Backups,
    IReadOnlyList<ProblemDriver> ProblemDrivers,
    string DduLabel,
    string? Problem = null);
