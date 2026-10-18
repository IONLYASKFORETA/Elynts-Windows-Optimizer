using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Elynt.Core.Apps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// winget through its command line, run by the elevated service.
/// <para>
/// Machine-wide installers run as the service (elevated, silent). Some apps (Discord, Spotify and others) only
/// ship per-user installers; installed as the service those would land in the system account's profile, so
/// they are installed again as the signed-in user instead, from the user's own winget.
/// </para>
/// <para>
/// The spec names Microsoft's COM interop package; the command line was chosen for the first build because it
/// needs no Windows SDK target and behaves the same under the system account. Progress percentages are not
/// available from the command line, so only per-app progress is reported.
/// </para>
/// </summary>
/// <param name="user">The signed-in user, as impersonated from the pipe client. Null disables per-user installs.</param>
[SupportedOSPlatform("windows")]
public sealed class WingetPackageManager(WindowsIdentity? user, ILogger<WingetPackageManager>? logger = null) : IPackageManager
{
    // winget's documented return codes: https://github.com/microsoft/winget-cli/blob/master/doc/windows/package-manager/winget/returnCodes.md
    private const int NoApplicableInstaller = unchecked((int)0x8A150010);
    private const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    private const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
    private const int RebootRequiredToFinish = unchecked((int)0x8A150109);
    private const int RebootInitiated = unchecked((int)0x8A15010A);

    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);

    private readonly ILogger _log = logger ?? NullLogger<WingetPackageManager>.Instance;

    public bool IsAvailable()
    {
        var winget = FindSystemWinget();
        if (winget is null)
            return false;
        try
        {
            return Run(winget, ["--version"], TimeSpan.FromMinutes(1)) == 0;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "winget is present but won't run");
            return false;
        }
    }

    /// <summary>
    /// Installs App Installer for every user with Microsoft's own WinGet PowerShell module
    /// (<c>Repair-WinGetPackageManager</c>), which also brings its dependencies.
    /// </summary>
    public void Bootstrap()
    {
        const string script =
            "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; " +
            "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; " +
            "Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope AllUsers | Out-Null; " +
            "Install-Module -Name Microsoft.WinGet.Client -Repository PSGallery -Force -Scope AllUsers -AllowClobber; " +
            "Import-Module Microsoft.WinGet.Client; " +
            "Repair-WinGetPackageManager -AllUsers -Latest -Force";
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var exit = Run(powershell, ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script], TimeSpan.FromMinutes(15));
        if (exit != 0)
            throw new InvalidOperationException($"Installing App Installer failed (PowerShell exit code {exit}).");
    }

    public PackageInstallResult Install(string packageId, string source, IProgress<double> progress)
    {
        var winget = FindSystemWinget() ?? throw new InvalidOperationException("winget isn't installed.");
        string[] common =
        [
            "install", "--id", packageId, "--exact", "--source", source, "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
        ];

        var exit = Run(winget, [.. common, "--scope", "machine"], InstallTimeout);
        if (exit == NoApplicableInstaller && user is not null)
        {
            _log.LogInformation("{Package} has no machine-wide installer; installing it for the signed-in user", packageId);
            exit = RunAsUser(user, UserWinget(user), common, InstallTimeout);
        }
        return Interpret(exit);
    }

    private static PackageInstallResult Interpret(int exit) => exit switch
    {
        0 => new(PackageInstallStatus.Installed),
        RebootRequiredToFinish or RebootInitiated => new(PackageInstallStatus.Installed, RebootRequired: true),
        UpdateNotApplicable or PackageAlreadyInstalled => new(PackageInstallStatus.AlreadyInstalled),
        NoApplicableInstaller => new(PackageInstallStatus.Failed, "It has no installer that fits this PC."),
        _ => new(PackageInstallStatus.Failed, $"winget exit code 0x{exit:X8}."),
    };

    /// <summary>The service's own winget: the newest App Installer package folder under Program Files.</summary>
    private static string? FindSystemWinget()
    {
        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        try
        {
            return Directory.EnumerateDirectories(windowsApps, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                .Select(dir => (Dir: dir, Version: ParseVersion(Path.GetFileName(dir))))
                .OrderByDescending(d => d.Version)
                .Select(d => Path.Combine(d.Dir, "winget.exe"))
                .FirstOrDefault(File.Exists);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Version ParseVersion(string folder)
    {
        var parts = folder.Split('_');
        return parts.Length > 1 && Version.TryParse(parts[1], out var version) ? version : new Version(0, 0);
    }

    /// <summary>The user's winget app execution alias, which carries App Installer's package identity.</summary>
    private static string UserWinget(WindowsIdentity user)
    {
        using var profiles = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{user.User?.Value}");
        var profile = profiles?.GetValue("ProfileImagePath") as string
            ?? throw new InvalidOperationException("Couldn't find the signed-in user's profile folder.");
        return Path.Combine(Environment.ExpandEnvironmentVariables(profile), @"AppData\Local\Microsoft\WindowsApps\winget.exe");
    }

    private int Run(string exe, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {exe}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Path.GetFileName(exe)} didn't finish within {timeout.TotalMinutes:0} minutes.");
        }
        process.WaitForExit();
        _log.LogInformation("{Exe} {Args} exited {Exit}: {Output} {Errors}",
            Path.GetFileName(exe), string.Join(' ', args), process.ExitCode, output.Result.Trim(), errors.Result.Trim());
        return process.ExitCode;
    }

    /// <summary>Starts winget with the signed-in user's own (unelevated) token and waits for it.</summary>
    private int RunAsUser(WindowsIdentity identity, string exe, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var exit = UserProcess.Run(identity, exe, args, timeout);
        _log.LogInformation("winget (as user) {Args} exited {Exit}", string.Join(' ', args), exit);
        return exit;
    }
}
