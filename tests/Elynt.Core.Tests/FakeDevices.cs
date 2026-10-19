using Elynt.Core.Devices;

namespace Elynt.Core.Tests;

/// <summary>Plug and Play devices in memory.</summary>
public sealed class FakeDevices : IDevices
{
    private readonly Dictionary<string, (string HardwareId, bool Enabled)> _devices = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string instanceId, string hardwareId, bool enabled) => _devices[instanceId] = (hardwareId, enabled);

    /// <summary>Takes the device away, as if it were removed from the PC.</summary>
    public void Remove(string instanceId) => _devices.Remove(instanceId);

    public IReadOnlyList<string> FindByHardwareId(string hardwareId) =>
        _devices.Where(d => d.Value.HardwareId.Equals(hardwareId, StringComparison.OrdinalIgnoreCase)).Select(d => d.Key).ToList();

    public bool? IsEnabled(string instanceId) => _devices.TryGetValue(instanceId, out var device) ? device.Enabled : null;

    public void SetEnabled(string instanceId, bool enabled)
    {
        if (!_devices.TryGetValue(instanceId, out var device))
            throw new InvalidOperationException($"There is no device {instanceId}.");
        _devices[instanceId] = device with { Enabled = enabled };
    }
}
