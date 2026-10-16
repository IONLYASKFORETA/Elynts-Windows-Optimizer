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
public sealed record DisplayAdapter(string Name, string DeviceId, string DriverVersion)
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
}

/// <summary>A detected GPU as the Drivers page shows it.</summary>
/// <param name="DriverVersion">The version in the vendor's own format (591.74 for NVIDIA), or Windows' when there is none.</param>
/// <param name="WindowsDriverVersion">Windows' four-part version, e.g. 32.0.15.9174.</param>
public sealed record InstalledGpu(string Name, string DeviceId, GpuVendor Vendor, string DriverVersion, string WindowsDriverVersion);

/// <summary>What the Drivers page shows: the detected GPUs, or why detection failed.</summary>
public sealed record DriverReport(IReadOnlyList<InstalledGpu> Gpus, string? Problem = null);

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
    public static string Display(GpuVendor vendor, string windowsVersion) => vendor switch
    {
        GpuVendor.Nvidia => Nvidia(windowsVersion) ?? windowsVersion,
        _ => windowsVersion,
    };
}
