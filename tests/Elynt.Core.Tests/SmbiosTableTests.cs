using System.Text;
using Elynt.Core.Bios;

namespace Elynt.Core.Tests;

/// <summary>Board and memory facts read from the raw SMBIOS table Windows returns from GetSystemFirmwareTable('RSMB').</summary>
public class SmbiosTableTests
{
    [Fact]
    public void Reads_the_board_and_every_installed_memory_module()
    {
        var raw = RawSmbios(
            Baseboard("Micro-Star International Co., Ltd.", "MAG B650 TOMAHAWK WIFI (MS-7D75)"),
            MemoryDevice(sizeMb: 16384, type: 0x22, speed: 4800, configured: 6000),
            MemoryDevice(sizeMb: 0, type: 0x02, speed: 0, configured: 0), // empty slot
            MemoryDevice(sizeMb: 16384, type: 0x22, speed: 4800, configured: 6000),
            Structure(127, []));

        var table = SmbiosTable.Parse(raw);

        Assert.Equal("Micro-Star International Co., Ltd.", table.BoardManufacturer);
        Assert.Equal("MAG B650 TOMAHAWK WIFI (MS-7D75)", table.BoardProduct);
        Assert.Equal([new MemoryModule(MemoryType.Ddr5, 4800, 6000) { SizeMb = 16384 }, new MemoryModule(MemoryType.Ddr5, 4800, 6000) { SizeMb = 16384 }], table.Memory);
    }

    [Fact]
    public void Ddr4_and_speeds_above_65535_use_the_extended_fields()
    {
        var raw = RawSmbios(
            MemoryDevice(sizeMb: 8192, type: 0x1A, speed: 3200, configured: 3600),
            MemoryDevice(sizeMb: 8192, type: 0x22, speed: 0xFFFF, configured: 0xFFFF, extendedSpeed: 70000, extendedConfigured: 72000));

        var table = SmbiosTable.Parse(raw);

        Assert.Equal([new MemoryModule(MemoryType.Ddr4, 3200, 3600) { SizeMb = 8192 }, new MemoryModule(MemoryType.Ddr5, 70000, 72000) { SizeMb = 8192 }], table.Memory);
        Assert.Equal("", table.BoardManufacturer);
    }

    [Fact]
    public void Reads_each_modules_size_with_modules_of_32_GB_and_more_in_the_extended_field()
    {
        var raw = RawSmbios(
            MemoryDevice(sizeMb: 16384, type: 0x22, speed: 4800, configured: 6000),
            MemoryDevice(sizeMb: 0x7FFF, type: 0x22, speed: 4800, configured: 6000, extendedSizeMb: 49152),
            MemoryDevice(sizeMb: 0x8000 | 512, type: 0x02, speed: 0, configured: 0)); // bit 15: the size is in KB

        var table = SmbiosTable.Parse(raw);

        Assert.Equal([16384, 49152, 0], table.Memory.Select(m => m.SizeMb));
    }

    [Fact]
    public void A_truncated_table_gives_what_was_read_before_the_cut()
    {
        var raw = RawSmbios(Baseboard("ASRock", "B650M Pro RS"));
        var cut = raw[..^3];

        var table = SmbiosTable.Parse(cut);

        Assert.Empty(table.Memory);
    }

    private static byte[] RawSmbios(params byte[][] structures)
    {
        var body = structures.SelectMany(s => s).ToArray();
        var header = new byte[8];
        header[1] = 3; // major version
        header[2] = 6;
        BitConverter.GetBytes(body.Length).CopyTo(header, 4);
        return [.. header, .. body];
    }

    private static byte[] Baseboard(string manufacturer, string product)
    {
        var formatted = new byte[0x0F - 4];
        formatted[0] = 1; // offset 0x04: manufacturer string 1
        formatted[1] = 2; // offset 0x05: product string 2
        return Structure(2, formatted, manufacturer, product);
    }

    private static byte[] MemoryDevice(
        int sizeMb, byte type, ushort speed, ushort configured, uint extendedSpeed = 0, uint extendedConfigured = 0, uint extendedSizeMb = 0)
    {
        var formatted = new byte[0x5C - 4];
        BitConverter.GetBytes((ushort)sizeMb).CopyTo(formatted, 0x0C - 4);
        BitConverter.GetBytes(extendedSizeMb).CopyTo(formatted, 0x1C - 4);
        formatted[0x12 - 4] = type;
        BitConverter.GetBytes(speed).CopyTo(formatted, 0x15 - 4);
        BitConverter.GetBytes(configured).CopyTo(formatted, 0x20 - 4);
        BitConverter.GetBytes(extendedSpeed).CopyTo(formatted, 0x54 - 4);
        BitConverter.GetBytes(extendedConfigured).CopyTo(formatted, 0x58 - 4);
        return Structure(17, formatted);
    }

    private static byte[] Structure(byte type, byte[] formatted, params string[] strings)
    {
        byte[] header = [type, (byte)(formatted.Length + 4), 0, 0];
        var stringSet = strings.Length == 0
            ? new byte[] { 0, 0 }
            : [.. strings.SelectMany(s => Encoding.ASCII.GetBytes(s).Append((byte)0)), 0];
        return [.. header, .. formatted, .. stringSet];
    }
}
