using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Elynt.Core.Apps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

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

    /// <summary>Starts a process with the signed-in user's own (unelevated) token and waits for it.</summary>
    private int RunAsUser(WindowsIdentity identity, string exe, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var commandLine = string.Join(' ', new[] { exe }.Concat(args).Select(Quote));
        if (!Native.DuplicateTokenEx(identity.Token, Native.MaximumAllowed, IntPtr.Zero, Native.SecurityImpersonation, Native.TokenPrimary, out var primary))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't take the signed-in user's token.");
        using (primary)
        {
            if (!Native.CreateEnvironmentBlock(out var environment, primary, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't load the signed-in user's environment.");
            try
            {
                var startup = new Native.StartupInfo { cb = Marshal.SizeOf<Native.StartupInfo>(), lpDesktop = @"winsta0\default" };
                if (!Native.CreateProcessAsUser(primary, null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                        Native.CreateUnicodeEnvironment | Native.CreateNoWindow, environment, null, ref startup, out var info))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't start winget as the signed-in user.");

                using var process = new SafeProcessHandle(info.hProcess, ownsHandle: true);
                Native.CloseHandle(info.hThread);
                if (Native.WaitForSingleObject(process, (uint)timeout.TotalMilliseconds) != 0)
                {
                    Native.TerminateProcess(process, 1);
                    throw new TimeoutException($"winget didn't finish within {timeout.TotalMinutes:0} minutes.");
                }
                Native.GetExitCodeProcess(process, out var exit);
                _log.LogInformation("winget (as user) {Args} exited {Exit}", string.Join(' ', args), (int)exit);
                return (int)exit;
            }
            finally
            {
                Native.DestroyEnvironmentBlock(environment);
            }
        }
    }

    private static string Quote(string arg) => arg.Contains(' ') || arg.Contains('"') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;

    private static class Native
    {
        public const uint MaximumAllowed = 0x02000000;
        public const int SecurityImpersonation = 2;
        public const int TokenPrimary = 1;
        public const uint CreateUnicodeEnvironment = 0x00000400;
        public const uint CreateNoWindow = 0x08000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct StartupInfo
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int level, int type, out SafeAccessTokenHandle token);

        [DllImport("userenv.dll", SetLastError = true)]
        public static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeAccessTokenHandle token, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        public static extern bool DestroyEnvironmentBlock(IntPtr environment);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessAsUser(
            SafeAccessTokenHandle token, string? application, string commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            bool inheritHandles, uint flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInformation info);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetExitCodeProcess(SafeProcessHandle handle, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateProcess(SafeProcessHandle handle, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
