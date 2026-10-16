using Elynt.Core.Tweaks;

namespace Elynt.Core.Bios;

public static class BiosReadoutIds
{
    public const string MemoryProfile = "bios.memory-profile";
    public const string Pbo = "bios.pbo";
}

/// <summary>One setting that lives in the BIOS, as detected from Windows.</summary>
/// <param name="Detail">What was read and how sure the reading is, in plain words.</param>
public sealed record BiosReadout(string Id, string Name, ReadoutState State, string Detail);

/// <summary>A step-by-step guide for a change only the BIOS can make. The app never makes it itself.</summary>
/// <param name="Summary">What the change does, and its risks.</param>
public sealed record BiosGuide(string Id, string Title, string Summary, IReadOnlyList<string> Steps, IReadOnlyList<Source> Sources);

/// <summary>A free tool for checking the PC is stable after a BIOS change.</summary>
public sealed record StabilityTool(string Name, string Url, string WhatToRun);

/// <summary>The Overclocking section's BIOS page: what was detected, the guides that fit this PC, and stability tools.</summary>
/// <param name="Hardware">The detected board and CPU, in plain words.</param>
public sealed record BiosReport(
    string Hardware,
    IReadOnlyList<BiosReadout> Readouts,
    IReadOnlyList<BiosGuide> Guides,
    IReadOnlyList<StabilityTool> StabilityTools);
