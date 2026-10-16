using Elynt.Core.Drivers;

namespace Elynt.Core.Tuning;

/// <summary>The vendor's own one-click tuning, run by the vendor's driver.</summary>
public enum AutoTuneKind
{
    OverclockGpu,
    UndervoltGpu,
    OverclockVram,
}

/// <summary>Stable ids for the controls a GPU adapter may offer. An adapter offers only the ones its card supports.</summary>
public static class GpuControls
{
    public const string MinClock = "gpuMinClock";
    public const string MaxClock = "gpuMaxClock";
    public const string Voltage = "gpuVoltage";
    public const string VramMaxClock = "vramMaxClock";
    public const string PowerLimit = "powerLimit";
    public const string FanSpeed = "fanSpeed";
}

/// <summary>One slider as the vendor SDK reports it. The SDK's range is the only range the app allows.</summary>
public sealed record TuningControlSpec(string Id, string Name, string Unit, int Min, int Max, int Step, int Default)
{
    /// <summary>Why <paramref name="value"/> isn't allowed, or null when it is.</summary>
    public string? WhyNot(int value)
    {
        if (value < Min || value > Max)
            return $"{Name} must be between {Min} and {Max} {Unit}; {value} is outside the range the driver allows.";
        if (Step > 1 && (value - Min) % Step != 0)
            return $"{Name} moves in steps of {Step} {Unit} from {Min}; {value} isn't one of them.";
        return null;
    }
}

/// <summary>A tunable GPU as its adapter sees it.</summary>
/// <param name="Id">Unique across adapters, e.g. "amd:0".</param>
public sealed record GpuDevice(
    string Id,
    string Name,
    GpuVendor Vendor,
    IReadOnlyList<TuningControlSpec> Controls,
    IReadOnlyList<AutoTuneKind> AutoTunes);

/// <summary>
/// One vendor's GPU tuning SDK (ADLX, IGCL, NVML). The only code that changes GPU settings. Values are whole numbers
/// in each control's unit.
/// </summary>
public interface IGpuTuningAdapter
{
    /// <summary>Why this vendor's tuning can't be used on this PC, or null when it can.</summary>
    string? UnavailableReason { get; }

    IReadOnlyList<GpuDevice> Devices();

    IReadOnlyDictionary<string, int> Read(string deviceId);

    /// <summary>The card runs on factory settings, with no manual or automatic tuning active.</summary>
    bool IsAtDefaults(string deviceId);

    /// <summary>Sets the given controls, leaving the others as they are. Values are already inside the reported ranges.</summary>
    void Write(string deviceId, IReadOnlyDictionary<string, int> values);

    void ResetToDefaults(string deviceId);

    /// <summary>Runs the vendor's automatic tuning and returns once it has finished.</summary>
    void AutoTune(string deviceId, AutoTuneKind kind);
}

/// <summary>A slider ready to show: the SDK's range and default plus the card's current value.</summary>
public sealed record TuningControl(string Id, string Name, string Unit, int Min, int Max, int Step, int Default, int Current);

/// <summary>One GPU on the Overclocking page.</summary>
/// <param name="RevertsAt">When the unconfirmed change goes back, or null when nothing is waiting for confirmation.</param>
/// <param name="Notice">Something the user should know, such as a change that went back because it wasn't confirmed.</param>
public sealed record GpuTuningView(
    string DeviceId,
    string Name,
    GpuVendor Vendor,
    IReadOnlyList<TuningControl> Controls,
    IReadOnlyList<AutoTuneKind> AutoTunes,
    bool AtDefaults,
    DateTimeOffset? RevertsAt,
    string? Notice);

/// <param name="Notice">A page-wide note, such as tuning being unavailable or settings reset after a crash.</param>
public sealed record GpuTuningReport(IReadOnlyList<GpuTuningView> Gpus, string? Notice);

public sealed record TuningResult(bool Succeeded, string Message, GpuTuningView? Gpu);
