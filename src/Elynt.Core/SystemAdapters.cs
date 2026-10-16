using Elynt.Core.Apps;
using Elynt.Core.Registry;

namespace Elynt.Core;

/// <summary>Everything the engine may touch on the PC. Real adapters wrap Windows; fakes hold in-memory state.</summary>
public sealed record SystemAdapters(IRegistry Registry, ISystemInfo System, IPackageManager? Packages = null)
{
    /// <summary>winget. Falls back to one that reports itself missing when none is wired up.</summary>
    public IPackageManager Packages { get; init; } = Packages ?? new NoPackageManager();
}

public interface ISystemInfo
{
    /// <summary>The Windows build and revision, e.g. 26100.4652.</summary>
    string OsBuild { get; }
}
