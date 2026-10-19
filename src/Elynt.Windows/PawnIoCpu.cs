using System.Runtime.InteropServices;
using System.Text;
using Elynt.Core.LowLevel;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// Reads the CPU through PawnIO's user-mode library (PawnIOLib.dll, installed with the driver) and its official signed
/// modules, which the service downloads to <c>modulesFolder</c>. Our own thin layer: it uses only PawnIO's IOCTL entry
/// points and public register facts (Intel SDM MSRs; AMD's Tctl register), and no code from GPL tools.
/// Not yet verified on real hardware: module function names, the PM table layout and PawnIOLib's install path.
/// </summary>
public sealed class PawnIoCpu(string modulesFolder) : IPawnIoCpu
{
    // Intel SDM vol. 4: IA32_PACKAGE_THERM_STATUS bits 22:16 are degrees below TjMax; MSR_TEMPERATURE_TARGET bits 23:16 are TjMax.
    private const ulong Ia32PackageThermStatus = 0x1B1;
    private const ulong MsrTemperatureTarget = 0x1A2;

    // AMD Family 17h+ SMN THM_TCON_CUR_TMP: bits 31:21 are Tctl in 0.125 °C steps; bit 19 selects the -49 °C range.
    private const ulong ThmTconCurTmp = 0x00059800;

    /// <summary>Big enough for every desktop PM table (inference: the largest public dumps are under 0x1000 bytes).</summary>
    private const int PmTableLength = 0x200;

    private readonly Lock _gate = new();

    public double? Temperature()
    {
        lock (_gate)
        {
            return Vendor() switch
            {
                "GenuineIntel" => IntelPackageTemperature(),
                "AuthenticAMD" => AmdTctl(),
                _ => null,
            };
        }
    }

    public double? PptLimitWatts()
    {
        lock (_gate)
        {
            if (Vendor() != "AuthenticAMD")
                return null;
            using var module = Module.Open(modulesFolder, "RyzenSMU.bin");
            if (module is null)
                return null;

            // The SMU is shared with other tools; PawnIO's modules expect callers to hold this mutex.
            using var pci = new Mutex(false, @"Global\Access_PCI");
            if (!pci.WaitOne(TimeSpan.FromSeconds(2)))
                return null;
            try
            {
                if (module.Execute("ioctl_resolve_pm_table", [], 2) is null)
                    return null;
                if (module.Execute("ioctl_update_pm_table", [], 0) is null)
                    return null;
                // The table is 32-bit floats packed two to a ulong; on Zen 2 to Zen 5 desktop tables the first float is
                // the PPT limit (inference from public PM table dumps).
                if (module.Execute("ioctl_read_pm_table", [], PmTableLength) is not [var first, ..])
                    return null;
                var ppt = BitConverter.Int32BitsToSingle((int)(first & 0xFFFFFFFF));
                return float.IsFinite(ppt) && ppt > 0 ? Math.Round(ppt, 1) : null;
            }
            finally
            {
                pci.ReleaseMutex();
            }
        }
    }

    private double? IntelPackageTemperature()
    {
        using var module = Module.Open(modulesFolder, "IntelMSR.bin");
        if (module?.Execute("ioctl_read_msr", [MsrTemperatureTarget], 1) is not [var target]
            || module.Execute("ioctl_read_msr", [Ia32PackageThermStatus], 1) is not [var status])
            return null;
        var tjMax = (int)((target >> 16) & 0xFF);
        var belowTjMax = (int)((status >> 16) & 0x7F);
        return tjMax > 0 ? tjMax - belowTjMax : null;
    }

    private double? AmdTctl()
    {
        using var module = Module.Open(modulesFolder, "AMDFamily17.bin");
        if (module?.Execute("ioctl_read_smn", [ThmTconCurTmp], 1) is not [var raw])
            return null;
        var value = (uint)raw;
        var celsius = (value >> 21) * 0.125;
        if ((value & (1u << 19)) != 0)
            celsius -= 49;
        return Math.Round(celsius, 1);
    }

    private static string? Vendor()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("VendorIdentifier") as string)?.Trim();
    }

    /// <summary>One loaded PawnIO module. Disposing it closes the driver handle.</summary>
    private sealed unsafe class Module : IDisposable
    {
        private static readonly string Library = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "PawnIOLib.dll");

        // PawnIOLib's exports; each returns an HRESULT.
        private static delegate* unmanaged<nint*, int> _open;
        private static delegate* unmanaged<nint, byte*, nuint, int> _load;
        private static delegate* unmanaged<nint, byte*, ulong*, nuint, ulong*, nuint, nuint*, int> _execute;
        private static delegate* unmanaged<nint, int> _close;

        private nint _handle;

        private Module(nint handle) => _handle = handle;

        /// <returns>The loaded module, or null when PawnIO, its library or the module file isn't there.</returns>
        public static Module? Open(string folder, string file)
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path) || !EnsureLibrary())
                return null;

            nint handle;
            if (_open(&handle) < 0)
                return null;
            var module = new Module(handle);
            var blob = File.ReadAllBytes(path);
            fixed (byte* data = blob)
            {
                if (_load(handle, data, (nuint)blob.Length) < 0)
                {
                    module.Dispose();
                    return null;
                }
            }
            return module;
        }

        /// <returns>The function's output, or null when it failed.</returns>
        public ulong[]? Execute(string function, ulong[] input, int outputLength)
        {
            var name = Encoding.ASCII.GetBytes(function + "\0");
            var inputs = input.Length == 0 ? new ulong[1] : input;
            var output = new ulong[Math.Max(outputLength, 1)];
            nuint returned = 0;
            int result;
            fixed (byte* namePtr = name)
            fixed (ulong* inPtr = inputs)
            fixed (ulong* outPtr = output)
                result = _execute(_handle, namePtr, inPtr, (nuint)input.Length, outPtr, (nuint)outputLength, &returned);
            if (result < 0)
                return null;
            return output[..(int)Math.Min((nuint)outputLength, returned)];
        }

        public void Dispose()
        {
            if (_handle != 0)
                _close(_handle);
            _handle = 0;
        }

        private static bool EnsureLibrary()
        {
            if (_execute != null)
                return true;
            // Loaded by full path from Program Files, which only administrators can write.
            if (!File.Exists(Library) || !NativeLibrary.TryLoad(Library, out var library))
                return false;
            if (!NativeLibrary.TryGetExport(library, "pawnio_open", out var open) ||
                !NativeLibrary.TryGetExport(library, "pawnio_load", out var load) ||
                !NativeLibrary.TryGetExport(library, "pawnio_execute", out var execute) ||
                !NativeLibrary.TryGetExport(library, "pawnio_close", out var close))
                return false;
            _open = (delegate* unmanaged<nint*, int>)open;
            _load = (delegate* unmanaged<nint, byte*, nuint, int>)load;
            _close = (delegate* unmanaged<nint, int>)close;
            _execute = (delegate* unmanaged<nint, byte*, ulong*, nuint, ulong*, nuint, nuint*, int>)execute;
            return true;
        }
    }
}
