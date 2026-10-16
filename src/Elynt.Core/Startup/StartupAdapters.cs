namespace Elynt.Core.Startup;

/// <summary>The Run keys Task Manager lists, each with its own StartupApproved switch key.</summary>
public enum RunKey
{
    /// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</summary>
    CurrentUser,
    /// <summary>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run</summary>
    LocalMachine,
    /// <summary>HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run (32-bit apps)</summary>
    LocalMachine32,
}

/// <summary>The signed-in user's Startup folder, or the all-users one under ProgramData.</summary>
public enum StartupFolder
{
    User,
    AllUsers,
}

/// <summary>One value under a Run key.</summary>
/// <param name="ExecutablePath">The program the command starts, with environment variables expanded, or null when it can't be told.</param>
public sealed record RunValue(RunKey Key, string Name, string Command, string? ExecutablePath);

/// <summary>One file (usually a shortcut) in a Startup folder.</summary>
/// <param name="Target">The program it starts (a shortcut's target), or null when it can't be told.</param>
public sealed record StartupFile(StartupFolder Folder, string FileName, string FullPath, string? Target);

/// <summary>A Store (MSIX) app's declared startup task.</summary>
/// <param name="EnabledByDefault">What the app's manifest asks for before anyone switches it.</param>
public sealed record StoreStartupTask(
    string PackageFamilyName,
    string TaskId,
    string DisplayName,
    string? Publisher,
    bool EnabledByDefault,
    string? ExecutablePath);

/// <summary>One scheduled task, read from Task Scheduler.</summary>
/// <param name="Path">The task's full path, e.g. <c>\GoogleSystem\GoogleUpdater\Task</c>.</param>
/// <param name="RunsAtSignIn">It has a logon trigger.</param>
public sealed record ScheduledTaskInfo(
    string Path,
    bool Enabled,
    bool RunsAtSignIn,
    string? Author,
    string? Command,
    string? ExecutablePath);

public enum SignatureStatus
{
    /// <summary>Signed and the signature checks out.</summary>
    Signed,
    Unsigned,
    /// <summary>Signed, but the signature is broken, expired or untrusted.</summary>
    Invalid,
    /// <summary>The file is missing or couldn't be checked.</summary>
    Unknown,
}

/// <param name="Publisher">The signer, or the file's company name when it isn't signed. Null when neither is known.</param>
public sealed record FileSignature(SignatureStatus Status, string? Publisher);

/// <summary>What one program cost at the last sign-in, as Windows measured it for Task Manager.</summary>
public sealed record StartupMeasurement(string ExecutablePath, TimeSpan Cpu, long DiskBytes);

/// <summary>Reads Run keys, Startup folders and Store app startup tasks. Their switches are registry values.</summary>
public interface IStartupSources
{
    IReadOnlyList<RunValue> RunValues();
    IReadOnlyList<StartupFile> StartupFiles();
    IReadOnlyList<StoreStartupTask> StoreStartupTasks();
    void DeleteRunValue(RunKey key, string name);
    void DeleteStartupFile(StartupFolder folder, string fileName);
}

/// <summary>Task Scheduler.</summary>
public interface IScheduledTasks
{
    IReadOnlyList<ScheduledTaskInfo> List();
    void SetEnabled(string path, bool enabled);
    void Delete(string path);
}

/// <summary>Authenticode checks of the program a startup item runs.</summary>
public interface IFileSignatures
{
    FileSignature Inspect(string path);
}

/// <summary>Windows' own measurement of each startup program at the last sign-in (what Task Manager's impact column uses).</summary>
public interface IStartupImpactLog
{
    IReadOnlyList<StartupMeasurement> Read();
}

/// <summary>Everything the startup manager reads and changes besides the registry.</summary>
public sealed record StartupAdapters(
    IStartupSources Sources,
    IScheduledTasks Tasks,
    IFileSignatures Signatures,
    IStartupImpactLog Impact);
