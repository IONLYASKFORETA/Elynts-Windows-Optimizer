using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elynt.Core.Drift;

/// <summary>A change marked not supported on one Windows build by one app version.</summary>
public sealed record UnsupportedMark(string OsBuild, string AppVersion, string Reason);

/// <summary>One user's drift state, kept between service starts.</summary>
/// <param name="LastBuild">The Windows build at the last check; a different build means a full check.</param>
public sealed record DriftState(
    string? LastBuild,
    bool AutoReapply,
    IReadOnlyDictionary<string, UnsupportedMark> Unsupported)
{
    public static DriftState Initial { get; } = new(null, false, new Dictionary<string, UnsupportedMark>());
}

public interface IDriftStore
{
    DriftState Load(string user);

    void Save(string user, DriftState state);
}

public sealed class InMemoryDriftStore : IDriftStore
{
    private readonly Dictionary<string, DriftState> _states = [];
    private readonly Lock _gate = new();

    public DriftState Load(string user)
    {
        lock (_gate)
            return _states.GetValueOrDefault(user, DriftState.Initial);
    }

    public void Save(string user, DriftState state)
    {
        lock (_gate)
            _states[user] = state;
    }
}

/// <summary>Every user's drift state in one JSON file, read once and replaced atomically on every save.</summary>
public sealed class JsonFileDriftStore(string path) : IDriftStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private readonly Lock _gate = new();
    private Dictionary<string, DriftState>? _states;

    public DriftState Load(string user)
    {
        lock (_gate)
            return States().GetValueOrDefault(user, DriftState.Initial);
    }

    public void Save(string user, DriftState state)
    {
        lock (_gate)
        {
            var states = States();
            states[user] = state;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(states, Options));
            File.Move(temp, path, overwrite: true);
        }
    }

    private Dictionary<string, DriftState> States()
    {
        if (_states is not null)
            return _states;
        try
        {
            _states = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, DriftState>>(File.ReadAllText(path), Options) ?? []
                : [];
        }
        catch (JsonException)
        {
            // A damaged file only loses drift preferences and marks; the next check rebuilds them.
            _states = [];
        }
        return _states;
    }
}
