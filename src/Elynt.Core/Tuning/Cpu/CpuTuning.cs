using Elynt.Core.Bios;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elynt.Core.Tuning.Cpu;

/// <summary>
/// CPU tuning through PawnIO: AMD negative-only Curve Optimizer and lower-only PPT/TDC/EDC, Intel lower-only PL1/PL2.
/// One instance lives for the whole service. Every limit and the per-codename SMU allowlist are enforced here, never
/// in the UI. Every change goes back after <see cref="ConfirmWindow"/> unless kept; a kept tune is re-applied (after
/// sign-in and resume, never at boot) only once it has passed the stress test, and crashes or WHEA errors step it
/// back until two strikes turn it off.
/// </summary>
public sealed class CpuTuning : IDisposable
{
    public static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(60);

    /// <summary>How long after sign-in or resume a kept tune comes back: inside the 60–120 s window, never at boot.</summary>
    public static readonly TimeSpan ReapplyDelay = TimeSpan.FromSeconds(90);

    public static readonly TimeSpan StressTestLength = TimeSpan.FromMinutes(5);

    public const int CurveOptimizerCap = -15;
    public const int AdvancedCurveOptimizerCap = -30;

    /// <summary>How far a crash or WHEA error steps Curve Optimizer back towards 0.</summary>
    public const int StepBack = 5;

    public const int StrikesToDisable = 2;

    /// <summary>The lowest Intel PL1/PL2 the app sets (inference: below this a desktop CPU crawls).</summary>
    public const int IntelFloorWatts = 35;

    /// <summary>AMD limits never go below this share of the BIOS value (inference).</summary>
    public const double AmdFloorShare = 0.5;

    internal const string Key = "cpu";

    // Intel SDM vol. 4: MSR_RAPL_POWER_UNIT (bits 3:0 = power unit 1/2^n W) and MSR_PKG_POWER_LIMIT.
    internal const uint RaplPowerUnit = 0x606;
    internal const uint PackagePowerLimit = 0x610;
    private const ulong PowerLimitMask = 0x7FFF;
    private const ulong LockBit = 1UL << 63;

    private readonly ICpuTuningHardware _hardware;
    private readonly TuningSafety _safety;
    private readonly ICpuProfileStore _profiles;
    private readonly ICpuStressTest _stress;
    private readonly ICpuStabilityEvents _events;
    private readonly TimeProvider _clock;
    private readonly ILogger _log;
    private readonly Lock _gate = new();
    private readonly ConfirmOrRevert _probation;

    /// <summary>What this app has put on the CPU in this run, or null when it runs its BIOS settings.</summary>
    private CpuTune? _active;
    private string? _notice;
    private CpuProfile? _profile;

    public CpuTuning(
        ICpuTuningHardware hardware,
        TuningSafety safety,
        ICpuProfileStore profiles,
        ICpuStressTest stress,
        ICpuStabilityEvents events,
        TimeProvider? clock = null,
        ILogger<CpuTuning>? logger = null)
    {
        _hardware = hardware;
        _safety = safety;
        _profiles = profiles;
        _stress = stress;
        _events = events;
        _clock = clock ?? TimeProvider.System;
        _log = logger ?? NullLogger<CpuTuning>.Instance;
        _probation = new ConfirmOrRevert(_clock, _gate, (_, ex) => _log.LogError(ex, "Putting the CPU back failed"));
    }

    private CpuProfile Profile
    {
        get => _profile ??= _profiles.Load();
        set
        {
            _profiles.Save(value);
            _profile = value;
        }
    }

    public CpuTuningReport Describe(bool advanced)
    {
        lock (_gate)
        {
            return Report(advanced);
        }
    }

    /// <summary>Puts <paramref name="tune"/> on the CPU, on probation for <see cref="ConfirmWindow"/>.</summary>
    public CpuTuningResult Set(CpuTune tune, CpuTuningOptions options)
    {
        lock (_gate)
        {
            if (TryInspect(out var inspection) is { } failure)
                return Result(false, failure + " Nothing was changed.", options.Advanced);
            if (inspection.WhyNot is { } whyNot)
                return Result(false, whyNot + " Nothing was changed.", options.Advanced);
            if (tune.IsEmpty)
                return Result(false, "Nothing to change.", options.Advanced);
            if (_stressRunning)
                return Result(false, "The stress test is running. Wait for it to finish.", options.Advanced);
            if (WhyNot(inspection, tune, options) is { } refusal)
            {
                _log.LogWarning("Refused CPU tune {Tune}: {Refusal}", tune, refusal);
                return Result(false, refusal + " Nothing was changed.", options.Advanced);
            }
            return Change(inspection, tune, options.Advanced);
        }
    }

    /// <summary>Keeps the waiting change. A new kept tune needs its own stress test before auto-apply.</summary>
    public CpuTuningResult Confirm()
    {
        lock (_gate)
        {
            if (!_probation.Confirm(Key))
                return Result(false, "Nothing is waiting to be kept. A change that wasn't kept in time has already gone back.");
            if (_active is null)
                return Result(true, "The CPU is on its BIOS settings.");

            try
            {
                var profile = Profile;
                if (profile.Kept != _active)
                    profile = profile with { Kept = _active, AutoApply = false, Strikes = 0, Disabled = false };
                Profile = profile;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Couldn't save the kept CPU tune");
                return Result(false, $"Kept for now, but couldn't save it for next time: {ex.Message}");
            }

            _notice = null;
            _log.LogInformation("Kept CPU tune {Tune}", _active);
            return Result(true, "Kept until the PC restarts or sleeps. Pass the stress test to have it re-applied automatically.");
        }
    }

    /// <summary>Puts the waiting change back now instead of waiting for the countdown.</summary>
    public CpuTuningResult Revert()
    {
        lock (_gate)
        {
            if (!_probation.RevertNow(Key))
                return Result(false, "Nothing is waiting to be undone.");
            return Result(true, _active is null ? "Back on the BIOS settings." : "Back on your kept settings.");
        }
    }

    /// <summary>The BIOS settings, straight away and without probation. Also forgets the kept tune and auto-apply.</summary>
    public CpuTuningResult ResetToDefaults()
    {
        lock (_gate)
        {
            _probation.Cancel(Key);
            CancelReapply();
            try
            {
                if (_active is not null)
                    Write(Inspect(), new CpuTune(), _active);
                _active = null;
                _safety.MarkDefaults(Key);
                Profile = Profile with { Kept = null, StressPassedFor = null, AutoApply = false, Strikes = 0, Disabled = false };
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Resetting the CPU failed");
                return Result(false, $"Couldn't reset the CPU: {ex.Message} Restart the PC to bring back the BIOS settings.");
            }
            _notice = null;
            _log.LogInformation("Reset the CPU to its BIOS settings");
            return Result(true, "Back on the BIOS settings. Restarting the PC also brings back every BIOS value.");
        }
    }

    /// <summary>
    /// Runs the stress test on the kept tune while it is on the CPU. Passing (no wrong result and no WHEA error)
    /// unlocks auto-apply; failing puts the BIOS settings back and drops the tune.
    /// </summary>
    public CpuTuningResult RunStressTest(CancellationToken cancellation = default)
    {
        CpuTune tested;
        DateTimeOffset started;
        lock (_gate)
        {
            if (_stressRunning)
                return Result(false, "The stress test is already running.");
            if (Profile.Kept is not { } kept || _active != kept || _probation.Deadline(Key) is not null)
                return Result(false, "Keep a tune first. The stress test checks the kept tune while it is on the CPU.");
            tested = kept;
            started = _clock.GetUtcNow();
            _stressRunning = true;
        }

        StressOutcome outcome;
        try
        {
            _log.LogInformation("Stress testing CPU tune {Tune}", tested);
            outcome = _stress.Run(StressTestLength, cancellation);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _stressRunning = false;
                return Result(false, "The stress test was stopped, so it doesn't count.");
            }
        }
        catch (Exception ex)
        {
            outcome = new StressOutcome(false, ex.Message);
        }

        lock (_gate)
        {
            _stressRunning = false;
            if (_active != tested || Profile.Kept != tested)
                return Result(false, "The settings changed during the stress test, so the result doesn't count.");

            var whea = SafeWheaErrors(started);
            if (outcome.Passed && whea == 0)
            {
                Profile = Profile with { StressPassedFor = tested };
                _log.LogInformation("CPU tune {Tune} passed the stress test", tested);
                return Result(true, "Passed. You can now have this tune re-applied after each sign-in and resume.");
            }

            var why = whea > 0 ? $"Windows logged {whea} WHEA hardware error(s) during the test" : outcome.Detail.TrimEnd('.');
            _log.LogWarning("CPU tune {Tune} failed the stress test: {Why}", tested, why);
            try
            {
                Write(Inspect(), new CpuTune(), _active);
                _active = null;
                _safety.MarkDefaults(Key);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Putting the CPU back after a failed stress test failed");
            }
            Profile = Profile with { Kept = null, StressPassedFor = null, AutoApply = false };
            _notice = $"The stress test failed ({why}), so the tune was dropped and the CPU is back on its BIOS settings. Try a milder tune.";
            return Result(false, _notice);
        }
    }

    /// <summary>Re-applies the kept tune after each sign-in and resume. Turning it on needs a passed stress test.</summary>
    public CpuTuningResult SetAutoApply(bool on)
    {
        lock (_gate)
        {
            var profile = Profile;
            if (!on)
            {
                Profile = profile with { AutoApply = false };
                CancelReapply();
                return Result(true, "Auto-apply is off. The tune lasts until the PC restarts or sleeps.");
            }
            if (profile.Kept is null)
                return Result(false, "Keep a tune first.");
            if (profile.StressPassedFor != profile.Kept)
                return Result(false, "Run the stress test on the kept tune first. Auto-apply needs a passed test.");
            Profile = profile with { AutoApply = true, Disabled = false };
            return Result(true, $"On. The kept tune comes back {ReapplyDelay.TotalSeconds:0} seconds after each sign-in and resume from sleep.");
        }
    }

    /// <summary>Someone signed in: the kept tune comes back after <see cref="ReapplyDelay"/>.</summary>
    public void OnLogon() => ScheduleReapply("sign-in");

    /// <summary>
    /// The PC woke from sleep, which wipes every runtime CPU setting: the kept tune comes back after
    /// <see cref="ReapplyDelay"/>.
    /// </summary>
    public void OnResume()
    {
        lock (_gate)
        {
            _probation.Cancel(Key);
            _active = null;
        }
        ScheduleReapply("resume");
    }

    /// <summary>A clean stop: an unkept change goes back. Call before <see cref="GpuTuning.Shutdown"/> ends the session.</summary>
    public void Shutdown()
    {
        lock (_gate)
        {
            CancelReapply();
            _probation.RevertAll();
        }
    }

    /// <summary>
    /// At service start. Never applies anything: the kept tune comes back only after sign-in. If Windows crashed or
    /// logged WHEA errors while the CPU was tuned, this session skips re-apply, Curve Optimizer steps back by
    /// <see cref="StepBack"/>, and the second strike turns the tune off.
    /// </summary>
    public void RecoverAtStartup(SessionStart start)
    {
        lock (_gate)
        {
            // CPU settings live only until a restart, so after one the CPU runs its BIOS settings.
            _active = null;
            if (!start.WasLive(Key))
                return;

            var profile = Profile;
            var since = profile.LastAppliedAt ?? DateTimeOffset.MinValue;
            var crashed = start.Unclean || SafeUncleanShutdown(since);
            var whea = SafeWheaErrors(since);
            try
            {
                _safety.MarkDefaults(Key);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Couldn't record the CPU as back on its BIOS settings");
            }

            if (!crashed && whea == 0)
                return;
            _skipReapply = true;
            Strike(profile, crashed ? "Windows didn't shut down cleanly" : $"Windows logged {whea} WHEA hardware error(s)");
        }
    }

    private void Strike(CpuProfile profile, string what)
    {
        var strikes = profile.Strikes + 1;
        var kept = profile.Kept;
        string stepped = "";
        if (kept?.CurveOptimizer is { } co)
        {
            var milder = Math.Min(0, co + StepBack);
            kept = kept with { CurveOptimizer = milder == 0 ? null : milder };
            if (kept.IsEmpty)
                kept = null;
            stepped = $" Curve Optimizer stepped back from {co} to {milder}.";
        }

        if (strikes >= StrikesToDisable)
        {
            Profile = profile with { Kept = kept, StressPassedFor = null, AutoApply = false, Disabled = true, Strikes = strikes };
            _notice = $"{what} while your CPU tune was on, for the second time, so it is turned off and wasn't re-applied.{stepped} " +
                      "Run the stress test again before turning auto-apply back on.";
        }
        else
        {
            // The stepped-back tune is milder than the one that passed, so it keeps the pass.
            Profile = profile with { Kept = kept, StressPassedFor = kept, AutoApply = profile.AutoApply && kept is not null, Strikes = strikes };
            _notice = $"{what} while your CPU tune was on, so it wasn't re-applied this time.{stepped} " +
                      "One more crash or hardware error turns it off.";
        }
        _log.LogWarning("CPU tuning strike {Strikes}: {What}", strikes, what);
    }

    private bool SafeUncleanShutdown(DateTimeOffset since)
    {
        try
        {
            return _events.UncleanShutdownSince(since);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read unclean shutdowns from the event log");
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            CancelReapply();
        }
        _probation.Dispose();
    }

    private bool _stressRunning;
    private ITimer? _reapply;

    /// <summary>Set by the crash rule: this session never re-applies.</summary>
    private bool _skipReapply;

    private bool ShouldReapply(CpuProfile profile) =>
        !_skipReapply && profile is { Kept: not null, AutoApply: true, Disabled: false } && profile.StressPassedFor == profile.Kept;

    private void ScheduleReapply(string why)
    {
        lock (_gate)
        {
            CancelReapply();
            if (!ShouldReapply(Profile))
                return;
            _log.LogInformation("Re-applying the CPU tune {Seconds} s after {Why}", ReapplyDelay.TotalSeconds, why);
            _reapply = _clock.CreateTimer(_ => Reapply(), null, ReapplyDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Reapply()
    {
        lock (_gate)
        {
            CancelReapply();
            var profile = Profile;
            if (!ShouldReapply(profile) || _probation.Deadline(Key) is not null || _stressRunning)
                return;
            var kept = profile.Kept!;
            try
            {
                var inspection = Inspect();
                var refusal = inspection.WhyNot ?? WhyNot(inspection, kept, new CpuTuningOptions(Advanced: true, Unofficial: true));
                if (refusal is not null)
                {
                    _log.LogWarning("Not re-applying CPU tune {Tune}: {Refusal}", kept, refusal);
                    _notice = $"Your kept CPU tune wasn't re-applied: {refusal}";
                    return;
                }
                _safety.MarkLive(Key);
                Write(inspection, kept, _active);
                _active = kept;
                Profile = profile with { LastAppliedAt = _clock.GetUtcNow() };
                _log.LogInformation("Re-applied CPU tune {Tune}", kept);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Re-applying the CPU tune failed");
                _notice = $"Your kept CPU tune couldn't be re-applied: {ex.Message}";
            }
        }
    }

    private void CancelReapply()
    {
        _reapply?.Dispose();
        _reapply = null;
    }

    private int SafeWheaErrors(DateTimeOffset since)
    {
        try
        {
            return _events.WheaErrorsSince(since);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read WHEA errors from the event log");
            return 0;
        }
    }

    private CpuTuningResult Change(Inspection inspection, CpuTune tune, bool advanced)
    {
        var previous = _active;
        try
        {
            _safety.MarkLive(Key);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Couldn't record the CPU as tuned before changing it");
            return Result(false, $"Couldn't save the tuning state before changing the CPU: {ex.Message} Nothing was changed.", advanced);
        }

        try
        {
            Write(inspection, tune, previous);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Tuning the CPU failed; putting it back");
            TryWrite(inspection, previous ?? new CpuTune(), tune);
            return Result(false, $"The CPU refused the change: {ex.Message} The previous settings are back.", advanced);
        }

        _active = tune;
        _notice = null;
        Profile = Profile with { LastAppliedAt = _clock.GetUtcNow() };
        _probation.Start(Key, ConfirmWindow, () => PutBack(previous));
        _log.LogInformation("Tuned the CPU to {Tune}; reverting in {Seconds} s unless kept", tune, ConfirmWindow.TotalSeconds);
        return Result(true, $"Changed. Keep these settings within {ConfirmWindow.TotalSeconds:0} seconds or they go back.", advanced);
    }

    /// <summary>The probation ran out: back to what ran before the change (the kept tune, or the BIOS settings).</summary>
    private void PutBack(CpuTune? target)
    {
        var inspection = Inspect();
        Write(inspection, target ?? new CpuTune(), _active);
        _active = target;
        if (target is null)
            _safety.MarkDefaults(Key);
        _notice = target is null
            ? "The change wasn't kept, so the CPU is back on its BIOS settings."
            : "The change wasn't kept, so the CPU is back on your kept settings.";
        _log.LogInformation("CPU change not kept; back to {Tune}", (object?)target ?? "BIOS settings");
    }

    private static string? WhyNot(Inspection inspection, CpuTune tune, CpuTuningOptions options)
    {
        var stock = inspection.Stock;
        if (inspection.Smu is { } smu)
        {
            if (tune.Pl1Watts is not null || tune.Pl2Watts is not null)
                return "PL1 and PL2 are Intel settings; this is an AMD CPU.";
            if (tune.CurveOptimizer is { } co)
            {
                var cpu = inspection.Cpu!;
                if (smu.SetAllCoreCurve is null)
                    return $"AMD doesn't support Curve Optimizer on the {Short(cpu)}.";
                if (CpuCatalog.CurveOptimizerUnofficial(cpu) && !options.Unofficial)
                    return $"Curve Optimizer works on the {Short(cpu)} only unofficially. Tick \"unofficial\" first if you accept the risk.";
                if (co > 0)
                    return "Curve Optimizer only goes negative (less voltage). Positive values aren't allowed.";
                var cap = options.Advanced ? AdvancedCurveOptimizerCap : CurveOptimizerCap;
                if (co < cap)
                    return $"Curve Optimizer goes down to {cap}{(options.Advanced ? "" : " (-30 with Advanced on)")}; {co} is too far.";
            }
            return LowerOnly("PPT", "W", tune.PptWatts, stock.PptWatts!.Value, AmdFloor(stock.PptWatts.Value))
                ?? LowerOnly("TDC", "A", tune.TdcAmps, stock.TdcAmps!.Value, AmdFloor(stock.TdcAmps.Value))
                ?? LowerOnly("EDC", "A", tune.EdcAmps, stock.EdcAmps!.Value, AmdFloor(stock.EdcAmps.Value));
        }

        if (tune.CurveOptimizer is not null || tune.PptWatts is not null || tune.TdcAmps is not null || tune.EdcAmps is not null)
            return "Curve Optimizer, PPT, TDC and EDC are AMD settings; this is an Intel CPU.";
        int pl1 = stock.Pl1Watts!.Value, pl2 = stock.Pl2Watts!.Value;
        if (LowerOnly("PL1", "W", tune.Pl1Watts, pl1, Math.Min(IntelFloorWatts, pl1)) is { } pl1Refusal)
            return pl1Refusal;
        if (LowerOnly("PL2", "W", tune.Pl2Watts, pl2, Math.Min(IntelFloorWatts, pl2)) is { } pl2Refusal)
            return pl2Refusal;
        if ((tune.Pl1Watts ?? pl1) > (tune.Pl2Watts ?? pl2))
            return "PL1 can't be higher than PL2.";
        return null;
    }

    private static string? LowerOnly(string name, string unit, int? value, int stock, int floor)
    {
        if (value > stock)
            return $"{name} can only go down from the BIOS value of {stock} {unit}; {value} {unit} is higher.";
        if (value < floor)
            return $"{name} can't go below {floor} {unit}.";
        return null;
    }

    /// <summary>
    /// Writes <paramref name="target"/>. A setting the target leaves out goes back to its BIOS value only if
    /// <paramref name="previous"/> had changed it, so a BIOS Curve Optimizer is left alone unless we touched it.
    /// </summary>
    private void Write(Inspection inspection, CpuTune target, CpuTune? previous)
    {
        var stock = inspection.Stock;
        if (inspection.Smu is { } smu)
        {
            if (Pick(target.CurveOptimizer, previous?.CurveOptimizer, 0) is { } co)
                SendSmu(smu, smu.SetAllCoreCurve ?? throw new InvalidOperationException("Curve Optimizer isn't on this CPU's allowlist."), unchecked((ushort)(short)co));
            if (Pick(target.PptWatts, previous?.PptWatts, stock.PptWatts!.Value) is { } ppt)
                SendSmu(smu, smu.SetPpt, (uint)ppt * 1000);
            if (Pick(target.TdcAmps, previous?.TdcAmps, stock.TdcAmps!.Value) is { } tdc)
                SendSmu(smu, smu.SetTdc, (uint)tdc * 1000);
            if (Pick(target.EdcAmps, previous?.EdcAmps, stock.EdcAmps!.Value) is { } edc)
                SendSmu(smu, smu.SetEdc, (uint)edc * 1000);
            return;
        }

        if (target.Pl1Watts is null && target.Pl2Watts is null && previous?.Pl1Watts is null && previous?.Pl2Watts is null)
            return;
        var raw = _hardware.ReadMsr(PackagePowerLimit);
        if ((raw & LockBit) != 0)
            throw new InvalidOperationException("The BIOS locked the power limits.");
        var unit = PowerUnit();
        var pl1 = (ulong)Math.Round((target.Pl1Watts ?? stock.Pl1Watts!.Value) / unit) & PowerLimitMask;
        var pl2 = (ulong)Math.Round((target.Pl2Watts ?? stock.Pl2Watts!.Value) / unit) & PowerLimitMask;
        raw = (raw & ~PowerLimitMask & ~(PowerLimitMask << 32)) | pl1 | (pl2 << 32);
        WriteMsr(PackagePowerLimit, raw);
    }

    private static int? Pick(int? target, int? previous, int stock) => target ?? (previous is not null ? stock : null);

    /// <summary>The only way an SMU message leaves the app: anything off the codename's allowlist is refused.</summary>
    private void SendSmu(SmuAllowlist smu, uint message, uint argument)
    {
        if (!smu.Messages.Contains(message))
            throw new InvalidOperationException($"SMU message 0x{message:X} isn't on the {smu.Codename} allowlist.");
        _hardware.SendSmu(message, [argument]);
    }

    /// <summary>The only MSR the app ever writes is the package power limit.</summary>
    private void WriteMsr(uint register, ulong value)
    {
        if (register != PackagePowerLimit)
            throw new InvalidOperationException($"MSR 0x{register:X} isn't on the allowlist.");
        _hardware.WriteMsr(register, value);
    }

    private void TryWrite(Inspection inspection, CpuTune target, CpuTune? previous)
    {
        try
        {
            Write(inspection, target, previous);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Putting the CPU back failed too; a restart brings back the BIOS settings");
        }
    }

    private string? TryInspect(out Inspection inspection)
    {
        try
        {
            inspection = Inspect();
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read the CPU");
            inspection = new Inspection(null, null, null, new CpuTune(), false);
            return $"Couldn't read the CPU: {ex.Message}";
        }
    }

    private CpuTuningResult Result(bool succeeded, string message, bool advanced = false) => new(succeeded, message, Report(advanced));

    /// <summary>The CPU as it is now: who it is, why it can't be tuned, its BIOS limits.</summary>
    private sealed record Inspection(CpuIdentity? Cpu, string? WhyNot, SmuAllowlist? Smu, CpuTune Stock, bool IntelLocked);

    private Inspection Inspect()
    {
        if (_hardware.UnavailableReason is { } unavailable)
            return new Inspection(TryIdentify(), unavailable, null, new CpuTune(), false);

        var cpu = _hardware.Identify();
        switch (cpu.Maker)
        {
            case CpuMaker.Amd:
            {
                if (CpuCatalog.Allowlist(cpu) is not { } smu)
                    return new Inspection(cpu, $"The {cpu.Name.Trim()} isn't on the reviewed list of CPUs the app tunes, so nothing is sent to it.", null, new CpuTune(), false);
                var stock = Stock(() => _hardware.ReadAmdLimits() is { } limits
                    ? new CpuTune(PptWatts: Round(limits.PptWatts), TdcAmps: Round(limits.TdcAmps), EdcAmps: Round(limits.EdcAmps))
                    : null);
                if (stock is null)
                    return new Inspection(cpu, "Couldn't read this CPU's power limits through PawnIO.", smu, new CpuTune(), false);
                return new Inspection(cpu, null, smu, stock, false);
            }
            case CpuMaker.Intel:
            {
                var raw = _hardware.ReadMsr(PackagePowerLimit);
                if ((raw & LockBit) != 0)
                    return new Inspection(cpu, "The BIOS locked this CPU's power limits, so they can't be changed from Windows.", null, new CpuTune(), true);
                var unit = PowerUnit();
                var stock = Stock(() => new CpuTune(
                    Pl1Watts: (int)Math.Round((raw & PowerLimitMask) * unit),
                    Pl2Watts: (int)Math.Round(((raw >> 32) & PowerLimitMask) * unit)))!;
                return new Inspection(cpu, null, null, stock, false);
            }
            default:
                return new Inspection(cpu, "Only AMD Ryzen and Intel Core CPUs can be tuned.", null, new CpuTune(), false);
        }
    }

    /// <summary>The BIOS limits: read fresh while nothing of ours is on the CPU, otherwise the copy saved before our first change.</summary>
    private CpuTune? Stock(Func<CpuTune?> read)
    {
        if (_active is not null && Profile.Stock is { } saved)
            return saved;
        var stock = read();
        if (stock is not null && stock != Profile.Stock)
            Profile = Profile with { Stock = stock };
        return stock;
    }

    private double PowerUnit() => 1.0 / (1 << (int)(_hardware.ReadMsr(RaplPowerUnit) & 0xF));

    private CpuTuningReport Report(bool advanced)
    {
        Inspection inspection;
        try
        {
            inspection = Inspect();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read the CPU");
            inspection = new Inspection(TryIdentify(), $"Couldn't read the CPU: {ex.Message}", null, new CpuTune(), false);
        }

        var cpu = inspection.Cpu;
        var warnings = new List<string>();
        CpuTune? intelDefaults = null;
        var unofficial = cpu is not null && CpuCatalog.CurveOptimizerUnofficial(cpu);
        if (cpu is not null)
        {
            if (unofficial)
                warnings.Add($"AMD didn't enable Curve Optimizer on the {Short(cpu)}. It works only unofficially: tick \"unofficial\" to use it at your own risk.");
            if (CpuCatalog.IsRaptorLakeDesktop(cpu) && cpu.Microcode is { } microcode && microcode < CpuCatalog.IntelVminFixMicrocode)
                warnings.Add($"This CPU runs microcode 0x{microcode:X}, older than 0x12B, Intel's fix for 13th and 14th gen instability. " +
                             "Update the BIOS from your board maker's website before tuning.");
            if (inspection.WhyNot is null && CpuCatalog.IntelDefaultSettings(cpu) is { } defaults)
                intelDefaults = new CpuTune(
                    Pl1Watts: Math.Min(defaults.Pl1, inspection.Stock.Pl1Watts ?? defaults.Pl1),
                    Pl2Watts: Math.Min(defaults.Pl2, inspection.Stock.Pl2Watts ?? defaults.Pl2));
        }

        var profile = Profile;
        return new CpuTuningReport(
            inspection.WhyNot is null,
            cpu is null ? "Unknown CPU" : Short(cpu),
            cpu?.Maker ?? CpuMaker.Unknown,
            inspection.WhyNot is null ? Controls(inspection, advanced) : [],
            _active,
            profile.Kept,
            _probation.Deadline(Key),
            profile.Kept is not null && profile.StressPassedFor == profile.Kept,
            profile.AutoApply,
            profile.Strikes,
            profile.Disabled,
            unofficial,
            intelDefaults,
            warnings,
            _notice ?? inspection.WhyNot);
    }

    private List<TuningControl> Controls(Inspection inspection, bool advanced)
    {
        var stock = inspection.Stock;
        var active = _active ?? new CpuTune();
        var controls = new List<TuningControl>();
        if (inspection.Smu is { } smu)
        {
            if (smu.SetAllCoreCurve is not null)
            {
                var cap = advanced ? AdvancedCurveOptimizerCap : CurveOptimizerCap;
                controls.Add(new TuningControl(CpuControls.CurveOptimizer, "Curve Optimizer (all cores)", "counts", cap, 0, 1, 0, active.CurveOptimizer ?? 0));
            }
            controls.Add(Limit(CpuControls.Ppt, "PPT (socket power)", "W", AmdFloor(stock.PptWatts!.Value), stock.PptWatts.Value, active.PptWatts));
            controls.Add(Limit(CpuControls.Tdc, "TDC (sustained current)", "A", AmdFloor(stock.TdcAmps!.Value), stock.TdcAmps.Value, active.TdcAmps));
            controls.Add(Limit(CpuControls.Edc, "EDC (peak current)", "A", AmdFloor(stock.EdcAmps!.Value), stock.EdcAmps.Value, active.EdcAmps));
        }
        else if (stock.Pl1Watts is { } pl1 && stock.Pl2Watts is { } pl2)
        {
            controls.Add(Limit(CpuControls.Pl1, "PL1 (long power limit)", "W", Math.Min(IntelFloorWatts, pl1), pl1, active.Pl1Watts));
            controls.Add(Limit(CpuControls.Pl2, "PL2 (short power limit)", "W", Math.Min(IntelFloorWatts, pl2), pl2, active.Pl2Watts));
        }
        return controls;
    }

    private static TuningControl Limit(string id, string name, string unit, int min, int max, int? current) =>
        new(id, name, unit, min, max, 1, max, current ?? max);

    private static int AmdFloor(int stock) => (int)Math.Ceiling(stock * AmdFloorShare);

    private static int Round(double value) => (int)Math.Round(value);

    private static string Short(CpuIdentity cpu) =>
        cpu.Name.Replace("(R)", "").Replace("(TM)", "").Split(" w/ ")[0].Split(" with ")[0]
            .Replace(" Processor", "").Trim() is var name && name.Contains("-Core", StringComparison.Ordinal)
            ? name[..name.LastIndexOf(' ')]
            : name;

    private CpuIdentity? TryIdentify()
    {
        try
        {
            return _hardware.Identify();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't identify the CPU");
            return null;
        }
    }
}
