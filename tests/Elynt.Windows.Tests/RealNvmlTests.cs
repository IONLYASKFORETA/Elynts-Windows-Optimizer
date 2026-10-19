using Elynt.Core.Tuning;

namespace Elynt.Windows.Tests;

/// <summary>
/// The real NVML adapter. On a PC without an NVIDIA driver (such as a CI runner) it must say why tuning is
/// unavailable instead of failing; with one, listing and reading must work. Changing settings is tested by hand.
/// </summary>
public sealed class RealNvmlTests
{
    public RealNvmlTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows.");

    [Fact]
    public void Without_an_NVIDIA_driver_tuning_is_reported_unavailable_and_with_one_cards_can_be_read()
    {
        using var nvml = new NvmlLibrary();
        var tuning = new NvidiaGpuTuning(nvml);

        if (nvml.UnavailableReason is not null)
        {
            Assert.Empty(tuning.Devices());
            return;
        }

        foreach (var gpu in tuning.Devices())
        {
            Assert.NotNull(tuning.Read(gpu.Id));
            Assert.NotNull(tuning.Readouts(gpu.Id));
        }
    }
}
