using Elynt.Core.Journal;

namespace Elynt.Core.Tweaks;

/// <summary>Turns a Windows optional feature on or off, keeping its files so undo needs no download.</summary>
/// <param name="Missing">Why a PC might not have the feature, shown as "not applicable" when it doesn't.</param>
public sealed record OptionalFeatureStep(string Feature, bool Enabled, string Missing) : TweakStep
{
    public override ChangeTarget Target => new OptionalFeatureTarget(Feature, Enabled);

    public override string DescribeChange() =>
        $"Turn {(Enabled ? "on" : "off")} the Windows feature {Feature} ({(Enabled ? "Enable" : "Disable")}-WindowsOptionalFeature -Online, files kept)";

    public override string? WhyNotHere(SystemAdapters system) => system.Features.IsEnabled(Feature) is null ? Missing : null;

    public override bool IsApplied(SystemAdapters system) => system.Features.IsEnabled(Feature) == Enabled;

    public override StepRecord Capture(SystemAdapters system) =>
        new OptionalFeatureRecord(Feature, system.Features.IsEnabled(Feature), Enabled);

    public override void Apply(SystemAdapters system)
    {
        if (Enabled)
            system.Features.Enable(Feature);
        else
            system.Features.Disable(Feature);
    }
}

/// <param name="Before">Whether the feature was on, or null when the PC didn't have it.</param>
/// <param name="After">What the app set.</param>
public sealed record OptionalFeatureRecord(string Feature, bool? Before, bool After) : StepRecord
{
    /// <summary>A Windows feature is on or off for the whole PC.</summary>
    public override bool AffectsEveryAccount() => true;

    public override string? FindConflict(SystemAdapters system)
    {
        var now = system.Features.IsEnabled(Feature);
        return now == After || now == Before
            ? null
            : $"Something else turned the Windows feature {Feature} {(now == true ? "on" : "off")} since the app changed it.";
    }

    public override void Undo(SystemAdapters system)
    {
        if (Before is not { } before || system.Features.IsEnabled(Feature) == before)
            return;
        if (before)
            system.Features.Enable(Feature);
        else
            system.Features.Disable(Feature);
    }
}
