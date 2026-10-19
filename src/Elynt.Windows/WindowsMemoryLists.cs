using System.ComponentModel;
using System.Runtime.InteropServices;
using Elynt.Core.Memory;

namespace Elynt.Windows;

/// <summary>
/// Windows' memory lists through the native system information calls RAMMap and ISLC use. Purging needs
/// SeProfileSingleProcessPrivilege, which the service's SYSTEM account holds and this enables first.
/// </summary>
public sealed partial class WindowsMemoryLists : IMemoryLists
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;

    // SYSTEM_MEMORY_LIST_INFORMATION: Zero, Free, Modified, ModifiedNoWrite, Bad, PageCountByPriority[8],
    // RepurposedPagesByPriority[8], ModifiedPageCountPageFile, all ULONG_PTR page counts.
    private const int Fields = 22;
    private const int FirstStandbyPriority = 5;

    private bool _privilegeEnabled;

    public MemoryListSizes Read()
    {
        var counts = new nuint[Fields];
        var status = NtQuerySystemInformation(SystemMemoryListInformation, counts, counts.Length * nuint.Size, out _);
        if (status != 0)
            throw new Win32Exception(RtlNtStatusToDosError(status), "Couldn't read the memory lists.");

        long page = Environment.SystemPageSize;
        long standby = 0;
        for (var i = FirstStandbyPriority; i < FirstStandbyPriority + 8; i++)
            standby += (long)counts[i];
        return new MemoryListSizes(standby * page, ((long)counts[0] + (long)counts[1]) * page);
    }

    public void PurgeStandby()
    {
        if (!_privilegeEnabled)
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            _privilegeEnabled = true;
        }
        var command = MemoryPurgeStandbyList;
        var status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
        if (status != 0)
            throw new Win32Exception(RtlNtStatusToDosError(status), "Couldn't empty the standby list.");
    }

    private static void EnablePrivilege(string name)
    {
        const uint TokenAdjustPrivileges = 0x20;
        const uint TokenQuery = 0x8;
        const uint SePrivilegeEnabled = 0x2;

        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = SePrivilegeEnabled };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, 0, 0) || Marshal.GetLastPInvokeError() != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Couldn't enable {name}.");
        }
        finally
        {
            CloseHandle(token);
        }
    }

    // TOKEN_PRIVILEGES with one LUID_AND_ATTRIBUTES; the LUID is two 32-bit halves, so 4-byte packing.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQuerySystemInformation(int infoClass, [Out] nuint[] info, int length, out int returnLength);

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetSystemInformation(int infoClass, ref int info, int length);

    [LibraryImport("ntdll.dll")]
    private static partial int RtlNtStatusToDosError(int status);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? system, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(
        nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges state, int length, nint previous, nint returnLength);
}
