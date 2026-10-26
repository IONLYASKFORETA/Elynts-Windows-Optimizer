using Elynt.Core.Journal;
using Elynt.Core.Safety;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Startup;

/// <summary>Turns one scheduled task on or off.</summary>
public sealed record ScheduledTaskTarget(string TaskPath, bool Enabled) : ChangeTarget;

/// <summary>A step that enables or disables one scheduled task.</summary>
public sealed record ScheduledTaskStep(string TaskPath, bool Enabled) : TweakStep
{
    public override ChangeTarget Target => new ScheduledTaskTarget(TaskPath, Enabled);

    public override BackupStore? Store => BackupStore.ScheduledTasks;

    public override string DescribeChange() => $"{(Enabled ? "Enable" : "Disable")} the scheduled task {TaskPath}";

    public override bool IsApplied(SystemAdapters system) => State(system, TaskPath) == Enabled;

    public override StepRecord Capture(SystemAdapters system) =>
        new ScheduledTaskRecord(TaskPath, State(system, TaskPath) ?? throw new InvalidOperationException($"There is no scheduled task {TaskPath}."), Enabled);

    public override void Apply(SystemAdapters system) => Tasks(system).SetEnabled(TaskPath, Enabled);

    internal static bool? State(SystemAdapters system, string path) =>
        Tasks(system).List().FirstOrDefault(t => t.Path.Equals(path, StringComparison.OrdinalIgnoreCase))?.Enabled;

    internal static IScheduledTasks Tasks(SystemAdapters system) =>
        system.Startup?.Tasks ?? throw new InvalidOperationException("Scheduled tasks aren't available here.");
}

/// <summary>Whether a scheduled task was enabled before the app switched it.</summary>
public sealed record ScheduledTaskRecord(string TaskPath, bool WasEnabled, bool SetEnabled) : StepRecord
{
    /// <summary>Task Scheduler keeps its tasks for the whole PC, even one that runs at one account's sign-in.</summary>
    public override bool AffectsEveryAccount() => true;

    public override string? FindConflict(SystemAdapters system)
    {
        var now = ScheduledTaskStep.State(system, TaskPath);
        if (now is null)
            return $"The scheduled task {TaskPath} has since been deleted.";
        // Still what the app set, or already back to how it was: nothing to overwrite.
        return null;
    }

    public override void Revert(SystemAdapters system)
    {
        if (ScheduledTaskStep.State(system, TaskPath) != WasEnabled)
            ScheduledTaskStep.Tasks(system).SetEnabled(TaskPath, WasEnabled);
    }
}
