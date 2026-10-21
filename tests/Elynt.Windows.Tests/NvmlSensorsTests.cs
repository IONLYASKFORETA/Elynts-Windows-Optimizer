using Elynt.Core.Sensors;
using Elynt.Core.Tuning;

namespace Elynt.Windows.Tests;

/// <summary>NVIDIA sensor readings come from the same NVML binding as NVIDIA tuning (NvmlLibrary), here faked.</summary>
public sealed class NvmlSensorsTests
{
    private sealed class FakeNvml : INvml
    {
        public string? UnavailableReason { get; set; }
        public bool FailCards { get; set; }
        public Dictionary<int, NvmlReadings> Values { get; } = [];

        public IReadOnlyList<NvmlCard> Cards() =>
            FailCards ? throw new NvmlException(999, "The NVIDIA driver couldn't count the NVIDIA GPUs.")
                : Values.Keys.Select(i => new NvmlCard(i, $"NVIDIA GeForce RTX 40{i}0", $"GPU-{i}")).ToList();

        public NvmlReadings Readings(int card) => Values[card];

        public NvmlPowerLimits? PowerLimits(int card) => throw new NotSupportedException();
        public void SetPowerLimit(int card, int milliwatts) => throw new NotSupportedException();
        public NvmlRange? ClockOffset(int card, NvmlClock clock) => throw new NotSupportedException();
        public void SetClockOffset(int card, NvmlClock clock, int mhz) => throw new NotSupportedException();
        public (int Min, int Max)? SupportedGraphicsClocks(int card) => throw new NotSupportedException();
        public void LockGraphicsClocks(int card, int minMHz, int maxMHz) => throw new NotSupportedException();
        public void ResetGraphicsClocks(int card) => throw new NotSupportedException();
    }

    [Fact]
    public void Each_card_reports_the_temperature_clock_power_and_load_nvml_gives()
    {
        var nvml = new FakeNvml { Values = { [7] = new(TemperatureC: 61, GraphicsClockMHz: 2610, PowerMilliwatts: 201_456, UtilizationPercent: 97) } };

        var readings = new NvmlSensors(nvml).Read();

        Assert.Equal(
            [
                new SensorReading("nvidia7.temperature", "NVIDIA GeForce RTX 4070 temperature", SensorKind.Temperature, 61, "°C"),
                new SensorReading("nvidia7.clock", "NVIDIA GeForce RTX 4070 clock", SensorKind.Clock, 2610, "MHz"),
                new SensorReading("nvidia7.power", "NVIDIA GeForce RTX 4070 power", SensorKind.Power, 201.5, "W"),
                new SensorReading("nvidia7.load", "NVIDIA GeForce RTX 4070 load", SensorKind.Load, 97, "%"),
            ],
            readings);
    }

    [Fact]
    public void Values_a_card_doesnt_report_are_left_out()
    {
        var nvml = new FakeNvml { Values = { [0] = new(TemperatureC: 50) } };

        Assert.Equal("nvidia0.temperature", Assert.Single(new NvmlSensors(nvml).Read()).Id);
    }

    [Fact]
    public void Without_nvml_or_when_it_fails_nothing_is_reported()
    {
        Assert.Empty(new NvmlSensors(new FakeNvml { UnavailableReason = "No NVIDIA driver." }).Read());
        Assert.Empty(new NvmlSensors(new FakeNvml { FailCards = true }).Read());
    }
}
