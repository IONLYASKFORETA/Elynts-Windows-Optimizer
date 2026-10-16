namespace Elynt.Core.Repair;

public enum StepStatus
{
    Ok,
    /// <summary>The step worked and found something that needs attention (e.g. disk errors).</summary>
    ProblemsFound,
    Failed,
}

/// <summary>What one step's run means, in plain words.</summary>
public sealed record StepOutcome(StepStatus Status, string Summary, bool RestartNeeded = false, string? FollowUpToolId = null)
{
    public static StepOutcome Ok(string summary, bool restartNeeded = false) => new(StepStatus.Ok, summary, restartNeeded);

    public static StepOutcome Failed(string summary) => new(StepStatus.Failed, summary);
}

/// <summary>One declarative piece of a repair tool.</summary>
/// <param name="AlwaysRun">Runs even after an earlier step failed or the run was stopped (e.g. restarting services a tool stopped).</param>
public abstract record RepairStep(string Name, bool AlwaysRun)
{
    /// <summary>The exact command or file change, as shown before the tool runs.</summary>
    public abstract string Describe(string systemDrive);
}

/// <summary>Runs a program and reads what its exit code and output mean.</summary>
public sealed record RunProgramStep(
    string Name,
    ProgramRun Run,
    Func<int, string, StepOutcome> Interpret,
    bool AlwaysRun = false) : RepairStep(Name, AlwaysRun)
{
    public const string SystemDriveToken = "{SystemDrive}";

    public ProgramRun Resolve(string systemDrive) =>
        Run with { Arguments = Run.Arguments.Select(a => a.Replace(SystemDriveToken, systemDrive)).ToList() };

    public override string Describe(string systemDrive) => $"Run {Resolve(systemDrive)}";
}

/// <summary>Deletes cache files the system rebuilds by itself.</summary>
public sealed record ClearFolderStep(
    string Name,
    RepairFolder Root,
    string RelativePath,
    string Pattern,
    bool IncludeSubfolders,
    bool AlwaysRun = false) : RepairStep(Name, AlwaysRun)
{
    public override string Describe(string systemDrive)
    {
        var folder = Root switch
        {
            RepairFolder.Windows => "%windir%",
            _ => "%LocalAppData%",
        };
        var path = RelativePath.Length == 0 ? folder : $@"{folder}\{RelativePath}";
        return IncludeSubfolders
            ? $@"Delete everything in {path}"
            : $@"Delete {path}\{Pattern}";
    }
}

/// <summary>A folder that must exist for a tool to apply (e.g. the Store's package folder).</summary>
public sealed record FolderRequirement(RepairFolder Root, string RelativePath, string ReasonWhenMissing);

/// <summary>One repair tool: what it does, whether it needs a restart, and its steps.</summary>
/// <param name="Confirmation">When set, the tool is opt-in: it runs only with the user's explicit confirmation of this text.</param>
public sealed record RepairToolDefinition(
    string Id,
    string Name,
    string WhatItDoes,
    RestartNeed Restart,
    string RestartText,
    IReadOnlyList<RepairStep> Steps,
    string? Confirmation = null,
    bool IsFollowUp = false,
    FolderRequirement? Requires = null);
