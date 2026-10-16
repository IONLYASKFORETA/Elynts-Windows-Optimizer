using Elynt.Core.Drivers;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// Reads the display adapters from the Display device class key, where each adapter's driver records its name
/// (DriverDesc), hardware ID (MatchingDeviceId) and version (DriverVersion, the INF's DriverVer).
/// </summary>
public sealed class WindowsDriverInventory : IDriverInventory
{
    private const string DisplayAdapterClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public IReadOnlyList<DisplayAdapter> DisplayAdapters()
    {
        var found = new List<DisplayAdapter>();
        using var adapters = Registry.LocalMachine.OpenSubKey(DisplayAdapterClass);
        foreach (var name in adapters?.GetSubKeyNames() ?? [])
        {
            if (!int.TryParse(name, out _))
                continue; // "Properties" and the like are not adapters.

            RegistryKey? adapter;
            try
            {
                adapter = adapters!.OpenSubKey(name);
            }
            catch (System.Security.SecurityException)
            {
                continue;
            }

            using (adapter)
            {
                var device = adapter?.GetValue("MatchingDeviceId") as string;
                var version = adapter?.GetValue("DriverVersion") as string;
                if (string.IsNullOrWhiteSpace(device) || string.IsNullOrWhiteSpace(version))
                    continue;
                // Adapters that were removed keep their key; only PCI devices are real GPUs.
                if (!device.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))
                    continue;
                var description = adapter?.GetValue("DriverDesc") as string ?? device;
                found.Add(new DisplayAdapter(description, device, version));
            }
        }

        return found
            .GroupBy(a => a.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }
}
