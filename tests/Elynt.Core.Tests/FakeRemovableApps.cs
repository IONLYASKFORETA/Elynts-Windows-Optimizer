using Elynt.Core.Apps;

namespace Elynt.Core.Tests;

/// <summary>In-memory preinstalled apps for one user, plus the packages Windows provisions for new accounts.</summary>
public sealed class FakeRemovableApps : IRemovableApps
{
    private readonly HashSet<string> _installed = [];
    private readonly HashSet<string> _provisioned = [];

    /// <summary>Package names reinstalled, in order.</summary>
    public List<string> Reinstalls { get; } = [];

    /// <summary>Package names whose reinstall throws, as an offline PC or a withdrawn Store listing would.</summary>
    public HashSet<string> ReinstallFails { get; } = [];

    public void Install(string packageName) => _installed.Add(packageName);

    public void Provision(string packageName) => _provisioned.Add(packageName);

    public bool IsInstalled(string packageName) => _installed.Contains(packageName);

    public bool IsProvisioned(string packageName) => _provisioned.Contains(packageName);

    public bool IsInstalled(RemovableApp app) => _installed.Contains(app.PackageName);

    public void Remove(RemovableApp app) => _installed.Remove(app.PackageName);

    public void Reinstall(RemovableApp app)
    {
        if (ReinstallFails.Contains(app.PackageName))
            throw new InvalidOperationException($"Couldn't reach the Microsoft Store to reinstall {app.Name}.");
        Reinstalls.Add(app.PackageName);
        _installed.Add(app.PackageName);
    }

    public bool IsProvisioned(RemovableApp app) => _provisioned.Contains(app.PackageName);

    public void Deprovision(RemovableApp app) => _provisioned.Remove(app.PackageName);
}
