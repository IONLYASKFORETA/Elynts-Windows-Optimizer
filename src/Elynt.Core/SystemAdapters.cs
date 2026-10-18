using Elynt.Core.Apps;
using Elynt.Core.Bios;
using Elynt.Core.Drivers;
using Elynt.Core.Games;
using Elynt.Core.Registry;
using Elynt.Core.Repair;
using Elynt.Core.Tweaks;

namespace Elynt.Core;

/// <summary>Everything the engine may touch on the PC. Real adapters wrap Windows; fakes hold in-memory state.</summary>
/// <param name="Hardware">Read-only board, CPU and memory facts. Without it, BIOS readouts show as unknown.</param>
/// <param name="Packages">winget. Without it, app installs report winget as missing.</param>
/// <param name="Repair">Runs the Repair section's tools. Without it, no repair tools are listed.</param>
/// <param name="Drivers">GPU driver detection, lookup, download and install. Without it, the Drivers page is unavailable.</param>
/// <param name="Games">Where launchers record installed games. Without it, only games added by hand are known.</param>
public sealed record SystemAdapters(
    IRegistry Registry,
    ISystemInfo System,
    IHardwareInfo? Hardware = null,
    IPackageManager? Packages = null,
    IRepairSystem? Repair = null,
    DriverAdapters? Drivers = null,
    IGameSources? Games = null)
{
    public IPackageManager Packages { get; init; } = Packages ?? new NoPackageManager();

    /// <summary>Preinstalled apps the Debloat &amp; Privacy section removes. Without it, none show as installed.</summary>
    public IRemovableApps Apps { get; init; } = new NoRemovableApps();

    /// <summary>Windows optional features (e.g. Recall). Without it, this PC has none.</summary>
    public IOptionalFeatures Features { get; init; } = new NoOptionalFeatures();
}

public interface ISystemInfo
{
    /// <summary>The Windows build and revision, e.g. 26100.4652.</summary>
    string OsBuild { get; }

    /// <summary>The build number alone, e.g. 26100.</summary>
    int BuildNumber => int.TryParse(OsBuild.Split('.')[0], out var build) ? build : 0;

    WindowsEdition Edition { get; }

    HardwareTraits Hardware { get; }

    /// <summary>When Windows last started. Reboot-needing changes made after this are pending a reboot.</summary>
    DateTimeOffset BootTime { get; }
}
