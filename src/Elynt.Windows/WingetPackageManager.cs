using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
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
/// <para>
/// Search and the installed-apps list read winget through Microsoft's own WinGet PowerShell module
/// (<c>Find-WinGetPackage</c>, <c>Get-WinGetPackage</c>), which returns objects instead of a console table that
/// truncates long names and ids. They run as the signed-in user when there is one, so per-user apps are listed
/// too; updates and uninstalls fall back to the signed-in user the same way installs do. Where the module can't run
/// (the system account with nobody signed in), they read winget's command-line tables instead (<see cref="WingetTable"/>).
/// </para>
/// </summary>
/// <param name="user">The signed-in user, as impersonated from the pipe client. Null disables per-user installs.</param>
[SupportedOSPlatform("windows")]
public sealed class WingetPackageManager(WindowsIdentity? user, ILogger<WingetPackageManager>? logger = null) : IPackageManager
{
    // winget's documented return codes: https://github.com/microsoft/winget-cli/blob/master/doc/windows/package-manager/winget/returnCodes.md
    private const int NoApplicableInstaller = unchecked((int)0x8A150010);
    private const int NoPackagesFound = unchecked((int)0x8A150014);
    private const int PackageIsPinned = unchecked((int)0x8A150068);
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
            return Run(winget, ["--version"], TimeSpan.FromMinutes(1)).Exit == 0;
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
        var powershell = PowerShell.Path;
        var exit = Run(powershell, ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script], TimeSpan.FromMinutes(15)).Exit;
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

        var exit = Run(winget, [.. common, "--scope", "machine"], InstallTimeout).Exit;
        if (exit == NoApplicableInstaller && user is not null)
        {
            _log.LogInformation("{Package} has no machine-wide installer; installing it for the signed-in user", packageId);
            exit = RunAsUser(user, UserWinget(user), common, InstallTimeout);
        }
        return Interpret(exit);
    }

    public PackageInstallResult Upgrade(string packageId, string source, IProgress<double> progress) =>
        RunWithUserFallback(packageId,
        [
            "upgrade", "--id", packageId, "--exact", "--source", source, "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
        ]);

    public PackageInstallResult Uninstall(string packageId, IProgress<double> progress) =>
        RunWithUserFallback(packageId,
            ["uninstall", "--id", packageId, "--exact", "--silent", "--accept-source-agreements", "--disable-interactivity"]) switch
        {
            // Nothing found for the machine or the user: it is already gone.
            { Status: PackageInstallStatus.Failed, Error: NotFoundError } => new(PackageInstallStatus.AlreadyInstalled),
            var result => result,
        };

    /// <summary>
    /// Runs a winget command as the service; when winget finds no such package for the machine, the app is
    /// probably installed for the signed-in user only, so it runs again as that user.
    /// </summary>
    private PackageInstallResult RunWithUserFallback(string packageId, string[] args)
    {
        var winget = FindSystemWinget() ?? throw new InvalidOperationException("winget isn't installed.");
        var exit = Run(winget, args, InstallTimeout).Exit;
        if (exit == NoPackagesFound && user is not null)
        {
            _log.LogInformation("{Package} isn't installed for the machine; trying the signed-in user", packageId);
            exit = RunAsUser(user, UserWinget(user), args, InstallTimeout);
        }
        return Interpret(exit);
    }

    private const string NotFoundError = "winget couldn't find it installed.";

    public IReadOnlyList<PackageInfo> Search(string query, string source) => ModuleOrCommandLine(
        () =>
        {
            var json = RunModule(
                $"@(Find-WinGetPackage -Query {PowerShell.Quote(query)} -Source {PowerShell.Quote(source)} | Select-Object -First 100 | " +
                "ForEach-Object { [pscustomobject]@{ Id = $_.Id; Name = $_.Name; Version = $_.Version } })");
            return Parse(json)
                .Select(e => new PackageInfo(Text(e, "Id") ?? "", Text(e, "Name") ?? "", Text(e, "Version") ?? ""))
                .Where(p => p.Id.Length > 0)
                .ToList();
        },
        () => WingetTable.ParseSearch(RunTable(["search", "--query", query, "--source", source, "--count", "100"])));

    public IReadOnlyList<InstalledPackage> ListInstalled() => ModuleOrCommandLine(
        () =>
        {
            var json = RunModule(
                "@(Get-WinGetPackage | ForEach-Object { [pscustomobject]@{ Id = $_.Id; Name = $_.Name; Version = $_.InstalledVersion; " +
                "Available = $(if ($_.IsUpdateAvailable -and $_.AvailableVersions) { $_.AvailableVersions[0] } else { $null }); Source = $_.Source } })");
            return Parse(json)
                .Select(e => new InstalledPackage(
                    Text(e, "Id") ?? "", Text(e, "Name") ?? "", Text(e, "Version") ?? "", Text(e, "Available"),
                    Text(e, "Source") is { Length: > 0 } s ? s : null))
                .Where(p => p.Id.Length > 0)
                .ToList();
        },
        () => WingetTable.ParseList(RunTable(["list"])));

    private volatile bool _moduleFailed;

    /// <summary>
    /// Reads through the WinGet PowerShell module, or through winget's command line when the module can't run.
    /// The module talks to winget's COM server, which can't be started for the system account when nobody is
    /// signed in ("Failed to create instance: -2147023174"); once it has failed, the command line is used straight
    /// away. The command line runs as the service, so it doesn't see apps installed for the signed-in user only.
    /// </summary>
    private IReadOnlyList<T> ModuleOrCommandLine<T>(Func<IReadOnlyList<T>> module, Func<IReadOnlyList<T>> commandLine)
    {
        if (!_moduleFailed)
        {
            try
            {
                return module();
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or JsonException)
            {
                _moduleFailed = true;
                _log.LogWarning(ex, "The WinGet PowerShell module failed; using winget's command line instead");
            }
        }
        return commandLine();
    }

    /// <summary>Runs a read-only winget command as the service and returns the table it prints.</summary>
    private string RunTable(string[] args)
    {
        var winget = FindSystemWinget() ?? throw new InvalidOperationException("winget isn't installed.");
        var (exit, output) = Run(winget, [.. args, "--accept-source-agreements", "--disable-interactivity"], ModuleTimeout, logOutput: false);
        if (exit == NoPackagesFound)
            return "";
        if (exit != 0)
            throw new InvalidOperationException($"winget {args[0]} failed (exit code 0x{exit:X8}).");
        return output;
    }

    /// <summary>
    /// Runs a pipeline of the WinGet PowerShell module and returns its result as JSON. Installs the module for all
    /// users first if it is missing. Runs as the signed-in user when there is one (sending the JSON back over a
    /// private pipe), otherwise as the service.
    /// </summary>
    private string RunModule(string pipeline)
    {
        EnsureModule();
        var powershell = PowerShell.Path;
        const string prelude =
            "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; " +
            "Import-Module Microsoft.WinGet.Client; ";
        if (user is null)
        {
            var script = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; try { " + prelude +
                $"ConvertTo-Json -Compress -Depth 3 -InputObject {pipeline} }} catch {{ Write-Output $_.ToString(); exit 1 }}";
            var (exit, output) = Run(powershell, PowerShellArgs(script), ModuleTimeout, logOutput: false);
            if (exit != 0)
                throw new InvalidOperationException($"The WinGet PowerShell module failed (exit code {exit}): {output.Trim()}");
            return output;
        }

        // The JSON comes back over a pipe only this user and SYSTEM can open, never through a file in the user's
        // folders: as SYSTEM, reading and deleting a file there could be redirected by a link the user planted.
        var (userExit, json) = UserProcess.RunReturning(
            user,
            powershell,
            pipe => PowerShellArgs(prelude + PowerShell.SendToPipe(pipe, $"(ConvertTo-Json -Compress -Depth 3 -InputObject {pipeline})")),
            ModuleTimeout);
        _log.LogInformation("WinGet module (as user) exited {Exit}", userExit);
        if (userExit != 0)
            throw new InvalidOperationException($"The WinGet PowerShell module failed for the signed-in user (exit code {userExit}).");
        return json;
    }

    private void EnsureModule()
    {
        const string script =
            "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; " +
            "if (Get-Module -ListAvailable -Name Microsoft.WinGet.Client) { exit 0 }; " +
            "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; " +
            "try { Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope AllUsers | Out-Null; " +
            "Install-Module -Name Microsoft.WinGet.Client -Repository PSGallery -Force -Scope AllUsers -AllowClobber } " +
            "catch { Write-Output $_.ToString(); exit 1 }";
        var (exit, output) = Run(PowerShell.Path, PowerShellArgs(script), TimeSpan.FromMinutes(10));
        if (exit != 0)
            throw new InvalidOperationException($"Installing the WinGet PowerShell module failed (exit code {exit}): {output.Trim()}");
    }

    private static readonly TimeSpan ModuleTimeout = TimeSpan.FromMinutes(5);

    private static string[] PowerShellArgs(string script) => ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script];

    private static List<JsonElement> Parse(string json)
    {
        json = json.Trim();
        if (json.Length == 0)
            return [];
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : [root];
        return items.Where(e => e.ValueKind == JsonValueKind.Object).Select(e => e.Clone()).ToList();
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static PackageInstallResult Interpret(int exit) => exit switch
    {
        0 => new(PackageInstallStatus.Installed),
        RebootRequiredToFinish or RebootInitiated => new(PackageInstallStatus.Installed, RebootRequired: true),
        UpdateNotApplicable or PackageAlreadyInstalled => new(PackageInstallStatus.AlreadyInstalled),
        NoApplicableInstaller => new(PackageInstallStatus.Failed, "It has no installer that fits this PC."),
        NoPackagesFound => new(PackageInstallStatus.Failed, NotFoundError),
        PackageIsPinned => new(PackageInstallStatus.Failed, "It is pinned in winget, so it isn't updated."),
        _ => new(PackageInstallStatus.Failed, $"winget exit code 0x{exit:X8}."),
    };

    /// <summary>The service's own winget: the newest App Installer package folder under Program Files.</summary>
    private static string? FindSystemWinget() => UserProfiles.SystemWinget();

    /// <summary>The user's winget app execution alias, which carries App Installer's package identity.</summary>
    private static string UserWinget(WindowsIdentity user) =>
        Path.Combine(UserProfile(user), @"AppData\Local\Microsoft\WindowsApps\winget.exe");

    private static string UserProfile(WindowsIdentity user) => UserProfiles.Require(user.User?.Value);

    /// <param name="logOutput">False for commands whose output is data (JSON), so it doesn't fill the log.</param>
    private (int Exit, string Output) Run(string exe, IReadOnlyList<string> args, TimeSpan timeout, bool logOutput = true)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        // A PowerShell 7 parent (a terminal, CI) puts its own module folders here, which break Windows PowerShell's
        // PowerShellGet; without it Windows PowerShell uses its default folders.
        start.Environment.Remove("PSModulePath");

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
            Path.GetFileName(exe), string.Join(' ', args), process.ExitCode, logOutput ? output.Result.Trim() : "(data)", errors.Result.Trim());
        return (process.ExitCode, output.Result);
    }

    /// <summary>Starts winget with the signed-in user's own (unelevated) token and waits for it.</summary>
    private int RunAsUser(WindowsIdentity identity, string exe, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var exit = UserProcess.Run(identity, exe, args, timeout);
        _log.LogInformation("winget (as user) {Args} exited {Exit}", string.Join(' ', args), exit);
        return exit;
    }
}
