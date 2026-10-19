using System.Runtime.InteropServices;
using Elynt.Core.Sensors;

namespace Elynt.Windows;

/// <summary>
/// Sensors Windows itself reports through performance counters, with no extra driver: CPU load and clock (as Task
/// Manager computes them) and the ACPI thermal zone, which is often a motherboard sensor rather than the CPU's own.
/// Not yet verified on a real PC.
/// </summary>
public sealed unsafe class WindowsSensors : ISensorSource, IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const int PdhMoreData = unchecked((int)0x800007D2);

    private readonly Lock _gate = new();
    private nint _query;
    private nint _utility;
    private nint _frequency;
    private nint _performance;
    private nint _thermal;

    public WindowsSensors()
    {
        if (PdhOpenQueryW(null, 0, out _query) != 0)
            return;
        // Rate counters need two samples; take the first now so the first read has values.
        _utility = Add(@"\Processor Information(_Total)\% Processor Utility");
        _frequency = Add(@"\Processor Information(_Total)\Processor Frequency");
        _performance = Add(@"\Processor Information(_Total)\% Processor Performance");
        _thermal = Add(@"\Thermal Zone Information(*)\Temperature");
        PdhCollectQueryData(_query);
    }

    public string Name => "Windows";

    public IReadOnlyList<SensorReading> Read()
    {
        lock (_gate)
        {
            if (_query == 0 || PdhCollectQueryData(_query) != 0)
                return [];

            var readings = new List<SensorReading>();
            if (Single(_utility) is { } load)
                readings.Add(new(SensorIds.CpuLoad, "CPU load", SensorKind.Load, Math.Round(Math.Min(load, 100), 1), "%"));
            if (Single(_frequency) is { } mhz && Single(_performance) is { } performance && mhz > 0)
                readings.Add(new(SensorIds.CpuClock, "CPU clock", SensorKind.Clock, Math.Round(mhz * performance / 100), "MHz"));
            // Thermal zones report kelvin; the hottest zone is the closest to the CPU.
            var zones = Many(_thermal).Where(k => k > 200).ToList();
            if (zones.Count > 0)
                readings.Add(new(SensorIds.CpuTemperature, "CPU area (ACPI thermal zone)", SensorKind.Temperature, Math.Round(zones.Max() - 273.15, 1), "°C"));
            return readings;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_query != 0)
                PdhCloseQuery(_query);
            _query = 0;
        }
    }

    private nint Add(string path) => PdhAddEnglishCounterW(_query, path, 0, out var counter) == 0 ? counter : 0;

    private static double? Single(nint counter)
    {
        if (counter == 0)
            return null;
        PdhFmtCounterValue value;
        return PdhGetFormattedCounterValue(counter, PdhFmtDouble, null, &value) == 0 && value.CStatus == 0 ? value.DoubleValue : null;
    }

    private static List<double> Many(nint counter)
    {
        var values = new List<double>();
        if (counter == 0)
            return values;
        uint size = 0, count = 0;
        if (PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref size, ref count, null) != PdhMoreData || size == 0)
            return values;
        var buffer = new byte[size];
        fixed (byte* data = buffer)
        {
            if (PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref size, ref count, data) != 0)
                return values;
            var items = (PdhFmtCounterValueItem*)data;
            for (var i = 0; i < count; i++)
                if (items[i].Value.CStatus == 0)
                    values.Add(items[i].Value.DoubleValue);
        }
        return values;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFmtCounterValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItem
    {
        public nint Name;
        public PdhFmtCounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(nint query, string fullCounterPath, nint userData, out nint counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(nint query);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterValue(nint counter, uint format, uint* type, PdhFmtCounterValue* value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, ref uint itemCount, byte* itemBuffer);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(nint query);
}
