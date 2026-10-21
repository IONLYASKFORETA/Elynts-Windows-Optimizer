using Elynt.Core.Bios;
using Elynt.Core.Tuning.Cpu;

namespace Elynt.Core.Tests;

/// <summary>
/// A CPU behind PawnIO. AMD parts answer the publicly documented RSMU messages; every message sent is recorded, and
/// unknown ones are accepted silently, as the real RyzenSMU module does. Intel parts hold MSR 0x606 and 0x610.
/// Also the stress test and the System event log.
/// </summary>
public sealed class FakeCpu : ICpuTuningHardware, ICpuStressTest, ICpuStabilityEvents
{
    // Public RSMU message ids: Zen 2/3 (Matisse, Vermeer) and Zen 4/5 (Raphael, Granite Ridge).
    private const uint Zen3SetAllCo = 0x0B, Zen3Ppt = 0x53, Zen3Tdc = 0x54, Zen3Edc = 0x55;
    private const uint Zen4SetAllCo = 0x07, Zen4Ppt = 0x56, Zen4Tdc = 0x57, Zen4Edc = 0x58;

    private FakeCpu(CpuIdentity identity) => Identity = identity;

    public CpuIdentity Identity { get; set; }

    public string? Unavailable { get; set; }

    public List<(uint Message, uint[] Args)> SmuMessages { get; } = [];

    public List<(uint Register, ulong Value)> MsrWrites { get; } = [];

    public List<uint> MsrReads { get; } = [];

    public int CurveOptimizer { get; private set; }
    public double Ppt { get; private set; }
    public double Tdc { get; private set; }
    public double Edc { get; private set; }
    private AmdPowerLimits _stockAmd = new(0, 0, 0);

    private readonly Dictionary<uint, ulong> _msrs = [];
    private ulong _stock610;

    public bool StressPasses { get; set; } = true;
    public int StressRuns { get; private set; }
    public TimeSpan? LastStressDuration { get; private set; }

    /// <summary>Called while the stress test runs, e.g. to log a WHEA error mid-test.</summary>
    public Action? DuringStress { get; set; }

    public List<DateTimeOffset> Crashes { get; } = [];
    public List<DateTimeOffset> WheaErrors { get; } = [];

    public static FakeCpu Raphael7800X3D() => Amd("AMD Ryzen 7 7800X3D 8-Core Processor", 0x19, 0x61, new AmdPowerLimits(162, 120, 180));

    public static FakeCpu GraniteRidge9800X3D() => Amd("AMD Ryzen 7 9800X3D 8-Core Processor", 0x1A, 0x44, new AmdPowerLimits(162, 120, 180));

    public static FakeCpu Vermeer5900X() => Amd("AMD Ryzen 9 5900X 12-Core Processor", 0x19, 0x21, new AmdPowerLimits(142, 95, 140));

    public static FakeCpu Vermeer5800X3D() => Amd("AMD Ryzen 7 5800X3D 8-Core Processor", 0x19, 0x21, new AmdPowerLimits(142, 95, 140));

    public static FakeCpu Matisse3700X() => Amd("AMD Ryzen 7 3700X 8-Core Processor", 0x17, 0x71, new AmdPowerLimits(88, 60, 90));

    /// <summary>An 8000G desktop APU: not on the reviewed list.</summary>
    public static FakeCpu Phoenix8700G() => Amd("AMD Ryzen 7 8700G w/ Radeon 780M Graphics", 0x19, 0x75, new AmdPowerLimits(88, 75, 150));

    public static FakeCpu Intel14900K(bool locked = false, uint microcode = 0x12B, int pl1 = 253, int pl2 = 253) =>
        Intel("Intel(R) Core(TM) i9-14900K", 0xB7, microcode, locked, pl1, pl2);

    public static FakeCpu Intel12700K() => Intel("12th Gen Intel(R) Core(TM) i7-12700K", 0x97, 0x2C, false, 190, 190);

    private static FakeCpu Amd(string name, int family, int model, AmdPowerLimits stock)
    {
        var cpu = new FakeCpu(new CpuIdentity(CpuMaker.Amd, name, family, model)) { _stockAmd = stock };
        cpu.PowerCycle();
        return cpu;
    }

    private static FakeCpu Intel(string name, int model, uint microcode, bool locked, int pl1, int pl2)
    {
        var cpu = new FakeCpu(new CpuIdentity(CpuMaker.Intel, name, 6, model, microcode));
        // Power unit 1/8 W (bits 3:0 = 3); PL1 bits 14:0 with enable bit 15, PL2 bits 46:32 with enable bit 47, lock bit 63.
        cpu._msrs[0x606] = 0x000A0E03;
        cpu._stock610 = ((ulong)(pl1 * 8) | 1UL << 15 | (ulong)(pl2 * 8) << 32 | 1UL << 47) | (locked ? 1UL << 63 : 0);
        cpu.PowerCycle();
        return cpu;
    }

    public int Pl1 => (int)((_msrs[0x610] & 0x7FFF) / 8);
    public int Pl2 => (int)(((_msrs[0x610] >> 32) & 0x7FFF) / 8);

    /// <summary>A reboot or sleep: runtime settings are gone and the BIOS values are back.</summary>
    public void PowerCycle()
    {
        CurveOptimizer = 0;
        (Ppt, Tdc, Edc) = (_stockAmd.PptWatts, _stockAmd.TdcAmps, _stockAmd.EdcAmps);
        if (Identity.Maker == CpuMaker.Intel)
            _msrs[0x610] = _stock610;
    }

    public bool AtBios => CurveOptimizer == 0 && Ppt == _stockAmd.PptWatts && Tdc == _stockAmd.TdcAmps && Edc == _stockAmd.EdcAmps &&
                          (Identity.Maker != CpuMaker.Intel || _msrs[0x610] == _stock610);

    public bool Touched => SmuMessages.Count > 0 || MsrWrites.Count > 0;

    string? ICpuTuningHardware.UnavailableReason => Unavailable;

    public CpuIdentity Identify() => Identity;

    public uint[] SendSmu(uint message, uint[] args)
    {
        SmuMessages.Add((message, args));
        var zen4 = Identity.Family >= 0x19 && Identity.Model >= 0x60 || Identity.Family == 0x1A;
        var (co, ppt, tdc, edc) = zen4 ? (Zen4SetAllCo, Zen4Ppt, Zen4Tdc, Zen4Edc) : (Zen3SetAllCo, Zen3Ppt, Zen3Tdc, Zen3Edc);
        if (message == co)
            CurveOptimizer = (short)(args[0] & 0xFFFF);
        else if (message == ppt)
            Ppt = args[0] / 1000.0;
        else if (message == tdc)
            Tdc = args[0] / 1000.0;
        else if (message == edc)
            Edc = args[0] / 1000.0;
        return [0, 0, 0, 0, 0, 0];
    }

    public AmdPowerLimits? ReadAmdLimits() => new(Ppt, Tdc, Edc);

    public ulong ReadMsr(uint register)
    {
        MsrReads.Add(register);
        return _msrs.TryGetValue(register, out var value) ? value : throw new InvalidOperationException($"MSR {register:X} isn't readable.");
    }

    public void WriteMsr(uint register, ulong value)
    {
        MsrWrites.Add((register, value));
        if ((_msrs[register] & 1UL << 63) != 0)
            throw new InvalidOperationException("The register is locked.");
        _msrs[register] = value;
    }

    public StressOutcome Run(TimeSpan duration, CancellationToken cancellation)
    {
        StressRuns++;
        LastStressDuration = duration;
        DuringStress?.Invoke();
        return StressPasses ? new StressOutcome(true, "All results checked.") : new StressOutcome(false, "Core 3 returned a wrong result.");
    }

    public bool UncleanShutdownSince(DateTimeOffset since) => Crashes.Any(c => c >= since);

    public int WheaErrorsSince(DateTimeOffset since) => WheaErrors.Count(w => w >= since);
}
