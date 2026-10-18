using Elynt.Core.Security;

namespace Elynt.Windows;

/// <summary>
/// Microsoft Defender's folder exclusions through its own PowerShell module (Get/Add/Remove-MpPreference), the
/// documented way to manage them. Reading the list needs admin rights, which the service has.
/// </summary>
public sealed class WindowsScanExclusions : IScanExclusions
{
    public bool Contains(string folder) =>
        PowerShell("(Get-MpPreference).ExclusionPath")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(path => Same(path, folder));

    public void Add(string folder) => PowerShell($"Add-MpPreference -ExclusionPath {Quote(folder)}");

    public void Remove(string folder) => PowerShell($"Remove-MpPreference -ExclusionPath {Quote(folder)}");

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>A PowerShell single-quoted string: nothing inside is expanded, and a quote is written twice.</summary>
    private static string Quote(string text) => $"'{text.Replace("'", "''")}'";

    private static string PowerShell(string command) => CommandLine.Run(
        @"WindowsPowerShell\v1.0\powershell.exe",
        "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
        $"$ErrorActionPreference = 'Stop'; Import-Module Defender; {command}");
}
