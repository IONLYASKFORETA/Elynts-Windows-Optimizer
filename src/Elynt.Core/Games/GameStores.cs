using System.Text.Json;

namespace Elynt.Core.Games;

/// <summary>Holds the .exe paths of games each user added by hand.</summary>
public interface IGameStore
{
    IReadOnlyList<string> Load(string user);

    void Save(string user, IReadOnlyList<string> exePaths);
}

public sealed class InMemoryGameStore : IGameStore
{
    private readonly Dictionary<string, List<string>> _byUser = new();

    public IReadOnlyList<string> Load(string user) => _byUser.TryGetValue(user, out var paths) ? [.. paths] : [];

    public void Save(string user, IReadOnlyList<string> exePaths) => _byUser[user] = [.. exePaths];
}

/// <summary>One JSON file per user in a folder, replaced atomically on every save.</summary>
public sealed class JsonFileGameStore(string folder) : IGameStore
{
    public IReadOnlyList<string> Load(string user)
    {
        var path = PathFor(user);
        if (!File.Exists(path))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(string user, IReadOnlyList<string> exePaths)
    {
        AtomicFile.Write(PathFor(user), JsonSerializer.Serialize(exePaths));
    }

    // A SID is safe as a file name; anything else is stripped to letters, digits and dashes.
    private string PathFor(string user) =>
        Path.Combine(folder, new string(user.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()) + ".json");
}
