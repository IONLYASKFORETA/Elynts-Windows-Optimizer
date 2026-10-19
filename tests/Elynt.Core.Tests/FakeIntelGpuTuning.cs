using Elynt.Core.Drivers;
using Elynt.Core.Tuning;

namespace Elynt.Core.Tests;

/// <summary>
/// An in-memory Intel Arc GPU that behaves like IGCL: offsets and limits with reported ranges and defaults, no auto
/// tuning, and no change accepted until the overclock waiver is set in this process.
/// </summary>
public sealed class FakeIntelGpuTuning : IGpuTuningAdapter
{
    public const string DeviceId = "intel:0";
    public const string Warning = "Overclocking may damage your GPU.";

    public static IReadOnlyList<TuningControlSpec> ArcControls { get; } =
    [
        new(GpuControls.ClockOffset, "GPU clock offset", "MHz", Min: 0, Max: 250, Step: 1, Default: 0),
        new(GpuControls.VoltageOffset, "GPU voltage offset", "mV", Min: 0, Max: 100, Step: 1, Default: 0),
        new(GpuControls.PowerLimit, "Power limit", "W", Min: 190, Max: 228, Step: 1, Default: 190),
        new(GpuControls.TemperatureLimit, "Temperature limit", "°C", Min: 71, Max: 100, Step: 1, Default: 90),
    ];

    private readonly Dictionary<string, int> _values = ArcControls.ToDictionary(c => c.Id, c => c.Default);

    /// <summary>The driver's waiver, which IGCL forgets when the process ends.</summary>
    public bool WaiverSet { get; private set; }

    public int Resets { get; private set; }

    public string? UnavailableReason => null;

    public IReadOnlyList<GpuDevice> Devices() =>
        [new GpuDevice(DeviceId, "Intel Arc A770", GpuVendor.Intel, ArcControls, [], Warning)];

    public IReadOnlyDictionary<string, int> Read(string deviceId) => new Dictionary<string, int>(_values);

    public bool IsAtDefaults(string deviceId) => ArcControls.All(c => _values[c.Id] == c.Default);

    public void AcceptWarning(string deviceId) => WaiverSet = true;

    public void Write(string deviceId, IReadOnlyDictionary<string, int> values)
    {
        if (!WaiverSet)
            throw new InvalidOperationException("The overclock waiver isn't set.");
        foreach (var (id, value) in values)
            _values[id] = value;
    }

    public void ResetToDefaults(string deviceId)
    {
        if (!WaiverSet)
            throw new InvalidOperationException("The overclock waiver isn't set.");
        Resets++;
        foreach (var control in ArcControls)
            _values[control.Id] = control.Default;
    }

    public void AutoTune(string deviceId, AutoTuneKind kind) =>
        throw new NotSupportedException("IGCL has no automatic tuning.");

    /// <summary>Changes the card from outside the app, like a driver that kept a tune across a reboot.</summary>
    public void Force(string id, int value) => _values[id] = value;

    /// <summary>The service process ended, so the driver forgets the waiver; the tune itself may stay.</summary>
    public void ProcessEnded() => WaiverSet = false;

    public int Value(string id) => _values[id];
}
