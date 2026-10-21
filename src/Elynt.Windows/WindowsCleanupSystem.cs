using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Elynt.Core.Cleanup;
using Elynt.Core.Repair;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// Measures and clears cleanup folders on the real PC, as the service (SYSTEM), for one signed-in user. Never follows
/// junctions or symlinks, so a link the user planted can't steer the service into deleting files elsewhere.
/// </summary>
/// <param name="roots">Where each root is; tests point these at a scratch folder. Defaults to the real folders.</param>
public sealed partial class WindowsCleanupSystem(string userSid, Func<CleanupRoot, string>? roots = null) : ICleanupSystem
{
    private readonly WindowsRepairSystem _programs = new(userSid);

    private static readonly EnumerationOptions NoLinks = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
    };

    public IReadOnlyList<string> FindFolders(CleanupRoot root, string relativePath, string user)
    {
        var parts = relativePath.Split('\\', '/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is ".." or "." || part.Contains(':')))
            throw new ArgumentException("Cleanup paths must stay inside their folder.", nameof(relativePath));

        var basePath = Path.GetFullPath(Root(root));
        if (!Directory.Exists(basePath) || IsLink(basePath))
            return [];

        IEnumerable<string> found = [basePath];
        foreach (var part in parts)
        {
            found = part.Contains('*')
                ? found.SelectMany(dir => Directory.EnumerateDirectories(dir, part, NoLinks))
                : found.Select(dir => Path.Combine(dir, part)).Where(dir => Directory.Exists(dir) && !IsLink(dir));
        }
        return found.Where(dir => dir.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public FolderMeasure Measure(string folder, string pattern, bool includeSubfolders)
    {
        long bytes = 0;
        var files = 0;
        foreach (var file in Files(folder, pattern, includeSubfolders))
        {
            try
            {
                bytes += new FileInfo(file).Length;
                files++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Vanished or unreadable while scanning: leave it out of the total.
            }
        }
        return new(bytes, files);
    }

    public FolderCleanResult Clear(string folder, string pattern, bool includeSubfolders, bool removeFolder)
    {
        int removed = 0, inUse = 0;
        long freed = 0;
        foreach (var file in Files(folder, pattern, includeSubfolders).ToList())
        {
            try
            {
                var size = new FileInfo(file).Length;
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                removed++;
                freed += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                inUse++;
            }
        }

        if (includeSubfolders && Directory.Exists(folder))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
            foreach (var sub in Directory.EnumerateDirectories(folder, "*", options).OrderByDescending(d => d.Length))
                TryRemoveEmpty(sub);
            if (removeFolder)
                TryRemoveEmpty(folder);
        }
        return new(removed, freed, inUse);
    }

    public bool IsProcessRunning(string processName)
    {
        var running = Process.GetProcessesByName(processName);
        foreach (var process in running)
            process.Dispose();
        return running.Length > 0;
    }

    /// <summary>
    /// Third-party driver packages (oemNN.inf) from the driver database, with version, date and class from each .inf.
    /// A package is in use when any device's class key names it as its InfPath.
    /// </summary>
    public IReadOnlyList<DriverPackage> ListDriverPackages()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var inUse = InfsInUse();
        var packages = new List<DriverPackage>();
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var infFiles = machine.OpenSubKey(@"SYSTEM\DriverDatabase\DriverInfFiles");
        if (infFiles is null)
            return [];

        foreach (var published in infFiles.GetSubKeyNames().Where(n => OemInf().IsMatch(n)))
        {
            try
            {
                using var key = infFiles.OpenSubKey(published);
                var package = key?.GetValue("Active") as string ?? (key?.GetValue("") as string[])?.FirstOrDefault();
                var infPath = Path.Combine(windows, "INF", published);
                if (package is null || !File.Exists(infPath))
                    continue;
                var inf = Inf.Read(infPath);
                var repository = Path.Combine(windows, @"System32\DriverStore\FileRepository", package);
                var size = Directory.Exists(repository) ? Measure(repository, "*", includeSubfolders: true).Bytes : 0;
                var original = package[..(package.IndexOf(".inf", StringComparison.OrdinalIgnoreCase) is var i and > 0 ? i + 4 : package.Length)];
                packages.Add(new(published, original, inf.Provider, inf.ClassName, inf.Version, inf.Date, size, inUse.Contains(published)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                // An unreadable package is left out, so it is never offered for deletion.
            }
        }
        return packages;
    }

    public bool DeleteDriverPackage(string publishedName)
    {
        if (!OemInf().IsMatch(publishedName))
            throw new ArgumentException("Only third-party driver packages (oemNN.inf) can be deleted.", nameof(publishedName));
        // No /force: pnputil refuses a package a device still uses.
        var exit = _programs.RunAsync(new ProgramRun("pnputil.exe", ["/delete-driver", publishedName]), _ => { }, CancellationToken.None)
            .GetAwaiter().GetResult();
        return exit == 0;
    }

    public Task<int> RunAsync(ProgramRun run, Action<string> output, CancellationToken cancellation) =>
        _programs.RunAsync(run, output, cancellation);

    private IEnumerable<string> Files(string folder, string pattern, bool includeSubfolders) =>
        Directory.Exists(folder) && !IsLink(folder)
            ? Directory.EnumerateFiles(folder, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = includeSubfolders,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
            })
            : [];

    private static void TryRemoveEmpty(string folder)
    {
        try
        {
            Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not empty (a file was in use) or locked: left for next time.
        }
    }

    private static bool IsLink(string path) => new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);

    private string Root(CleanupRoot root)
    {
        if (roots is not null)
            return roots(root);
        var drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        return root switch
        {
            CleanupRoot.Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            CleanupRoot.ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            CleanupRoot.SystemDrive => drive + @"\",
            CleanupRoot.UserLocalAppData => Path.Combine(UserProfile(), "AppData", "Local"),
            CleanupRoot.UserLocalLow => Path.Combine(UserProfile(), "AppData", "LocalLow"),
            CleanupRoot.UserRecycleBin => Path.Combine(drive + @"\", "$Recycle.Bin", userSid),
            _ => throw new ArgumentOutOfRangeException(nameof(root)),
        };
    }

    private string UserProfile() => UserProfiles.Require(userSid);

    /// <summary>Every .inf a device (present or not) was installed from, from the device class keys.</summary>
    private static HashSet<string> InfsInUse()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var classes = machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class");
        if (classes is null)
            return used;
        foreach (var classGuid in classes.GetSubKeyNames())
        {
            try
            {
                using var cls = classes.OpenSubKey(classGuid);
                foreach (var instance in cls?.GetSubKeyNames() ?? [])
                {
                    try
                    {
                        using var device = cls!.OpenSubKey(instance);
                        if (device?.GetValue("InfPath") is string inf)
                            used.Add(inf);
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
                    {
                        // Some class keys (e.g. Properties) are locked; they hold no InfPath.
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
            }
        }
        return used;
    }

    [GeneratedRegex(@"^oem\d+\.inf$", RegexOptions.IgnoreCase)]
    private static partial Regex OemInf();

    /// <summary>The few [Version] facts cleanup needs from an .inf: provider, class and DriverVer.</summary>
    public sealed record Inf(string Provider, string ClassName, Version Version, DateOnly Date)
    {
        public static Inf Read(string path) => Parse(File.ReadAllText(path));

        public static Inf Parse(string text)
        {
            var section = "";
            var version = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Split(';')[0].Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1].Trim();
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var name = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim().Trim('"');
                if (section.Equals("Version", StringComparison.OrdinalIgnoreCase))
                    version[name] = value;
                else if (section.Equals("Strings", StringComparison.OrdinalIgnoreCase))
                    strings.TryAdd(name, value);
            }

            string Resolve(string value) =>
                value.StartsWith('%') && value.EndsWith('%') && strings.TryGetValue(value.Trim('%'), out var s) ? s : value;

            var driverVer = version.GetValueOrDefault("DriverVer", "").Split(',', StringSplitOptions.TrimEntries);
            var date = DateOnly.TryParseExact(driverVer[0], ["MM/dd/yyyy", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : DateOnly.MinValue;
            var ver = driverVer.Length > 1 && Version.TryParse(driverVer[1], out var v) ? v : new Version(0, 0);
            return new(Resolve(version.GetValueOrDefault("Provider", "")), Resolve(version.GetValueOrDefault("Class", "")), ver, date);
        }
    }
}
