using Elynt.Core.Drivers;

namespace Elynt.Core.Tests;

/// <summary>An in-memory driver store. Exports write a marker file, so a restore only brings back what was exported.</summary>
public sealed class FakeDriverStore : IDriverStore
{
    private readonly Dictionary<string, DriverPackage> _exported = new(StringComparer.OrdinalIgnoreCase);

    public List<DriverPackage> Installed { get; } = [];
    public List<string> Calls { get; } = [];
    public string? FailExportOf { get; set; }
    public string? FailDeleteOf { get; set; }

    public IReadOnlyList<DriverPackage> Packages() => Installed.ToList();

    public void Export(string publishedName, string folder)
    {
        Calls.Add($"export {publishedName}");
        if (string.Equals(publishedName, FailExportOf, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("pnputil couldn't export it.");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, publishedName), "exported");
        _exported[publishedName] = Installed.Single(p => p.PublishedName == publishedName);
    }

    public void Delete(string publishedName)
    {
        Calls.Add($"delete {publishedName}");
        if (string.Equals(publishedName, FailDeleteOf, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("pnputil couldn't delete it.");
        Installed.RemoveAll(p => p.PublishedName == publishedName);
    }

    public void Restore(string folder)
    {
        Calls.Add("restore");
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            if (_exported.TryGetValue(Path.GetFileName(file), out var package) && !Installed.Contains(package))
                Installed.Add(package);
    }
}

public sealed class FakeKernelDrivers : IKernelDrivers
{
    public List<KernelDriver> Drivers { get; } = [];

    public IReadOnlyList<KernelDriver> Installed() => Drivers;
}

public sealed class FakeDduSource : IDduSource
{
    public DduOffer Offer { get; set; } = new("18.1.6.1", new Uri("https://www.wagnardsoft.com/DDU/download/DDU%20v18.1.6.1_setup.exe"));
    public Exception? Fail { get; set; }
    public string? Signer { get; set; } = "Wagnardsoft";
    public string ProjectPage => "https://www.wagnardsoft.com/";

    public DduOffer FindLatest() => Fail is null ? Offer : throw Fail;
}

/// <summary>
/// Plays DDU's setup and DDU itself: the setup "installs" DDU into the folder given with /D=, and each run is recorded.
/// </summary>
public sealed class FakeDduRunner : IInstallerRunner
{
    public sealed record Run(string Path, IReadOnlyList<string> Arguments);

    public List<Run> Runs { get; } = [];
    public int SetupExitCode { get; set; }
    public int DduExitCode { get; set; }

    public int RunInstaller(string path, IReadOnlyList<string> arguments)
    {
        Runs.Add(new Run(path, arguments));
        if (arguments.FirstOrDefault(a => a.StartsWith("/D=", StringComparison.Ordinal)) is { } target)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("The setup wasn't downloaded.", path);
            var folder = target["/D=".Length..];
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, Ddu.Exe), "pretend DDU");
            return SetupExitCode;
        }

        if (!File.Exists(path))
            throw new FileNotFoundException("DDU isn't installed.", path);
        return DduExitCode;
    }
}
