using Elynt.Core.Registry;

namespace Elynt.Core.Tweaks;

/// <summary>
/// What a step changes, stated as data so the Engine can check it against the hard rules before anything runs.
/// Every step type declares one; new step types (services, BCD, drivers, cleanup, Appx) reuse these targets.
/// </summary>
public abstract record ChangeTarget;

/// <summary>Sets one registry value.</summary>
public sealed record RegistryTarget(RegistryLocation Location, RegistryValue Desired) : ChangeTarget;

/// <summary>
/// Sets one registry value under every key matching <paramref name="Pattern"/>'s key path, where a <c>*</c> segment
/// matches any one subkey (per-device and per-interface settings).
/// </summary>
public sealed record RegistryPatternTarget(RegistryLocation Pattern, RegistryValue Desired) : ChangeTarget;

/// <summary>Sets one BCD element of the current boot entry, or deletes it when <paramref name="Value"/> is null.</summary>
public sealed record BootSettingTarget(string Element, string? Value) : ChangeTarget;

public enum ServiceStart
{
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4,
}

/// <summary>Changes a service's or driver's start type.</summary>
public sealed record ServiceTarget(string ServiceName, ServiceStart Start) : ChangeTarget;

/// <summary>Installs or loads a kernel driver, by its file name or path.</summary>
public sealed record DriverTarget(string DriverFile) : ChangeTarget;

/// <summary>Enables or disables every present device with a hardware ID, in Device Manager.</summary>
public sealed record DeviceTarget(string HardwareId, bool Enabled) : ChangeTarget;

/// <summary>Deletes a file or folder.</summary>
public sealed record FileTarget(string Path) : ChangeTarget;

/// <summary>Removes an app package (Appx, provisioned or winget).</summary>
public sealed record PackageTarget(string PackageName) : ChangeTarget;

/// <summary>Sets one power setting on the active plan, plugged in and on battery.</summary>
public sealed record PowerSettingTarget(Guid Subgroup, Guid Setting, uint Value) : ChangeTarget;

/// <summary>Makes a power plan the active one.</summary>
public sealed record PowerPlanTarget(Guid Plan) : ChangeTarget;

/// <summary>Turns a Windows optional feature on or off (its files stay on disk).</summary>
public sealed record OptionalFeatureTarget(string Feature, bool Enabled) : ChangeTarget;
