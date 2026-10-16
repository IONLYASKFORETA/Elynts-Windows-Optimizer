using System.Text.RegularExpressions;
using Elynt.Core.Cleanup;
using Elynt.Core.Repair;

namespace Elynt.Core.Tests;

/// <summary>An in-memory PC for cleanup: files with sizes, running programs, driver packages and scripted programs.</summary>
public sealed class FakeCleanupSystem : ICleanupSystem
{
    public const string Profile = @"C:\Users\aden";

    private readonly Dictionary<string, (long Size, bool InUse)> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(CleanupRoot, string), List<string>> _overrides = [];

    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<DriverPackage> DriverPackages { get; } = [];

    /// <summary>Every folder measured, folder cleared, driver deleted and program run, in order.</summary>
    public List<string> Actions { get; } = [];

    public int RunExitCode { get; set; }

    public static string RootPath(CleanupRoot root, string user) => root switch
    {
        CleanupRoot.Windows => @"C:\Windows",
        CleanupRoot.ProgramData => @"C:\ProgramData",
        CleanupRoot.SystemDrive => @"C:",
        CleanupRoot.UserLocalAppData => $@"{Profile}\AppData\Local",
        CleanupRoot.UserLocalLow => $@"{Profile}\AppData\LocalLow",
        CleanupRoot.UserRecycleBin => $@"C:\$Recycle.Bin\{user}",
        _ => throw new ArgumentOutOfRangeException(nameof(root)),
    };

    public void AddFile(string path, long size, bool inUse = false)
    {
        _files[path] = (size, inUse);
        for (var folder = Parent(path); folder is not null; folder = Parent(folder))
            _folders.Add(folder);
    }

    public bool FileExists(string path) => _files.ContainsKey(path);

    public bool FolderExists(string path) => _folders.Contains(path);

    /// <summary>Makes the PC resolve a location to these folders instead, as a misbehaving adapter might.</summary>
    public void Resolve(CleanupRoot root, string relativePath, params string[] folders) =>
        _overrides[(root, relativePath)] = [.. folders];

    public IReadOnlyList<string> FindFolders(CleanupRoot root, string relativePath, string user)
    {
        if (_overrides.TryGetValue((root, relativePath), out var forced))
            return forced;
        var full = relativePath.Length == 0 ? RootPath(root, user) : $@"{RootPath(root, user)}\{relativePath}";
        var pattern = new Regex("^" + string.Join(@"\\", full.Split('\\').Select(part => part == "*" ? @"[^\\]+" : Regex.Escape(part))) + "$",
            RegexOptions.IgnoreCase);
        return _folders.Where(f => pattern.IsMatch(f)).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public FolderMeasure Measure(string folder, string pattern, bool includeSubfolders)
    {
        Actions.Add($"measure {folder}");
        var matches = Matching(folder, pattern, includeSubfolders).ToList();
        return new(matches.Sum(f => _files[f].Size), matches.Count);
    }

    public FolderCleanResult Clear(string folder, string pattern, bool includeSubfolders, bool removeFolder)
    {
        Actions.Add($"clear {folder}");
        int removed = 0, inUse = 0;
        long freed = 0;
        foreach (var file in Matching(folder, pattern, includeSubfolders).ToList())
        {
            if (_files[file].InUse)
            {
                inUse++;
                continue;
            }
            freed += _files[file].Size;
            _files.Remove(file);
            removed++;
        }
        if (removeFolder && !_files.Keys.Any(f => f.StartsWith(folder + @"\", StringComparison.OrdinalIgnoreCase)))
            _folders.RemoveWhere(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase) || f.StartsWith(folder + @"\", StringComparison.OrdinalIgnoreCase));
        return new(removed, freed, inUse);
    }

    public bool IsProcessRunning(string processName) => Running.Contains(processName);

    public IReadOnlyList<DriverPackage> ListDriverPackages() => DriverPackages.ToList();

    public bool DeleteDriverPackage(string publishedName)
    {
        Actions.Add($"delete driver {publishedName}");
        var package = DriverPackages.Single(p => p.PublishedName == publishedName);
        if (package.InUse)
            return false;
        DriverPackages.Remove(package);
        return true;
    }

    public Task<int> RunAsync(ProgramRun run, Action<string> output, CancellationToken cancellation)
    {
        Actions.Add(run.ToString());
        output("[==========================100.0%==========================]");
        return Task.FromResult(RunExitCode);
    }

    private IEnumerable<string> Matching(string folder, string pattern, bool includeSubfolders)
    {
        var name = new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase);
        var prefix = folder + @"\";
        return _files.Keys.Where(f =>
            f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            (includeSubfolders || !f[prefix.Length..].Contains('\\')) &&
            name.IsMatch(f.Split('\\')[^1]));
    }

    private static string? Parent(string path)
    {
        var cut = path.LastIndexOf('\\');
        return cut <= 0 ? null : path[..cut];
    }
}
