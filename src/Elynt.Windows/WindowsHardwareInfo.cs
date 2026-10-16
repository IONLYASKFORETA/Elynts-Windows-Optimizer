using System.Runtime.InteropServices;
using Elynt.Core.Bios;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// Reads the board and memory from the firmware's SMBIOS table and the CPU name from the registry. Read-only, and
/// needs no driver. PBO can't be read this way (it needs the AMD SMU, which only a kernel driver can reach), so it is
/// always reported as unknown.
/// </summary>
public sealed partial class WindowsHardwareInfo : IHardwareInfo
{
    private const uint Rsmb = 0x52534D42; // 'RSMB'

    public HardwareSnapshot Read()
    {
        var table = SmbiosTable.Parse(ReadSmbios());
        return new HardwareSnapshot(table.BoardManufacturer, table.BoardProduct, ReadCpuName(), table.Memory, ReadoutState.Unknown);
    }

    private static byte[] ReadSmbios()
    {
        var size = GetSystemFirmwareTable(Rsmb, 0, null, 0);
        if (size == 0)
            throw new InvalidOperationException($"Windows didn't return the SMBIOS table (error {Marshal.GetLastPInvokeError()}).");
        var buffer = new byte[size];
        var written = GetSystemFirmwareTable(Rsmb, 0, buffer, size);
        return buffer[..(int)Math.Min(written, size)];
    }

    private static string ReadCpuName()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId, byte[]? firmwareTableBuffer, uint bufferSize);
}
