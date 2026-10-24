using System.IO;
using Elynt.Core;
using Serilog;

namespace Elynt.App;

/// <summary>
/// This user's app settings (the release channel and Animated accents), in %LocalAppData%\ElyntOptimizer\settings.json.
/// A file that can't be read or written only costs the user their choices, so failures are logged, not shown.
/// </summary>
public static class UserSettings
{
    /// <summary>The settings file. The screenshot tour points it at a throwaway file so it never changes the user's own.</summary>
    public static AppSettingsFile File { get; set; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElyntOptimizer", "settings.json"));

    public static AppSettings Load()
    {
        try
        {
            return File.Load();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't read {File}", File.Path);
            return new();
        }
    }

    /// <summary>Saves one change, keeping the other settings.</summary>
    public static void Update(Func<AppSettings, AppSettings> change)
    {
        try
        {
            File.Update(change);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't save {File}", File.Path);
        }
    }
}
