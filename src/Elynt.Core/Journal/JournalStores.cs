using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elynt.Core.Journal;

public interface IJournalStore
{
    IReadOnlyList<JournalEntry> Load();

    /// <summary>Durably replaces the stored journal. Must not return before the data is on disk.</summary>
    void Save(IReadOnlyList<JournalEntry> entries);
}

public sealed class InMemoryJournalStore : IJournalStore
{
    private string _json = "[]";

    public IReadOnlyList<JournalEntry> Load() => JournalJson.Deserialize(_json);

    // Round-trips through JSON so tests exercise the same serialization as the file store.
    public void Save(IReadOnlyList<JournalEntry> entries) => _json = JournalJson.Serialize(entries);
}

/// <summary>The journal as a JSON file, replaced atomically on every save.</summary>
public sealed class JsonFileJournalStore(string path) : IJournalStore
{
    public IReadOnlyList<JournalEntry> Load() =>
        File.Exists(path) ? JournalJson.Deserialize(File.ReadAllText(path)) : [];

    public void Save(IReadOnlyList<JournalEntry> entries)
    {
        AtomicFile.Write(path, JournalJson.Serialize(entries));
    }
}

internal static class JournalJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IReadOnlyList<JournalEntry> entries) => JsonSerializer.Serialize(entries, Options);

    public static List<JournalEntry> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<JournalEntry>>(json, Options) ?? [];
}
