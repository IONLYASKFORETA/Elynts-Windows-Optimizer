namespace Elynt.Core.Sensors;

public enum SensorKind
{
    Temperature,
    Clock,
    Load,
    Power,
}

/// <summary>Stable ids, so the same sensor from different sources is shown once, from the best source.</summary>
public static class SensorIds
{
    public const string CpuTemperature = "cpu.temperature";
    public const string CpuClock = "cpu.clock";
    public const string CpuLoad = "cpu.load";
}

/// <summary>One live value from one source.</summary>
/// <param name="Id">Stable across sources, e.g. <see cref="SensorIds.CpuTemperature"/> or "gpu0.temperature".</param>
public sealed record SensorReading(string Id, string Name, SensorKind Kind, double Value, string Unit);

/// <summary>
/// Somewhere sensor values come from without PawnIO: a vendor SDK (NVML, ADLX) or Windows itself. Read-only.
/// </summary>
public interface ISensorSource
{
    /// <summary>Shown next to each value, e.g. "Windows" or "NVIDIA NVML".</summary>
    string Name { get; }

    IReadOnlyList<SensorReading> Read();
}

/// <summary>One sensor as the page shows it, with where the value came from.</summary>
public sealed record SensorView(string Id, string Name, SensorKind Kind, double Value, string Unit, string Source);

/// <param name="FromPawnIo">PawnIO is installed and gave at least one value.</param>
/// <param name="Note">What's missing and why, in plain words, or null when nothing is.</param>
public sealed record SensorReport(IReadOnlyList<SensorView> Sensors, bool FromPawnIo, string? Note);
