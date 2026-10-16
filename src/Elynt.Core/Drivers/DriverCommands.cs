namespace Elynt.Core.Drivers;

/// <summary>Which GPU's driver to check or install, and how.</summary>
/// <param name="DeviceId">The GPU's PnP hardware ID, from <see cref="InstalledGpu.DeviceId"/>.</param>
/// <param name="CleanInstall">Reset the vendor's driver settings as part of the install.</param>
public sealed record DriverRequest(string DeviceId, DriverBranch Branch = DriverBranch.GameReady, bool CleanInstall = false);

/// <summary>The result of looking up a GPU's latest driver.</summary>
/// <param name="Found">The lookup returned a driver. When false, <see cref="VendorPage"/> is where to get one by hand.</param>
/// <param name="VendorPage">The vendor's driver page, offered whenever the lookup didn't give a trustworthy answer.</param>
public sealed record DriverCheck(
    bool Found,
    string Message,
    InstalledGpu? Gpu,
    DriverOffer? Latest,
    bool UpdateAvailable,
    string? VendorPage);

/// <summary>The result of downloading, verifying and installing a driver.</summary>
/// <param name="RebootNeeded">The installer succeeded but Windows must restart to finish; the app shows a reboot prompt.</param>
/// <param name="Version">The driver version that was installed, when one was.</param>
/// <param name="VendorPage">The vendor's driver page, offered when the app couldn't install a trustworthy download.</param>
public sealed record DriverInstallResult(
    bool Succeeded,
    bool RebootNeeded,
    string Message,
    string? Version = null,
    string? VendorPage = null);
