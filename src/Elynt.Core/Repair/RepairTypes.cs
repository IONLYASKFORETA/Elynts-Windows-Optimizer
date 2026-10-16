namespace Elynt.Core.Repair;

/// <summary>Whether a repair tool needs the PC restarted to finish. Shown before the tool runs.</summary>
public enum RestartNeed
{
    None,
    /// <summary>Only sometimes; the result says so when it does.</summary>
    Maybe,
    Restart,
}

/// <summary>A repair tool as the UI shows it, before it runs.</summary>
/// <param name="ExactCommands">Every command and file change the tool makes, in order.</param>
/// <param name="Confirmation">When set, the tool is opt-in and only runs once the user confirms this text.</param>
/// <param name="IsFollowUp">Offered only from another tool's result (e.g. fixing disk problems the check found).</param>
public sealed record RepairToolView(
    string Id,
    string Section,
    string Name,
    string WhatItDoes,
    IReadOnlyList<string> ExactCommands,
    RestartNeed Restart,
    string RestartText,
    string? Confirmation,
    bool IsFollowUp,
    bool Available,
    string? NotAvailableReason);

/// <summary>Live progress from a running repair tool: a new step starting, or one line of output.</summary>
/// <param name="Step">1-based.</param>
/// <param name="Percent">The step's own progress when its output reports one.</param>
public sealed record RepairProgress(string ToolId, int Step, int StepCount, string StepName, double? Percent, string? Line);

/// <summary>How a repair run ended.</summary>
/// <param name="Summary">Plain words: a headline, then one line per step.</param>
/// <param name="FollowUpToolId">A tool to offer next, e.g. fixing the disk problems a check found.</param>
/// <param name="LogLocation">Where the run's log was saved, or null if nothing ran or saving failed.</param>
/// <param name="LogText">The full log, for "copy log".</param>
public sealed record RepairResult(
    string ToolId,
    bool Succeeded,
    string Summary,
    bool RestartNeeded,
    string? FollowUpToolId,
    string? LogLocation,
    string LogText);

/// <summary>Folders a repair tool may clear, resolved by the adapter.</summary>
public enum RepairFolder
{
    /// <summary>%windir%.</summary>
    Windows,
    /// <summary>The signed-in user's %LocalAppData% (the service runs as SYSTEM, so it is resolved from the user's SID).</summary>
    UserLocalAppData,
}

/// <summary>How a console program writes its output. SFC writes UTF-16 when redirected.</summary>
public enum ConsoleText
{
    Oem,
    Unicode,
}

/// <summary>A program run by a repair tool. <see cref="Program"/> is a bare name the adapter resolves to a trusted path.</summary>
public sealed record ProgramRun(string Program, IReadOnlyList<string> Arguments, ConsoleText Output = ConsoleText.Oem)
{
    public override string ToString() => string.Join(' ', [Program, .. Arguments.Select(Quote)]);

    private static string Quote(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;
}

public sealed record FolderClearResult(int Removed, int InUse);

/// <summary>Everything a repair tool may touch on the PC. Real adapter wraps Windows; the fake holds in-memory state.</summary>
public interface IRepairSystem
{
    /// <summary>The Windows drive, e.g. "C:".</summary>
    string SystemDrive { get; }

    /// <summary>Runs a program to completion, passing each output line (split on CR or LF) as it arrives.</summary>
    /// <returns>The exit code.</returns>
    Task<int> RunAsync(ProgramRun run, Action<string> output, CancellationToken cancellation);

    bool FolderExists(RepairFolder root, string relativePath, string user);

    /// <summary>Deletes the files matching <paramref name="pattern"/> in a folder (and its subfolders when asked). Files in use are skipped and counted. A missing folder clears nothing.</summary>
    FolderClearResult ClearFolder(RepairFolder root, string relativePath, string pattern, bool includeSubfolders, string user);
}

/// <summary>Where every repair run's log is kept.</summary>
public interface IRepairLogStore
{
    /// <returns>Where the log was saved, shown to the user.</returns>
    string Save(string name, string text);
}

public sealed class InMemoryRepairLogStore : IRepairLogStore
{
    private readonly List<(string Name, string Text)> _saved = [];

    public IReadOnlyList<(string Name, string Text)> Saved => _saved;

    public string Save(string name, string text)
    {
        _saved.Add((name, text));
        return name;
    }
}

/// <summary>Repair logs as text files in one folder.</summary>
public sealed class FileRepairLogStore(string folder) : IRepairLogStore
{
    public string Save(string name, string text)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        return path;
    }
}
