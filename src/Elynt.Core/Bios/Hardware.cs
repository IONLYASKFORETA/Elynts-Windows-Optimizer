namespace Elynt.Core.Bios;

/// <summary>What a BIOS readout found. NotApplicable means the setting doesn't exist on this hardware.</summary>
public enum ReadoutState
{
    Unknown,
    On,
    Off,
    NotApplicable,
}

public enum MemoryType
{
    Other,
    Ddr4,
    Ddr5,
}

/// <summary>One installed memory module, with speeds in MT/s (the "DDR5-6000" number).</summary>
/// <param name="RatedMts">The module's own SMBIOS speed: the JEDEC rating firmware reports, not the XMP/EXPO speed.</param>
/// <param name="ConfiguredMts">The speed the BIOS is running it at right now.</param>
public sealed record MemoryModule(MemoryType Type, int RatedMts, int ConfiguredMts);

/// <summary>Read-only facts about the PC's board, CPU and memory, as firmware reports them.</summary>
/// <param name="Pbo">Whether AMD Precision Boost Overdrive is on, if the adapter can tell.</param>
public sealed record HardwareSnapshot(
    string BoardManufacturer,
    string BoardProduct,
    string CpuName,
    IReadOnlyList<MemoryModule> Memory,
    ReadoutState Pbo);

/// <summary>Reads the hardware facts the BIOS readouts and guides are built from. It never changes anything.</summary>
public interface IHardwareInfo
{
    HardwareSnapshot Read();
}
