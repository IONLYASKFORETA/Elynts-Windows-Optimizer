using Elynt.Core.Journal;

namespace Elynt.Core.Tweaks;

public enum EvidenceTier
{
    Proven,
    Situational,
    PossiblePlacebo,
}

/// <summary>Something a signed-in session must do after the change so it takes effect without signing out.</summary>
public enum SessionRefresh
{
    None,
    /// <summary>Re-read the mouse settings (SystemParametersInfo SPI_SETMOUSE).</summary>
    MouseSettings,
}

public sealed record Source(string Title, string Url);

/// <summary>One described change to Windows that the app can apply and undo.</summary>
/// <param name="WhatItDoes">Plain words for the detail pane.</param>
/// <param name="Evidence">Why the tweak is worth having, and how sure we are.</param>
/// <param name="Undo">What switching it off puts back.</param>
public sealed record TweakDefinition(
    string Id,
    string Section,
    string Name,
    string WhatItDoes,
    string Evidence,
    IReadOnlyList<Source> Sources,
    EvidenceTier Tier,
    string Undo,
    IReadOnlyList<TweakStep> Steps,
    SessionRefresh Refresh = SessionRefresh.None)
{
    /// <summary>What undo can and cannot do. Destructive tweaks need an explicit confirmation to apply.</summary>
    public Reversibility Reversibility { get; init; } = Reversibility.Reversible;

    /// <summary>For a destructive tweak: what cannot be undone, spelled out in the confirmation.</summary>
    public string? DestructiveWarning { get; init; }

    /// <summary>
    /// Something the user must read and confirm before this tweak is applied, even though undo can put it back
    /// (e.g. OneDrive's cloud-only files). The Engine refuses to apply without that confirmation.
    /// </summary>
    public string? ApplyWarning { get; init; }

    public RiskFlags Risks { get; init; } = RiskFlags.None;

    /// <summary>Anti-cheats that object to this tweak. The app warns and never blocks.</summary>
    public IReadOnlyList<AntiCheatConflict> AntiCheat { get; init; } = [];

    /// <summary>The hardware, edition and build this tweak fits.</summary>
    public Applicability Applicability { get; init; } = Applicability.Always;

    /// <summary>Warnings that only show on PCs with particular hardware, e.g. dual-CCD X3D CPUs.</summary>
    public IReadOnlyList<HardwareNote> HardwareNotes { get; init; } = [];

    /// <summary>
    /// Takes effect immediately with no reboot and no signed-in session step, so a per-game profile may apply and
    /// undo it. Never true for reboot-needing, destructive or Security tweaks.
    /// </summary>
    public bool ProfileEligible { get; init; }

    /// <summary>A per-game choice listed before a game is picked (see <see cref="PerGameTweak"/>). It can't be applied itself.</summary>
    public bool PerGame { get; init; }
}

/// <summary>
/// One declarative piece of a tweak. Behaviour comes from shared step types (registry value today;
/// services, tasks, BCD and the rest later), not per-tweak code.
/// </summary>
public abstract record TweakStep
{
    /// <summary>What this step changes, checked against <see cref="HardRules"/> before anything runs.</summary>
    public abstract ChangeTarget Target { get; }

    /// <summary>The exact change, as shown in the detail pane.</summary>
    public abstract string DescribeChange();

    /// <summary>
    /// The whole store this step changes, backed up before the first change of its kind; null when the journal
    /// alone is enough (registry values).
    /// </summary>
    public virtual Safety.BackupStore? Store => null;

    /// <summary>
    /// Why this PC doesn't have what the step changes (e.g. a Windows feature only some PCs ship), or null when it
    /// does. Checked as well as the tweak's <see cref="Applicability"/>.
    /// </summary>
    public virtual string? WhyNotHere(SystemAdapters system) => null;

    /// <summary>Whether the system is already in this step's desired state.</summary>
    public abstract bool IsApplied(SystemAdapters system);

    /// <summary>Reads the exact prior state, before anything is changed.</summary>
    public abstract StepRecord Capture(SystemAdapters system);

    public abstract void Apply(SystemAdapters system);
}
