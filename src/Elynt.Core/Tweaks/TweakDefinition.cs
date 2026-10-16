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
    SessionRefresh Refresh = SessionRefresh.None);

/// <summary>
/// One declarative piece of a tweak. Behaviour comes from shared step types (registry value today;
/// services, tasks, BCD and the rest later), not per-tweak code.
/// </summary>
public abstract record TweakStep
{
    /// <summary>The exact change, as shown in the detail pane.</summary>
    public abstract string DescribeChange();

    /// <summary>
    /// The whole store this step changes, backed up before the first change of its kind; null when the journal
    /// alone is enough (registry values).
    /// </summary>
    public virtual Safety.BackupStore? Store => null;

    /// <summary>Whether the system is already in this step's desired state.</summary>
    public abstract bool IsApplied(SystemAdapters system);

    /// <summary>Reads the exact prior state, before anything is changed.</summary>
    public abstract StepRecord Capture(SystemAdapters system);

    public abstract void Apply(SystemAdapters system);
}
