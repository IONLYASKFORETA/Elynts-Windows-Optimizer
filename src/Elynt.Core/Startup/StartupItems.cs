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
            CanDelete: true);
    }
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
