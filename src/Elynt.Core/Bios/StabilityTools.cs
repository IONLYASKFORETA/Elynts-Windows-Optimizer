namespace Elynt.Core.Bios;

/// <summary>Free tools for checking stability after any BIOS change. Linked, never bundled.</summary>
public static class StabilityTools
{
    public static StabilityTool Occt { get; } = new(
        "OCCT",
        "https://www.ocbase.com/",
        "Free for personal use. Run the CPU test for 1 hour and the Memory test for 1 hour after each change. Any error means the change is not stable: undo it or make it smaller.");

    public static StabilityTool ThreeDMark { get; } = new(
        "3DMark (free demo on Steam)",
        "https://store.steampowered.com/app/223850/3DMark/",
        "The free demo includes Time Spy. Run it a few times and compare the score with your score from before the change; a crash or a lower score means undo it.");

    public static IReadOnlyList<StabilityTool> All { get; } = [Occt, ThreeDMark];
}
