using Elynt.Core.Bios;
using Elynt.Core.Drivers;
using Elynt.Core.Tuning.Cpu;

namespace Elynt.Windows.Tests;

/// <summary>The real CPU tuning adapters, read-only. They need Windows and are skipped elsewhere; nothing here writes to the CPU.</summary>
public sealed class RealCpuTuningTests
{
    private sealed class NoInstaller : IInstallerRunner
    {
        public int RunInstaller(string path, IReadOnlyList<string> arguments) =>
            throw new InvalidOperationException("Tests never run an installer.");
    }

    [Fact]
    public void CPUID_names_the_CPU_without_any_driver()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows.");

        var cpu = new PawnIoCpuTuning(new WindowsPawnIo(new NoInstaller()), Path.GetTempPath()).Identify();

        Assert.NotEqual(CpuMaker.Unknown, cpu.Maker);
        Assert.False(string.IsNullOrWhiteSpace(cpu.Name));
        Assert.True(cpu.Family > 0);
    }

    [Fact]
    public void Without_PawnIO_or_its_modules_the_CPU_cannot_be_tuned()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows.");
        var empty = Path.Combine(Path.GetTempPath(), "elynt-no-modules-" + Guid.NewGuid().ToString("N"));

        var reason = new PawnIoCpuTuning(new WindowsPawnIo(new NoInstaller()), empty).UnavailableReason;

        Assert.Contains("PawnIO", reason);
    }

    [Fact]
    public void The_System_event_log_can_be_queried_for_crashes_and_WHEA_errors()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the event log.");
        var events = new WindowsCpuStabilityEvents();
        var lastHour = DateTimeOffset.UtcNow.AddHours(-1);

        _ = events.UncleanShutdownSince(lastHour);
        Assert.True(events.WheaErrorsSince(lastHour) >= 0);
    }
}
