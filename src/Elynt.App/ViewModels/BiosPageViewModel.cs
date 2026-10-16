using Elynt.Core.Bios;
using Elynt.Core.Tweaks;

namespace Elynt.App.ViewModels;

/// <summary>The Overclocking section's BIOS page: readouts, guides and stability tools, ready to bind.</summary>
public sealed class BiosPageViewModel(BiosReport report)
{
    public string Hardware { get; } = report.Hardware;

    public IReadOnlyList<ReadoutItem> Readouts { get; } = report.Readouts.Select(r => new ReadoutItem(r.Name, StateText(r.State), r.Detail)).ToList();

    public IReadOnlyList<GuideItem> Guides { get; } = report.Guides
        .Select(g => new GuideItem(g.Title, g.Summary, g.Steps.Select((s, i) => $"{i + 1}. {s}").ToList(), g.Sources))
        .ToList();

    public IReadOnlyList<StabilityTool> StabilityTools { get; } = report.StabilityTools;

    private static string StateText(ReadoutState state) => state switch
    {
        ReadoutState.On => "On",
        ReadoutState.Off => "Off",
        ReadoutState.NotApplicable => "Not on this PC",
        _ => "Can't tell",
    };

    public sealed record ReadoutItem(string Name, string State, string Detail);

    public sealed record GuideItem(string Title, string Summary, IReadOnlyList<string> Steps, IReadOnlyList<Source> Sources);
}
