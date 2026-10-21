using Elynt.Core.Games;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// Reads launcher records from the real file system and HKEY_LOCAL_MACHINE (64-bit view, so WOW6432Node paths are
/// read exactly as written). Read-only. Anything that can't be read is treated as absent.
/// </summary>
public sealed class WindowsGameSources : IGameSources
{
    public string ProgramData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public IReadOnlyList<string> Drives => Try(() => DriveInfo.GetDrives()
        .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
        .Select(d => d.RootDirectory.FullName)
        .ToList(), []);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string? ReadText(string path) => Try(() => File.Exists(path) ? File.ReadAllText(path) : null, null);

    public byte[]? ReadBytes(string path) => Try(() => File.Exists(path) ? File.ReadAllBytes(path) : null, null);

    public IReadOnlyList<string> Files(string directory, string extension) =>
        Try(() => Directory.Exists(directory) ? Directory.GetFiles(directory, "*" + extension) : [], []);

    public IReadOnlyList<string> Directories(string directory) =>
        Try(() => Directory.Exists(directory) ? Directory.GetDirectories(directory) : [], []);

    public IReadOnlyList<string> SubKeys(string keyPath) => Try(() =>
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(keyPath);
        return key?.GetSubKeyNames() ?? [];
    }, []);

    public string? Value(string keyPath, string name) => Try(() =>
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(keyPath);
        return key?.GetValue(name) as string;
    }, null);

    private static T Try<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return fallback;
        }
    }
}
