using Elynt.Core.Apps;
using Elynt.Core.Bios;
using Elynt.Core.Cleanup;
using Elynt.Core.Boot;
using Elynt.Core.Drivers;
using Elynt.Core.Games;
using Elynt.Core.LowLevel;
using Elynt.Core.Power;
using Elynt.Core.Registry;
using Elynt.Core.Startup;
using Elynt.Core.Repair;
using Elynt.Core.Sensors;
using Elynt.Core.Tweaks;

namespace Elynt.Core;

/// <summary>Everything the engine may touch on the PC. Real adapters wrap Windows; fakes hold in-memory state.</summary>
/// <param name="Hardware">Read-only board, CPU and memory facts. Without it, BIOS readouts show as unknown.</param>
/// <param name="Packages">winget. Without it, app installs report winget as missing.</param>
/// <param name="Repair">Runs the Repair section's tools. Without it, no repair tools are listed.</param>
/// <param name="Cleanup">The files cleanup measures and clears. Without it, a scan finds nothing.</param>
/// <param name="Drivers">GPU driver detection, lookup, download and install. Without it, the Drivers page is unavailable.</param>
/// <param name="Games">Where launchers record installed games. Without it, only games added by hand are known.</param>
public sealed record SystemAdapters(
    IRegistry Registry,
    ISystemInfo System,
    IHardwareInfo? Hardware = null,
    IPackageManager? Packages = null,
    IRepairSystem? Repair = null,
    DriverAdapters? Drivers = null,
    IGameSources? Games = null,
    ICleanupSystem? Cleanup = null)
{
    public IPackageManager Packages { get; init; } = Packages ?? new NoPackageManager();

    /// <summary>The Service Control Manager. Without it, no services are listed and boot drivers can't be detected.</summary>
    public Services.IServiceManager? Services { get; init; }

    /// <summary>Which devices the PC has, for hardware-aware service recommendations. Without it, none are made.</summary>
    public Services.IDevicePresence? Devices { get; init; }

    /// <summary>Startup sources, scheduled tasks, signatures and impact. Without it, the Startup page is empty.</summary>
    public StartupAdapters? Startup { get; init; }

    /// <summary>The current boot entry's settings (BCD). Tweaks with boot steps fail without it.</summary>
    public IBootConfig? Boot { get; init; }

    /// <summary>Power plans. Tweaks with power plan steps fail without it.</summary>
    public IPowerPlans? Power { get; init; }

    /// <summary>The optional PawnIO driver, installed only when the user turns it on. Without it, PawnIO is unavailable.</summary>
    public PawnIoAdapters? PawnIo { get; init; }

    /// <summary>Sensor sources that need no extra driver (vendor SDKs, then Windows), best first.</summary>
    public IReadOnlyList<ISensorSource> Sensors { get; init; } = [];

    /// <summary>Driver cleaning (pnputil, DDU) and problem-driver checks. Without it, cleaning is unavailable.</summary>
    public DriverCleaningAdapters? DriverCleaning { get; init; }

    /// <summary>Preinstalled apps the Debloat &amp; Privacy section removes. Without it, none show as installed.</summary>
    public IRemovableApps Apps { get; init; } = new NoRemovableApps();

    /// <summary>Windows optional features (e.g. Recall). Without it, this PC has none.</summary>
    public IOptionalFeatures Features { get; init; } = new NoOptionalFeatures();

    /// <summary>Running programs, for per-game profiles. Without it, no apps are closed and priority can't be set.</summary>
    public Profiles.IProcesses Processes { get; init; } = new Profiles.NoProcesses();

    /// <summary>The system timer's resolution, for per-game profiles.</summary>
    public Profiles.ITimerResolution Timer { get; init; } = new Profiles.NoTimerResolution();
    /// <summary>Plug and Play devices. Tweaks with device steps fail without it.</summary>
    public Devices.IDevices? PnpDevices { get; init; }

    /// <summary>Microsoft Defender's scan exclusions. Exclusion tweaks fail without it.</summary>
    public Security.IScanExclusions? ScanExclusions { get; init; }

    /// <summary>
    /// Microsoft Defender's Tamper Protection and running state, read only. Without it, the Defender-off tweak fails
    /// safe and refuses (it can't confirm Tamper Protection is off).
    /// </summary>
    public Security.IDefenderState? DefenderState { get; init; }

    /// <summary>
    /// The PC's clock, for steps whose value depends on today (the update pause's end date). The Engine uses it too
    /// unless it is given its own.
    /// </summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;
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
