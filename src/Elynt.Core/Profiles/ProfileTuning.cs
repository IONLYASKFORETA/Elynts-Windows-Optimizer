using Elynt.Core.Tuning.Cpu;

namespace Elynt.Core.Profiles;

/// <summary>
/// One GPU's settings while the game runs. Checked against the vendor SDK's ranges when saved and again when the game
/// starts; put back to what ran before (factory settings or the kept tune) when the profile is undone.
/// </summary>
/// <param name="DeviceId">The GPU, as the Overclocking page names it (e.g. "amd:0").</param>
/// <param name="Values">Control id to value, in each control's unit. Controls left out stay as they are.</param>
public sealed record GpuProfileTuning(string DeviceId, IReadOnlyDictionary<string, int> Values)
{
    public bool Equals(GpuProfileTuning? other) =>
        other is not null && DeviceId == other.DeviceId && Values.Count == other.Values.Count &&
        Values.All(v => other.Values.TryGetValue(v.Key, out var value) && value == v.Value);

    public override int GetHashCode() => HashCode.Combine(DeviceId, Values.Count);
}

/// <summary>
/// A CPU tune while the game runs, under the same limits as the Overclocking page (negative-only Curve Optimizer,
/// lower-only power limits). Put back to what ran before (BIOS settings or the kept tune) when the profile is undone.
/// </summary>
/// <param name="Advanced">Curve Optimizer may go to -30 instead of -15.</param>
/// <param name="Unofficial">Curve Optimizer on a CPU where AMD didn't enable it, accepted by the user.</param>
public sealed record CpuProfileTuning(CpuTune Tune, bool Advanced = false, bool Unofficial = false)
{
    public CpuTuningOptions Options => new(Advanced, Unofficial);
}
