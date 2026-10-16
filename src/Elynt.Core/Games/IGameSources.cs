namespace Elynt.Core.Games;

/// <summary>
/// Read-only access to where launchers record installed games: files and folders on fixed drives, and values under
/// HKEY_LOCAL_MACHINE. Paths are Windows paths. The service runs as SYSTEM, so per-user registry hives are not read.
/// </summary>
public interface IGameSources
{
    /// <summary>The ProgramData folder, e.g. <c>C:\ProgramData</c>.</summary>
    string ProgramData { get; }

    /// <summary>Roots of the fixed drives, e.g. <c>C:\</c>.</summary>
    IReadOnlyList<string> Drives { get; }

    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>The file as UTF-8 text, or null when it is missing or can't be read.</summary>
    string? ReadText(string path);

    /// <summary>The file's bytes, or null when it is missing or can't be read.</summary>
    byte[]? ReadBytes(string path);

    /// <summary>Full paths of files directly in the folder whose names end with <paramref name="extension"/>.</summary>
    IReadOnlyList<string> Files(string directory, string extension);

    /// <summary>Full paths of folders directly in the folder.</summary>
    IReadOnlyList<string> Directories(string directory);

    /// <summary>Names of the subkeys of an HKLM key, or none when the key doesn't exist.</summary>
    IReadOnlyList<string> SubKeys(string keyPath);

    /// <summary>An HKLM string value, or null when it doesn't exist.</summary>
    string? Value(string keyPath, string name);
}

/// <summary>A PC with no launcher records, for engines built without a games adapter.</summary>
internal sealed class NoGameSources : IGameSources
{
    public static NoGameSources Instance { get; } = new();

    public string ProgramData => "";
    public IReadOnlyList<string> Drives => [];
    public bool FileExists(string path) => false;
    public bool DirectoryExists(string path) => false;
    public string? ReadText(string path) => null;
    public byte[]? ReadBytes(string path) => null;
    public IReadOnlyList<string> Files(string directory, string extension) => [];
    public IReadOnlyList<string> Directories(string directory) => [];
    public IReadOnlyList<string> SubKeys(string keyPath) => [];
    public string? Value(string keyPath, string name) => null;
}
