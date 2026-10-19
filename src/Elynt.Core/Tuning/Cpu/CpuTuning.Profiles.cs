using Microsoft.Extensions.Logging;

namespace Elynt.Core.Tuning.Cpu;

/// <summary>
/// A CPU tune held by a running per-game profile. It skips the confirm window (nobody is watching while a game
/// starts) but keeps every limit and the crash rule: the CPU is marked live before it changes, so a crash mid-game
/// counts as a strike, and a clean stop puts back what ran before the game.
/// </summary>
public sealed partial class CpuTuning
{
    /// <summary>A profile holds the CPU right now.</summary>
    private bool _profileHolds;

    /// <summary>What ran before the profile (the kept tune, or null for the BIOS settings).</summary>
    private CpuTune? _beforeProfile;

    /// <summary>Why this tune can't be part of a profile, or null when it can. Checked on save and again at game start.</summary>
    public string? WhyNotInProfile(CpuTune tune, CpuTuningOptions options)
    {
        lock (_gate)
        {
            if (TryInspect(out var inspection) is { } failure)
                return failure;
            return WhyNotInProfile(inspection, tune, options);
        }
    }

    private static string? WhyNotInProfile(Inspection inspection, CpuTune tune, CpuTuningOptions options)
    {
        if (inspection.WhyNot is { } whyNot)
            return whyNot;
        if (tune.IsEmpty)
            return "The CPU tuning has no settings.";
        return WhyNot(inspection, tune, options);
    }

    /// <summary>Puts a profile's tune on the CPU until <see cref="EndProfile"/>.</summary>
    public CpuTuningResult ApplyForProfile(CpuTune tune, CpuTuningOptions options)
    {
        lock (_gate)
        {
            if (_stressRunning)
                return Result(false, "The stress test is running, so the CPU was left alone.");
            if (TryInspect(out var inspection) is { } failure)
                return Result(false, failure + " Nothing was changed.");
            if (WhyNotInProfile(inspection, tune, options) is { } refusal)
                return Result(false, refusal + " Nothing was changed.");

            // A change still waiting to be kept goes back first, so the way back after the game is the kept state.
            _probation.RevertNow(Key);
            if (!_profileHolds)
                _beforeProfile = _active;

            try
            {
                _safety.MarkLive(Key);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Couldn't record the CPU as tuned before the profile changed it");
                return Result(false, $"Couldn't save the tuning state before changing the CPU: {ex.Message} Nothing was changed.");
            }

            try
            {
                Write(inspection, tune, _active);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "The profile's CPU tune failed; putting it back");
                TryWrite(inspection, _beforeProfile ?? new CpuTune(), tune);
                _active = _beforeProfile;
                _profileHolds = false;
                return Result(false, $"The CPU refused the change: {ex.Message} The previous settings are back.");
            }

            _active = tune;
            _profileHolds = true;
            _notice = null;
            Profile = Profile with { LastAppliedAt = _clock.GetUtcNow() };
            _log.LogInformation("A game profile tuned the CPU to {Tune}", tune);
            return Result(true, "Tuned for the game.");
        }
    }

    /// <summary>The game closed: the CPU goes back to what ran before. Returns why it couldn't, or null.</summary>
    public string? EndProfile()
    {
        lock (_gate)
        {
            if (!_profileHolds)
                return null;
            _profileHolds = false;
            var target = _beforeProfile;
            _beforeProfile = null;
            try
            {
                Write(Inspect(), target ?? new CpuTune(), _active);
                _active = target;
                if (target is null)
                    _safety.MarkDefaults(Key);
                _log.LogInformation("Put the CPU back after the game to {Tune}", (object?)target ?? "BIOS settings");
                return null;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Putting the CPU back after the game failed");
                return $"Couldn't put the CPU back: {ex.Message} Restart the PC to bring back the BIOS settings.";
            }
        }
    }
}
