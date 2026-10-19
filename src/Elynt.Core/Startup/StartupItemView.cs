namespace Elynt.Core.Startup;

public enum StartupSource
{
    RunKey,
    StartupFolder,
    /// <summary>A third-party scheduled task that runs at sign-in.</summary>
    SignInTask,
    StoreApp,
    /// <summary>A third-party scheduled task on another trigger (updaters and the like). Not a startup item, but switchable here.</summary>
    ScheduledTask,
}

/// <summary>Task Manager's startup impact, from what the program cost at the last sign-in.</summary>
public enum StartupImpact
{
    NotMeasured,
    Low,
    Medium,
    High,
}

/// <summary>One startup item (or third-party scheduled task) as the Startup page shows it.</summary>
/// <param name="Id">Stable across reads, e.g. <c>run:HKCU:Discord</c>.</param>
/// <param name="Location">Where it lives: the registry key, folder or task path.</param>
/// <param name="Enabled">Whether it runs at sign-in (or, for a scheduled task, at all).</param>
/// <param name="CanSwitch">False for items a policy controls.</param>
/// <param name="CanDelete">Deleting is destructive and needs its own confirmation. Store apps can't be deleted here.</param>
public sealed record StartupItemView(
    string Id,
    string Name,
    StartupSource Source,
    string Location,
    string Command,
    string? Publisher,
    SignatureStatus Signature,
    StartupImpact Impact,
    bool Enabled,
    bool CanSwitch,
    bool CanDelete)
{
    /// <summary>A switch the app flipped that can still be undone.</summary>
    public bool CanUndo { get; init; }

    /// <summary>Why it can't be switched, when it can't.</summary>
    public string? Note { get; init; }
}

public sealed record StartupCommandResult(bool Succeeded, string Message, StartupItemView? Item);
