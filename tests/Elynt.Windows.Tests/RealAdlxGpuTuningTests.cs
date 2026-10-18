namespace Elynt.Windows.Tests;

/// <summary>
/// The real AMD adapter on a PC without the ADLX bridge or an AMD GPU (such as a CI runner): it must say why tuning
/// is unavailable instead of failing. Tuning itself needs real AMD hardware and is tested by hand.
/// </summary>
public sealed class RealAdlxGpuTuningTests
{
    public RealAdlxGpuTuningTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows.");

    [Fact]
    public void Without_the_bridge_or_an_AMD_GPU_tuning_is_reported_unavailable()
    {
        using var adapter = new AdlxGpuTuning();

        if (adapter.UnavailableReason is null && adapter.Devices().Count > 0)
            Assert.Skip("This PC has a tunable AMD GPU; covered by the manual hardware checks.");

        Assert.Empty(adapter.Devices());
        Assert.NotNull(adapter.UnavailableReason);
    }
}
