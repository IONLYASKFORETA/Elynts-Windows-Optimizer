using Elynt.Core.Drivers;
using Elynt.Core.Sensors;

namespace Elynt.Windows.Tests;

/// <summary>PawnIO's real adapter and the driver-free sensors. Reading tests need Windows and are skipped elsewhere.</summary>
public sealed class RealPawnIoAndSensorTests
{
    private sealed class NoInstaller : IInstallerRunner
    {
        public int RunInstaller(string path, IReadOnlyList<string> arguments) =>
            throw new InvalidOperationException("Tests never run an installer.");
    }

    [Theory]
    [InlineData(@"""C:\Program Files\PawnIO\PawnIO_setup.exe"" -uninstall", @"C:\Program Files\PawnIO\PawnIO_setup.exe", "-uninstall")]
    [InlineData(@"C:\Program Files\PawnIO\PawnIO_setup.exe -uninstall -silent", @"C:\Program Files\PawnIO\PawnIO_setup.exe", "-uninstall -silent")]
    [InlineData(@"""C:\Program Files\PawnIO\uninstall.exe""", @"C:\Program Files\PawnIO\uninstall.exe", "")]
    public void The_uninstall_command_splits_into_program_and_arguments(string command, string exe, string arguments)
    {
        var (program, args) = WindowsPawnIo.Split(command);

        Assert.Equal(exe, program);
        Assert.Equal(arguments, string.Join(' ', args));
    }

    [Fact]
    public void Checking_for_pawnio_never_installs_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the real registry.");

        // A runner that throws proves the check runs nothing; on a runner without PawnIO it reads as not installed.
        var version = new WindowsPawnIo(new NoInstaller()).InstalledVersion();

        Assert.True(version is null || version.Length > 0);
    }

    [Fact]
    public void Without_its_modules_pawnio_reads_nothing_and_does_not_throw()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the real registry.");

        var cpu = new PawnIoCpu(Path.Combine(Path.GetTempPath(), "no-pawnio-modules-" + Guid.NewGuid().ToString("N")));

        Assert.Null(cpu.Temperature());
        Assert.Null(cpu.PptLimitWatts());
    }

    [Fact]
    public void Windows_reports_cpu_load_without_any_driver()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows performance counters.");

        using var sensors = new WindowsSensors();
        Thread.Sleep(500);
        var readings = sensors.Read();

        var load = Assert.Single(readings, r => r.Id == SensorIds.CpuLoad);
        Assert.InRange(load.Value, 0, 100);
    }

    [Fact]
    public void Nvml_reports_nothing_rather_than_failing_without_an_nvidia_driver()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows.");

        using var nvml = new NvmlLibrary();
        var readings = new NvmlSensors(nvml).Read();

        Assert.All(readings, r => Assert.StartsWith("nvidia", r.Id));
    }
}
