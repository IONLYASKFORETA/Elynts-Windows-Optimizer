using Elynt.Core.Security;

namespace Elynt.Core.Tests;

/// <summary>Defender's scan exclusion list in memory. Windows compares paths case-insensitively.</summary>
public sealed class FakeScanExclusions : IScanExclusions
{
    public List<string> Paths { get; } = [];

    public bool Contains(string folder) => Paths.Contains(folder, StringComparer.OrdinalIgnoreCase);

    public void Add(string folder)
    {
        if (!Contains(folder))
            Paths.Add(folder);
    }

    public void Remove(string folder) => Paths.RemoveAll(p => p.Equals(folder, StringComparison.OrdinalIgnoreCase));
}
