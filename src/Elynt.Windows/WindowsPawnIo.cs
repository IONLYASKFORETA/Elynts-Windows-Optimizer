using Elynt.Core.Drivers;
using Elynt.Core.LowLevel;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// PawnIO through its own setup: installed state comes from its Add/Remove Programs entry, install and uninstall run
/// PawnIO's setup silently. Not yet verified on a real PC: the "-install"/"-uninstall" and "-silent" switches and the
/// "PawnIO" uninstall key name are from community tools that ship PawnIO, not from PawnIO's own documentation.
/// </summary>
public sealed class WindowsPawnIo(IInstallerRunner runner) : IPawnIoDriver
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    public string? InstalledVersion()
    {
        using var key = OpenUninstallKey();
        if (key is null)
            return null;
        return key.GetValue("DisplayVersion") as string is { Length: > 0 } version ? version : "unknown version";
    }

    public int Install(string setupPath) => runner.RunInstaller(setupPath, ["-install", "-silent"]);

    public int Uninstall()
    {
        using var key = OpenUninstallKey() ?? throw new InvalidOperationException("PawnIO isn't installed.");
        var command = key.GetValue("UninstallString") as string
            ?? throw new InvalidOperationException("PawnIO's uninstall entry has no uninstall command.");
        var (exe, arguments) = Split(command);
        if (!arguments.Contains("-silent", StringComparer.OrdinalIgnoreCase))
            arguments = [.. arguments, "-silent"];
        if (!arguments.Contains("-uninstall", StringComparer.OrdinalIgnoreCase))
            arguments = ["-uninstall", .. arguments];
        return runner.RunInstaller(exe, arguments);
    }

    private static RegistryKey? OpenUninstallKey()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return machine.OpenSubKey(UninstallKey);
    }

    /// <summary>Splits an uninstall command into the program and its arguments; the program may be quoted.</summary>
    public static (string Exe, IReadOnlyList<string> Arguments) Split(string command)
    {
        command = command.Trim();
        string exe, rest;
        if (command.StartsWith('"'))
        {
            var close = command.IndexOf('"', 1);
            if (close < 0)
                throw new InvalidOperationException($"PawnIO's uninstall command can't be read: {command}");
            exe = command[1..close];
            rest = command[(close + 1)..];
        }
        else
        {
            var exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            var split = exeEnd >= 0 ? exeEnd + 4 : command.IndexOf(' ') is var space && space >= 0 ? space : command.Length;
            exe = command[..split];
            rest = command[split..];
        }

        return (exe, rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
