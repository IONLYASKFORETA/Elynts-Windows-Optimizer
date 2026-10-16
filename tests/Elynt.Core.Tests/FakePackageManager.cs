using Elynt.Core.Apps;

namespace Elynt.Core.Tests;

/// <summary>An in-memory winget.</summary>
public sealed class FakePackageManager : IPackageManager
{
    public bool IsAvailable() => true;

    public void Bootstrap() { }

    public PackageInstallResult Install(string packageId, string source, IProgress<double> progress) =>
        new(PackageInstallStatus.Installed);
}
