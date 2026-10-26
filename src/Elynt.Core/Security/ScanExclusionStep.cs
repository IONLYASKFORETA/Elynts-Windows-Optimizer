using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Security;

/// <summary>Microsoft Defender's folder scan exclusions (Windows Security > Virus &amp; threat protection > Exclusions).</summary>
public interface IScanExclusions
{
    /// <summary>Whether the folder is already excluded. Windows compares paths without regard to case.</summary>
    bool Contains(string folder);

    void Add(string folder);

    /// <summary>Removes the folder. Does nothing when it isn't excluded.</summary>
    void Remove(string folder);
}

/// <summary>A tweak step that stops Defender's real-time and scheduled scans from scanning one folder.</summary>
public sealed record ScanExclusionStep(string Folder) : TweakStep
{
    public override ChangeTarget Target => new ScanExclusionTarget(Folder);

    public override string DescribeChange() =>
        $"Add {Folder} to Microsoft Defender's scan exclusions (Add-MpPreference -ExclusionPath)";

    public override bool IsApplied(SystemAdapters system) => Exclusions(system).Contains(Folder);

    public override StepRecord Capture(SystemAdapters system) =>
        new ScanExclusionRecord(Folder, Exclusions(system).Contains(Folder));

    public override void Apply(SystemAdapters system) => Exclusions(system).Add(Folder);

    internal static IScanExclusions Exclusions(SystemAdapters system) =>
        system.ScanExclusions ?? throw new InvalidOperationException("Microsoft Defender's exclusions can't be read on this PC.");
}

/// <summary>Whether the folder was already excluded before the app added it.</summary>
public sealed record ScanExclusionRecord(string Folder, bool WasExcluded) : StepRecord
{
    /// <summary>Microsoft Defender's exclusions hold for the whole PC.</summary>
    public override bool AffectsEveryAccount() => true;

    /// <summary>
    /// An exclusion is either there or not, and either is what the app wrote or what was there before, so undo never
    /// overwrites someone else's change.
    /// </summary>
    public override string? FindConflict(SystemAdapters system) => null;

    public override void Revert(SystemAdapters system)
    {
        if (!WasExcluded)
            ScanExclusionStep.Exclusions(system).Remove(Folder);
    }
}
