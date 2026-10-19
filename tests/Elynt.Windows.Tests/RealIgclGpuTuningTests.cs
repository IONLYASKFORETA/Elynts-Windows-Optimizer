namespace Elynt.Windows.Tests;

/// <summary>
/// The real Intel adapter on a PC without the IGCL bridge or an Intel Arc GPU (such as a CI runner): it must say why
/// tuning is unavailable instead of failing. Tuning itself needs real Intel hardware and is tested by hand.
/// </summary>
public sealed class RealIgclGpuTuningTests
{
    public RealIgclGpuTuningTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows.");

    [Fact]
    public void Without_the_bridge_or_an_Intel_GPU_tuning_is_reported_unavailable()
    {
        using var adapter = new IgclGpuTuning();

        if (adapter.UnavailableReason is null && adapter.Devices().Count > 0)
            Assert.Skip("This PC has a tunable Intel GPU; covered by the manual hardware checks.");

        Assert.Empty(adapter.Devices());
        Assert.NotNull(adapter.UnavailableReason);
    }
}
