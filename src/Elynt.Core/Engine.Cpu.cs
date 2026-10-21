using Elynt.Core.Bios;
using Elynt.Core.Tuning.Cpu;
using Microsoft.Extensions.Logging;

namespace Elynt.Core;

public partial interface IEngine
{
    /// <summary>CPU tuning as the Overclocking page shows it. <paramref name="advanced"/> widens Curve Optimizer to -30.</summary>
    CpuTuningReport GetCpuTuning(bool advanced);

    /// <summary>Puts a tune on the CPU. It goes back after 60 s unless <see cref="ConfirmCpuTuning"/> keeps it.</summary>
    CpuTuningResult SetCpuTuning(CpuTune tune, CpuTuningOptions options);

    CpuTuningResult ConfirmCpuTuning();

    /// <summary>Puts the waiting CPU change back now.</summary>
    CpuTuningResult RevertCpuTuning();

    /// <summary>Back to the BIOS values now (Curve Optimizer 0, BIOS limits) and forgets the kept tune.</summary>
    CpuTuningResult ResetCpuTuning();

    /// <summary>Runs the stress test on the kept tune; passing it unlocks auto-apply.</summary>
    CpuTuningResult RunCpuStressTest(CancellationToken cancellation = default);

    /// <summary>Re-applies the kept tune after each sign-in and resume. Needs a passed stress test.</summary>
    CpuTuningResult SetCpuAutoApply(bool on);
}

public sealed partial class Engine
{
    public CpuTuningReport GetCpuTuning(bool advanced) => WithFaceitWarning(cpuTuning?.Describe(advanced) ?? NoCpuTuning);

    public CpuTuningResult SetCpuTuning(CpuTune tune, CpuTuningOptions options) => Cpu(t => t.Set(tune, options));

    public CpuTuningResult ConfirmCpuTuning() => Cpu(t => t.Confirm());

    public CpuTuningResult RevertCpuTuning() => Cpu(t => t.Revert());

    public CpuTuningResult ResetCpuTuning() => Cpu(t => t.ResetToDefaults());

    public CpuTuningResult RunCpuStressTest(CancellationToken cancellation = default) => Cpu(t => t.RunStressTest(cancellation));

    public CpuTuningResult SetCpuAutoApply(bool on) => Cpu(t => t.SetAutoApply(on));

    private CpuTuningResult Cpu(Func<CpuTuning, CpuTuningResult> command)
    {
        if (cpuTuning is null)
            return new CpuTuningResult(false, "CPU tuning isn't available on this PC.", NoCpuTuning);
        var result = command(cpuTuning);
        return result with { Cpu = WithFaceitWarning(result.Cpu) };
    }

    /// <summary>PawnIO is the only way in, and FACEIT blocks it: warn while it is installed, never block.</summary>
    private CpuTuningReport WithFaceitWarning(CpuTuningReport report)
    {
        if (system.PawnIo is null)
            return report;
        try
        {
            var pawnIo = PawnIo.Report();
            return pawnIo.Installed ? report with { Warnings = [pawnIo.FaceitWarning, .. report.Warnings] } : report;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't check PawnIO for the FACEIT warning");
            return report;
        }
    }

    private static CpuTuningReport NoCpuTuning => new(
        false, "Unknown CPU", CpuMaker.Unknown, [], null, null, null, false, false, 0, false, false, null, [],
        "CPU tuning isn't available on this PC.");
}
