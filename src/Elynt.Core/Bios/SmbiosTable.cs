using System.Buffers.Binary;
using System.Text;

namespace Elynt.Core.Bios;

/// <summary>
/// The parts of the SMBIOS table the BIOS page uses: the baseboard (type 2) and memory devices (type 17).
/// Field offsets follow the DMTF SMBIOS reference specification (DSP0134).
/// </summary>
public sealed record SmbiosTable(string BoardManufacturer, string BoardProduct, IReadOnlyList<MemoryModule> Memory)
{
    private const int RawHeaderLength = 8; // RawSMBIOSData: calling method, major, minor, DMI revision, DWORD length

    /// <summary>Parses the buffer GetSystemFirmwareTable('RSMB', 0) fills. Stops quietly at a malformed or cut-off structure.</summary>
    public static SmbiosTable Parse(ReadOnlySpan<byte> raw)
    {
        var manufacturer = "";
        var product = "";
        var memory = new List<MemoryModule>();
        if (raw.Length < RawHeaderLength)
            return new(manufacturer, product, memory);

        var declared = BinaryPrimitives.ReadInt32LittleEndian(raw[4..]);
        var table = raw[RawHeaderLength..];
        if (declared >= 0 && declared < table.Length)
            table = table[..declared];

        var offset = 0;
        while (offset + 4 <= table.Length)
        {
            var type = table[offset];
            var length = table[offset + 1];
            if (length < 4 || offset + length > table.Length)
                break;
            var formatted = table.Slice(offset, length);
            var stringsEnd = FindStringSetEnd(table, offset + length);
            if (stringsEnd < 0)
                break;
            var strings = ReadStrings(table[(offset + length)..stringsEnd]);

            if (type == 2 && length >= 6)
            {
                manufacturer = StringAt(strings, formatted[4]);
                product = StringAt(strings, formatted[5]);
            }
            else if (type == 17 && length >= 0x17 && MemoryDevice(formatted) is { } module)
            {
                memory.Add(module);
            }
            else if (type == 127)
            {
                break;
            }

            offset = stringsEnd + 2;
        }

        return new(manufacturer, product, memory);
    }

    private static MemoryModule? MemoryDevice(ReadOnlySpan<byte> s)
    {
        var size = BinaryPrimitives.ReadUInt16LittleEndian(s[0x0C..]);
        if (size == 0)
            return null; // empty slot

        var type = s[0x12] switch
        {
            0x1A => MemoryType.Ddr4,
            0x22 => MemoryType.Ddr5,
            _ => MemoryType.Other,
        };
        var speed = (int)BinaryPrimitives.ReadUInt16LittleEndian(s[0x15..]);
        var configured = s.Length >= 0x22 ? BinaryPrimitives.ReadUInt16LittleEndian(s[0x20..]) : 0;
        // 0xFFFF means the real value is in the SMBIOS 3.3 extended DWORD fields.
        if (speed == 0xFFFF)
            speed = s.Length >= 0x58 ? (int)(BinaryPrimitives.ReadUInt32LittleEndian(s[0x54..]) & 0x7FFFFFFF) : 0;
        if (configured == 0xFFFF)
            configured = s.Length >= 0x5C ? (int)(BinaryPrimitives.ReadUInt32LittleEndian(s[0x58..]) & 0x7FFFFFFF) : 0;
        return new MemoryModule(type, speed, configured);
    }

    /// <summary>The index of the first byte of the double-null that ends a string set, or -1 if it is cut off.</summary>
    private static int FindStringSetEnd(ReadOnlySpan<byte> table, int start)
    {
        for (var i = start; i + 1 < table.Length; i++)
        {
            if (table[i] == 0 && table[i + 1] == 0)
                return i;
        }
        return -1;
    }

    private static List<string> ReadStrings(ReadOnlySpan<byte> set)
    {
        var strings = new List<string>();
        foreach (var range in set.Split((byte)0))
        {
            var bytes = set[range];
            if (bytes.Length > 0)
                strings.Add(Encoding.ASCII.GetString(bytes).Trim());
        }
        return strings;
    }

    private static string StringAt(List<string> strings, byte index) =>
        index >= 1 && index <= strings.Count ? strings[index - 1] : "";
}
