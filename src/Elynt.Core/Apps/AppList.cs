using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Elynt.Core.Apps;

/// <summary>One app in a saved app list: its exact winget id, and the name to show before it is installed.</summary>
public sealed record AppListEntry(string Id, string Name);

public sealed record AppListReadResult(bool Succeeded, string Message, IReadOnlyList<AppListEntry> Apps);

/// <summary>
/// The user's own app list file: a small JSON file they save on one PC and load on another to reinstall the same
/// apps. The UI reads and writes the file as the signed-in user; installing goes through the engine as usual,
/// where apps outside the catalogue are marked not reviewed.
/// </summary>
public static partial class AppList
{
    public const string FileExtension = ".elyntapps";
    private const string Format = "elynt-app-list";
    private const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// The list as file text, each app once. Apps winget only knows from Windows' installed-programs list (no
    /// winget id) can't be reinstalled, so they are left out.
    /// </summary>
    public static string Write(IEnumerable<AppListEntry> apps) =>
        JsonSerializer.Serialize(
            new FileShape(Format, Version, apps.Where(a => IsWingetId(a.Id)).DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToList()),
            Options);

    public static AppListReadResult Read(string text)
    {
        FileShape? file;
        try
        {
            file = JsonSerializer.Deserialize<FileShape>(text, Options);
        }
        catch (JsonException)
        {
            file = null;
        }
        if (file is null || file.Format != Format || file.Apps is null)
            return new(false, "This isn't an Elynt app list.", []);
        if (file.Version != Version)
            return new(false, $"This app list was saved by a newer version of the app (format {file.Version}). Update the app, then load it again.", []);

        var apps = file.Apps.Where(a => a is not null).ToList();
        var good = apps.Where(a => IsWingetId(a.Id))
            .Select(a => a with { Name = string.IsNullOrWhiteSpace(a.Name) ? a.Id : a.Name.Trim() })
            .DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var skipped = apps.Where(a => !IsWingetId(a.Id)).Select(a => string.IsNullOrWhiteSpace(a.Name) ? "(no name)" : a.Name).ToList();
        var message = $"{good.Count} {(good.Count == 1 ? "app" : "apps")} loaded.";
        if (skipped.Count > 0)
            message += $" Skipped {string.Join(", ", skipped)}: not a winget id.";
        return new(true, message, good);
    }

    /// <summary>Whether this looks like a winget package id (Publisher.Package, letters, digits and . - _ +).</summary>
    public static bool IsWingetId(string? id) => id is { Length: > 0 and <= 128 } && WingetId().IsMatch(id);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.+_-]*$")]
    private static partial Regex WingetId();

    private sealed record FileShape(
        [property: JsonPropertyName("format")] string? Format,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("apps")] List<AppListEntry>? Apps);
}
