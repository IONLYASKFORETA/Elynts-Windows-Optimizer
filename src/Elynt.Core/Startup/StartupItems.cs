using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Startup;

/// <summary>One startup item as read from its source, with the switch that turns it on and off.</summary>
internal sealed record StartupItem(
    string Id,
    string Name,
    StartupSource Source,
    string Location,
    string Command,
    string? ExecutablePath,
    StartupSwitch Switch,
    bool CanDelete)
{
    /// <summary>Set when the source itself knows the publisher (Store packages); otherwise the signer is used.</summary>
    public string? Publisher { get; init; }

    /// <summary>Set when the source itself vouches for the signature (Store packages are verified on install).</summary>
    public SignatureStatus? Signature { get; init; }

    /// <summary>What deleting changes, checked against the hard rules first.</summary>
    public ChangeTarget? DeleteTarget { get; init; }

    /// <summary>Removes the item for good (its switch value too). Null when it can't be deleted here.</summary>
    public Action<SystemAdapters>? Remove { get; init; }

    /// <summary>Shown when the program isn't signed and has no company name (a task's author).</summary>
    public string? FallbackPublisher { get; init; }
}

/// <summary>Turns one startup item on or off, the way Task Manager (or Task Scheduler) does.</summary>
internal abstract record StartupSwitch
{
    public abstract bool IsEnabled(SystemAdapters system);

    public virtual bool CanSwitch(SystemAdapters system) => true;

    /// <summary>The step that sets the item on or off; captured and journaled like a tweak's step.</summary>
    public abstract TweakStep StepFor(bool enabled, DateTimeOffset now);
}

internal static class StartupItems
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    internal const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    /// <summary>A readable name for an item that is no longer there, from its id.</summary>
    public static string NameFromId(string itemId)
    {
        var rest = itemId[(itemId.IndexOf(':') + 1)..];
        if (itemId.StartsWith("task:", StringComparison.Ordinal))
            return rest[(rest.LastIndexOf('\\') + 1)..];
        return rest[(rest.IndexOf(':') + 1)..];
    }

    public static StartupItem FromRunValue(RunValue run)
    {
        var (root, approvedKey, location, tag) = run.Key switch
        {
            RunKey.CurrentUser => (RegistryRoot.CurrentUser, "Run", $@"HKCU\{RunPath}", "HKCU"),
            RunKey.LocalMachine => (RegistryRoot.LocalMachine, "Run", $@"HKLM\{RunPath}", "HKLM"),
            _ => (RegistryRoot.LocalMachine, "Run32", $@"HKLM\{Run32Path}", "HKLM32"),
        };
        return new StartupItem(
            $"run:{tag}:{run.Name}",
            run.Name,
            StartupSource.RunKey,
            location,
            run.Command,
            run.ExecutablePath,
            new StartupApprovedSwitch(new RegistryLocation(root, $@"{ApprovedPath}\{approvedKey}", run.Name)),
            CanDelete: true)
        {
            Remove = system =>
            {
                Sources(system).DeleteRunValue(run.Key, run.Name);
                system.Registry.DeleteValue(new RegistryLocation(root, $@"{ApprovedPath}\{approvedKey}", run.Name));
            },
        };
    }

    public static StartupItem FromStartupFile(StartupFile file)
    {
        var (root, tag) = file.Folder == StartupFolder.User ? (RegistryRoot.CurrentUser, "user") : (RegistryRoot.LocalMachine, "all");
        return new StartupItem(
            $"folder:{tag}:{file.FileName}",
            Path.GetFileNameWithoutExtension(file.FileName),
            StartupSource.StartupFolder,
            file.FullPath.LastIndexOf('\\') is > 0 and var slash ? file.FullPath[..slash] : file.FullPath,
            file.Target ?? file.FullPath,
            file.Target,
            new StartupApprovedSwitch(new RegistryLocation(root, $@"{ApprovedPath}\StartupFolder", file.FileName)),
            CanDelete: true)
        {
            DeleteTarget = new FileTarget(file.FullPath),
            Remove = system =>
            {
                Sources(system).DeleteStartupFile(file.Folder, file.FileName);
                system.Registry.DeleteValue(new RegistryLocation(root, $@"{ApprovedPath}\StartupFolder", file.FileName));
            },
        };
    }

    public static StartupItem FromStoreTask(StoreStartupTask task) => new(
        $"store:{task.PackageFamilyName}!{task.TaskId}",
        task.DisplayName,
        StartupSource.StoreApp,
        $"Store app {task.PackageFamilyName}",
        task.ExecutablePath ?? task.PackageFamilyName,
        task.ExecutablePath,
        new StoreTaskSwitch(
            new RegistryLocation(
                RegistryRoot.CurrentUser,
                $@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\{task.PackageFamilyName}\{task.TaskId}",
                "State"),
            task.EnabledByDefault),
        // Only the app (or uninstalling it) can remove its declared startup task.
        CanDelete: false)
    {
        Publisher = task.Publisher,
        // Store packages are signature-checked by Windows when they are installed.
        Signature = SignatureStatus.Signed,
    };

    public static StartupItem FromTask(ScheduledTaskInfo task) => new(
        $"task:{task.Path}",
        task.Path[(task.Path.LastIndexOf('\\') + 1)..],
        task.RunsAtSignIn ? StartupSource.SignInTask : StartupSource.ScheduledTask,
        task.Path,
        task.Command ?? "",
        task.ExecutablePath,
        new ScheduledTaskSwitch(task.Path, task.Enabled),
        CanDelete: !IsMicrosoftTask(task.Path))
    {
        FallbackPublisher = task.Author,
        Remove = IsMicrosoftTask(task.Path) ? null : system => ScheduledTaskStep.Tasks(system).Delete(task.Path),
    };

    private static IStartupSources Sources(SystemAdapters system) =>
        system.Startup?.Sources ?? throw new InvalidOperationException("Startup items aren't available here.");

    /// <summary>Windows' own tasks live under \Microsoft\; the app lists and changes only third-party ones.</summary>
    public static bool IsMicrosoftTask(string path) => path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A Store app's startup task: a State DWORD under AppModel\SystemAppData. 0 off, 1 off by the user (what Task
/// Manager and Settings write), 2 on, 3 off by policy, 4 on by policy. No value means the manifest's default.
/// </summary>
internal sealed record StoreTaskSwitch(RegistryLocation State, bool EnabledByDefault) : StartupSwitch
{
    private uint? Read(SystemAdapters system) =>
        system.Registry.GetValue(State) is { Kind: RegistryValueKind.DWord, Data.Length: 4 } value ? BitConverter.ToUInt32(value.Data) : null;

    public override bool IsEnabled(SystemAdapters system) => Read(system) switch
    {
        null => EnabledByDefault,
        2 or 4 => true,
        _ => false,
    };

    public override bool CanSwitch(SystemAdapters system) => Read(system) is not (3 or 4);

    public override TweakStep StepFor(bool enabled, DateTimeOffset now) =>
        new RegistryValueStep(State, RegistryValue.DWord(enabled ? 2u : 1u));
}

/// <summary>A scheduled task's own Enabled setting, as Task Scheduler's Disable and Enable set it.</summary>
internal sealed record ScheduledTaskSwitch(string Path, bool Enabled) : StartupSwitch
{
    public override bool IsEnabled(SystemAdapters system) => Enabled;

    public override bool CanSwitch(SystemAdapters system) => !StartupItems.IsMicrosoftTask(Path);

    public override TweakStep StepFor(bool enabled, DateTimeOffset now) => new ScheduledTaskStep(Path, enabled);
}

/// <summary>
/// Task Manager's switch for Run keys and Startup folders: a 12-byte REG_BINARY under Explorer\StartupApproved named
/// after the item. An odd first byte (03) means off, followed by the FILETIME it was turned off; 02 with zeros means
/// on; no value means on.
/// </summary>
internal sealed record StartupApprovedSwitch(RegistryLocation Approved) : StartupSwitch
{
    public static RegistryValue On { get; } = new(RegistryValueKind.Binary, [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

    public static RegistryValue Off(DateTimeOffset at)
    {
        var data = new byte[12];
        data[0] = 0x03;
        BitConverter.GetBytes(at.UtcDateTime.ToFileTimeUtc()).CopyTo(data, 4);
        return new RegistryValue(RegistryValueKind.Binary, data);
    }

    public override bool IsEnabled(SystemAdapters system) =>
        system.Registry.GetValue(Approved) is not { Data.Length: > 0 } value || (value.Data[0] & 1) == 0;

    public override TweakStep StepFor(bool enabled, DateTimeOffset now) =>
        new RegistryValueStep(Approved, enabled ? On : Off(now));
}
