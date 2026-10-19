using System.Runtime.InteropServices;
using System.Text;
using Elynt.Core.Tuning;

namespace Elynt.Windows;

/// <summary>
/// NVIDIA's NVML (<c>nvml.dll</c>), loaded at runtime from the installed driver: DCH drivers put it in System32,
/// older ones under <c>NVIDIA Corporation\NVSMI</c>. It is never shipped with the app (the driver licence forbids
/// redistributing it). Only documented NVML calls are used, declared by hand from NVIDIA's public NVML API reference
/// (https://docs.nvidia.com/deploy/nvml-api/); NVIDIA licenses the NVML interface royalty-free for use in individual
/// and commercial software. Setters need admin, which the service has.
/// Not yet verified on real NVIDIA hardware; on GeForce, NVML clock offsets on Windows are unconfirmed.
/// </summary>
public sealed unsafe class NvmlLibrary : INvml, IDisposable
{
    private const int Success = 0;
    private const int ClockGraphics = 0;
    private const int ClockMemory = 2;
    private const int TemperatureGpu = 0;
    private const int Pstate0 = 0;

    private readonly Lock _gate = new();
    private readonly nint _library;
    private readonly Dictionary<int, nint> _handles = [];
    private bool _initialized;

    public string? UnavailableReason { get; }

    public NvmlLibrary()
    {
        if (!OperatingSystem.IsWindows())
        {
            UnavailableReason = "NVIDIA GPU tuning needs Windows.";
            return;
        }

        foreach (var path in Candidates())
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out _library))
                break;
        }
        if (_library == 0)
        {
            UnavailableReason = "NVIDIA GPU tuning isn't available: no NVIDIA driver was found.";
            return;
        }

        try
        {
            var result = ((delegate* unmanaged<int>)Export("nvmlInit_v2"))();
            if (result != Success)
                UnavailableReason = $"NVIDIA GPU tuning isn't available: the NVIDIA driver's NVML didn't start ({ErrorText(result)}).";
            else
                _initialized = true;
        }
        catch (EntryPointNotFoundException)
        {
            UnavailableReason = "NVIDIA GPU tuning isn't available: the NVIDIA driver is too old.";
        }
    }

    public IReadOnlyList<NvmlCard> Cards()
    {
        lock (_gate)
        {
            uint count;
            Check(((delegate* unmanaged<uint*, int>)Export("nvmlDeviceGetCount_v2"))(&count), "count the NVIDIA GPUs");
            var cards = new List<NvmlCard>();
            for (var i = 0; i < (int)count; i++)
            {
                if (TryHandle(i) is not { } handle)
                    continue;
                var name = Text(handle, "nvmlDeviceGetName") ?? "NVIDIA GPU";
                var uuid = Text(handle, "nvmlDeviceGetUUID") ?? $"index-{i}";
                cards.Add(new NvmlCard(i, name, uuid));
            }
            return cards;
        }
    }

    public NvmlPowerLimits? PowerLimits(int card)
    {
        lock (_gate)
        {
            var device = Handle(card);
            uint min, max, standard, current;
            if (((delegate* unmanaged<nint, uint*, uint*, int>)Export("nvmlDeviceGetPowerManagementLimitConstraints"))(device, &min, &max) != Success ||
                ((delegate* unmanaged<nint, uint*, int>)Export("nvmlDeviceGetPowerManagementDefaultLimit"))(device, &standard) != Success ||
                ((delegate* unmanaged<nint, uint*, int>)Export("nvmlDeviceGetPowerManagementLimit"))(device, &current) != Success)
                return null;
            return new NvmlPowerLimits((int)min, (int)max, (int)standard, (int)current);
        }
    }

    public void SetPowerLimit(int card, int milliwatts)
    {
        lock (_gate)
        {
            Check(((delegate* unmanaged<nint, uint, int>)Export("nvmlDeviceSetPowerManagementLimit"))(Handle(card), (uint)milliwatts), "set the power limit");
        }
    }

    public NvmlRange? ClockOffset(int card, NvmlClock clock)
    {
        lock (_gate)
        {
            var device = Handle(card);
            if (TryExport("nvmlDeviceGetClockOffsets") is { } get)
            {
                var info = Offset(clock, 0);
                if (((delegate* unmanaged<nint, ClockOffsetV1*, int>)get)(device, &info) == Success)
                    return new NvmlRange(info.MinClockOffsetMHz, info.MaxClockOffsetMHz, info.ClockOffsetMHz);
            }

            // Drivers before NVML 12.5 only have the older per-domain calls (deprecated since NVML 13.0).
            var prefix = clock == NvmlClock.Graphics ? "nvmlDeviceGetGpcClk" : "nvmlDeviceGetMemClk";
            if (TryExport(prefix + "MinMaxVfOffset") is not { } range || TryExport(prefix + "VfOffset") is not { } current)
                return null;
            int min, max, offset;
            if (((delegate* unmanaged<nint, int*, int*, int>)range)(device, &min, &max) != Success ||
                ((delegate* unmanaged<nint, int*, int>)current)(device, &offset) != Success)
                return null;
            return new NvmlRange(min, max, offset);
        }
    }

    public void SetClockOffset(int card, NvmlClock clock, int mhz)
    {
        lock (_gate)
        {
            var device = Handle(card);
            var what = $"set the {(clock == NvmlClock.Graphics ? "core" : "memory")} clock offset";
            if (TryExport("nvmlDeviceSetClockOffsets") is { } set)
            {
                var info = Offset(clock, mhz);
                Check(((delegate* unmanaged<nint, ClockOffsetV1*, int>)set)(device, &info), what);
                return;
            }
            var legacy = Export(clock == NvmlClock.Graphics ? "nvmlDeviceSetGpcClkVfOffset" : "nvmlDeviceSetMemClkVfOffset");
            Check(((delegate* unmanaged<nint, int, int>)legacy)(device, mhz), what);
        }
    }

    public (int Min, int Max)? SupportedGraphicsClocks(int card)
    {
        lock (_gate)
        {
            var device = Handle(card);
            var memory = Clocks(device, null);
            if (memory is not { Length: > 0 })
                return null;
            var graphics = Clocks(device, memory.Max());
            return graphics is { Length: > 0 } ? ((int)graphics.Min(), (int)graphics.Max()) : null;
        }
    }

    public void LockGraphicsClocks(int card, int minMHz, int maxMHz)
    {
        lock (_gate)
        {
            Check(((delegate* unmanaged<nint, uint, uint, int>)Export("nvmlDeviceSetGpuLockedClocks"))(Handle(card), (uint)minMHz, (uint)maxMHz), "lock the GPU clocks");
        }
    }

    public void ResetGraphicsClocks(int card)
    {
        lock (_gate)
        {
            Check(((delegate* unmanaged<nint, int>)Export("nvmlDeviceResetGpuLockedClocks"))(Handle(card)), "unlock the GPU clocks");
        }
    }

    public NvmlReadings Readings(int card)
    {
        lock (_gate)
        {
            var device = Handle(card);
            uint temperature, graphics, memory, power, fan;
            Utilization utilization;
            var getClock = (delegate* unmanaged<nint, int, uint*, int>)Export("nvmlDeviceGetClockInfo");
            return new NvmlReadings(
                TemperatureC: ((delegate* unmanaged<nint, int, uint*, int>)Export("nvmlDeviceGetTemperature"))(device, TemperatureGpu, &temperature) == Success ? (int)temperature : null,
                GraphicsClockMHz: getClock(device, ClockGraphics, &graphics) == Success ? (int)graphics : null,
                MemoryClockMHz: getClock(device, ClockMemory, &memory) == Success ? (int)memory : null,
                PowerMilliwatts: ((delegate* unmanaged<nint, uint*, int>)Export("nvmlDeviceGetPowerUsage"))(device, &power) == Success ? (int)power : null,
                UtilizationPercent: ((delegate* unmanaged<nint, Utilization*, int>)Export("nvmlDeviceGetUtilizationRates"))(device, &utilization) == Success ? (int)utilization.Gpu : null,
                FanPercent: ((delegate* unmanaged<nint, uint*, int>)Export("nvmlDeviceGetFanSpeed"))(device, &fan) == Success ? (int)fan : null);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_initialized)
                ((delegate* unmanaged<int>)Export("nvmlShutdown"))();
            _initialized = false;
            _handles.Clear();
        }
    }

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(Environment.SystemDirectory, "nvml.dll");
        var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvml.dll");
    }

    private static ClockOffsetV1 Offset(NvmlClock clock, int mhz) => new()
    {
        // NVML_STRUCT_VERSION(ClockOffset, 1): the struct size with the version number in the top byte.
        Version = (uint)sizeof(ClockOffsetV1) | (1u << 24),
        Type = clock == NvmlClock.Graphics ? ClockGraphics : ClockMemory,
        Pstate = Pstate0,
        ClockOffsetMHz = mhz,
    };

    /// <summary>A supported-clocks list: memory clocks when <paramref name="memoryClock"/> is null, else graphics clocks at that memory clock.</summary>
    private uint[]? Clocks(nint device, uint? memoryClock)
    {
        var buffer = new uint[512];
        var count = (uint)buffer.Length;
        int result;
        fixed (uint* clocks = buffer)
        {
            result = memoryClock is { } memory
                ? ((delegate* unmanaged<nint, uint, uint*, uint*, int>)Export("nvmlDeviceGetSupportedGraphicsClocks"))(device, memory, &count, clocks)
                : ((delegate* unmanaged<nint, uint*, uint*, int>)Export("nvmlDeviceGetSupportedMemoryClocks"))(device, &count, clocks);
        }
        // A card with more than 512 clock steps (InsufficientSize) is treated like one without the list.
        return result == Success ? buffer[..(int)Math.Min(count, (uint)buffer.Length)] : null;
    }

    private nint Handle(int card) =>
        TryHandle(card) ?? throw new NvmlException(6, $"The NVIDIA GPU at index {card} wasn't found.");

    private nint? TryHandle(int index)
    {
        if (!_initialized)
            throw new InvalidOperationException(UnavailableReason ?? "NVML isn't loaded.");
        if (_handles.TryGetValue(index, out var known))
            return known;
        nint handle;
        if (((delegate* unmanaged<uint, nint*, int>)Export("nvmlDeviceGetHandleByIndex_v2"))((uint)index, &handle) != Success)
            return null;
        return _handles[index] = handle;
    }

    private string? Text(nint device, string function)
    {
        var buffer = stackalloc byte[96];
        if (((delegate* unmanaged<nint, byte*, uint, int>)Export(function))(device, buffer, 96) != Success)
            return null;
        var text = Marshal.PtrToStringUTF8((nint)buffer)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private nint Export(string name) =>
        TryExport(name) ?? throw new EntryPointNotFoundException($"nvml.dll has no {name}.");

    private nint? TryExport(string name) =>
        _library != 0 && NativeLibrary.TryGetExport(_library, name, out var address) ? address : null;

    private void Check(int result, string what)
    {
        if (result != Success)
            throw new NvmlException(result, $"The NVIDIA driver couldn't {what} ({ErrorText(result)}).");
    }

    private string ErrorText(int result)
    {
        if (TryExport("nvmlErrorString") is { } errorString &&
            Marshal.PtrToStringUTF8(((delegate* unmanaged<int, nint>)errorString)(result)) is { Length: > 0 } text)
            return $"{text}, NVML code {result}";
        return $"NVML code {result}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClockOffsetV1
    {
        public uint Version;
        public int Type;
        public int Pstate;
        public int ClockOffsetMHz;
        public int MinClockOffsetMHz;
        public int MaxClockOffsetMHz;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }
}
