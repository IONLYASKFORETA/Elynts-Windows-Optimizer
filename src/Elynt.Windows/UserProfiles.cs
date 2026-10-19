using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// Where a user's things live, looked up for that user rather than for this process: the service runs as SYSTEM, so
/// its own profile and PATH say nothing about the signed-in user.
/// </summary>
internal static class UserProfiles
{
    /// <summary>The user's profile folder from the ProfileList (e.g. C:\Users\Aden), or null when Windows has none.</summary>
    public static string? Find(string? userSid)
    {
        if (string.IsNullOrEmpty(userSid))
            return null;
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{userSid}");
        return key?.GetValue("ProfileImagePath") as string is { } path ? Environment.ExpandEnvironmentVariables(path) : null;
    }

    /// <summary>The user's profile folder. Throws when Windows has none for them.</summary>
    public static string Require(string? userSid) =>
        Find(userSid) ?? throw new InvalidOperationException("Couldn't find the signed-in user's profile folder.");

    /// <summary>The service's own winget: the newest App Installer package folder under Program Files, or null.</summary>
    public static string? SystemWinget()
    {
        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        try
        {
            return Directory.EnumerateDirectories(windowsApps, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                .Select(dir => (Dir: dir, Version: PackageVersion(Path.GetFileName(dir))))
                .OrderByDescending(d => d.Version)
                .Select(d => Path.Combine(d.Dir, "winget.exe"))
                .FirstOrDefault(File.Exists);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Version PackageVersion(string folder)
    {
        var parts = folder.Split('_');
        return parts.Length > 1 && Version.TryParse(parts[1], out var version) ? version : new Version(0, 0);
    }
}
