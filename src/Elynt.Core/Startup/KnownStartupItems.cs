using System.Text.Json;

namespace Elynt.Core.Startup;

/// <summary>The startup items each user has already been told about, so only newly added ones raise a tray note.</summary>
public interface IKnownStartupItems
{
    /// <summary>The item ids seen for this user, or null before the first look.</summary>
    IReadOnlySet<string>? Load(string user);

    void Save(string user, IReadOnlySet<string> itemIds);
}

public sealed class InMemoryKnownStartupItems : IKnownStartupItems
{
    private readonly Dictionary<string, HashSet<string>> _byUser = [];
    private readonly Lock _gate = new();

    public IReadOnlySet<string>? Load(string user)
    {
        lock (_gate)
            return _byUser.TryGetValue(user, out var ids) ? new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase) : null;
    }

    public void Save(string user, IReadOnlySet<string> itemIds)
    {
        lock (_gate)
            _byUser[user] = new HashSet<string>(itemIds, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Known startup items as one JSON file for every user, replaced atomically on save.</summary>
public sealed class JsonFileKnownStartupItems(string path) : IKnownStartupItems
{
    private readonly Lock _gate = new();

    public IReadOnlySet<string>? Load(string user)
    {
        lock (_gate)
            return ReadAll().TryGetValue(user, out var ids) ? new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase) : null;
    }

    public void Save(string user, IReadOnlySet<string> itemIds)
    {
        lock (_gate)
        {
            var all = ReadAll();
            all[user] = [.. itemIds.Order(StringComparer.OrdinalIgnoreCase)];
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(all));
            File.Move(temp, path, overwrite: true);
        }
    }

    private Dictionary<string, List<string>> ReadAll()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (JsonException)
        {
            // A damaged file only means the next look takes a fresh baseline.
            return [];
        }
    }
}
