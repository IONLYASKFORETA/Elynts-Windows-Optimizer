using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Devices;

/// <summary>Plug and Play devices, as Device Manager shows them.</summary>
public interface IDevices
{
    /// <summary>The instance IDs of the present devices with this hardware ID (e.g. ACPI\PNP0103 for HPET).</summary>
    IReadOnlyList<string> FindByHardwareId(string hardwareId);

    /// <summary>Whether the device is enabled, or null when it isn't present.</summary>
    bool? IsEnabled(string instanceId);

    /// <summary>Enables or disables the device, and keeps it that way across restarts.</summary>
    void SetEnabled(string instanceId, bool enabled);
}

/// <summary>
/// A tweak step that enables or disables every present device with <paramref name="HardwareId"/>. A PC without such
/// a device has nothing to change, so the step counts as applied there.
/// </summary>
public sealed record DeviceEnabledStep(string HardwareId, string DeviceName, bool Enabled) : TweakStep
{
    public override ChangeTarget Target => new DeviceTarget(HardwareId, Enabled);

    public override string DescribeChange() =>
        $"{(Enabled ? "Enable" : "Disable")} the {DeviceName} device ({HardwareId}) in Device Manager";

    public override bool IsApplied(SystemAdapters system)
    {
        var devices = Devices(system);
        return devices.FindByHardwareId(HardwareId).All(id => devices.IsEnabled(id) == Enabled);
    }

    public override StepRecord Capture(SystemAdapters system)
    {
        var devices = Devices(system);
        return new DeviceEnabledRecord(
            DeviceName,
            devices.FindByHardwareId(HardwareId)
                .Select(id => new DeviceState(id, devices.IsEnabled(id) ?? Enabled))
                .ToList(),
            Enabled);
    }

    public override void Apply(SystemAdapters system)
    {
        var devices = Devices(system);
        foreach (var id in devices.FindByHardwareId(HardwareId))
            if (devices.IsEnabled(id) != Enabled)
                devices.SetEnabled(id, Enabled);
    }

    internal static IDevices Devices(SystemAdapters system) =>
        system.PnpDevices ?? throw new InvalidOperationException("Devices can't be read on this PC.");
}

/// <summary>One device and whether it was enabled.</summary>
public sealed record DeviceState(string InstanceId, bool Enabled);

/// <summary>Whether each device was enabled before the app changed it.</summary>
/// <param name="After">What the app set every device to.</param>
public sealed record DeviceEnabledRecord(string DeviceName, IReadOnlyList<DeviceState> Before, bool After) : StepRecord
{
    public override string? FindConflict(SystemAdapters system)
    {
        var devices = DeviceEnabledStep.Devices(system);
        // Enabled or not are the only states, and both are either what the app set or what was there before.
        var gone = Before.FirstOrDefault(device => devices.IsEnabled(device.InstanceId) is null);
        return gone is null
            ? null
            : $"The {DeviceName} device {gone.InstanceId} is no longer present, so it can't be put back.";
    }

    public override void Revert(SystemAdapters system)
    {
        var devices = DeviceEnabledStep.Devices(system);
        foreach (var device in Before)
            if (devices.IsEnabled(device.InstanceId) != device.Enabled)
                devices.SetEnabled(device.InstanceId, device.Enabled);
    }
}
