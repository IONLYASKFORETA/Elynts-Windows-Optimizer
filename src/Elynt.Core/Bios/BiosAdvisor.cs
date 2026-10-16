namespace Elynt.Core.Bios;

/// <summary>Turns a hardware snapshot into readouts and the BIOS guides that fit this board and CPU.</summary>
public static class BiosAdvisor
{
    private static readonly HardwareSnapshot Unread = new("", "", "", [], ReadoutState.Unknown);

    /// <param name="read">The hardware facts, or null when they couldn't be read.</param>
    public static BiosReport Build(HardwareSnapshot? read)
    {
        var hardware = read ?? Unread;
        var cpu = Cpu.Parse(hardware.CpuName);
        return new BiosReport(
            Describe(hardware),
            [MemoryProfile(hardware), Pbo(hardware, cpu)],
            read is null ? [] : BiosGuides.For(hardware, cpu),
            StabilityTools.All);
    }

    private static string Describe(HardwareSnapshot hardware)
    {
        var board = $"{hardware.BoardManufacturer} {hardware.BoardProduct}".Trim();
        var parts = new[] { board, hardware.CpuName.Trim() }.Where(p => p.Length > 0).ToList();
        return parts.Count == 0 ? "Couldn't read this PC's board and CPU." : string.Join(", ", parts);
    }

    private static BiosReadout MemoryProfile(HardwareSnapshot hardware)
    {
        const string name = "XMP / EXPO memory profile";
        var modules = hardware.Memory.Where(m => m.ConfiguredMts > 0).ToList();
        if (modules.Count == 0)
            return new(BiosReadoutIds.MemoryProfile, name, ReadoutState.Unknown,
                "Windows didn't report the memory speed, so we can't tell. Check the memory speed on the BIOS's first screen.");

        var configured = modules.Max(m => m.ConfiguredMts);
        var rated = modules.Where(m => m.RatedMts > 0).Select(m => m.RatedMts).DefaultIfEmpty(0).Max();
        var type = modules[0].Type;
        var ceiling = StandardCeiling(type);

        if (rated > 0 && configured > rated)
            return new(BiosReadoutIds.MemoryProfile, name, ReadoutState.On,
                $"Memory runs at {configured} MT/s, above its standard rating of {rated} MT/s, so a profile is loaded.");
        if (ceiling is int max && configured > max)
            return new(BiosReadoutIds.MemoryProfile, name, ReadoutState.On,
                $"Memory runs at {configured} MT/s, faster than any standard {Label(type)} speed (up to {max} MT/s), so a profile is loaded.");

        var ratedText = rated > 0 ? $"its standard rating of {rated} MT/s" : "a standard speed";
        return new(BiosReadoutIds.MemoryProfile, name, ReadoutState.Off,
            $"Memory runs at {configured} MT/s, {ratedText}. If the kit's box lists a faster speed, the profile is off.");
    }

    private static BiosReadout Pbo(HardwareSnapshot hardware, Cpu cpu)
    {
        const string name = "Precision Boost Overdrive (PBO)";
        return cpu.Maker switch
        {
            CpuMaker.Intel => new(BiosReadoutIds.Pbo, name, ReadoutState.NotApplicable, "PBO is an AMD Ryzen setting. This PC has an Intel CPU."),
            CpuMaker.Amd => hardware.Pbo switch
            {
                ReadoutState.On => new(BiosReadoutIds.Pbo, name, ReadoutState.On, "PBO is on: the CPU may boost past AMD's stock power limits."),
                ReadoutState.Off => new(BiosReadoutIds.Pbo, name, ReadoutState.Off, "PBO is off: the CPU runs at AMD's stock power limits."),
                ReadoutState.NotApplicable => new(BiosReadoutIds.Pbo, name, ReadoutState.NotApplicable, "This CPU doesn't support PBO."),
                _ => new(BiosReadoutIds.Pbo, name, ReadoutState.Unknown,
                    "Windows can't read PBO without a low-level driver, which the app doesn't load. Check it in the BIOS, or in AMD Ryzen Master."),
            },
            _ => new(BiosReadoutIds.Pbo, name, ReadoutState.Unknown, "Couldn't tell which CPU this PC has."),
        };
    }

    /// <summary>
    /// The fastest JEDEC speed consumer modules of each type ship at without a profile (inference: DDR4 tops out at
    /// 3200; DDR5 kits sold without XMP/EXPO run at most 6400). Anything faster must come from a profile.
    /// </summary>
    private static int? StandardCeiling(MemoryType type) => type switch
    {
        MemoryType.Ddr4 => 3200,
        MemoryType.Ddr5 => 6400,
        _ => null,
    };

    private static string Label(MemoryType type) => type switch
    {
        MemoryType.Ddr4 => "DDR4",
        MemoryType.Ddr5 => "DDR5",
        _ => "memory",
    };
}
