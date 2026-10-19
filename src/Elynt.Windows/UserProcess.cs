using System.ComponentModel;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Elynt.Windows;

/// <summary>
/// Starts a process with the signed-in user's own (unelevated) token, from the service, and waits for it. For work
/// that only acts on the account that runs it (per-user winget installs, Add-AppxPackage, OneDrive setup).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class UserProcess
{
    public static int Run(WindowsIdentity identity, string exe, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var commandLine = WindowsArguments.CommandLine(exe, args);
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
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Couldn't start {Path.GetFileName(exe)} as the signed-in user.");

                using var process = new SafeProcessHandle(info.hProcess, ownsHandle: true);
                Native.CloseHandle(info.hThread);
                if (Native.WaitForSingleObject(process, (uint)timeout.TotalMilliseconds) != 0)
                {
                    Native.TerminateProcess(process, 1);
                    throw new TimeoutException($"{Path.GetFileName(exe)} didn't finish within {timeout.TotalMinutes:0} minutes.");
                }
                if (!Native.GetExitCodeProcess(process, out var exit))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Couldn't read how {Path.GetFileName(exe)} exited.");
                return (int)exit;
            }
            finally
            {
                Native.DestroyEnvironmentBlock(environment);
            }
        }
    }

    /// <summary>
    /// Runs a program as the signed-in user and returns what it sends back over a private named pipe, made by the
    /// service, that only that user and SYSTEM may open. Standard output can't be used: handles aren't inherited
    /// across sessions, and the service runs in session 0. Nothing goes through a file the user could swap for a link.
    /// </summary>
    /// <param name="argsFor">The arguments, given the pipe's name to write the result to.</param>
    public static (int Exit, string Output) RunReturning(
        WindowsIdentity identity, string exe, Func<string, IReadOnlyList<string>> argsFor, TimeSpan timeout)
    {
        var name = $"elynt-result-{Guid.NewGuid():N}";
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            identity.User ?? throw new InvalidOperationException("Couldn't tell which user is signed in."),
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        using var pipe = NamedPipeServerStreamAcl.Create(
            name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, security);
        using var stop = new CancellationTokenSource();
        var read = Task.Run(async () =>
        {
            await pipe.WaitForConnectionAsync(stop.Token);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
            return await reader.ReadToEndAsync(stop.Token);
        });

        try
        {
            var exit = Run(identity, exe, argsFor(name), timeout);
            // The program wrote everything before it exited; it may also have failed before connecting.
            string output;
            try
            {
                output = read.Wait(TimeSpan.FromSeconds(10)) ? read.Result : "";
            }
            catch (AggregateException)
            {
                output = "";
            }
            return (exit, output);
        }
        finally
        {
            stop.Cancel();
        }
    }

    /// <summary>Runs a Windows PowerShell 5.1 script as the signed-in user and returns its exit code.</summary>
    public static int RunPowerShell(WindowsIdentity identity, string script, TimeSpan timeout) =>
        Run(identity, PowerShell.Path,
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", PowerShell.Encode(script)], timeout);

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

/// <summary>Windows PowerShell 5.1, which has the Appx and DISM modules (PowerShell 7 doesn't load them natively).</summary>
internal static class PowerShell
{
    public static string Path { get; } =
        System.IO.Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    public static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    /// <summary>A PowerShell single-quoted string literal.</summary>
    public static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

    /// <summary>Script that writes the string <paramref name="expression"/> evaluates to into the named pipe, as UTF-8.</summary>
    public static string SendToPipe(string pipeName, string expression) =>
        $"$elyntResult = {expression}; " +
        $"$elyntPipe = [IO.Pipes.NamedPipeClientStream]::new('.', {Quote(pipeName)}, [IO.Pipes.PipeDirection]::Out); " +
        "$elyntPipe.Connect(30000); " +
        "$elyntWriter = [IO.StreamWriter]::new($elyntPipe, [Text.UTF8Encoding]::new($false)); " +
        "if ($null -ne $elyntResult) { $elyntWriter.Write([string]$elyntResult) }; $elyntWriter.Dispose()";

    /// <summary>Runs a script as the service and returns its output. Throws with PowerShell's own words when it fails.</summary>
    public static string Run(string script, TimeSpan? timeout = null) =>
        SystemCommand.Run(
            System.IO.Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Encode("$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'\n" + script)],
            timeout ?? TimeSpan.FromMinutes(10));
}
