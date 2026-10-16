using Elynt.Core.Tuning;

namespace Elynt.Core.Tests;

/// <summary>An in-memory GPU that behaves like a vendor SDK: reported ranges, defaults, factory reset and auto tuning.</summary>
public sealed class FakeGpuTuning : IGpuTuningAdapter
{
    public const string DeviceId = "amd:0";

    public static IReadOnlyList<TuningControlSpec> RadeonControls { get; } =
    [
        new(GpuControls.MaxClock, "Max GPU clock", "MHz", Min: 500, Max: 3000, Step: 5, Default: 2500),
        new(GpuControls.Voltage, "GPU voltage", "mV", Min: 700, Max: 1150, Step: 5, Default: 1150),
        new(GpuControls.PowerLimit, "Power limit", "%", Min: -10, Max: 15, Step: 1, Default: 0),
        new(GpuControls.FanSpeed, "Target fan speed", "RPM", Min: 500, Max: 3300, Step: 10, Default: 1500),
    ];

    private readonly Dictionary<string, int> _values;

    public FakeGpuTuning()
    {
        _values = RadeonControls.ToDictionary(c => c.Id, c => c.Default);
    }

    public string? UnavailableReason { get; set; }

    public AutoTuneKind? ActiveAuto { get; private set; }

    /// <summary>The next write throws after changing the first value, like a driver rejecting a combination.</summary>
    public bool FailNextWrite { get; set; }

    public int Writes { get; private set; }

    public int Resets { get; private set; }

    public IReadOnlyList<GpuDevice> Devices() => UnavailableReason is null
        ? [new GpuDevice(DeviceId, "AMD Radeon RX 7800 XT", GpuVendor.Amd, RadeonControls, [AutoTuneKind.OverclockGpu, AutoTuneKind.UndervoltGpu])]
        : [];

    public IReadOnlyDictionary<string, int> Read(string deviceId) => new Dictionary<string, int>(_values);

    public bool IsAtDefaults(string deviceId) =>
        ActiveAuto is null && RadeonControls.All(c => _values[c.Id] == c.Default);

    public void Write(string deviceId, IReadOnlyDictionary<string, int> values)
    {
        Writes++;
        ActiveAuto = null;
        foreach (var (id, value) in values)
        {
            _values[id] = value;
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new InvalidOperationException("The driver rejected that combination.");
            }
        }
    }

    public void ResetToDefaults(string deviceId)
    {
        Resets++;
        ActiveAuto = null;
        foreach (var control in RadeonControls)
            _values[control.Id] = control.Default;
    }

    public void AutoTune(string deviceId, AutoTuneKind kind)
    {
        ActiveAuto = kind;
        if (kind == AutoTuneKind.UndervoltGpu)
            _values[GpuControls.Voltage] = 1100;
        else
            _values[GpuControls.MaxClock] = 2700;
    }

    public int Value(string id) => _values[id];
}
