using System.Runtime.InteropServices;
using System.Text;
using Elynt.Core.Sensors;

namespace Elynt.Windows;

/// <summary>
/// NVIDIA GPU temperature, clock, power and load through NVML (nvml.dll, installed with the NVIDIA driver), using only
/// NVML's documented read calls. On a PC without an NVIDIA driver it reports nothing. Not yet verified on real hardware.
/// </summary>
public sealed class NvmlSensors : ISensorSource
{
    private const int Success = 0;
    private readonly Lock _gate = new();
    private bool? _ready;

    public string Name => "NVIDIA NVML";

    public IReadOnlyList<SensorReading> Read()
    {
        lock (_gate)
        {
            if (!Ready())
                return [];
            if (nvmlDeviceGetCount_v2(out var count) != Success)
                return [];

            var readings = new List<SensorReading>();
            for (uint i = 0; i < count; i++)
            {
                if (nvmlDeviceGetHandleByIndex_v2(i, out var device) != Success)
                    continue;
                var name = new byte[96];
                var gpu = nvmlDeviceGetName(device, name, (uint)name.Length) == Success
                    ? Encoding.ASCII.GetString(name).TrimEnd('\0')
                    : $"GPU {i}";
                var id = $"nvidia{i}";
                if (nvmlDeviceGetTemperature(device, 0, out var temperature) == Success)
                    readings.Add(new($"{id}.temperature", $"{gpu} temperature", SensorKind.Temperature, temperature, "°C"));
                if (nvmlDeviceGetClockInfo(device, 0, out var clock) == Success)
                    readings.Add(new($"{id}.clock", $"{gpu} clock", SensorKind.Clock, clock, "MHz"));
                if (nvmlDeviceGetPowerUsage(device, out var milliwatts) == Success)
                    readings.Add(new($"{id}.power", $"{gpu} power", SensorKind.Power, Math.Round(milliwatts / 1000.0, 1), "W"));
                if (nvmlDeviceGetUtilizationRates(device, out var utilization) == Success)
                    readings.Add(new($"{id}.load", $"{gpu} load", SensorKind.Load, utilization.Gpu, "%"));
            }
            return readings;
        }
    }

    private bool Ready()
    {
        if (_ready is { } known)
            return known;
        try
        {
            _ready = nvmlInit_v2() == Success;
        }
        catch (DllNotFoundException)
        {
            _ready = false;
        }
        catch (EntryPointNotFoundException)
        {
            _ready = false;
        }
        return _ready.Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [DllImport("nvml.dll")]
    private static extern int nvmlInit_v2();

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetCount_v2(out uint count);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out nint device);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetName(nint device, byte[] name, uint length);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetTemperature(nint device, int sensorType, out uint temperature);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetClockInfo(nint device, int clockType, out uint clockMhz);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetPowerUsage(nint device, out uint milliwatts);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetUtilizationRates(nint device, out Utilization utilization);
}
