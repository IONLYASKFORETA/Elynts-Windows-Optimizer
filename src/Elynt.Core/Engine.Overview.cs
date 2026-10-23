using Elynt.Core.Bios;
using Elynt.Core.Overview;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;

namespace Elynt.Core;

/// <summary>The PC overview: the Home page's figures in one query.</summary>
public partial interface IEngine
{
    /// <summary>
    /// The Home page's figures: the board, CPU, GPUs, memory and Windows, the tweaks that are on out of those that fit
    /// this PC, pending reboots, and each section's on and total counts. Tweaks that don't fit this PC are left out.
    /// </summary>
    PcOverview GetPcOverview();
}

public sealed partial class Engine
{
    public PcOverview GetPcOverview()
    {
        var tweaks = ListTweaks();
        // A per-game tweak's template is never on itself; each game it is set for is listed, and counted, on its own.
        var counted = tweaks.Where(t => t.Applicable && !t.PerGame).ToList();
        return new PcOverview(
            ReadPcHardware(),
            WindowsRelease.Of(system.System),
            counted.Count(IsOn),
            counted.Count,
            tweaks.Count(t => t.PendingReboot),
            Sections.All.Select(section =>
            {
                var inSection = counted.Where(t => t.Section == section).ToList();
                return new SectionCount(section, inSection.Count(IsOn), inSection.Count);
            }).ToList());

        static bool IsOn(TweakView tweak) => tweak.State == TweakState.On;
    }

    /// <summary>The board, CPU and memory from the firmware, and the GPUs from the driver inventory. Never throws.</summary>
    private PcHardware ReadPcHardware()
    {
        HardwareSnapshot? snapshot = null;
        try
        {
            snapshot = system.Hardware?.Read();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read the board, CPU and memory details");
        }

        IReadOnlyList<string> gpus = [];
        try
        {
            gpus = system.Drivers?.Inventory.DisplayAdapters().Select(adapter => adapter.Name).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read the display adapters");
        }

        var board = $"{snapshot?.BoardManufacturer} {snapshot?.BoardProduct}".Trim();
        var memoryMb = snapshot?.Memory.Sum(module => module.SizeMb) ?? 0;
        return new PcHardware(
            board.Length > 0 ? board : null,
            string.IsNullOrWhiteSpace(snapshot?.CpuName) ? null : snapshot.CpuName,
            gpus,
            memoryMb > 0 ? memoryMb : null);
    }
}
