using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elynt.Core.Profiles;

/// <summary>Holds each user's per-game profiles.</summary>
public interface IProfileStore
{
    IReadOnlyList<GameProfile> Load(string user);

    void Save(string user, IReadOnlyList<GameProfile> profiles);
}

public sealed class InMemoryProfileStore : IProfileStore
{
    private readonly Dictionary<string, string> _byUser = new();

    // Round-trips through JSON so tests exercise the same serialization as the file store.
    public IReadOnlyList<GameProfile> Load(string user) =>
        _byUser.TryGetValue(user, out var json) ? ProfileJson.Deserialize(json) : [];

    public void Save(string user, IReadOnlyList<GameProfile> profiles) => _byUser[user] = ProfileJson.Serialize(profiles);
}

/// <summary>One JSON file per user in a folder, replaced atomically on every save.</summary>
public sealed class JsonFileProfileStore(string folder) : IProfileStore
{
    public IReadOnlyList<GameProfile> Load(string user)
    {
        var path = PathFor(user);
        if (!File.Exists(path))
            return [];
        try
        {
            return ProfileJson.Deserialize(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(string user, IReadOnlyList<GameProfile> profiles)
    {
        AtomicFile.Write(PathFor(user), ProfileJson.Serialize(profiles));
    }

    // A SID is safe as a file name; anything else is stripped to letters, digits and dashes.
    private string PathFor(string user) =>
        Path.Combine(folder, new string(user.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()) + ".json");
}

internal static class ProfileJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IReadOnlyList<GameProfile> profiles) => JsonSerializer.Serialize(profiles, Options);

    public static List<GameProfile> Deserialize(string json) => JsonSerializer.Deserialize<List<GameProfile>>(json, Options) ?? [];
}
