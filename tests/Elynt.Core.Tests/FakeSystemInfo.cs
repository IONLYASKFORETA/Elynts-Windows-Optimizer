using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

public sealed class FakeSystemInfo : ISystemInfo
{
    public string OsBuild { get; set; } = "26100.1";
    public WindowsEdition Edition { get; set; } = WindowsEdition.Pro;
    public HardwareTraits Hardware { get; set; } = HardwareTraits.NvidiaGpu | HardwareTraits.AmdCpu;
    public DateTimeOffset BootTime { get; set; } = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
}
