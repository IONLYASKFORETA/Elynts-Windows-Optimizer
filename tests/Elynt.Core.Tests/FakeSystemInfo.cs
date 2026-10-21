using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

public sealed class FakeSystemInfo : ISystemInfo
{
    public string OsBuild { get; set; } = "26100.1";
    public WindowsEdition Edition { get; set; } = WindowsEdition.Pro;
    public HardwareTraits Hardware { get; set; } = HardwareTraits.NvidiaGpu | HardwareTraits.AmdCpu;
    public DateTimeOffset BootTime { get; set; } = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A PC the tweak fits: its lowest supported build, its first edition and all the hardware it needs.</summary>
    public static FakeSystemInfo Fitting(Applicability applicability)
    {
        var system = new FakeSystemInfo();
        if (applicability.MinBuild is { } min)
            system.OsBuild = $"{min}.1";
        else if (applicability.MaxBuild is { } max)
            system.OsBuild = $"{max}.1";
        if (applicability.Editions is [var edition, ..])
            system.Edition = edition;
        system.Hardware |= applicability.RequiresAll;
        if (applicability.RequiresAny != HardwareTraits.None && (system.Hardware & applicability.RequiresAny) == HardwareTraits.None)
            system.Hardware |= Enum.GetValues<HardwareTraits>().First(t => t != HardwareTraits.None && applicability.RequiresAny.HasFlag(t));
        return system;
    }
}
