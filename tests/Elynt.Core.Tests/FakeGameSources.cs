using System.Text;
using Elynt.Core.Games;

namespace Elynt.Core.Tests;

/// <summary>An in-memory PC: files, folders and HKLM registry values, keyed case-insensitively like Windows.</summary>
public sealed class FakeGameSources : IGameSources
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public string ProgramData { get; set; } = @"C:\ProgramData";

    public List<string> FixedDrives { get; } = [@"C:\"];

    IReadOnlyList<string> IGameSources.Drives => FixedDrives;

    /// <summary>How many times a file was read, to show scans are not repeated on every command.</summary>
    public int Reads { get; private set; }

    public FakeGameSources File(string path, string text = "") => File(path, Encoding.UTF8.GetBytes(text));

    public FakeGameSources File(string path, byte[] bytes)
    {
        path = Normalize(path);
        _files[path] = bytes;
        Directory(Parent(path));
        return this;
    }

    public FakeGameSources Directory(string path)
    {
        for (var dir = Normalize(path); dir.Length > 0; dir = Parent(dir))
            _directories.Add(dir);
        return this;
    }

    public FakeGameSources Key(string keyPath, params (string Name, string Value)[] values)
    {
        keyPath = keyPath.Trim('\\');
        for (var key = keyPath; key.Length > 0; key = Parent(key))
            _keys.TryAdd(key, new(StringComparer.OrdinalIgnoreCase));
        foreach (var (name, value) in values)
            _keys[keyPath][name] = value;
        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public string? ReadText(string path)
    {
        Reads++;
        return _files.TryGetValue(Normalize(path), out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }

    public byte[]? ReadBytes(string path)
    {
        Reads++;
        return _files.TryGetValue(Normalize(path), out var bytes) ? bytes : null;
    }

    public IReadOnlyList<string> Files(string directory, string extension) =>
        _files.Keys.Where(f => Parent(f).Equals(Normalize(directory), StringComparison.OrdinalIgnoreCase) &&
                               f.EndsWith(extension, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<string> Directories(string directory) =>
        _directories.Where(d => Parent(d).Equals(Normalize(directory), StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<string> SubKeys(string keyPath) =>
        _keys.Keys.Where(k => Parent(k).Equals(keyPath.Trim('\\'), StringComparison.OrdinalIgnoreCase))
            .Select(k => k[(k.LastIndexOf('\\') + 1)..]).ToList();

    public string? Value(string keyPath, string name) =>
        _keys.TryGetValue(keyPath.Trim('\\'), out var values) && values.TryGetValue(name, out var value) ? value : null;

    private static string Normalize(string path)
    {
        path = path.Replace('/', '\\');
        return path.Length > 3 ? path.TrimEnd('\\') : path;
    }

    private static string Parent(string path)
    {
        var trimmed = path.TrimEnd('\\');
        var slash = trimmed.LastIndexOf('\\');
        if (slash < 0)
            return "";
        // "C:\X" has the drive root "C:\" as its parent.
        return slash == 2 && trimmed[1] == ':' ? trimmed[..3] : trimmed[..slash];
    }
}
