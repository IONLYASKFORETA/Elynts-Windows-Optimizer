using Elynt.Core.Startup;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// The real startup sources for one signed-in user: the Run keys, the user's and the all-users Startup folders, and
/// the startup tasks Store apps declare in their manifests. The service runs as SYSTEM, so the user's hive is read
/// under HKEY_USERS\&lt;sid&gt; and their profile paths come from the ProfileList, never this process's own.
/// </summary>
public sealed class WindowsStartupSources(string userSid) : IStartupSources
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string PackagesPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public IReadOnlyList<RunValue> RunValues()
    {
        var values = new List<RunValue>();
        using (var user = OpenUser())
            values.AddRange(ReadRun(user, RunPath, RunKey.CurrentUser));
        using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        {
            values.AddRange(ReadRun(machine, RunPath, RunKey.LocalMachine));
            values.AddRange(ReadRun(machine, Run32Path, RunKey.LocalMachine32));
        }
        return values;
    }

    public IReadOnlyList<StartupFile> StartupFiles()
    {
        var files = new List<StartupFile>();
        if (UserStartupFolder() is { } user)
            files.AddRange(ReadFolder(user, StartupFolder.User));
        files.AddRange(ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupFolder.AllUsers));
        return files;
    }

    public IReadOnlyList<StoreStartupTask> StoreStartupTasks()
    {
        var tasks = new List<StoreStartupTask>();
        using var user = OpenUser();
        using var packages = user.OpenSubKey(PackagesPath);
        if (packages is null)
            return tasks;

        foreach (var fullName in packages.GetSubKeyNames())
        {
            using var package = packages.OpenSubKey(fullName);
            if (package?.GetValue("PackageRootFolder") is not string root)
                continue;
            var manifest = Path.Combine(root, "AppxManifest.xml");
            try
            {
                if (File.Exists(manifest))
                    tasks.AddRange(StoreManifest.StartupTasks(File.ReadAllText(manifest), FamilyName(fullName), root));
            }
            catch (IOException)
            {
                // A package being updated can't be read right now; it shows again on the next read.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return tasks;
    }

    public void DeleteRunValue(RunKey key, string name)
    {
        if (key == RunKey.CurrentUser)
        {
            using var user = OpenUser();
            using var run = user.OpenSubKey(RunPath, writable: true);
            run?.DeleteValue(name, throwOnMissingValue: false);
            return;
        }
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var machineRun = machine.OpenSubKey(key == RunKey.LocalMachine ? RunPath : Run32Path, writable: true);
        machineRun?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteStartupFile(StartupFolder folder, string fileName)
    {
        var directory = folder == StartupFolder.User
            ? UserStartupFolder() ?? throw new InvalidOperationException("Couldn't find this user's Startup folder.")
            : Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        // Only a file directly in the Startup folder, never a path the name could point elsewhere with.
        if (fileName.IndexOfAny(['\\', '/']) >= 0 || fileName.Contains(".."))
            throw new ArgumentException($"'{fileName}' isn't a file in the Startup folder.", nameof(fileName));
        File.Delete(Path.Combine(directory, fileName));
    }

    private IEnumerable<RunValue> ReadRun(RegistryKey root, string path, RunKey key)
    {
        using var run = root.OpenSubKey(path);
        if (run is null)
            yield break;
        foreach (var name in run.GetValueNames().Where(n => n.Length > 0))
        {
            if (run.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string command)
                continue;
            yield return new RunValue(key, name, command, StartupCommandLine.ExecutablePath(Expand(command)));
        }
    }

    private IEnumerable<StartupFile> ReadFolder(string folder, StartupFolder which)
    {
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder)
            .Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            .Select(f => new StartupFile(which, Path.GetFileName(f), f, Target(f)))
            .ToList();
    }

    /// <summary>A shortcut's target, read through the Windows Script Host shell object; the file itself otherwise.</summary>
    private static string? Target(string file)
    {
        if (!file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return file;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(file);
            string target = shortcut.TargetPath;
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? UserStartupFolder()
    {
        using var user = OpenUser();
        using var folders = user.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        return folders?.GetValue("Startup", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string startup
            ? Expand(startup)
            : UserProfile() is { } profile ? Path.Combine(profile, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup") : null;
    }

    /// <summary>Expands environment variables as the signed-in user would see them, not as SYSTEM does.</summary>
    private string Expand(string text)
    {
        if (UserProfile() is { } profile)
        {
            text = text
                .Replace("%USERPROFILE%", profile, StringComparison.OrdinalIgnoreCase)
                .Replace("%APPDATA%", Path.Combine(profile, @"AppData\Roaming"), StringComparison.OrdinalIgnoreCase)
                .Replace("%LOCALAPPDATA%", Path.Combine(profile, @"AppData\Local"), StringComparison.OrdinalIgnoreCase);
        }
        return Environment.ExpandEnvironmentVariables(text);
    }

    private string? UserProfile() => UserProfiles.Find(userSid);

    private RegistryKey OpenUser()
    {
        using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
        return users.OpenSubKey(userSid, writable: true)
            ?? throw new InvalidOperationException($"The settings of user {userSid} aren't loaded. Are they signed in?");
    }

    /// <summary>Name_Version_Arch_ResourceId_PublisherId becomes Name_PublisherId.</summary>
    private static string FamilyName(string packageFullName)
    {
        var parts = packageFullName.Split('_');
        return parts.Length >= 5 ? $"{parts[0]}_{parts[^1]}" : packageFullName;
    }
}
