using System.Text;

namespace Elynt.Core.Registry;

public enum RegistryRoot
{
    CurrentUser,
    LocalMachine,
}

/// <summary>Registry value types, numbered as in winnt.h so raw values map one-to-one.</summary>
public enum RegistryValueKind
{
    None = 0,
    String = 1,
    ExpandString = 2,
    Binary = 3,
    DWord = 4,
    DWordBigEndian = 5,
    Link = 6,
    MultiString = 7,
    ResourceList = 8,
    FullResourceDescriptor = 9,
    ResourceRequirementsList = 10,
    QWord = 11,
}

/// <summary>A registry value exactly as stored: its type and raw bytes, never a parsed value.</summary>
public sealed record RegistryValue(RegistryValueKind Kind, byte[] Data)
{
    /// <summary>A REG_SZ, stored as UTF-16 with a terminating null like Windows writes it.</summary>
    public static RegistryValue String(string value) =>
        new(RegistryValueKind.String, Encoding.Unicode.GetBytes(value + "\0"));

    public static RegistryValue DWord(uint value) =>
        new(RegistryValueKind.DWord, BitConverter.GetBytes(value));

    public bool Equals(RegistryValue? other) =>
        other is not null && Kind == other.Kind && Data.AsSpan().SequenceEqual(other.Data);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.AddBytes(Data);
        return hash.ToHashCode();
    }

    /// <summary>How the value reads in regedit, e.g. <c>"0" (REG_SZ)</c>.</summary>
    public override string ToString()
    {
        var shown = Kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString =>
                $"\"{Encoding.Unicode.GetString(Data).TrimEnd('\0')}\"",
            RegistryValueKind.DWord when Data.Length == 4 =>
                $"{BitConverter.ToUInt32(Data)} (0x{BitConverter.ToUInt32(Data):X8})",
            _ => Data.Length == 0 ? "(empty)" : Convert.ToHexString(Data),
        };
        return $"{shown} ({RegName(Kind)})";
    }

    private static string RegName(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.None => "REG_NONE",
        RegistryValueKind.String => "REG_SZ",
        RegistryValueKind.ExpandString => "REG_EXPAND_SZ",
        RegistryValueKind.Binary => "REG_BINARY",
        RegistryValueKind.DWord => "REG_DWORD",
        RegistryValueKind.DWordBigEndian => "REG_DWORD_BIG_ENDIAN",
        RegistryValueKind.Link => "REG_LINK",
        RegistryValueKind.MultiString => "REG_MULTI_SZ",
        RegistryValueKind.QWord => "REG_QWORD",
        _ => $"type {(int)kind}",
    };
}

/// <summary>One named value under a key. An empty <see cref="ValueName"/> is the key's default value.</summary>
public sealed record RegistryLocation(RegistryRoot Root, string KeyPath, string ValueName)
{
    public override string ToString()
    {
        var root = Root == RegistryRoot.CurrentUser ? "HKCU" : "HKLM";
        var name = ValueName.Length == 0 ? "(Default)" : ValueName;
        return $@"{root}\{KeyPath}\{name}";
    }
}
