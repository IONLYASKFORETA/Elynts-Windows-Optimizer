using System.Text.RegularExpressions;

namespace Elynt.Core.Bios;

public enum CpuMaker
{
    Unknown,
    Intel,
    Amd,
}

public enum BoardVendor
{
    Other,
    Asus,
    Msi,
    Gigabyte,
    AsRock,
}

/// <summary>The CPU as the guides need it, parsed from its marketing name.</summary>
/// <param name="IntelGeneration">Core i generation (e.g. 13 for i7-13700K); 15 for Core Ultra; null if unknown.</param>
/// <param name="RyzenSeries">Thousands digit of the Ryzen model (e.g. 7 for 7800X3D); null if unknown.</param>
public sealed record Cpu(CpuMaker Maker, int? IntelGeneration, int? RyzenSeries, bool IsX3D)
{
    public static Cpu Parse(string name)
    {
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("Core(TM) Ultra", StringComparison.OrdinalIgnoreCase) || name.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase))
                return new(CpuMaker.Intel, 15, null, false);
            var model = Regex.Match(name, @"i[3579]-(\d{4,5})");
            int? generation = model.Success
                ? model.Groups[1].Value is var digits && digits.Length == 5 ? int.Parse(digits[..2]) : int.Parse(digits[..1])
                : null;
            return new(CpuMaker.Intel, generation, null, false);
        }

        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
        {
            var model = Regex.Match(name, @"Ryzen\s+(?:Threadripper\s+(?:PRO\s+)?)?(?:\d\s+)?(?:PRO\s+)?(\d)\d{3}", RegexOptions.IgnoreCase);
            int? series = model.Success ? int.Parse(model.Groups[1].Value) : null;
            return new(CpuMaker.Amd, null, series, name.Contains("X3D", StringComparison.OrdinalIgnoreCase));
        }

        return new(CpuMaker.Unknown, null, null, false);
    }
}

public static class Boards
{
    /// <summary>Maps the SMBIOS baseboard manufacturer to the vendor whose BIOS layout the guides describe.</summary>
    public static BoardVendor Vendor(string manufacturer)
    {
        var m = manufacturer.ToUpperInvariant();
        if (m.Contains("ASUS"))
            return BoardVendor.Asus;
        if (m.Contains("MICRO-STAR") || m.Split(' ', ',', '.').Contains("MSI"))
            return BoardVendor.Msi;
        if (m.Contains("GIGABYTE"))
            return BoardVendor.Gigabyte;
        if (m.Contains("ASROCK"))
            return BoardVendor.AsRock;
        return BoardVendor.Other;
    }
}
