using Elynt.Core.Bios;
using Elynt.Core.Journal;
using Elynt.Core.LowLevel;
using Elynt.Core.Sensors;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>Sensor readouts and the PBO readout, with and without PawnIO.</summary>
public sealed class SensorTests
{
    private readonly FakePawnIo _pawnIo = new() { CpuTemperature = 64.5 };
    private readonly FakeHardwareInfo _hardware = new();

    private readonly FakeSensorSource _gpu = new("NVIDIA NVML",
        new SensorReading("gpu0.temperature", "GPU temperature", SensorKind.Temperature, 58, "°C"),
        new SensorReading("gpu0.clock", "GPU clock", SensorKind.Clock, 2610, "MHz"));

    private readonly FakeSensorSource _windows = new("Windows",
        new SensorReading(SensorIds.CpuTemperature, "CPU temperature", SensorKind.Temperature, 27.8, "°C"),
        new SensorReading(SensorIds.CpuLoad, "CPU load", SensorKind.Load, 12, "%"));

    private Engine NewEngine() => new(
        new Catalogue([LabelTests.TestTweak("test.plain")]),
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), _hardware)
        {
            PawnIo = new PawnIoAdapters(_pawnIo, _pawnIo, Path.GetTempPath(), new FakeDownloader(), new FakeSignatureVerifier()),
            Sensors = [_gpu, _windows],
        },
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test");

    private static SensorView Sensor(SensorReport report, string id) => report.Sensors.Single(s => s.Id == id);

    [Fact]
    public void Without_pawnio_sensors_come_from_vendor_sdks_and_windows()
    {
        var report = NewEngine().GetSensors();

        Assert.False(report.FromPawnIo);
        Assert.Equal(["gpu0.temperature", "gpu0.clock", SensorIds.CpuTemperature, SensorIds.CpuLoad], report.Sensors.Select(s => s.Id));
        Assert.Equal("NVIDIA NVML", Sensor(report, "gpu0.clock").Source);
        Assert.Equal(("Windows", 27.8), (Sensor(report, SensorIds.CpuTemperature).Source, Sensor(report, SensorIds.CpuTemperature).Value));
        Assert.Contains("PawnIO", report.Note);
        Assert.Equal(0, _pawnIo.CpuReads);
    }

    [Fact]
    public void With_pawnio_the_cpu_temperature_comes_from_the_cpu_itself_and_the_rest_still_shows()
    {
        _pawnIo.Version = "2.0.1";

        var report = NewEngine().GetSensors();

        Assert.True(report.FromPawnIo);
        var cpu = Sensor(report, SensorIds.CpuTemperature);
        Assert.Equal(("PawnIO", 64.5), (cpu.Source, cpu.Value));
        Assert.Equal(["gpu0.temperature", "gpu0.clock", SensorIds.CpuLoad], report.Sensors.Where(s => s.Source != "PawnIO").Select(s => s.Id));
        Assert.Null(report.Note);
    }

    [Fact]
    public void A_failing_source_does_not_hide_the_others()
    {
        _gpu.Fail = new InvalidOperationException("NVML isn't loaded.");

        var report = NewEngine().GetSensors();

        Assert.Equal([SensorIds.CpuTemperature, SensorIds.CpuLoad], report.Sensors.Select(s => s.Id));
    }

    [Theory]
    [InlineData(1000.0, ReadoutState.On)]
    [InlineData(200.0, ReadoutState.On)]
    [InlineData(162.0, ReadoutState.Off)]
    [InlineData(88.0, ReadoutState.Off)]
    public void With_pawnio_pbo_is_read_from_the_ppt_limit_on_a_ryzen(double ppt, ReadoutState expected)
    {
        _pawnIo.Version = "2.0.1";
        _pawnIo.PptLimit = ppt;
        _hardware.Snapshot = _hardware.Snapshot with { CpuName = "AMD Ryzen 7 7800X3D 8-Core Processor", Pbo = ReadoutState.Unknown };

        var pbo = NewEngine().GetBiosReport().Readouts.Single(r => r.Id == BiosReadoutIds.Pbo);

        Assert.Equal(expected, pbo.State);
    }

    [Fact]
    public void Without_pawnio_pbo_stays_unknown_and_nothing_is_read_through_it()
    {
        _pawnIo.PptLimit = 1000;
        _hardware.Snapshot = _hardware.Snapshot with { CpuName = "AMD Ryzen 7 7800X3D 8-Core Processor", Pbo = ReadoutState.Unknown };

        var pbo = NewEngine().GetBiosReport().Readouts.Single(r => r.Id == BiosReadoutIds.Pbo);

        Assert.Equal(ReadoutState.Unknown, pbo.State);
        Assert.Contains("PawnIO", pbo.Detail);
        Assert.Equal(0, _pawnIo.CpuReads);
    }
}
