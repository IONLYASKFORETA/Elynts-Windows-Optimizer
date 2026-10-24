using Elynt.Core.Tweaks;

namespace Elynt.Core.Overview;

/// <summary>
/// The PC overview the Home page shows: this PC's hardware and Windows, and how far its tweaks have gone. Tweaks that
/// don't fit this PC are left out of every count.
/// </summary>
/// <param name="TweaksOn">Tweaks that fit this PC and are on.</param>
/// <param name="TweaksApplicable">Tweaks that fit this PC, on or off.</param>
/// <param name="PendingReboots">Reboot-needing changes applied or undone since Windows last started.</param>
/// <param name="Sections">Every section, in sidebar order, with its on and total counts.</param>
public sealed record PcOverview(
    PcHardware Hardware,
    WindowsRelease Windows,
    int TweaksOn,
    int TweaksApplicable,
    int PendingReboots,
    IReadOnlyList<SectionCount> Sections);

/// <summary>The PC's main parts, as firmware and Windows name them. What couldn't be read is null (or no GPUs).</summary>
/// <param name="Board">The motherboard's maker and model.</param>
/// <param name="Gpus">Every graphics card and integrated GPU.</param>
/// <param name="MemoryMb">All installed memory, in MB.</param>
public sealed record PcHardware(string? Board, string? Cpu, IReadOnlyList<string> Gpus, int? MemoryMb);

/// <summary>The Windows 11 this PC runs.</summary>
/// <param name="Version">The version name, e.g. 24H2, or null for a build that has none (an Insider build).</param>
/// <param name="Build">The build and revision, e.g. 26100.4652.</param>
public sealed record WindowsRelease(WindowsEdition Edition, string? Version, string Build)
{
    /// <summary>Windows 11 version names by build number.</summary>
    private static readonly Dictionary<int, string> Versions = new()
    {
        [22000] = "21H2",
        [22621] = "22H2",
        [22631] = "23H2",
        [26100] = "24H2",
        [26200] = "25H2",
    };

    public static WindowsRelease Of(ISystemInfo system) =>
        new(system.Edition, Versions.GetValueOrDefault(system.BuildNumber), system.OsBuild);
}

/// <summary>One section's tweaks that fit this PC: how many are on, out of how many. A section of tools counts 0 of 0.</summary>
public sealed record SectionCount(string Section, int On, int Total);
