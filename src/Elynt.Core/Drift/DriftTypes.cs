namespace Elynt.Core.Drift;

/// <summary>What something else (usually a Windows update, or an app's own update) did to a change the app made.</summary>
public enum DriftKind
{
    /// <summary>A setting the app changed was put back to another value.</summary>
    SettingChanged,
    /// <summary>An app the app removed is installed again.</summary>
    AppReturned,
    /// <summary>A startup item the app switched off (or on) was switched back, usually by its own app's update.</summary>
    StartupItemSwitchedBack,
}

/// <summary>
/// How thorough a drift check was. Quick checks detect whether every applied change is still in place; a full check,
/// run first and whenever the Windows build changed, also re-checks whether every change still fits this build and PC.
/// </summary>
public enum DriftCheckKind
{
    Quick,
    Full,
}

/// <summary>One change the app made that something else has since undone.</summary>
/// <param name="Changes">The exact changes that were undone, as the detail pane words them.</param>
/// <param name="NeedsConfirmation">Destructive: re-applying needs the user's separate confirmation.</param>
/// <param name="NeedsReboot">Re-applying takes effect after a restart, so it is never re-applied automatically.</param>
public sealed record DriftItem(
    string TweakId,
    string Name,
    DriftKind Kind,
    IReadOnlyList<string> Changes,
    bool NeedsConfirmation,
    bool NeedsReboot);

/// <summary>A change whose target this Windows build no longer has. It is skipped quietly until the build or the app changes.</summary>
public sealed record UnsupportedChange(string TweakId, string Name, string Reason);

/// <summary>What a drift check found.</summary>
/// <param name="Drifted">Changes something else undid that wait for the user: re-apply all, or pick.</param>
/// <param name="Reapplied">Changes automatic re-apply put back during this check.</param>
/// <param name="NewlyUnsupported">Changes found not supported on this build in this check. Each is reported once.</param>
public sealed record DriftReport(
    DriftCheckKind Kind,
    string OsBuild,
    IReadOnlyList<DriftItem> Drifted,
    IReadOnlyList<DriftItem> Reapplied,
    IReadOnlyList<UnsupportedChange> NewlyUnsupported,
    string Message);

/// <summary>What happened to one change during a re-apply.</summary>
public sealed record ReapplyOutcome(string TweakId, string Name, bool Succeeded, string Message);

public sealed record ReapplyResult(bool Succeeded, string Message, IReadOnlyList<ReapplyOutcome> Changes);

/// <summary>The user's drift preferences.</summary>
/// <param name="AutoReapply">Re-apply drifted changes during a check without asking. Off by default. Never for
/// destructive, reboot-needing or must-confirm changes, which still ask.</param>
public sealed record DriftSettings(bool AutoReapply);
