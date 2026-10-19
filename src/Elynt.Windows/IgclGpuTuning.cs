using System.Runtime.InteropServices;
using Elynt.Core.Drivers;
using Elynt.Core.Tuning;

namespace Elynt.Windows;

/// <summary>
/// Intel Arc GPU tuning through IGCL (Intel Graphics Control Library), via our flat C bridge <c>elynt_igcl.dll</c>
/// (native/ElyntIgcl). Ranges, steps and defaults come straight from the driver. IGCL refuses changes until the
/// overclock waiver is set, and Intel requires every app to show its own warning before setting it, so each GPU
/// carries <see cref="Warning"/>. Without the bridge or an Intel driver, tuning is reported as unavailable.
/// Not yet verified on real Intel hardware.
/// </summary>
public sealed partial class IgclGpuTuning : IGpuTuningAdapter, IDisposable
{
    private const string Bridge = "elynt_igcl.dll";
    private const int NotSupported = -1; // the bridge maps CTL_RESULT_ERROR_UNSUPPORTED_FEATURE to -1
    private const int WaiverNotSet = 0x44000008; // CTL_RESULT_ERROR_CORE_OVERCLOCK_WAIVER_NOT_SET
    private const int ResetRequired = 0x40000024; // CTL_RESULT_ERROR_RESET_DEVICE_REQUIRED
    private const int RebootRequired = 0x40000025; // CTL_RESULT_ERROR_FULL_REBOOT_REQUIRED

    /// <summary>Our own wording of the risks Intel asks apps to show before the overclock waiver is set.</summary>
    public const string Warning =
        "Intel asks you to accept this before any tuning: higher clocks, voltage, power or temperature limits put " +
        "more electrical and heat stress on the GPU. That can shorten its life, crash games or Windows, and may " +
        "affect your warranty. Elynt puts every change back after 15 seconds unless you keep it, and starts on " +
        "default settings after a crash, but the risk of tuning is yours.";

    // VRAM speed is left out on purpose: IGCL may only apply it after a device reset or reboot, which can't be
    // undone within the 15 s confirm-or-revert window.
    private static readonly (int Native, string Id, string Name)[] ControlMap =
    [
        (0, GpuControls.ClockOffset, "GPU clock offset"),
        (1, GpuControls.VoltageOffset, "GPU voltage offset"),
        (2, GpuControls.PowerLimit, "Power limit"),
        (3, GpuControls.TemperatureLimit, "Temperature limit"),
    ];

    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _indexById = [];
    private readonly Dictionary<string, Dictionary<string, Scaled>> _scales = [];
    private List<GpuDevice>? _devices;
    private bool _initialized;

    public string? UnavailableReason { get; private set; }

    public IgclGpuTuning()
    {
        try
        {
            var result = elynt_igcl_init();
            if (result != 0)
                UnavailableReason = $"Intel GPU tuning isn't available: the Intel driver's control library didn't start (code 0x{result:X}). It needs an Intel Arc GPU with a current driver.";
            else
                _initialized = true;
        }
        catch (DllNotFoundException)
        {
            UnavailableReason = "Intel GPU tuning isn't included in this build (the IGCL bridge is missing).";
        }
        catch (EntryPointNotFoundException)
        {
            UnavailableReason = "Intel GPU tuning isn't available: the IGCL bridge is out of date.";
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Intel GPU tuning isn't available: {ex.Message}";
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
            foreach (var (id, scaled) in _scales[deviceId])
            {
                if (elynt_igcl_read(index, scaled.Native, out var value) == 0)
                    values[id] = scaled.ToApp(value);
            }
            return values;
        }
    }

    public bool IsAtDefaults(string deviceId)
    {
        lock (_gate)
        {
            var index = Index(deviceId);
            foreach (var scaled in _scales[deviceId].Values)
            {
                Check(elynt_igcl_read(index, scaled.Native, out var value), "read the current settings");
                if (Math.Abs(value - scaled.NativeDefault) > Math.Max(scaled.NativeStep, 1e-6) / 2)
                    return false;
            }
            return true;
        }
    }

    public void Write(string deviceId, IReadOnlyDictionary<string, int> values)
    {
        lock (_gate)
        {
            var index = Index(deviceId);
            foreach (var (id, value) in values)
            {
                var scaled = _scales[deviceId][id];
                Check(elynt_igcl_write(index, scaled.Native, scaled.ToNative(value)), $"set {id} to {value}");
            }
        }
    }

    public void ResetToDefaults(string deviceId)
    {
        lock (_gate)
        {
            Check(elynt_igcl_reset(Index(deviceId)), "reset the GPU to default settings");
        }
    }

    public void AutoTune(string deviceId, AutoTuneKind kind) =>
        throw new NotSupportedException("The Intel driver has no automatic tuning.");

    public void AcceptWarning(string deviceId)
    {
        lock (_gate)
        {
            Check(elynt_igcl_waiver(Index(deviceId)), "accept the overclock waiver");
        }
    }

    public void Dispose()
    {
        if (_initialized)
            elynt_igcl_terminate();
        _initialized = false;
    }

    private List<GpuDevice> Enumerate()
    {
        var devices = new List<GpuDevice>();
        var count = elynt_igcl_gpu_count();
        for (var index = 0; index < count; index++)
        {
            var name = new byte[128];
            elynt_igcl_gpu_name(index, name, name.Length);
            elynt_igcl_gpu_luid(index, out var luid);
            var id = $"intel:{luid:X}";
            _indexById[id] = index;

            var scales = new Dictionary<string, Scaled>();
            var controls = new List<TuningControlSpec>();
            foreach (var (native, controlId, controlName) in ControlMap)
            {
                if (elynt_igcl_control_info(index, native, out var info) != 0 || info.Supported == 0)
                    continue;
                if (Scaled.From(native, info) is not { } scaled)
                    continue;
                scales[controlId] = scaled;
                controls.Add(scaled.Spec(controlId, controlName));
            }
            _scales[id] = scales;

            if (controls.Count > 0)
                devices.Add(new GpuDevice(id, NameFrom(name), GpuVendor.Intel, controls, [], Warning));
        }

        if (devices.Count == 0)
            UnavailableReason = "No Intel GPU on this PC supports tuning through the Intel driver.";
        return devices;
    }

    private int Index(string deviceId)
    {
        Devices();
        return _indexById.TryGetValue(deviceId, out var index)
            ? index
            : throw new InvalidOperationException($"There is no Intel GPU called '{deviceId}'.");
    }

    private static string NameFrom(byte[] buffer)
    {
        var end = Array.IndexOf(buffer, (byte)0);
        var name = System.Text.Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end).Trim();
        return name.Length > 0 ? name : "Intel GPU";
    }

    private static void Check(int result, string what)
    {
        switch (result)
        {
            case 0:
                return;
            case NotSupported:
                throw new InvalidOperationException($"The Intel driver doesn't support this: couldn't {what}.");
            case WaiverNotSet:
                throw new InvalidOperationException($"The Intel driver couldn't {what} because the warning hasn't been accepted.");
            case ResetRequired or RebootRequired:
                throw new InvalidOperationException($"The Intel driver can only {what} after a restart, which can't be undone in time, so it wasn't changed.");
            default:
                throw new InvalidOperationException($"The Intel driver couldn't {what} (IGCL code 0x{result:X}).");
        }
    }

    /// <summary>
    /// One IGCL control in the app's whole-number units. IGCL reports units per GPU generation (V vs mV, W vs mW),
    /// so values are converted to mV, W, MHz and °C.
    /// </summary>
    private sealed record Scaled(int Native, string Unit, double Factor, int Min, int Max, int Step, int Default, double NativeDefault, double NativeStep)
    {
        public static Scaled? From(int native, NativeControlInfo info)
        {
            (string Unit, double Factor)? unit = info.Units switch
            {
                0 => ("MHz", 1),        // CTL_UNITS_FREQUENCY_MHZ
                3 => ("mV", 1000),      // CTL_UNITS_VOLTAGE_VOLTS
                13 => ("mV", 1),        // CTL_UNITS_VOLTAGE_MILLIVOLTS
                4 => ("W", 1),          // CTL_UNITS_POWER_WATTS
                10 => ("W", 0.001),     // CTL_UNITS_POWER_MILLIWATTS
                5 => ("°C", 1),         // CTL_UNITS_TEMPERATURE_CELSIUS
                11 => ("%", 1),         // CTL_UNITS_PERCENT
                _ => null,
            };
            if (unit is not { } u || info.Max < info.Min)
                return null;

            var min = (int)Math.Ceiling(info.Min * u.Factor - 1e-9);
            var max = (int)Math.Floor(info.Max * u.Factor + 1e-9);
            var step = Math.Max(1, (int)Math.Round(info.Step * u.Factor));
            var @default = Math.Clamp((int)Math.Round(info.Default * u.Factor), min, max);
            // A grid that doesn't hold the default would refuse going back to it, so fall back to single steps.
            if ((@default - min) % step != 0 || (max - min) % step != 0)
                step = 1;
            return new Scaled(native, u.Unit, u.Factor, min, max, step, @default, info.Default, info.Step);
        }

        public TuningControlSpec Spec(string id, string name) => new(id, name, Unit, Min, Max, Step, Default);

        public int ToApp(double value) => (int)Math.Round(value * Factor);

        public double ToNative(int value) => value / Factor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeControlInfo
    {
        public int Supported;
        public int Units;
        public double Min;
        public double Max;
        public double Step;
        public double Default;
    }

    [LibraryImport(Bridge)]
    private static partial int elynt_igcl_init();

    [LibraryImport(Bridge)]
    private static partial void elynt_igcl_terminate();

    [LibraryImport(Bridge)]
    private static partial int elynt_igcl_gpu_count();

    [LibraryImport(Bridge)]
    private static partial int elynt_igcl_gpu_name(int index, [Out] byte[] buffer, int length);

    [LibraryImport(Bridge)]
    private static partial int elynt_igcl_gpu_luid(int index, out long luid);

    [LibraryImport(Bridge)]
    private static partial int elynt_igcl_control_info(int index, int control, out NativeControlInfo info);

    [LibraryImport(Bridge)]
    private static partial int elynt_igcl_read(int index, int control, out double value);

    [LibraryImport(Bridge)]
    private static partial int elynt_igcl_write(int index, int control, double value);

    [LibraryImport(Bridge)]
    private static partial int elynt_igcl_waiver(int index);

    [LibraryImport(Bridge)]
    private static partial int elynt_igcl_reset(int index);
}
