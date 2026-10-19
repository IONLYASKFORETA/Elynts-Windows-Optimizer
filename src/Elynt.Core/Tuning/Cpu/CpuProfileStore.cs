using System.Text.Json;

namespace Elynt.Core.Tuning.Cpu;

/// <summary>What the service remembers about CPU tuning across restarts.</summary>
/// <param name="Kept">The confirmed tune.</param>
/// <param name="StressPassedFor">The tune that last passed the stress test.</param>
/// <param name="AutoApply">Re-apply <see cref="Kept"/> after sign-in and resume.</param>
/// <param name="Strikes">Unclean shutdowns or WHEA errors while the tune was on.</param>
/// <param name="Disabled">Two strikes: no re-apply until a new tune passes the stress test.</param>
/// <param name="LastAppliedAt">When the app last put a tune on the CPU; crashes and WHEA errors after it count.</param>
/// <param name="Stock">The BIOS limits read before any change of ours: the ceiling for every limit.</param>
public sealed record CpuProfile(
    CpuTune? Kept = null,
    CpuTune? StressPassedFor = null,
    bool AutoApply = false,
    int Strikes = 0,
    bool Disabled = false,
    DateTimeOffset? LastAppliedAt = null,
    CpuTune? Stock = null);

public interface ICpuProfileStore
{
    CpuProfile Load();

    void Save(CpuProfile profile);
}

public sealed class InMemoryCpuProfileStore : ICpuProfileStore
{
    private string _json = JsonSerializer.Serialize(new CpuProfile());

    public CpuProfile Load() => JsonSerializer.Deserialize<CpuProfile>(_json) ?? new CpuProfile();

    // Round-trips through JSON so tests exercise the same serialization as the file store.
    public void Save(CpuProfile profile) => _json = JsonSerializer.Serialize(profile);
}

/// <summary>The CPU profile as a JSON file, replaced atomically on every save.</summary>
public sealed class JsonFileCpuProfileStore(string path) : ICpuProfileStore
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public CpuProfile Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<CpuProfile>(File.ReadAllText(path)) ?? new CpuProfile() : new CpuProfile();
        }
        catch (JsonException)
        {
            // A damaged file: forget the tune rather than re-apply something we can't read.
            return new CpuProfile();
        }
    }

    public void Save(CpuProfile profile)
    {
        AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(profile, Indented));
    }
}
