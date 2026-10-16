using System.Collections.ObjectModel;
using Elynt.Core;
using Elynt.Core.Ipc;
using Elynt.Core.Setup;
using Elynt.Core.Tweaks;
using Elynt.Core.Updates;
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
    private readonly AppUpdater _updater = new();
    private string? _updateReady;

    /// <summary>The sidebar: the ten sections, then per-game profiles and the undo history.</summary>
    public IReadOnlyList<string> Pages { get; } = [.. Sections.All, PerGameProfiles, UndoHistory];

    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = [];

    public string SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (Set(ref _selectedPage, value))
                ShowPage();
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
                OnPropertyChanged(nameof(IsDisconnected));
        }
    }

    public bool IsDisconnected => !IsConnected;

    public IReadOnlyList<ReleaseChannel> Channels { get; } = Enum.GetValues<ReleaseChannel>();

    /// <summary>Stable for friends; Beta gets every build first.</summary>
    public ReleaseChannel Channel
    {
        get => _updater.Channel;
        set
        {
            if (value == _updater.Channel)
                return;
            _updater.ChangeChannel(value);
            UpdateReady = null;
            OnPropertyChanged();
            _ = CheckForUpdatesAsync();
        }
    }

    /// <summary>The version downloaded and waiting for a restart, or null.</summary>
    public string? UpdateReady
    {
        get => _updateReady;
        private set
        {
            if (Set(ref _updateReady, value))
                OnPropertyChanged(nameof(HasUpdateReady));
        }
    }

    public bool HasUpdateReady => UpdateReady is not null;

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            UpdateReady = await _updater.DownloadUpdateAsync();
        }
        catch (Exception ex)
        {
            // Offline or GitHub unreachable: try again next start.
            Log.Warning(ex, "Couldn't check for updates");
        }
    }

    public void RestartIntoUpdate() => _updater.RestartIntoUpdate();

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
            if (!await EnsureServiceAsync())
                return;
            _client = await EnginePipeClient.ConnectAsync(timeout: TimeSpan.FromSeconds(3));
            var views = await _client.ListTweaksAsync();
            _allTweaks = views.Select(v => new TweakItemViewModel(v, FlipAsync)).ToList();
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

    /// <summary>
    /// Installs, updates or restarts the background service when it needs it. That takes admin rights once, so
    /// Windows shows a UAC prompt. False when the user declined or setup failed.
    /// </summary>
    private async Task<bool> EnsureServiceAsync()
    {
        var state = await Task.Run(ServiceBootstrap.Check);
        if (!ServiceBootstrap.NeedsSetup(state))
            return true;

        Status = state switch
        {
            ServiceSetupState.NotInstalled => "Installing the background service. Windows will ask for permission once.",
            ServiceSetupState.Outdated => "Updating the background service to match this version. Windows will ask for permission.",
            _ => "Starting the background service. Windows will ask for permission.",
        };
        if (await ServiceBootstrap.SetUpElevatedAsync())
            return true;

        Status = "The background service isn't set up, so nothing can be changed yet. It needs admin rights once: " +
                 "press Retry and choose Yes when Windows asks.";
        return false;
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
