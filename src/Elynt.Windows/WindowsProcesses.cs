using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Elynt.Core.Profiles;
using Microsoft.Win32.SafeHandles;

namespace Elynt.Windows;

/// <summary>Running programs, from the service: closing background apps, reopening them as the user, game priority.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcesses : IProcesses
{
    public bool IsRunning(string exePath) => Matching(exePath).Any();

    /// <summary>
    /// Ends every process running from the .exe. A service can't post WM_CLOSE to windows in the user's session, so
    /// this ends the process the way Task Manager's End task does.
    /// </summary>
    public void Close(string exePath)
    {
        foreach (var process in Matching(exePath))
        {
            using (process)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5_000);
                }
                catch (InvalidOperationException)
                {
                    // It already exited.
                }
            }
        }
    }

    /// <summary>
    /// Starts the .exe with the token of <paramref name="user"/>'s console session. When that user isn't signed in
    /// at the console (e.g. recovering after a reboot, before sign-in), there's nothing to reopen into, so it is skipped.
    /// </summary>
    public void Start(string exePath, string user)
    {
        var session = Native.WTSGetActiveConsoleSessionId();
        if (session == 0xFFFFFFFF || !Native.WTSQueryUserToken(session, out var token))
            return;
        using (token)
        {
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            {
                if (!string.Equals(identity.User?.Value, user, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            if (!Native.CreateEnvironmentBlock(out var environment, token, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't load the signed-in user's environment.");
            try
            {
                var startup = new Native.StartupInfo { cb = Marshal.SizeOf<Native.StartupInfo>(), lpDesktop = @"winsta0\default" };
                if (!Native.CreateProcessAsUser(token, null, $"\"{exePath}\"", IntPtr.Zero, IntPtr.Zero, false,
                        Native.CreateUnicodeEnvironment, environment, Path.GetDirectoryName(exePath), ref startup, out var info))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Couldn't reopen {Path.GetFileName(exePath)}.");
                Native.CloseHandle(info.hThread);
                Native.CloseHandle(info.hProcess);
            }
            finally
            {
                Native.DestroyEnvironmentBlock(environment);
            }
        }
    }

    public void SetPriority(int processId, GamePriority priority)
    {
        using var process = Process.GetProcessById(processId);
        process.PriorityClass = priority switch
        {
            GamePriority.High => ProcessPriorityClass.High,
            GamePriority.AboveNormal => ProcessPriorityClass.AboveNormal,
            _ => ProcessPriorityClass.Normal,
        };
    }

    private static IEnumerable<Process> Matching(string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath);
        foreach (var process in Process.GetProcessesByName(name))
        {
            if (string.Equals(ImagePath(process.Id), exePath, StringComparison.OrdinalIgnoreCase))
                yield return process;
            else
                process.Dispose();
        }
    }

    /// <summary>The full path of a process's .exe, or null when it can't be read (it exited, or it is protected).</summary>
    internal static string? ImagePath(int processId)
    {
        using var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
            return null;
        var buffer = new StringBuilder(1024);
        var size = buffer.Capacity;
        return Native.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
    }

    private static class Native
    {
        public const uint ProcessQueryLimitedInformation = 0x1000;
        public const uint CreateUnicodeEnvironment = 0x00000400;

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

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        public static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);

        [DllImport("userenv.dll", SetLastError = true)]
        public static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeAccessTokenHandle token, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        public static extern bool DestroyEnvironmentBlock(IntPtr environment);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessAsUser(
            SafeAccessTokenHandle token, string? application, string commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory, ref StartupInfo startup,
            out ProcessInformation info);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}

/// <summary>
/// Raises the system timer to 0.5 ms (NtSetTimerResolution) from the service's own process. Since Windows 10 2004 a
/// process's request only reaches other programs when the global timer-resolution tweak
/// (GlobalTimerResolutionRequests) is on; the request ends by itself when the service stops.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTimerResolution : ITimerResolution
{
    private const uint HalfMillisecond = 5_000; // in 100 ns units

    public void Raise()
    {
        var status = NtSetTimerResolution(HalfMillisecond, true, out _);
        if (status != 0)
            throw new Win32Exception($"Windows refused the timer request (NTSTATUS 0x{status:X8}).");
    }

    public void Release() => NtSetTimerResolution(HalfMillisecond, false, out _);

    [DllImport("ntdll.dll")]
    private static extern uint NtSetTimerResolution(uint desired, bool set, out uint current);
}

/// <summary>
/// Process starts and exits from WMI's Win32_ProcessStartTrace and Win32_ProcessStopTrace. These are extrinsic events
/// fed by the kernel's ETW process provider, so Windows pushes them: nothing is polled. Needs admin (the service).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiProcessEvents : IProcessEvents, IDisposable
{
    private readonly ManagementEventWatcher _starts = new(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
    private readonly ManagementEventWatcher _stops = new(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));

    public event EventHandler<ProcessStarted>? Started;

    public event EventHandler<int>? Exited;

    public void Start()
    {
        _starts.EventArrived += OnStart;
        _stops.EventArrived += OnStop;
        _starts.Start();
        _stops.Start();
    }

    private void OnStart(object sender, EventArrivedEventArgs e)
    {
        var pid = Convert.ToInt32(e.NewEvent["ProcessID"]);
        // The trace has only the file name; the full path is read from the process itself, which may be gone already.
        if (WindowsProcesses.ImagePath(pid) is not { } path)
            return;
        if (e.NewEvent["Sid"] is not byte[] sid)
            return;
        Started?.Invoke(this, new ProcessStarted(pid, path, new SecurityIdentifier(sid, 0).Value));
    }

    private void OnStop(object sender, EventArrivedEventArgs e) => Exited?.Invoke(this, Convert.ToInt32(e.NewEvent["ProcessID"]));

    public void Dispose()
    {
        _starts.Stop();
        _stops.Stop();
        _starts.Dispose();
        _stops.Dispose();
    }
}
