using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Elynt.Core.Services;
using Elynt.Core.Tweaks;

namespace Elynt.Windows;

/// <summary>
/// The real Service Control Manager. Reads with QueryServiceConfig(2) and writes with ChangeServiceConfig(2), never
/// by editing the registry Start value (research/undo-mechanism.md §2). Writing needs admin rights.
/// </summary>
public sealed class WindowsServiceManager : IServiceManager
{
    public IReadOnlyList<ServiceInfo> List()
    {
        var found = new List<ServiceInfo>();
        using var scm = Native.OpenScm();
        foreach (var controller in ServiceController.GetServices().Concat(ServiceController.GetDevices()))
        {
            using (controller)
            {
                if (Read(scm, controller) is { } service)
                    found.Add(service);
            }
        }
        return found;
    }

    public ServiceInfo? Find(string name)
    {
        using var controller = new ServiceController(name);
        try
        {
            _ = controller.ServiceType;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        using var scm = Native.OpenScm();
        return Read(scm, controller);
    }

    public void Configure(string name, ServiceConfig config)
    {
        using var scm = Native.OpenScm();
        using var service = Native.OpenService(scm, name, Native.ServiceQueryConfig | Native.ServiceChangeConfig);
        var current = Native.QueryConfig(service);

        // Only parts that differ are written; the rest are passed as "no change".
        string? Changed(string want, string have) => string.Equals(want, have, StringComparison.OrdinalIgnoreCase) ? null : want;
        var dependencies = config.Dependencies.SequenceEqual(current.Dependencies, StringComparer.OrdinalIgnoreCase)
            ? null
            : string.Concat(config.Dependencies.Select(d => d + '\0')) + '\0';
        // A built-in account (LocalSystem, LocalService, NetworkService) needs no password; nothing else is changed.
        var account = Changed(config.Account, current.Account);

        if (!Native.ChangeServiceConfig(service, Native.NoChange, (uint)config.Start, (uint)config.ErrorControl,
                Changed(config.BinaryPath, current.BinaryPath), Changed(config.LoadOrderGroup, current.LoadOrderGroup), IntPtr.Zero,
                dependencies, account, null, null))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (config.DelayedAutoStart != Native.QueryDelayed(service))
        {
            var info = new Native.DelayedAutoStartInfo { Delayed = config.DelayedAutoStart };
            if (!Native.ChangeServiceConfig2(service, Native.ConfigDelayedAutoStart, ref info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static ServiceInfo? Read(SafeServiceHandle scm, ServiceController controller)
    {
        try
        {
            var kind = (controller.ServiceType & (ServiceType.KernelDriver | ServiceType.FileSystemDriver)) != 0
                ? ServiceKind.Driver
                : ServiceKind.Service;
            var running = controller.Status == ServiceControllerStatus.Running;

            using var service = Native.OpenService(scm, controller.ServiceName, Native.ServiceQueryConfig);
            var config = Native.QueryConfig(service) with { DelayedAutoStart = Native.QueryDelayed(service) };
            config = config with { TriggerStart = Native.HasTriggers(service) };
            return new ServiceInfo(controller.ServiceName, controller.DisplayName, Native.QueryDescription(service), kind, running, config);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Some services can't even be queried; they are left out rather than shown with a guessed state.
            return null;
        }
    }

    private static class Native
    {
        public const uint NoChange = 0xFFFFFFFF;
        public const uint ServiceQueryConfig = 0x0001;
        public const uint ServiceChangeConfig = 0x0002;
        public const uint ConfigDescription = 1;
        public const uint ConfigDelayedAutoStart = 3;
        public const uint ConfigTriggerInfo = 8;
        private const uint ScManagerConnect = 0x0001;
        private const int ErrorInsufficientBuffer = 122;

        [StructLayout(LayoutKind.Sequential)]
        public struct DelayedAutoStartInfo
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool Delayed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct QueryServiceConfigData
        {
            public uint ServiceType;
            public uint StartType;
            public uint ErrorControl;
            public IntPtr BinaryPathName;
            public IntPtr LoadOrderGroup;
            public uint TagId;
            public IntPtr Dependencies;
            public IntPtr ServiceStartName;
            public IntPtr DisplayName;
        }

        public static SafeServiceHandle OpenScm()
        {
            var handle = OpenSCManager(null, null, ScManagerConnect);
            return handle.IsInvalid ? throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't open the Service Control Manager.") : handle;
        }

        public static SafeServiceHandle OpenService(SafeServiceHandle scm, string name, uint access)
        {
            var handle = OpenServiceW(scm, name, access);
            return handle.IsInvalid ? throw new Win32Exception(Marshal.GetLastWin32Error()) : handle;
        }

        public static ServiceConfig QueryConfig(SafeServiceHandle service)
        {
            QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!QueryServiceConfig(service, buffer, needed, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var data = Marshal.PtrToStructure<QueryServiceConfigData>(buffer);
                return new ServiceConfig(
                    (ServiceStart)data.StartType,
                    ErrorControl: (int)data.ErrorControl,
                    BinaryPath: Marshal.PtrToStringUni(data.BinaryPathName) ?? "",
                    Account: Marshal.PtrToStringUni(data.ServiceStartName) ?? "",
                    LoadOrderGroup: Marshal.PtrToStringUni(data.LoadOrderGroup) ?? "")
                {
                    Dependencies = MultiString(data.Dependencies),
                };
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static bool QueryDelayed(SafeServiceHandle service) =>
            Query2(service, ConfigDelayedAutoStart, buffer => Marshal.ReadInt32(buffer) != 0, false);

        public static bool HasTriggers(SafeServiceHandle service) =>
            Query2(service, ConfigTriggerInfo, buffer => Marshal.ReadInt32(buffer) > 0, false);

        public static string QueryDescription(SafeServiceHandle service) =>
            Query2(service, ConfigDescription, buffer => Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer)) ?? "", "");

        private static T Query2<T>(SafeServiceHandle service, uint level, Func<IntPtr, T> read, T fallback)
        {
            if (!QueryServiceConfig2(service, level, IntPtr.Zero, 0, out var needed) && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                return fallback;
            var buffer = Marshal.AllocHGlobal((int)Math.Max(needed, 16));
            try
            {
                return QueryServiceConfig2(service, level, buffer, Math.Max(needed, 16), out _) ? read(buffer) : fallback;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static IReadOnlyList<string> MultiString(IntPtr pointer)
        {
            var items = new List<string>();
            if (pointer == IntPtr.Zero)
                return items;
            while (Marshal.PtrToStringUni(pointer) is { Length: > 0 } item)
            {
                items.Add(item);
                pointer += (item.Length + 1) * sizeof(char);
            }
            return items;
        }

        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeServiceHandle OpenSCManager(string? machine, string? database, uint access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeServiceHandle OpenServiceW(SafeServiceHandle scm, string name, uint access);

        [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)]
        private static extern bool QueryServiceConfig(SafeServiceHandle service, IntPtr buffer, uint size, out uint needed);

        [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfig2W", SetLastError = true)]
        private static extern bool QueryServiceConfig2(SafeServiceHandle service, uint level, IntPtr buffer, uint size, out uint needed);

        [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool ChangeServiceConfig(
            SafeServiceHandle service, uint serviceType, uint startType, uint errorControl, string? binaryPath, string? loadOrderGroup,
            IntPtr tagId, string? dependencies, string? account, string? password, string? displayName);

        [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
        public static extern bool ChangeServiceConfig2(SafeServiceHandle service, uint level, ref DelayedAutoStartInfo info);
    }
}

/// <summary>A Service Control Manager handle, closed with CloseServiceHandle.</summary>
internal sealed class SafeServiceHandle() : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
{
    protected override bool ReleaseHandle() => CloseServiceHandle(handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
