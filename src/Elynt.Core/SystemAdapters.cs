using Elynt.Core.Registry;
using Elynt.Core.Repair;

namespace Elynt.Core;

/// <summary>Everything the engine may touch on the PC. Real adapters wrap Windows; fakes hold in-memory state.</summary>
public sealed record SystemAdapters(IRegistry Registry, ISystemInfo System, IRepairSystem Repair);

public interface ISystemInfo
{
    /// <summary>The Windows build and revision, e.g. 26100.4652.</summary>
    string OsBuild { get; }
}
