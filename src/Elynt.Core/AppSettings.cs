using System.Text.Json;
using Elynt.Core.Updates;

namespace Elynt.Core;

/// <summary>The app window's own preferences for one user. A null value means the user hasn't chosen yet.</summary>
/// <param name="UpdateChannel">The release channel the app updates from.</param>
/// <param name="AnimatedAccents">The "Animated accents" switch on the Settings page.</param>
public sealed record AppSettings(ReleaseChannel? UpdateChannel = null, bool? AnimatedAccents = null)
{
    /// <summary>
    /// Whether the "Animated accents" switch is on: the user's saved choice, or else on unless Windows' animation
    /// effects are off, so a user who turned those off never has to find the switch.
    /// </summary>
    public bool AccentsAnimate(bool windowsAnimationsOn) => AnimatedAccents ?? windowsAnimationsOn;
}

/// <summary>The app's per-user settings file, a small JSON file read and replaced whole on every change.</summary>
public sealed class AppSettingsFile(string path)
{
    public string Path => path;

    /// <summary>The saved settings. A missing or damaged file reads as nothing chosen yet.</summary>
    public AppSettings Load()
    {
        if (!File.Exists(path))
            return new();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    /// <summary>Reads the file, changes one setting and writes it back, keeping the others.</summary>
    public void Update(Func<AppSettings, AppSettings> change) =>
        AtomicFile.Write(path, JsonSerializer.Serialize(change(Load())));
}
