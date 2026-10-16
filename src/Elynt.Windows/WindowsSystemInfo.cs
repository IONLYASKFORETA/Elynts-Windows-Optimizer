using Elynt.Core;
using Elynt.Core.Setup;
using Microsoft.Win32;

namespace Elynt.Windows;

public sealed class WindowsSystemInfo : ISystemInfo
{
    public string OsBuild { get; } = ReadBuild();

    /// <summary>What <see cref="OsSupport"/> needs to decide whether the app may run here. Reads only.</summary>
    public static OsDescription Describe()
    {
        using var key = CurrentVersionKey();
        var build = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var number) ? number : 0;
        // "Client" on desktop Windows; "Server" or "Server Core" on Windows Server.
        var installationType = key?.GetValue("InstallationType") as string ?? "";
        return new OsDescription(build, installationType.StartsWith("Server", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadBuild()
    {
        using var key = CurrentVersionKey();
        return $"{key?.GetValue("CurrentBuild")}.{key?.GetValue("UBR")}";
    }

    private static RegistryKey? CurrentVersionKey()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
    }
}
