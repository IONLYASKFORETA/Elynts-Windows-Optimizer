using System.Runtime.Versioning;
using System.Security.Principal;
using Elynt.Core.Apps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// Preinstalled apps on the real PC, from the service (SYSTEM) on behalf of the signed-in user, through Windows
/// PowerShell's Appx and DISM cmdlets. Removal runs as the service with <c>-User &lt;sid&gt;</c>; reinstalling runs
/// as the user, because Add-AppxPackage and wsreset only act on the account that runs them.
/// <para>
/// Listing packages is slow (about a second per call), so the user's packages and the provisioned packages are each
/// read once and kept until this adapter changes something or <see cref="CacheFor"/> passes.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRemovableApps(WindowsIdentity user, ILogger<WindowsRemovableApps>? logger = null) : IRemovableApps
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UserCommandTimeout = TimeSpan.FromMinutes(10);

    private readonly ILogger _log = logger ?? NullLogger<WindowsRemovableApps>.Instance;
    private readonly string _sid = user.User?.Value ?? throw new ArgumentException("The user has no SID.", nameof(user));
    private readonly Lock _gate = new();
    private (DateTime At, HashSet<string> Names)? _installed;
    private (DateTime At, HashSet<string> Names)? _provisioned;

    public bool IsInstalled(RemovableApp app) => app.Kind switch
    {
        AppKind.OneDrive => FindOneDrive() is not null,
        _ => Cached(ref _installed, () => $"Get-AppxPackage -User {PowerShell.Quote(_sid)} | ForEach-Object Name")
            .Contains(app.PackageName),
    };

    public bool IsProvisioned(RemovableApp app) =>
        app.Kind == AppKind.Appx &&
        Cached(ref _provisioned, () => "Get-AppxProvisionedPackage -Online | ForEach-Object DisplayName").Contains(app.PackageName);

    public void Remove(RemovableApp app)
    {
        try
        {
            if (app.Kind == AppKind.OneDrive)
            {
                RemoveOneDrive();
                return;
            }
            var sid = PowerShell.Quote(_sid);
            PowerShell.Run($"Get-AppxPackage -User {sid} -Name {PowerShell.Quote(app.PackageName)} | Remove-AppxPackage -User {sid}");
        }
        finally
        {
            Forget();
        }
    }

    public void Reinstall(RemovableApp app)
    {
        try
        {
            switch (app.ReinstallFrom)
            {
                case ReinstallFrom.StoreReset:
                    var reset = UserProcess.Run(user, Path.Combine(Environment.SystemDirectory, "WSReset.exe"), ["-i"], UserCommandTimeout);
                    _log.LogInformation("wsreset -i exited {Exit}", reset);
                    if (reset != 0)
                        throw new InvalidOperationException($"wsreset -i didn't put the Microsoft Store back (exit code {reset}). Check the internet connection and try again.");
                    break;
                case ReinstallFrom.MicrosoftStore:
                    ReregisterOrSendToStore(app);
                    break;
                default:
                    throw new NotSupportedException($"{app.Name} is reinstalled with winget, not here.");
            }
        }
        finally
        {
            Forget();
        }
    }

    public void Deprovision(RemovableApp app)
    {
        try
        {
            PowerShell.Run(
                $"Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq {PowerShell.Quote(app.PackageName)} | " +
                "Remove-AppxProvisionedPackage -Online | Out-Null");
        }
        finally
        {
            Forget();
        }
    }

    /// <summary>
    /// Re-registers the copy Windows still keeps (it is still staged while it is provisioned or another account has
    /// it), which needs no download. Otherwise the user has to get it from the Store, so this says where.
    /// </summary>
    private void ReregisterOrSendToStore(RemovableApp app)
    {
        var location = PowerShell.Run(
            $"Get-AppxPackage -AllUsers -Name {PowerShell.Quote(app.PackageName)} | Sort-Object Version -Descending | " +
            "Select-Object -First 1 -ExpandProperty InstallLocation").Trim();
        var manifest = location.Length > 0 ? Path.Combine(location, "AppxManifest.xml") : null;
        if (manifest is not null && File.Exists(manifest))
        {
            var exit = UserProcess.RunPowerShell(user,
                $"$ErrorActionPreference = 'Stop'; Add-AppxPackage -Register {PowerShell.Quote(manifest)} -DisableDevelopmentMode",
                UserCommandTimeout);
            _log.LogInformation("Re-registering {Package} exited {Exit}", app.PackageName, exit);
            if (exit == 0)
                return;
        }

        var page = app.ReinstallId is { } id ? $" https://apps.microsoft.com/detail/{id}" : "";
        throw new InvalidOperationException(
            $"Windows no longer keeps a copy of {app.Name}, so it has to come from the Microsoft Store: install it " +
            $"from its Store page.{page}");
    }

    private void RemoveOneDrive()
    {
        var setup = FindOneDriveSetup() ?? throw new InvalidOperationException("Couldn't find OneDrive's setup program to uninstall it.");
        var exit = UserProcess.Run(user, setup, ["/uninstall"], UserCommandTimeout);
        _log.LogInformation("OneDriveSetup /uninstall exited {Exit}", exit);
        if (exit != 0 || FindOneDrive() is not null)
            throw new InvalidOperationException($"OneDrive's setup didn't uninstall it (exit code {exit}).");
    }

    /// <summary>OneDrive.exe, per-user (the usual install) or per-machine, or null when it isn't installed.</summary>
    private string? FindOneDrive() =>
        new[]
        {
            Path.Combine(UserLocalAppData(), "Microsoft", "OneDrive", "OneDrive.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft OneDrive", "OneDrive.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft OneDrive", "OneDrive.exe"),
        }.FirstOrDefault(File.Exists);

    /// <summary>The setup that came with the installed OneDrive (in its version folder), else the one Windows ships.</summary>
    private string? FindOneDriveSetup()
    {
        var installed = FindOneDrive() is { } exe ? Path.GetDirectoryName(exe)! : null;
        var versioned = installed is not null && Directory.Exists(installed)
            ? Directory.EnumerateFiles(installed, "OneDriveSetup.exe", SearchOption.AllDirectories).OrderDescending().FirstOrDefault()
            : null;
        var inbox = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "OneDriveSetup.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "OneDriveSetup.exe"),
        }.FirstOrDefault(File.Exists);
        return versioned ?? inbox;
    }

    private string UserLocalAppData() => Path.Combine(UserProfiles.Require(_sid), "AppData", "Local");

    private HashSet<string> Cached(ref (DateTime At, HashSet<string> Names)? cache, Func<string> script)
    {
        lock (_gate)
        {
            if (cache is { } hit && DateTime.UtcNow - hit.At < CacheFor)
                return hit.Names;
            var names = PowerShell.Run(script())
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            cache = (DateTime.UtcNow, names);
            return names;
        }
    }

    private void Forget()
    {
        lock (_gate)
        {
            _installed = null;
            _provisioned = null;
        }
    }
}

/// <summary>Windows optional features through Windows PowerShell's DISM cmdlets. Features are turned off with their files kept.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsOptionalFeatures : IOptionalFeatures
{
    public bool? IsEnabled(string name)
    {
        // The State enum (Enabled, Disabled, DisabledWithPayloadRemoved, ...) is not localized; DISM's text output is.
        var state = PowerShell.Run(
            $"$f = Get-WindowsOptionalFeature -Online -FeatureName {PowerShell.Quote(name)} -ErrorAction SilentlyContinue; " +
            "if ($f) { [string]$f.State } else { 'Missing' }").Trim();
        return state switch
        {
            "Missing" or "" => null,
            "Enabled" or "EnablePending" => true,
            _ => false,
        };
    }

    public void Enable(string name) =>
        PowerShell.Run($"Enable-WindowsOptionalFeature -Online -FeatureName {PowerShell.Quote(name)} -All -NoRestart | Out-Null");

    // No -Remove: the files stay, so turning it back on needs no download.
    public void Disable(string name) =>
        PowerShell.Run($"Disable-WindowsOptionalFeature -Online -FeatureName {PowerShell.Quote(name)} -NoRestart | Out-Null");
}
