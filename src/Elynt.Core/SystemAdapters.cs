using Elynt.Core.Bios;
using Elynt.Core.Registry;
using Elynt.Core.Repair;
using Elynt.Core.Tweaks;

namespace Elynt.Core;

/// <summary>Everything the engine may touch on the PC. Real adapters wrap Windows; fakes hold in-memory state.</summary>
/// <param name="Hardware">Read-only board, CPU and memory facts. Without it, BIOS readouts show as unknown.</param>
/// <param name="Repair">Runs the Repair section's tools. Without it, no repair tools are listed.</param>
public sealed record SystemAdapters(IRegistry Registry, ISystemInfo System, IHardwareInfo? Hardware = null, IRepairSystem? Repair = null);

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
