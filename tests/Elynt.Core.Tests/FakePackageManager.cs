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
}
