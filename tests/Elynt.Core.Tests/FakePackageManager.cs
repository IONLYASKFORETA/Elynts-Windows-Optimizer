using Elynt.Core.Apps;

namespace Elynt.Core.Tests;

/// <summary>An in-memory winget. Each install reports half-way progress, then finishes.</summary>
public sealed class FakePackageManager : IPackageManager
{
    private int _running;

    /// <summary>Every install asked for, in order, with the source it was asked from.</summary>
    public List<(string Id, string Source)> Installs { get; } = [];

    /// <summary>The most installs that were running at the same moment.</summary>
    public int MostAtOnce { get; private set; }

    /// <summary>What an install of a given id returns. Ids not listed install fine.</summary>
    public Dictionary<string, PackageInstallResult> Results { get; } = [];

    /// <summary>Ids whose install throws, as a COM or process failure would.</summary>
    public HashSet<string> Crashes { get; } = [];

    /// <summary>Whether winget is installed.</summary>
    public bool Available { get; set; } = true;

    /// <summary>Whether a bootstrap makes winget available.</summary>
    public bool BootstrapWorks { get; set; } = true;

    /// <summary>Whether a bootstrap throws, as a failed download would.</summary>
    public bool BootstrapThrows { get; set; }

    public int Bootstraps { get; private set; }

    /// <summary>What the community source offers to search.</summary>
    public List<PackageInfo> Community { get; } = [];

    /// <summary>Every search asked for, in order.</summary>
    public List<(string Query, string Source)> Searches { get; } = [];

    /// <summary>What is installed on the PC. Upgrades and uninstalls change it.</summary>
    public List<InstalledPackage> Installed { get; } = [];

    /// <summary>Every upgrade asked for, in order, with the source it was asked from.</summary>
    public List<(string Id, string Source)> Upgrades { get; } = [];

    /// <summary>Every uninstall asked for, in order.</summary>
    public List<string> Uninstalls { get; } = [];

    /// <summary>When set, a search throws this, as a broken source would.</summary>
    public Exception? SearchFails { get; set; }

    public bool IsAvailable() => Available;

    public void Bootstrap()
    {
        Bootstraps++;
        if (BootstrapThrows)
            throw new HttpRequestException("The download failed.");
        if (BootstrapWorks)
            Available = true;
    }

    public PackageInstallResult Install(string packageId, string source, IProgress<double> progress)
    {
        if (!Available)
            throw new InvalidOperationException("winget isn't installed.");
        MostAtOnce = Math.Max(MostAtOnce, Interlocked.Increment(ref _running));
        try
        {
            Installs.Add((packageId, source));
            progress.Report(0.5);
            if (Crashes.Contains(packageId))
                throw new InvalidOperationException("winget stopped responding.");
            return Results.GetValueOrDefault(packageId) ?? new(PackageInstallStatus.Installed);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    public IReadOnlyList<PackageInfo> Search(string query, string source)
    {
        if (!Available)
            throw new InvalidOperationException("winget isn't installed.");
        Searches.Add((query, source));
        if (SearchFails is not null)
            throw SearchFails;
        return Community
            .Where(p => p.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IReadOnlyList<InstalledPackage> ListInstalled() =>
        Available ? Installed.ToList() : throw new InvalidOperationException("winget isn't installed.");

    public PackageInstallResult Upgrade(string packageId, string source, IProgress<double> progress) =>
        Run(packageId, progress, () => Upgrades.Add((packageId, source)), done =>
        {
            var index = Installed.FindIndex(p => p.Id == packageId);
            if (index >= 0 && Installed[index].AvailableVersion is { } newer)
                Installed[index] = Installed[index] with { Version = newer, AvailableVersion = null };
        });

    public PackageInstallResult Uninstall(string packageId, IProgress<double> progress) =>
        Run(packageId, progress, () => Uninstalls.Add(packageId), done => Installed.RemoveAll(p => p.Id == packageId));

    private PackageInstallResult Run(string packageId, IProgress<double> progress, Action record, Action<PackageInstallResult> onSuccess)
    {
        if (!Available)
            throw new InvalidOperationException("winget isn't installed.");
        MostAtOnce = Math.Max(MostAtOnce, Interlocked.Increment(ref _running));
        try
        {
            record();
            progress.Report(0.5);
            if (Crashes.Contains(packageId))
                throw new InvalidOperationException("winget stopped responding.");
            var result = Results.GetValueOrDefault(packageId) ?? new(PackageInstallStatus.Installed);
            if (result.Status == PackageInstallStatus.Installed)
                onSuccess(result);
            return result;
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }
}
