using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Elynt.App.ViewModels;
using Elynt.Core.Drift;
using Elynt.Core.Startup;
using Serilog;
using Forms = System.Windows.Forms;

namespace Elynt.App;

/// <summary>Which kinds of tray note the user wants. Each kind can be turned off; saved per user.</summary>
public sealed class TrayNoteSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElyntOptimizer", "tray-notes.json");

    /// <summary>A note when an app adds itself to startup.</summary>
    public bool NewStartupItems { get; set; } = true;

    /// <summary>A note when a Windows update undid changes the app made.</summary>
    public bool Drift { get; set; } = true;

    /// <summary>The one copy the tray and the pages share.</summary>
    public static TrayNoteSettings Current { get; } = Load();

    private static TrayNoteSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<TrayNoteSettings>(File.ReadAllText(FilePath)) ?? new() : new();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't read the tray note settings; using the defaults");
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't save the tray note settings");
        }
    }
}

/// <summary>
/// The tray icon and its notes. Every minute it asks the service which startup items were added since the last look
/// and, when there are any, shows a note; clicking the note turns them off (reversibly, like Task Manager).
/// </summary>
public sealed class TrayNotes : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    private readonly Forms.NotifyIcon _icon;
    private readonly DispatcherTimer _timer;
    private readonly StartupPageViewModel _startup;
    private IReadOnlyList<StartupItemView> _shown = [];
    private Action? _driftClicked;

    public TrayNotes(StartupPageViewModel startup, Action showWindow)
    {
        _startup = startup;
        _icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Elynt's Windows Optimizer",
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => showWindow();
        _icon.BalloonTipClicked += async (_, _) =>
        {
            // A click acts on the note last shown.
            if (_driftClicked is { } openDrift)
            {
                _driftClicked = null;
                openDrift();
                return;
            }
            await TurnOffShownAsync();
        };
        _timer = new DispatcherTimer { Interval = CheckEvery };
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
    }

    /// <summary>Asks once now (the first look after connecting only takes note of what is there).</summary>
    public async Task CheckAsync()
    {
        var added = await _startup.CheckNewAsync();
        if (added.Count == 0 || !TrayNoteSettings.Current.NewStartupItems)
            return;

        _shown = added;
        _driftClicked = null;
        var names = string.Join(", ", added.Select(i => i.Name));
        var text = added.Count == 1
            ? $"{names} added itself to startup. Click here to turn it off."
            : $"{added.Count} apps added themselves to startup: {names}. Click here to turn them off.";
        _icon.ShowBalloonTip(15_000, "New startup item", text, Forms.ToolTipIcon.Info);
    }

    /// <summary>
    /// A note listing what Windows undid, or what was put back automatically. Clicking it opens the list, where the
    /// user re-applies all or picks.
    /// </summary>
    public void ShowDrift(DriftReport report, Action openDrift)
    {
        if (!TrayNoteSettings.Current.Drift)
            return;
        if (report.Drifted.Count > 0)
        {
            _driftClicked = openDrift;
            _icon.ShowBalloonTip(15_000, "Windows undid some changes", $"{report.Message} Click here to choose.", Forms.ToolTipIcon.Info);
        }
        else if (report.Reapplied.Count > 0 || report.NewlyUnsupported.Count > 0)
        {
            _driftClicked = openDrift;
            _icon.ShowBalloonTip(10_000, "After a Windows update", report.Message, Forms.ToolTipIcon.Info);
        }
    }

    private async Task TurnOffShownAsync()
    {
        var shown = _shown;
        _shown = [];
        var failed = new List<string>();
        foreach (var item in shown)
        {
            try
            {
                var result = await _startup.SetEnabledAsync(item.Id, enabled: false);
                if (result is not { Succeeded: true })
                    failed.Add(item.Name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Turning off new startup item {Item} failed", item.Id);
                failed.Add(item.Name);
            }
        }

        var message = failed.Count == 0
            ? "Turned off. Switch it back on any time on the Startup & Services page."
            : $"Couldn't turn off {string.Join(", ", failed)}. See the Startup & Services page.";
        _icon.ShowBalloonTip(5_000, "Startup", message, failed.Count == 0 ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
    }

    public void Dispose()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
