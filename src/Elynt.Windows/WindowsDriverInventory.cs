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
                // AMD's driver records its Adrenalin version here; Windows' version can't be turned into it.
                var vendorVersion = adapter?.GetValue("RadeonSoftwareVersion") as string;
                found.Add(new DisplayAdapter(description, device, version, vendorVersion));
            }
        }

        return found
            .GroupBy(a => a.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static readonly string[] UninstallKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    /// <summary>
    /// The DisplayVersion of the "AMD Chipset Software" or "Intel(R) Chipset Device Software" Uninstall entry, or null
    /// when the package isn't installed.
    /// </summary>
    public string? ChipsetVersion(GpuVendor vendor)
    {
        var product = vendor switch
        {
            GpuVendor.Amd => "AMD Chipset Software",
            GpuVendor.Intel => "Intel(R) Chipset Device Software",
            _ => null,
        };
        if (product is null)
            return null;

        foreach (var path in UninstallKeys)
        {
            using var uninstall = Registry.LocalMachine.OpenSubKey(path);
            foreach (var name in uninstall?.GetSubKeyNames() ?? [])
            {
                using var entry = uninstall!.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") is string displayName &&
                    displayName.StartsWith(product, StringComparison.OrdinalIgnoreCase) &&
                    entry.GetValue("DisplayVersion") is string version &&
                    !string.IsNullOrWhiteSpace(version))
                    return version.Trim();
            }
        }

        return null;
    }
}
