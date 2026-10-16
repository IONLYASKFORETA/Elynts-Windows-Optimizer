using Elynt.Core.Bios;
using Elynt.Core.Registry;

namespace Elynt.Core;

/// <summary>Everything the engine may touch on the PC. Real adapters wrap Windows; fakes hold in-memory state.</summary>
/// <param name="Hardware">Read-only board, CPU and memory facts. Without it, BIOS readouts show as unknown.</param>
public sealed record SystemAdapters(IRegistry Registry, ISystemInfo System, IHardwareInfo? Hardware = null);

public interface ISystemInfo
{
    /// <summary>The Windows build and revision, e.g. 26100.4652.</summary>
    string OsBuild { get; }
}
