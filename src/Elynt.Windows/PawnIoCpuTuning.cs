using System.Runtime.Intrinsics.X86;
using System.Text;
using Elynt.Core.Bios;
using Elynt.Core.LowLevel;
using Elynt.Core.Tuning.Cpu;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// CPU tuning through PawnIO's official signed RyzenSMU and IntelMSR modules. A thin layer of our own over PawnIO's
/// IOCTL entry points: it sends exactly what <see cref="CpuTuning"/> asks for, and CpuTuning only asks for messages
/// on its per-codename allowlist. No code from GPL tools (ZenStates-Core, UXTU) was used.
/// Not yet verified on real hardware: the module function names and argument layout (ioctl_send_smu_command,
/// ioctl_read_msr, ioctl_write_msr) and the PM table offsets of the TDC and EDC limits.
/// </summary>
public sealed class PawnIoCpuTuning(IPawnIoDriver driver, string modulesFolder) : ICpuTuningHardware
{
    private const string RyzenSmu = "RyzenSMU.bin";
    private const string IntelMsr = "IntelMSR.bin";

    /// <summary>The PM table holds 32-bit floats; PPT, TDC and EDC limits sit at these float indexes (Zen 2 to Zen 5 desktop, inference from public dumps).</summary>
    private const int PptIndex = 0, TdcIndex = 2, EdcIndex = 8;

    private const int PmTableLength = 0x200;

    private readonly Lock _gate = new();
    private CpuIdentity? _identity;

    public string? UnavailableReason
    {
        get
        {
            if (driver.InstalledVersion() is null)
                return "Turn on PawnIO (on the Sensors panel) to tune the CPU. It is the only way in from Windows.";
            var module = Identify().Maker switch
            {
                CpuMaker.Amd => RyzenSmu,
                CpuMaker.Intel => IntelMsr,
                _ => null,
            };
            if (module is not null && !File.Exists(Path.Combine(modulesFolder, module)))
                return $"PawnIO's {Path.GetFileNameWithoutExtension(module)} module isn't downloaded. Turn PawnIO off and on again to fetch it.";
            return null;
        }
    }

    public CpuIdentity Identify() => _identity ??= ReadIdentity();

    public uint[] SendSmu(uint message, uint[] args)
    {
        lock (_gate)
        {
            using var module = PawnIoCpu.Module.Open(modulesFolder, RyzenSmu)
                ?? throw new InvalidOperationException("PawnIO's RyzenSMU module couldn't be loaded.");
            using var pci = new Mutex(false, @"Global\Access_PCI");
            if (!pci.WaitOne(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("Another tool is using the SMU. Close other tuning or monitoring tools and try again.");
            try
            {
                var input = new ulong[7];
                input[0] = message;
                for (var i = 0; i < Math.Min(args.Length, 6); i++)
                    input[i + 1] = args[i];
                var reply = module.Execute("ioctl_send_smu_command", input, 6)
                    ?? throw new InvalidOperationException($"The SMU refused message 0x{message:X}.");
                return reply.Select(r => (uint)r).ToArray();
            }
            finally
            {
                pci.ReleaseMutex();
            }
        }
    }

    public AmdPowerLimits? ReadAmdLimits()
    {
        lock (_gate)
        {
            using var module = PawnIoCpu.Module.Open(modulesFolder, RyzenSmu);
            if (module is null)
                return null;
            using var pci = new Mutex(false, @"Global\Access_PCI");
            if (!pci.WaitOne(TimeSpan.FromSeconds(2)))
                return null;
            try
            {
                if (module.Execute("ioctl_resolve_pm_table", [], 2) is null || module.Execute("ioctl_update_pm_table", [], 0) is null)
                    return null;
                if (module.Execute("ioctl_read_pm_table", [], PmTableLength) is not { } table)
                    return null;
                if (Float(table, PptIndex) is not { } ppt || Float(table, TdcIndex) is not { } tdc || Float(table, EdcIndex) is not { } edc)
                    return null;
                return new AmdPowerLimits(ppt, tdc, edc);
            }
            finally
            {
                pci.ReleaseMutex();
            }
        }
    }

    public ulong ReadMsr(uint register)
    {
        lock (_gate)
        {
            using var module = PawnIoCpu.Module.Open(modulesFolder, IntelMsr)
                ?? throw new InvalidOperationException("PawnIO's IntelMSR module couldn't be loaded.");
            return module.Execute("ioctl_read_msr", [register], 1) is [var value]
                ? value
                : throw new InvalidOperationException($"Couldn't read MSR 0x{register:X}.");
        }
    }

    public void WriteMsr(uint register, ulong value)
    {
        lock (_gate)
        {
            using var module = PawnIoCpu.Module.Open(modulesFolder, IntelMsr)
                ?? throw new InvalidOperationException("PawnIO's IntelMSR module couldn't be loaded.");
            if (module.Execute("ioctl_write_msr", [register, value], 0) is null)
                throw new InvalidOperationException($"The CPU refused the write to MSR 0x{register:X}.");
        }
    }

    private static double? Float(ulong[] table, int index)
    {
        if (index / 2 >= table.Length)
            return null;
        var packed = table[index / 2];
        var bits = index % 2 == 0 ? (int)(packed & 0xFFFFFFFF) : (int)(packed >> 32);
        var value = BitConverter.Int32BitsToSingle(bits);
        return float.IsFinite(value) && value > 0 ? value : null;
    }

    /// <summary>CPUID for the maker, family, model and brand string; the registry for the loaded microcode.</summary>
    private static CpuIdentity ReadIdentity()
    {
        if (!X86Base.IsSupported)
            return new CpuIdentity(CpuMaker.Unknown, "Unknown CPU", 0, 0);

        var (_, ebx, ecx, edx) = X86Base.CpuId(0, 0);
        var vendor = Encoding.ASCII.GetString([.. BitConverter.GetBytes(ebx), .. BitConverter.GetBytes(edx), .. BitConverter.GetBytes(ecx)]);
        var maker = vendor switch
        {
            "AuthenticAMD" => CpuMaker.Amd,
            "GenuineIntel" => CpuMaker.Intel,
            _ => CpuMaker.Unknown,
        };

        var signature = (uint)X86Base.CpuId(1, 0).Eax;
        var baseFamily = (int)((signature >> 8) & 0xF);
        var family = baseFamily == 0xF ? baseFamily + (int)((signature >> 20) & 0xFF) : baseFamily;
        var model = (int)((signature >> 4) & 0xF);
        if (baseFamily is 0x6 or 0xF)
            model |= (int)((signature >> 16) & 0xF) << 4;

        var brand = new StringBuilder();
        for (var leaf = 0x80000002u; leaf <= 0x80000004u; leaf++)
        {
            var (a, b, c, d) = X86Base.CpuId(unchecked((int)leaf), 0);
            foreach (var register in new[] { a, b, c, d })
                brand.Append(Encoding.ASCII.GetString(BitConverter.GetBytes(register)));
        }

        return new CpuIdentity(maker, brand.ToString().TrimEnd('\0').Trim(), family, model, Microcode());
    }

    /// <summary>Windows records the loaded microcode as "Update Revision": 8 bytes, the revision in the high 4.</summary>
    private static uint? Microcode()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return key?.GetValue("Update Revision") is byte[] { Length: >= 8 } bytes ? BitConverter.ToUInt32(bytes, 4) : null;
    }
}
