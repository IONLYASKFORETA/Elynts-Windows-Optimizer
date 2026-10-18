using System.Runtime.InteropServices;
using Elynt.Core.Drivers;
using Elynt.Core.Tuning;

namespace Elynt.Windows;

/// <summary>
/// AMD GPU tuning through ADLX, via our flat C bridge <c>elynt_adlx.dll</c> (native/ElyntAdlx). Ranges come straight
/// from the driver. Without the bridge or an AMD driver, tuning is reported as unavailable instead of failing.
/// Not yet verified on real AMD hardware.
/// </summary>
public sealed partial class AdlxGpuTuning : IGpuTuningAdapter, IDisposable
{
    private const string Bridge = "elynt_adlx.dll";
    private const int AdlxNotSupported = -1; // the bridge maps ADLX_NOT_SUPPORTED to -1
    private static readonly TimeSpan AutoTuneTimeout = TimeSpan.FromMinutes(10);

    private static readonly (int Native, string Id, string Name, string Unit)[] ControlMap =
    [
        (0, GpuControls.MinClock, "Min GPU clock", "MHz"),
        (1, GpuControls.MaxClock, "Max GPU clock", "MHz"),
        (2, GpuControls.Voltage, "GPU voltage", "mV"),
        (3, GpuControls.VramMaxClock, "Max VRAM clock", "MHz"),
        (4, GpuControls.PowerLimit, "Power limit", "%"),
        (5, GpuControls.FanSpeed, "Target fan speed", "RPM"),
    ];

    private static readonly (int Native, AutoTuneKind Kind)[] AutoMap =
    [
        (0, AutoTuneKind.OverclockGpu),
        (1, AutoTuneKind.UndervoltGpu),
        (2, AutoTuneKind.OverclockVram),
    ];

    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _indexById = [];
    private readonly Dictionary<string, Dictionary<string, int>> _defaults = [];
    private List<GpuDevice>? _devices;
    private bool _initialized;

    public string? UnavailableReason { get; private set; }

    public AdlxGpuTuning()
    {
        try
        {
            var result = elynt_adlx_init();
            if (result != 0)
                UnavailableReason = $"AMD GPU tuning isn't available: the AMD driver's ADLX didn't start (code {result}). It needs an AMD Radeon GPU with a current Adrenalin driver.";
            else
                _initialized = true;
        }
        catch (DllNotFoundException)
        {
            UnavailableReason = "AMD GPU tuning isn't included in this build (the ADLX bridge is missing).";
        }
        catch (EntryPointNotFoundException)
        {
            UnavailableReason = "AMD GPU tuning isn't available: the ADLX bridge is out of date.";
        }
        catch (Exception ex)
        {
            UnavailableReason = $"AMD GPU tuning isn't available: {ex.Message}";
        }
    }

    public IReadOnlyList<GpuDevice> Devices()
    {
        lock (_gate)
        {
            if (!_initialized)
                return [];
            return _devices ??= Enumerate();
        }
    }

    public IReadOnlyDictionary<string, int> Read(string deviceId)
    {
        lock (_gate)
        {
            var index = Index(deviceId);
            var values = new Dictionary<string, int>();
            foreach (var (native, id, _, _) in ControlMap)
            {
                if (elynt_adlx_read(index, native, out var range) == 0)
                    values[id] = range.Current;
            }
            return values;
        }
    }

    public bool IsAtDefaults(string deviceId)
    {
        lock (_gate)
        {
            Check(elynt_adlx_is_at_factory(Index(deviceId), out var atFactory), "read whether the GPU is at factory settings");
            return atFactory != 0;
        }
    }

    public void Write(string deviceId, IReadOnlyDictionary<string, int> values)
    {
        lock (_gate)
        {
            var index = Index(deviceId);
            // ADLX refuses a max clock below the min clock, so when both move up the max goes first, and when both
            // move down the min goes first.
            var raisingMax = values.TryGetValue(GpuControls.MaxClock, out var max) &&
                             elynt_adlx_read(index, 1, out var currentMax) == 0 && max >= currentMax.Current;
            foreach (var (id, value) in values.OrderBy(v => v.Key == (raisingMax ? GpuControls.MaxClock : GpuControls.MinClock) ? 0 : 1))
            {
                var native = ControlMap.Single(c => c.Id == id).Native;
                Check(elynt_adlx_write(index, native, value), $"set {id} to {value}");
            }
        }
    }

    public void ResetToDefaults(string deviceId)
    {
        lock (_gate)
        {
            Check(elynt_adlx_reset(Index(deviceId)), "reset the GPU to factory settings");
        }
    }

    public void AutoTune(string deviceId, AutoTuneKind kind)
    {
        lock (_gate)
        {
            var native = AutoMap.Single(a => a.Kind == kind).Native;
            Check(elynt_adlx_auto(Index(deviceId), native, (int)AutoTuneTimeout.TotalMilliseconds), $"run {kind}");
        }
    }

    public void Dispose()
    {
        if (_initialized)
            elynt_adlx_terminate();
        _initialized = false;
    }

    private List<GpuDevice> Enumerate()
    {
        var devices = new List<GpuDevice>();
        var count = elynt_adlx_gpu_count();
        for (var index = 0; index < count; index++)
        {
            var name = new byte[256];
            elynt_adlx_gpu_name(index, name, name.Length);
            elynt_adlx_gpu_unique_id(index, out var uniqueId);
            var id = $"amd:{uniqueId}";
            _indexById[id] = index;

            // ADLX reports ranges but not defaults. The values seen while the card is at factory settings are the
            // defaults; otherwise the current values stand in (only the slider's "default" mark depends on it).
            elynt_adlx_is_at_factory(index, out var atFactory);
            var defaults = _defaults.TryGetValue(id, out var known) ? known : [];
            var controls = new List<TuningControlSpec>();
            foreach (var (native, controlId, controlName, unit) in ControlMap)
            {
                if (elynt_adlx_read(index, native, out var range) != 0)
                    continue;
                if (atFactory != 0 || !defaults.ContainsKey(controlId))
                    defaults[controlId] = controlId == GpuControls.PowerLimit && range.Min <= 0 && range.Max >= 0 ? 0 : range.Current;
                controls.Add(new TuningControlSpec(controlId, controlName, unit, range.Min, range.Max, Math.Max(1, range.Step), defaults[controlId]));
            }
            _defaults[id] = defaults;

            var autoTunes = AutoMap
                .Where(a => elynt_adlx_auto_supported(index, a.Native, out var supported) == 0 && supported != 0)
                .Select(a => a.Kind)
                .ToList();

            if (controls.Count > 0 || autoTunes.Count > 0)
                devices.Add(new GpuDevice(id, NameFrom(name), GpuVendor.Amd, controls, autoTunes));
        }

        if (devices.Count == 0)
            UnavailableReason = "No AMD GPU on this PC supports tuning through the AMD driver.";
        return devices;
    }

    private int Index(string deviceId)
    {
        Devices();
        return _indexById.TryGetValue(deviceId, out var index)
            ? index
            : throw new InvalidOperationException($"There is no AMD GPU called '{deviceId}'.");
    }

    private static string NameFrom(byte[] buffer)
    {
        var end = Array.IndexOf(buffer, (byte)0);
        var name = System.Text.Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end).Trim();
        return name.Length > 0 ? name : "AMD GPU";
    }

    private static void Check(int result, string what)
    {
        if (result == AdlxNotSupported)
            throw new InvalidOperationException($"The AMD driver doesn't support this: couldn't {what}.");
        if (result != 0)
            throw new InvalidOperationException($"The AMD driver couldn't {what} (ADLX code {result}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRange
    {
        public int Min;
        public int Max;
        public int Step;
        public int Current;
    }

    [LibraryImport(Bridge)]
    private static partial int elynt_adlx_init();

    [LibraryImport(Bridge)]
    private static partial void elynt_adlx_terminate();

    [LibraryImport(Bridge)]
    private static partial int elynt_adlx_gpu_count();

    [LibraryImport(Bridge)]
    private static partial int elynt_adlx_gpu_name(int index, [Out] byte[] buffer, int length);

    [LibraryImport(Bridge)]
    private static partial int elynt_adlx_gpu_unique_id(int index, out int id);

    [LibraryImport(Bridge)]
    private static partial int elynt_adlx_read(int index, int control, out NativeRange range);

    [LibraryImport(Bridge)]
    private static partial int elynt_adlx_write(int index, int control, int value);

    [LibraryImport(Bridge)]
    private static partial int elynt_adlx_is_at_factory(int index, out int atFactory);

    [LibraryImport(Bridge)]
    private static partial int elynt_adlx_reset(int index);

    [LibraryImport(Bridge)]
    private static partial int elynt_adlx_auto_supported(int index, int kind, out int supported);

    [LibraryImport(Bridge)]
    private static partial int elynt_adlx_auto(int index, int kind, int timeoutMs);
}
