using Elynt.Core.Games;
using Elynt.Core.Profiles;

namespace Elynt.Core;

public partial interface IEngine
{
    /// <summary>
    /// Checks a profile being edited without saving it: whether it could be saved, and the anti-cheat warnings for the
    /// game (such as PawnIO on a FACEIT game), so the editor can show them before the user saves.
    /// </summary>
    ProfileResult CheckProfile(GameProfile profile);
}

/// <summary>GPU and CPU tuning inside per-game profiles: made when the game starts and put back with the rest of the profile.</summary>
public sealed partial class Engine
{
    public ProfileResult CheckProfile(GameProfile profile) =>
        WhyNotInProfile(profile) is { } problem
            ? ProfileResult.Failed(problem)
            : new(true, "This profile can be saved.", profile, AntiCheatClashes(profile));

    /// <summary>
    /// Warnings for tuning in a game with anti-cheat. CPU tuning needs PawnIO, which FACEIT blocks; an unstable CPU tune
    /// can trip BattlEye's memory checks (its "Corrupted Memory" kick; linking that to tuning is an inference). GPU tuning
    /// goes through the vendor's own driver, which no anti-cheat documents a problem with.
    /// </summary>
    private static IEnumerable<string> TuningClashes(GameProfile profile, InstalledGame game)
    {
        if (profile.CpuTuning is null)
            yield break;
        if (game.AntiCheats.Contains(Tweaks.AntiCheat.FaceIt))
            yield return $"CPU tuning: it needs the PawnIO driver, and FACEIT's anti-cheat blocks PawnIO, so while it is installed FACEIT may refuse to start {game.Name}. Uninstall PawnIO before playing on FACEIT.";
        if (game.AntiCheats.Contains(Tweaks.AntiCheat.BattlEye))
            yield return $"CPU tuning: an unstable CPU tune can get you kicked from {game.Name} by BattlEye (\"Corrupted Memory\"). Pass the stress test with this tune first.";
    }

    /// <summary>
    /// Why the profile's tuning can't be saved, or null when it can: the same limits as the Overclocking page (the SDK's
    /// ranges for GPUs, negative-only Curve Optimizer and lower-only power limits for CPUs).
    /// </summary>
    private string? WhyNotTuningInProfile(GameProfile profile)
    {
        if (profile.GpuTuning.Count > 0 && gpuTuning is null)
            return "GPU tuning isn't available on this PC.";
        if (profile.GpuTuning.GroupBy(g => g.DeviceId).FirstOrDefault(g => g.Count() > 1) is { } twice)
            return $"The GPU '{twice.Key}' is in the profile twice.";
        foreach (var gpu in profile.GpuTuning)
        {
            if (gpuTuning!.WhyNotInProfile(gpu.DeviceId, gpu.Values) is { } problem)
                return problem;
        }

        if (profile.CpuTuning is { } cpu)
        {
            if (cpuTuning is null)
                return "CPU tuning isn't available on this PC.";
            if (cpuTuning.WhyNotInProfile(cpu.Tune, cpu.Options) is { } problem)
                return problem;
        }
        return null;
    }

    /// <summary>Puts the profile's GPU and CPU tuning on. Each failure is added to <paramref name="problems"/>; the rest still goes ahead.</summary>
    private void StartProfileTuning(GameProfile profile, List<string> problems)
    {
        foreach (var gpu in profile.GpuTuning)
        {
            if (gpuTuning is null)
            {
                problems.Add("GPU tuning isn't available on this PC.");
                break;
            }
            var result = gpuTuning.ApplyForProfile(gpu.DeviceId, gpu.Values);
            if (!result.Succeeded)
                problems.Add($"GPU tuning: {result.Message}");
        }

        if (profile.CpuTuning is { } cpu)
        {
            if (cpuTuning is null)
                problems.Add("CPU tuning isn't available on this PC.");
            else if (cpuTuning.ApplyForProfile(cpu.Tune, cpu.Options) is { Succeeded: false } failed)
                problems.Add($"CPU tuning: {failed.Message}");
        }
    }

    /// <summary>Puts every GPU and CPU a profile tuned back to what ran before the game.</summary>
    private void StopProfileTuning(List<string> problems)
    {
        if (gpuTuning is not null)
            problems.AddRange(gpuTuning.EndProfile().Select(p => $"GPU tuning: {p}"));
        if (cpuTuning?.EndProfile() is { } cpuProblem)
            problems.Add($"CPU tuning: {cpuProblem}");
    }
}
