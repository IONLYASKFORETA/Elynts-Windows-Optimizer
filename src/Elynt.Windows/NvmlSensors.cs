using Elynt.Core.Sensors;
using Elynt.Core.Tuning;

namespace Elynt.Windows;

/// <summary>
/// NVIDIA GPU temperature, clock, power and load through NVML, read through the same binding NVIDIA tuning uses
/// (<see cref="NvmlLibrary"/>, which finds nvml.dll in System32 or the older NVSMI folder). On a PC without an NVIDIA
/// driver it reports nothing. Not yet verified on real hardware.
/// </summary>
public sealed class NvmlSensors(INvml nvml) : ISensorSource
{
    public string Name => "NVIDIA NVML";

    public IReadOnlyList<SensorReading> Read()
    {
        if (nvml.UnavailableReason is not null)
            return [];

        var readings = new List<SensorReading>();
        try
        {
            foreach (var card in nvml.Cards())
            {
                var values = nvml.Readings(card.Index);
                var id = $"nvidia{card.Index}";
                if (values.TemperatureC is { } temperature)
                    readings.Add(new($"{id}.temperature", $"{card.Name} temperature", SensorKind.Temperature, temperature, "°C"));
                if (values.GraphicsClockMHz is { } clock)
                    readings.Add(new($"{id}.clock", $"{card.Name} clock", SensorKind.Clock, clock, "MHz"));
                if (values.PowerMilliwatts is { } milliwatts)
                    readings.Add(new($"{id}.power", $"{card.Name} power", SensorKind.Power, Math.Round(milliwatts / 1000.0, 1), "W"));
                if (values.UtilizationPercent is { } load)
                    readings.Add(new($"{id}.load", $"{card.Name} load", SensorKind.Load, load, "%"));
            }
        }
        catch (NvmlException)
        {
            return [];
        }
        return readings;
    }
}
