using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elynt.Core.Tuning;

/// <summary>Confirmed hardware settings worth re-applying after a clean start.</summary>
/// <param name="Mode">A vendor mode such as an auto-tune kind, or null for plain values.</param>
public sealed record SavedTuning(string? Mode, IReadOnlyDictionary<string, int> Values);

/// <summary>What the safety store holds on disk.</summary>
public sealed record TuningState
{
    /// <summary>The service is running, or stopped without saying goodbye: a crash, a freeze or a power cut.</summary>
    public bool SessionOpen { get; init; }

    /// <summary>Hardware (by key, e.g. "gpu:amd:0") that may be off its defaults right now.</summary>
    public IReadOnlyList<string> Live { get; init; } = [];

    /// <summary>Confirmed settings by hardware key.</summary>
    public IReadOnlyDictionary<string, SavedTuning> Saved { get; init; } = new Dictionary<string, SavedTuning>();

    /// <summary>Hardware keys whose vendor warning (such as Intel's overclock waiver) the user has accepted.</summary>
    public IReadOnlyList<string> AcceptedWarnings { get; init; } = [];

    /// <summary>
    /// Hardware that was tuned when Windows last crashed: game profiles leave it alone until the user keeps a tune on
    /// it again, so a profile's crash tune can't come back at the next launch.
    /// </summary>
    public IReadOnlyList<string> ProfilesBlocked { get; init; } = [];
}

public interface ITuningStore
{
    TuningState Load();

    /// <summary>Durably replaces the stored state. Must not return before the data is on disk.</summary>
    void Save(TuningState state);
}

public sealed class InMemoryTuningStore : ITuningStore
{
    private string _json = TuningJson.Serialize(new TuningState());

    public TuningState Load() => TuningJson.Deserialize(_json);

    // Round-trips through JSON so tests exercise the same serialization as the file store.
    public void Save(TuningState state) => _json = TuningJson.Serialize(state);
}

/// <summary>The tuning state as a JSON file, replaced atomically on every save.</summary>
public sealed class JsonFileTuningStore(string path) : ITuningStore
{
    public TuningState Load()
    {
        try
        {
            return File.Exists(path) ? TuningJson.Deserialize(File.ReadAllText(path)) : new TuningState();
        }
        catch (JsonException)
        {
            // A torn or damaged file can only come from a crash, so treat it as one: everything goes back to defaults.
            return new TuningState { SessionOpen = true, Live = [TuningSafety.EverythingKey] };
        }
    }

    public void Save(TuningState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(System.Text.Encoding.UTF8.GetBytes(TuningJson.Serialize(state)));
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }
}

internal static class TuningJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(TuningState state) => JsonSerializer.Serialize(state, Options);

    public static TuningState Deserialize(string json) =>
        JsonSerializer.Deserialize<TuningState>(json, Options) ?? new TuningState();
}

/// <summary>How the last session ended, worked out once when the service starts.</summary>
/// <param name="Unclean">The service didn't stop cleanly last time (crash, freeze, power cut) while hardware was tuned.</param>
/// <param name="Live">Hardware keys that may still be off their defaults.</param>
/// <param name="Saved">Confirmed settings by hardware key.</param>
public sealed record SessionStart(bool Unclean, IReadOnlyList<string> Live, IReadOnlyDictionary<string, SavedTuning> Saved)
{
    /// <summary>The key, or the whole store, may be off its defaults after an unclean end.</summary>
    public bool WasLive(string key) => Live.Contains(key) || Live.Contains(TuningSafety.EverythingKey);
}

/// <summary>
/// Crash-safe bookkeeping for every kind of hardware tuning (GPU and CPU). Hardware is marked live on disk before
/// it is changed, and the service marks the session closed only on a clean stop. So after a crash, freeze or power
/// cut the next start knows exactly what to put back on defaults, and never re-applies a tune that may have caused it.
/// </summary>
public sealed class TuningSafety(ITuningStore store)
{
    /// <summary>Stands for "every tunable device", used when the store itself was damaged.</summary>
    public const string EverythingKey = "*";

    private readonly Lock _gate = new();
    private TuningState? _state;

    private TuningState State => _state ??= store.Load();

    /// <summary>Opens this session and reports how the last one ended. Call once at service start.</summary>
    public SessionStart BeginSession()
    {
        lock (_gate)
        {
            _state = store.Load();
            var start = new SessionStart(_state.SessionOpen && _state.Live.Count > 0, _state.Live, _state.Saved);
            Save(_state with { SessionOpen = true });
            return start;
        }
    }

    /// <summary>Records that this hardware is about to leave its defaults. Call before changing it.</summary>
    public void MarkLive(string key)
    {
        lock (_gate)
        {
            if (!State.Live.Contains(key))
                Save(State with { Live = [.. State.Live, key] });
        }
    }

    /// <summary>The hardware is back on defaults: nothing to reset and nothing to re-apply.</summary>
    public void MarkDefaults(string key)
    {
        lock (_gate)
        {
            var saved = new Dictionary<string, SavedTuning>(State.Saved);
            saved.Remove(key);
            Save(State with { Live = State.Live.Where(k => k != key).ToList(), Saved = saved });
        }
    }

    /// <summary>The user confirmed these settings, so they are re-applied after a clean start.</summary>
    public void SaveConfirmed(string key, SavedTuning tuning)
    {
        lock (_gate)
        {
            var saved = new Dictionary<string, SavedTuning>(State.Saved) { [key] = tuning };
            Save(State with { Saved = saved });
        }
    }

    public SavedTuning? Confirmed(string key)
    {
        lock (_gate)
        {
            return State.Saved.GetValueOrDefault(key);
        }
    }

    /// <summary>The user accepted this hardware's vendor warning; it stays accepted across restarts.</summary>
    public void AcceptWarning(string key)
    {
        lock (_gate)
        {
            if (!State.AcceptedWarnings.Contains(key))
                Save(State with { AcceptedWarnings = [.. State.AcceptedWarnings, key] });
        }
    }

    public bool WarningAccepted(string key)
    {
        lock (_gate)
        {
            return State.AcceptedWarnings.Contains(key);
        }
    }

    /// <summary>Game profiles stop tuning this hardware (after a crash) until <see cref="AllowProfiles"/>.</summary>
    public void BlockProfiles(string key)
    {
        lock (_gate)
        {
            if (!State.ProfilesBlocked.Contains(key))
                Save(State with { ProfilesBlocked = [.. State.ProfilesBlocked, key] });
        }
    }

    /// <summary>The user kept a tune on this hardware again, so game profiles may tune it.</summary>
    public void AllowProfiles(string key)
    {
        lock (_gate)
        {
            if (State.ProfilesBlocked.Contains(key))
                Save(State with { ProfilesBlocked = State.ProfilesBlocked.Where(k => k != key).ToList() });
        }
    }

    public bool ProfilesAllowed(string key)
    {
        lock (_gate)
        {
            return !State.ProfilesBlocked.Contains(key) && !State.ProfilesBlocked.Contains(EverythingKey);
        }
    }

    /// <summary>Forgets the confirmed settings without touching the hardware's live mark.</summary>
    public void Forget(string key)
    {
        lock (_gate)
        {
            var saved = new Dictionary<string, SavedTuning>(State.Saved);
            if (saved.Remove(key))
                Save(State with { Saved = saved });
        }
    }

    /// <summary>A clean stop: the next start may re-apply confirmed settings. Call last, after pending changes went back.</summary>
    public void EndSession()
    {
        lock (_gate)
        {
            Save(State with { SessionOpen = false });
        }
    }

    private void Save(TuningState state)
    {
        store.Save(state);
        _state = state;
    }
}
