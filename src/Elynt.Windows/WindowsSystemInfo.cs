using Elynt.Core;
using Microsoft.Win32;

namespace Elynt.Windows;

public sealed class WindowsSystemInfo : ISystemInfo
{
    public string OsBuild { get; } = ReadBuild();

    private static string ReadBuild()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return $"{key?.GetValue("CurrentBuild")}.{key?.GetValue("UBR")}";
    }
}
