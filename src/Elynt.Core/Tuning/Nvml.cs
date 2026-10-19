namespace Elynt.Core.Tuning;

/// <summary>The two clock domains NVML can offset.</summary>
public enum NvmlClock
{
    Graphics,
    Memory,
}

/// <summary>One NVIDIA GPU as NVML lists it.</summary>
/// <param name="Uuid">NVML's stable id, e.g. "GPU-3f1c…". Survives cards being re-ordered.</param>
public sealed record NvmlCard(int Index, string Name, string Uuid);

/// <summary>A range and the current value, as NVML reports it.</summary>
public sealed record NvmlRange(int Min, int Max, int Current);

/// <summary>The board power limit in milliwatts: the vBIOS range, the default and what is set now.</summary>
public sealed record NvmlPowerLimits(int MinMilliwatts, int MaxMilliwatts, int DefaultMilliwatts, int CurrentMilliwatts);

/// <summary>Live sensor values. Each is null when the card doesn't report it.</summary>
public sealed record NvmlReadings(
    int? TemperatureC = null,
    int? GraphicsClockMHz = null,
    int? MemoryClockMHz = null,
    int? PowerMilliwatts = null,
    int? UtilizationPercent = null,
    int? FanPercent = null);

/// <summary>An NVML call returned an error code instead of success.</summary>
public sealed class NvmlException(int code, string message) : Exception(message)
{
    public const int NotSupported = 3;
    public const int NoPermission = 4;
    public const int InvalidArgument = 2;

    public int Code { get; } = code;

    /// <summary>The card or driver refuses this control (common on GeForce, which NVML supports only partly).</summary>
    public bool Refused => Code is NotSupported or NoPermission;
}

/// <summary>
/// The part of NVIDIA's documented NVML library (<c>nvml.dll</c>, shipped with the driver) the app uses. Getters
/// return null when the card doesn't support them; setters throw <see cref="NvmlException"/>. Clocks are in MHz,
/// power in milliwatts. Nothing NVML sets survives a reboot or driver reload.
/// </summary>
public interface INvml
{
    /// <summary>Why NVML can't be used on this PC, or null when it can.</summary>
    string? UnavailableReason { get; }

    IReadOnlyList<NvmlCard> Cards();

    NvmlPowerLimits? PowerLimits(int card);

    void SetPowerLimit(int card, int milliwatts);

    /// <summary>The offset range and current offset for the top performance state (P0).</summary>
    NvmlRange? ClockOffset(int card, NvmlClock clock);

    void SetClockOffset(int card, NvmlClock clock, int mhz);

    /// <summary>The lowest and highest graphics clocks the card supports, for clock locking.</summary>
    (int Min, int Max)? SupportedGraphicsClocks(int card);

    void LockGraphicsClocks(int card, int minMHz, int maxMHz);

    void ResetGraphicsClocks(int card);

    NvmlReadings Readings(int card);
}
