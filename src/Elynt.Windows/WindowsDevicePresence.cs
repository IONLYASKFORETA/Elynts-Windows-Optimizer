using System.Runtime.InteropServices;
using Elynt.Core.Services;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>Detects printers and Bluetooth adapters. Reads only.</summary>
public sealed class WindowsDevicePresence : IDevicePresence
{
    private static readonly Guid BluetoothClass = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");

    /// <summary>Ports used only by the printers every Windows PC has (Print to PDF, XPS, OneNote, Fax).</summary>
    private static readonly string[] VirtualPorts = ["PORTPROMPT:", "nul:", "SHRFAX:", "XPSPort:", "FILE:"];

    public bool Has(DeviceKind device) => device switch
    {
        DeviceKind.Printer => HasRealPrinter(),
        DeviceKind.BluetoothAdapter => HasPresentDevice(BluetoothClass),
        _ => true,
    };

    private static bool HasRealPrinter()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var printers = machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers");
        foreach (var name in printers?.GetSubKeyNames() ?? [])
        {
            using var printer = printers!.OpenSubKey(name);
            var port = printer?.GetValue("Port") as string ?? "";
            if (port.Length > 0 &&
                !VirtualPorts.Any(v => port.Equals(v, StringComparison.OrdinalIgnoreCase)) &&
                !port.Contains("OneNote", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Network printers are connected per user, under each signed-in user's hive.
        using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
        foreach (var sid in users.GetSubKeyNames())
        {
            using var connections = users.OpenSubKey($@"{sid}\Printers\Connections");
            if (connections?.SubKeyCount > 0)
                return true;
        }
        return false;
    }

    /// <summary>Whether a device of this setup class is plugged in and working now (not just a leftover driver).</summary>
    private static bool HasPresentDevice(Guid deviceClass)
    {
        var set = SetupDiGetClassDevs(ref deviceClass, null, IntPtr.Zero, DigcfPresent);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
            return false;
        try
        {
            var info = new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };
            return SetupDiEnumDeviceInfo(set, 0, ref info);
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private const uint DigcfPresent = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfoData info);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
