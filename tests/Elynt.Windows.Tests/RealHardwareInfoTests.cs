using Elynt.Core.Bios;

namespace Elynt.Windows.Tests;

/// <summary>The real SMBIOS and registry reads, on a real Windows PC. Skipped elsewhere.</summary>
public sealed class RealHardwareInfoTests
{
    public RealHardwareInfoTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs real Windows firmware tables.");

    [Fact]
    public void Reads_the_cpu_name_and_memory_without_a_driver()
    {
        var snapshot = new WindowsHardwareInfo().Read();

        Assert.NotEmpty(snapshot.CpuName);
        Assert.Equal(ReadoutState.Unknown, snapshot.Pbo);
        Assert.NotEmpty(BiosAdvisor.Build(snapshot).Readouts);
    }
}
