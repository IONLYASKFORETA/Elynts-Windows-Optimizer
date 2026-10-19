using Elynt.Core.Drivers;

namespace Elynt.Core.Tuning;

/// <summary>
/// NVIDIA GPU tuning through NVIDIA's documented NVML: core and memory clock offsets, the board power limit and
/// graphics clock locking. NVML supports GeForce cards only partly, so each control is probed per card and only the
/// ones the card accepts are offered. No voltage or VF-curve editing: NVML has none.
/// </summary>
public sealed class NvidiaGpuTuning(INvml nvml) : IGpuTuningAdapter, IGpuReadouts
{
    private readonly Lock _gate = new();
    private Dictionary<string, Card>? _cards;

    public string? UnavailableReason => nvml.UnavailableReason;

    public IReadOnlyList<GpuDevice> Devices()
    {
        lock (_gate)
        {
            return Cards().Values.Select(c => c.Device).ToList();
        }
    }

    public IReadOnlyDictionary<string, int> Read(string deviceId)
    {
        lock (_gate)
        {
            var card = Find(deviceId);
            var values = new Dictionary<string, int>();
            foreach (var control in card.Device.Controls)
            {
                values[control.Id] = control.Id switch
                {
                    GpuControls.PowerLimit => Watts(nvml.PowerLimits(card.Index)?.CurrentMilliwatts ?? control.Default * 1000),
                    GpuControls.CoreClockOffset => nvml.ClockOffset(card.Index, NvmlClock.Graphics)?.Current ?? 0,
                    GpuControls.MemoryClockOffset => nvml.ClockOffset(card.Index, NvmlClock.Memory)?.Current ?? 0,
                    // NVML has no getter for locked clocks, so the adapter remembers what it set.
                    GpuControls.MinClock => card.Lock?.Min ?? control.Default,
                    GpuControls.MaxClock => card.Lock?.Max ?? control.Default,
                    _ => control.Default,
                };
            }
            return values;
        }
    }

    public bool IsAtDefaults(string deviceId)
    {
        var card = Find(deviceId);
        var values = Read(deviceId);
        return card.Device.Controls.All(c => values[c.Id] == c.Default);
    }

    /// <summary>In NVIDIA's suggested order: power limit, then offsets, then the clock lock.</summary>
    public void Write(string deviceId, IReadOnlyDictionary<string, int> values)
    {
        lock (_gate)
        {
            var card = Find(deviceId);
            if (values.TryGetValue(GpuControls.PowerLimit, out var watts))
                nvml.SetPowerLimit(card.Index, watts * 1000);
            if (values.TryGetValue(GpuControls.CoreClockOffset, out var core))
                nvml.SetClockOffset(card.Index, NvmlClock.Graphics, core);
            if (values.TryGetValue(GpuControls.MemoryClockOffset, out var memory))
                nvml.SetClockOffset(card.Index, NvmlClock.Memory, memory);

            var hasMin = values.TryGetValue(GpuControls.MinClock, out var min);
            var hasMax = values.TryGetValue(GpuControls.MaxClock, out var max);
            if (!hasMin && !hasMax)
                return;
            var floor = Spec(card, GpuControls.MinClock);
            var cap = Spec(card, GpuControls.MaxClock);
            min = hasMin ? min : card.Lock?.Min ?? floor.Default;
            max = hasMax ? max : card.Lock?.Max ?? cap.Default;
            if (min > max)
                throw new InvalidOperationException($"The locked min GPU clock ({min} MHz) can't be above the max GPU clock ({max} MHz).");

            if (min == floor.Default && max == cap.Default)
            {
                nvml.ResetGraphicsClocks(card.Index);
                card.Lock = null;
            }
            else
            {
                nvml.LockGraphicsClocks(card.Index, min, max);
                card.Lock = (min, max);
            }
        }
    }

    public void ResetToDefaults(string deviceId)
    {
        lock (_gate)
        {
            Write(deviceId, Find(deviceId).Device.Controls.ToDictionary(c => c.Id, c => c.Default));
        }
    }

    public IReadOnlyList<GpuReadout> Readouts(string deviceId)
    {
        lock (_gate)
        {
            var r = nvml.Readings(Find(deviceId).Index);
            var readouts = new List<GpuReadout>();
            void Add(string name, int? value, string unit)
            {
                if (value is { } v)
                    readouts.Add(new GpuReadout(name, v, unit));
            }
            Add("Temperature", r.TemperatureC, "°C");
            Add("GPU clock", r.GraphicsClockMHz, "MHz");
            Add("Memory clock", r.MemoryClockMHz, "MHz");
            Add("Power draw", r.PowerMilliwatts / 1000, "W");
            Add("GPU load", r.UtilizationPercent, "%");
            Add("Fan", r.FanPercent, "%");
            return readouts;
        }
    }

    public void AutoTune(string deviceId, AutoTuneKind kind) =>
        throw new NotSupportedException("NVIDIA's automatic tuning lives in the NVIDIA App and can't be started from here.");

    private Card Find(string deviceId)
    {
        lock (_gate)
        {
            return Cards().GetValueOrDefault(deviceId) ?? throw new InvalidOperationException($"There is no NVIDIA GPU called '{deviceId}'.");
        }
    }

    private Dictionary<string, Card> Cards()
    {
        if (_cards is not null)
            return _cards;
        if (nvml.UnavailableReason is not null)
            return [];
        _cards = nvml.Cards().Select(Probe).Where(c => c.Device.Controls.Count > 0).ToDictionary(c => c.Device.Id);
        return _cards;
    }

    /// <summary>
    /// Offers a control only when the card both reports it and accepts a write. Each probe writes back the value the
    /// card already has (or clears a clock lock, which nothing else has set yet), so probing changes nothing.
    /// </summary>
    private Card Probe(NvmlCard card)
    {
        var i = card.Index;
        var controls = new List<TuningControlSpec>();
        if (nvml.ClockOffset(i, NvmlClock.Graphics) is { } core && Accepts(() => nvml.SetClockOffset(i, NvmlClock.Graphics, core.Current)))
            controls.Add(new(GpuControls.CoreClockOffset, "Core clock offset", "MHz", core.Min, core.Max, 1, 0));
        if (nvml.ClockOffset(i, NvmlClock.Memory) is { } memory && Accepts(() => nvml.SetClockOffset(i, NvmlClock.Memory, memory.Current)))
            controls.Add(new(GpuControls.MemoryClockOffset, "Memory clock offset", "MHz", memory.Min, memory.Max, 1, 0));
        if (nvml.PowerLimits(i) is { } power && Accepts(() => nvml.SetPowerLimit(i, power.CurrentMilliwatts)))
        {
            var min = (power.MinMilliwatts + 999) / 1000;
            var max = power.MaxMilliwatts / 1000;
            controls.Add(new(GpuControls.PowerLimit, "Power limit", "W", min, max, 1, Math.Clamp(Watts(power.DefaultMilliwatts), min, max)));
        }
        if (nvml.SupportedGraphicsClocks(i) is { } clocks && Accepts(() => nvml.ResetGraphicsClocks(i)))
        {
            controls.Add(new(GpuControls.MinClock, "Locked min GPU clock", "MHz", clocks.Min, clocks.Max, 1, clocks.Min));
            controls.Add(new(GpuControls.MaxClock, "Max GPU clock (cap)", "MHz", clocks.Min, clocks.Max, 1, clocks.Max));
        }

        return new Card(i, new GpuDevice("nvidia:" + card.Uuid, card.Name, GpuVendor.Nvidia, controls, []));
    }

    private static TuningControlSpec Spec(Card card, string id) => card.Device.Controls.Single(c => c.Id == id);

    private static bool Accepts(Action probe)
    {
        try
        {
            probe();
            return true;
        }
        catch (NvmlException)
        {
            return false;
        }
    }

    private static int Watts(int milliwatts) => (int)Math.Round(milliwatts / 1000.0);

    private sealed class Card(int index, GpuDevice device)
    {
        public int Index { get; } = index;

        public GpuDevice Device { get; } = device;

        /// <summary>The clock lock this adapter set, or null when the clocks are free.</summary>
        public (int Min, int Max)? Lock { get; set; }
    }
}
