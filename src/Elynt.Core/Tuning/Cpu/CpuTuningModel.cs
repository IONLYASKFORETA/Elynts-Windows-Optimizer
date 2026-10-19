using Elynt.Core.Bios;

namespace Elynt.Core.Tuning.Cpu;

/// <summary>What CPUID and Windows say about the CPU. Needs no driver.</summary>
/// <param name="Name">The brand string, e.g. "AMD Ryzen 7 7800X3D 8-Core Processor".</param>
/// <param name="Family">The display family (base plus extended), e.g. 0x19 for Zen 3 and Zen 4.</param>
/// <param name="Model">The display model (base plus extended), e.g. 0x61 for Raphael.</param>
/// <param name="Microcode">The loaded microcode revision, or null when Windows doesn't say.</param>
public sealed record CpuIdentity(CpuMaker Maker, string Name, int Family, int Model, uint? Microcode = null);

/// <summary>The power limits a Ryzen's SMU is enforcing right now, from its PM table.</summary>
public sealed record AmdPowerLimits(double PptWatts, double TdcAmps, double EdcAmps);

/// <summary>
/// Raw CPU access through PawnIO's official signed modules (RyzenSMU, IntelMSR). Only <see cref="CpuTuning"/> calls
/// it, and only with the messages and registers on its own allowlist: the RyzenSMU module accepts any SMU message,
/// so the allowlist in the Engine is the only guard.
/// </summary>
public interface ICpuTuningHardware
{
    /// <summary>Why the CPU can't be tuned right now (PawnIO isn't installed, a module is missing), or null.</summary>
    string? UnavailableReason { get; }

    CpuIdentity Identify();

    /// <summary>Sends one message to the Ryzen SMU's RSMU mailbox and returns its reply arguments. Throws when the SMU refuses.</summary>
    uint[] SendSmu(uint message, uint[] args);

    /// <summary>The PPT/TDC/EDC limits from the SMU's PM table, or null when it can't be read.</summary>
    AmdPowerLimits? ReadAmdLimits();

    ulong ReadMsr(uint register);

    void WriteMsr(uint register, ulong value);
}

/// <summary>A short in-app all-core stress test that checks every result.</summary>
public interface ICpuStressTest
{
    StressOutcome Run(TimeSpan duration, CancellationToken cancellation);
}

/// <param name="Detail">What went wrong, or a short summary of the run.</param>
public sealed record StressOutcome(bool Passed, string Detail);

/// <summary>Windows' own record of crashes and hardware errors (the System event log).</summary>
public interface ICpuStabilityEvents
{
    /// <summary>Windows went down without shutting down cleanly (Kernel-Power 41, EventLog 6008) since <paramref name="since"/>.</summary>
    bool UncleanShutdownSince(DateTimeOffset since);

    /// <summary>Hardware errors (WHEA-Logger) logged since <paramref name="since"/>.</summary>
    int WheaErrorsSince(DateTimeOffset since);
}

/// <summary>
/// A CPU tune. Every value only ever lowers the CPU from its BIOS settings; null leaves that setting where the BIOS put it.
/// </summary>
/// <param name="CurveOptimizer">AMD all-core Curve Optimizer counts, negative only.</param>
/// <param name="PptWatts">AMD socket power limit.</param>
/// <param name="TdcAmps">AMD sustained current limit.</param>
/// <param name="EdcAmps">AMD peak current limit.</param>
/// <param name="Pl1Watts">Intel long-duration package power limit.</param>
/// <param name="Pl2Watts">Intel short-duration package power limit.</param>
public sealed record CpuTune(
    int? CurveOptimizer = null,
    int? PptWatts = null,
    int? TdcAmps = null,
    int? EdcAmps = null,
    int? Pl1Watts = null,
    int? Pl2Watts = null)
{
    public bool IsEmpty => this == new CpuTune();
}

/// <param name="Advanced">Advanced is on: Curve Optimizer may go to -30 instead of -15.</param>
/// <param name="Unofficial">
/// The user ticked "unofficial" for a CPU where AMD didn't enable Curve Optimizer (5800X3D, 5700X3D, 5600X3D).
/// </param>
public sealed record CpuTuningOptions(bool Advanced = false, bool Unofficial = false)
{
    public static CpuTuningOptions Default { get; } = new();
}

/// <summary>Stable ids for the CPU controls. The report lists only the ones this CPU offers.</summary>
public static class CpuControls
{
    public const string CurveOptimizer = "curveOptimizer";
    public const string Ppt = "ppt";
    public const string Tdc = "tdc";
    public const string Edc = "edc";
    public const string Pl1 = "pl1";
    public const string Pl2 = "pl2";
}

/// <summary>CPU tuning as the Overclocking page shows it.</summary>
/// <param name="Available">The CPU can be tuned now: PawnIO is installed and the CPU is on the reviewed list.</param>
/// <param name="Controls">Only what this CPU offers. Ranges already hold the Engine's limits (negative-only, lower-only).</param>
/// <param name="Active">What this app has set right now, or null when the CPU runs its BIOS settings.</param>
/// <param name="Kept">The confirmed tune.</param>
/// <param name="RevertsAt">When the unconfirmed change goes back, or null when nothing is waiting.</param>
/// <param name="StressTestPassed">The kept tune passed the stress test, so auto-apply can be turned on.</param>
/// <param name="AutoApply">The kept tune is re-applied after each sign-in and resume from sleep.</param>
/// <param name="Strikes">Crashes or hardware errors seen while the tune was on. Two turn auto-apply off.</param>
/// <param name="Disabled">Two strikes: the tune is no longer re-applied until a new one passes the stress test.</param>
/// <param name="CurveOptimizerUnofficial">Curve Optimizer works on this CPU only unofficially; it needs the "unofficial" tick.</param>
/// <param name="IntelDefaults">Intel Default Settings for this CPU (PL1/PL2), never above the BIOS values, or null.</param>
/// <param name="Warnings">Things to know: the FACEIT warning, an old microcode, an unofficial CPU.</param>
/// <param name="Notice">Something that happened, such as a change that went back or a strike after a crash.</param>
public sealed record CpuTuningReport(
    bool Available,
    string CpuName,
    CpuMaker Maker,
    IReadOnlyList<TuningControl> Controls,
    CpuTune? Active,
    CpuTune? Kept,
    DateTimeOffset? RevertsAt,
    bool StressTestPassed,
    bool AutoApply,
    int Strikes,
    bool Disabled,
    bool CurveOptimizerUnofficial,
    CpuTune? IntelDefaults,
    IReadOnlyList<string> Warnings,
    string? Notice);

public sealed record CpuTuningResult(bool Succeeded, string Message, CpuTuningReport Cpu);
