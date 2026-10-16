using System.Collections.ObjectModel;
using Elynt.Core;
using Elynt.Core.Apps;
using Elynt.Core.Ipc;
using Elynt.Core.Tweaks;
using Serilog;

namespace Elynt.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public const string PerGameProfiles = "Per-game profiles";
    public const string UndoHistory = "Undo history";

    private EnginePipeClient? _client;
    private IReadOnlyList<TweakItemViewModel> _allTweaks = [];
    private string _selectedPage = Sections.Gaming;
    private TweakItemViewModel? _selectedTweak;
    private string _status = "Connecting to the Elynt Optimizer service…";
    private bool _isConnected;
    private bool _isConnecting;
    private bool _installAllRuntimes;
    private bool _isInstalling;
    private string? _installStatus;

    /// <summary>The sidebar: the ten sections, then per-game profiles and the undo history.</summary>
    public IReadOnlyList<string> Pages { get; } = [.. Sections.All, PerGameProfiles, UndoHistory];

    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = [];

    /// <summary>The app catalogue, in group order. The Apps page groups it by <see cref="AppItemViewModel.Group"/>.</summary>
    public ObservableCollection<AppItemViewModel> Apps { get; } = [];

    public bool IsAppsPage => SelectedPage == Sections.Apps;

    public bool IsTweakPage => !IsAppsPage;

    /// <summary>Adds every Visual C++, DirectX legacy and .NET runtime to the install queue.</summary>
    public bool InstallAllRuntimes
    {
        get => _installAllRuntimes;
        set => Set(ref _installAllRuntimes, value);
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (Set(ref _isInstalling, value))
                OnPropertyChanged(nameof(CanInstall));
        }
    }

    public bool CanInstall => IsConnected && !IsInstalling;

    /// <summary>What the install queue is doing now, or how it ended.</summary>
    public string? InstallStatus
    {
        get => _installStatus;
        private set => Set(ref _installStatus, value);
    }

    public string SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (Set(ref _selectedPage, value))
            {
                OnPropertyChanged(nameof(IsAppsPage));
                OnPropertyChanged(nameof(IsTweakPage));
                ShowPage();
            }
        }
    }

    public TweakItemViewModel? SelectedTweak
    {
        get => _selectedTweak;
        set
        {
            if (Set(ref _selectedTweak, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => SelectedTweak is not null;

    public bool IsEmpty => Tweaks.Count == 0;

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (Set(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(IsDisconnected));
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    public bool IsDisconnected => !IsConnected;

    public async Task ConnectAsync()
    {
        // Loaded and Retry can both ask; only one connect runs at a time.
        if (_isConnecting)
            return;
        _isConnecting = true;
        Status = "Connecting to the Elynt Optimizer service…";
        try
        {
            await DisconnectAsync();
            _client = await EnginePipeClient.ConnectAsync(timeout: TimeSpan.FromSeconds(3));
            var views = await _client.ListTweaksAsync();
            _allTweaks = views.Select(v => new TweakItemViewModel(v, FlipAsync)).ToList();
            var apps = await _client.ListAppsAsync();
            Apps.Clear();
            foreach (var app in apps)
                Apps.Add(new AppItemViewModel(app));
            IsConnected = true;
            Status = "Connected. Every switch shows the setting as it is on this PC right now.";
            ShowPage();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't reach the service");
            await DisconnectAsync();
            Status = "The Elynt Optimizer service isn't running, so nothing can be changed yet. Start it, then press Retry.";
        }
        finally
        {
            _isConnecting = false;
        }
    }

    private async Task DisconnectAsync()
    {
        IsConnected = false;
        var client = _client;
        _client = null;
        if (client is not null)
            await client.DisposeAsync();
    }

    private void ShowPage()
    {
        Tweaks.Clear();
        foreach (var tweak in _allTweaks.Where(t => t.View.Section == SelectedPage))
            Tweaks.Add(tweak);
        SelectedTweak = Tweaks.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Installs the ticked apps one at a time, showing each app's progress as the service reports it.</summary>
    public async Task InstallTickedAsync()
    {
        if (_client is null || IsInstalling)
            return;

        var ticked = Apps.Where(a => a.IsTicked).ToList();
        var byId = Apps.ToDictionary(a => a.View.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var app in Apps)
            app.Status = null;
        foreach (var app in ticked)
            app.Status = "Queued";
        if (InstallAllRuntimes)
            foreach (var app in Apps.Where(a => a.View.InAllRuntimes))
                app.Status = "Queued";

        IsInstalling = true;
        InstallStatus = "Starting…";
        // Progress<T> brings each event back to the UI thread.
        var progress = new Progress<InstallProgress>(p =>
        {
            InstallStatus = p.Position > 0 ? $"{p.Position} of {p.Total}: {p.Message}" : p.Message;
            if (p.AppId is not null && byId.TryGetValue(p.AppId, out var item))
                item.Status = StatusText(p.Stage, p.Percent);
        });
        try
        {
            var report = await _client.InstallAppsAsync(ticked.Select(a => a.View.Id).ToList(), InstallAllRuntimes, progress);
            Log.Information("Install queue: {Succeeded} {Message}", report.Succeeded, report.Message);
            foreach (var outcome in report.Apps)
                if (byId.TryGetValue(outcome.AppId, out var item))
                    item.Status = outcome.Stage == InstallStage.Failed ? outcome.Message : StatusText(outcome.Stage, null);
            foreach (var app in Apps.Where(a => a.Status == "Queued"))
                app.Status = null;
            InstallStatus = report.Message;
            if (report.Succeeded)
            {
                foreach (var app in ticked)
                    app.IsTicked = false;
                InstallAllRuntimes = false;
            }
        }
        catch (EngineServiceException ex)
        {
            Log.Error(ex, "The service couldn't run the install queue");
            InstallStatus = $"The service couldn't do this: {ex.Message}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Installing apps failed");
            InstallStatus = $"Couldn't reach the service: {ex.Message}";
            await DisconnectAsync();
            Status = "Lost the connection to the service. Press Retry.";
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private static string StatusText(InstallStage stage, double? percent) => stage switch
    {
        InstallStage.Installing => percent is { } p ? $"Installing… {p:P0}" : "Installing…",
        InstallStage.Installed => "Installed",
        InstallStage.AlreadyInstalled => "Already installed",
        InstallStage.Failed => "Didn't install",
        _ => "",
    };

    private async Task FlipAsync(TweakItemViewModel item, bool turnOn)
    {
        if (_client is null)
            return;

        item.IsBusy = true;
        try
        {
            var result = turnOn ? await _client.ApplyAsync(item.View.Id) : await _client.RevertAsync(item.View.Id);
            Log.Information("{Action} {Tweak}: {Succeeded} {Message}", turnOn ? "Apply" : "Revert", item.View.Id, result.Succeeded, result.Message);
            if (result.Tweak is not null)
                item.View = result.Tweak;
            item.Message = result.Message;

            if (result.Succeeded && item.View.Refresh == SessionRefresh.MouseSettings)
                MouseSettings.ReloadFromRegistry();
        }
        catch (EngineServiceException ex)
        {
            // The service answered, so the connection is fine; the command itself failed.
            Log.Error(ex, "The service couldn't run the command for {Tweak}", item.View.Id);
            item.Message = $"The service couldn't do this: {ex.Message}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Flipping {Tweak} failed", item.View.Id);
            item.Message = $"Couldn't reach the service: {ex.Message}";
            await DisconnectAsync();
            Status = "Lost the connection to the service. Press Retry.";
        }
        finally
        {
            item.IsBusy = false;
            item.RefreshSwitch();
        }
    }
}
