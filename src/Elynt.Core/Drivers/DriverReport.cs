namespace Elynt.Core.Drivers;

/// <summary>A GPU maker, keyed on the PCI vendor ID in the device's hardware ID.</summary>
public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
}

/// <summary>A display adapter exactly as Windows reports it.</summary>
/// <param name="Name">The device name, e.g. "NVIDIA GeForce RTX 4070".</param>
/// <param name="DeviceId">The PnP hardware ID, e.g. PCI\VEN_10DE&amp;DEV_2786&amp;….</param>
/// <param name="DriverVersion">Windows' four-part driver version, e.g. 32.0.15.9174.</param>
/// <param name="VendorVersion">
/// The vendor's own package version when its driver records one, e.g. AMD's Adrenalin version 25.9.1, which can't be
/// worked out from Windows' version.
/// </param>
public sealed record DisplayAdapter(string Name, string DeviceId, string DriverVersion, string? VendorVersion = null)
{
    public GpuVendor Vendor
    {
        get
        {
            var id = DeviceId.ToUpperInvariant();
            if (id.Contains("VEN_10DE")) return GpuVendor.Nvidia;
            if (id.Contains("VEN_1002")) return GpuVendor.Amd;
            if (id.Contains("VEN_8086")) return GpuVendor.Intel;
            return GpuVendor.Unknown;
        }
    }
}

/// <summary>Reads the PC's display adapters and their installed drivers. It never changes anything.</summary>
public interface IDriverInventory
{
    IReadOnlyList<DisplayAdapter> DisplayAdapters();

    /// <summary>The installed version of this CPU maker's chipset driver package, or null when it isn't installed.</summary>
    string? ChipsetVersion(GpuVendor vendor);
}

/// <summary>The PC's chipset driver package as the Drivers page shows it, picked by the CPU's maker.</summary>
/// <param name="DriverVersion">The installed package version, or null when the package isn't installed.</param>
public sealed record InstalledChipset(string Name, GpuVendor Vendor, string? DriverVersion);

/// <summary>A detected GPU as the Drivers page shows it.</summary>
/// <param name="DriverVersion">The version in the vendor's own format (591.74 for NVIDIA), or Windows' when there is none.</param>
/// <param name="WindowsDriverVersion">Windows' four-part version, e.g. 32.0.15.9174.</param>
public sealed record InstalledGpu(string Name, string DeviceId, GpuVendor Vendor, string DriverVersion, string WindowsDriverVersion);

/// <summary>What the Drivers page shows: the detected GPUs and the chipset driver, or why detection failed.</summary>
/// <param name="Chipset">The chipset driver for the CPU's maker, or null when the maker isn't AMD or Intel.</param>
public sealed record DriverReport(IReadOnlyList<InstalledGpu> Gpus, string? Problem = null, InstalledChipset? Chipset = null);

/// <summary>Converts driver versions between Windows' format and the vendors'.</summary>
public static class DriverVersions
{
    /// <summary>
    /// NVIDIA's version is the last five digits of Windows' version with a dot after the third:
    /// 32.0.15.9174 is 591.74. Returns null when the version doesn't have that shape.
    /// </summary>
    public static string? Nvidia(string windowsVersion)
    {
        var digits = new string(windowsVersion.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length < 5 || windowsVersion.Split('.').Length != 4)
            return null;
        var last5 = digits[^5..];
        return $"{int.Parse(last5[..3])}.{last5[3..]}";
    }

    /// <summary>The version a user sees for this vendor's driver.</summary>
    public static string Display(GpuVendor vendor, string windowsVersion, string? vendorVersion = null) =>
        Vendor(vendor, windowsVersion, vendorVersion) ?? windowsVersion;

    /// <summary>
    /// The installed version in the format the vendor's lookup uses, or null when it can't be told: NVIDIA's from
    /// Windows' version, AMD's Adrenalin version only when the driver recorded it, and Intel's, which is Windows' own.
    /// </summary>
    public static string? Vendor(GpuVendor vendor, string windowsVersion, string? vendorVersion = null) => vendor switch
    {
        GpuVendor.Nvidia => Nvidia(windowsVersion),
        GpuVendor.Amd => string.IsNullOrWhiteSpace(vendorVersion) ? null : vendorVersion.Trim(),
        _ => windowsVersion,
    };
}
