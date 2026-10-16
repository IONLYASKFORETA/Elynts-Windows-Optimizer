using System.Collections.ObjectModel;
using Elynt.Core;
using Elynt.Core.Ipc;
using Elynt.Core.Setup;
using Elynt.Core.Tweaks;
using Elynt.Core.Updates;
using Serilog;

namespace Elynt.App.ViewModels;

/// <param name="confirmDestructive">
/// Asks the user, separately from flipping the switch, to confirm a change that can't be undone.
/// </param>
public sealed class MainViewModel(Func<TweakView, bool> confirmDestructive) : ObservableObject
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
    private string? _historyMessage;
    private bool _isRestoring;
    private readonly AppUpdater _updater = new();
    private string? _updateReady;
    private BiosPageViewModel? _bios;
    private WindowsUpdatePageViewModel? _windowsUpdate;

    /// <summary>The sidebar: the ten sections, then per-game profiles and the undo history.</summary>
    public IReadOnlyList<string> Pages { get; } = [.. Sections.All, PerGameProfiles, UndoHistory];

    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = [];

    /// <summary>The Undo history page: every change the app made for this user, newest first.</summary>
    public ObservableCollection<HistoryItemViewModel> History { get; } = [];

    public string SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (Set(ref _selectedPage, value))
            {
                OnPropertyChanged(nameof(IsHistoryPage));
                OnPropertyChanged(nameof(IsTweakPage));
                ShowPage();
            }
        }
    }

    public bool IsHistoryPage => SelectedPage == UndoHistory;

    public bool IsTweakPage => !IsHistoryPage;

    public bool IsHistoryEmpty => History.Count == 0;

    /// <summary>The answer to the last restore everything, shown above the history.</summary>
    public string? HistoryMessage
    {
        get => _historyMessage;
        private set => Set(ref _historyMessage, value);
    }

    public bool IsRestoring
    {
        get => _isRestoring;
        private set
        {
            if (Set(ref _isRestoring, value))
                OnPropertyChanged(nameof(CanRestoreEverything));
        }
    }

    public bool CanRestoreEverything => IsConnected && !IsRestoring && History.Any(h => h.Change.CanUndo);

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

    public bool IsEmpty => Tweaks.Count == 0 && !IsBiosPage;

    /// <summary>The BIOS readouts and guides, shown on the Overclocking page. Null until the service sends them.</summary>
    public BiosPageViewModel? Bios
    {
        get => _bios;
        private set
        {
            if (Set(ref _bios, value))
                OnPropertyChanged(nameof(IsBiosPage));
        }
    }

    public bool IsBiosPage => Bios is not null && SelectedPage == Sections.Overclocking;

    /// <summary>"Check and install now" with its progress, shown above the Windows Update tweaks.</summary>
    public WindowsUpdatePageViewModel WindowsUpdate => _windowsUpdate ??= new(() => _client);

    public bool IsWindowsUpdatePage => IsConnected && SelectedPage == Sections.WindowsUpdate;

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
                OnPropertyChanged(nameof(IsWindowsUpdatePage));
                OnPropertyChanged(nameof(CanRestoreEverything));
            }
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
            Bios = await LoadBiosAsync(_client);
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

    private static async Task<BiosPageViewModel?> LoadBiosAsync(EnginePipeClient client)
    {
        try
        {
            return new BiosPageViewModel(await client.GetBiosReportAsync());
        }
        catch (EngineServiceException ex)
        {
            Log.Warning(ex, "The service couldn't build the BIOS report");
            return null;
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

    /// <summary>Undoes one change from the history. A refusal (e.g. something else changed it since) shows on that row.</summary>
    public async Task UndoAsync(HistoryItemViewModel item)
    {
        if (_client is not { } client || !item.CanUndo)
            return;

        item.IsBusy = true;
        try
        {
            var result = await client.UndoChangeAsync(item.Change.Id);
            Log.Information("Undo change {Change} ({Tweak}): {Succeeded} {Message}", item.Change.Id, item.Change.TweakId, result.Succeeded, result.Message);
            HistoryMessage = null;
            if (result.Succeeded)
            {
                ReloadSessionSettings(result.Tweak?.Refresh);
                await ReloadAsync(client);
            }
            else
            {
                item.Message = result.Message;
            }
        }
        catch (Exception ex)
        {
            await CommandFailedAsync(ex, "Undoing a change");
            item.Message = $"Couldn't do this: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    /// <summary>Undoes every change still in place, newest first, and says which ones couldn't be undone and why.</summary>
    public async Task RestoreEverythingAsync()
    {
        if (_client is not { } client || IsRestoring)
            return;

        IsRestoring = true;
        try
        {
            var result = await client.RestoreEverythingAsync();
            Log.Information("Restore everything: {Succeeded} {Message}", result.Succeeded, result.Message);
            var problems = result.Changes.Where(c => !c.Succeeded).Select(c => $"{c.TweakName}: {c.Message}");
            HistoryMessage = string.Join(Environment.NewLine + Environment.NewLine, [result.Message, .. problems]);
            if (result.Changes.Any(c => c.Succeeded))
            {
                // Any of them may have been a session setting; re-reading is cheap.
                ReloadSessionSettings(SessionRefresh.MouseSettings);
            }
            await ReloadAsync(client);
        }
        catch (Exception ex)
        {
            await CommandFailedAsync(ex, "Restore everything");
            HistoryMessage = $"Couldn't do this: {ex.Message}";
        }
        finally
        {
            IsRestoring = false;
        }
    }

    /// <summary>Re-reads the switches and the history after an undo, so both show the PC as it is now.</summary>
    private async Task ReloadAsync(EnginePipeClient client)
    {
        var views = await client.ListTweaksAsync();
        _allTweaks = views.Select(v => new TweakItemViewModel(v, FlipAsync)).ToList();
        await LoadHistoryAsync(client);
        if (!IsHistoryPage)
            ShowPage();
    }

    private async Task LoadHistoryAsync(EnginePipeClient client)
    {
        var history = await client.ListHistoryAsync();
        History.Clear();
        foreach (var change in history)
            History.Add(new HistoryItemViewModel(change));
        OnPropertyChanged(nameof(IsHistoryEmpty));
        OnPropertyChanged(nameof(CanRestoreEverything));
    }

    private async Task ShowHistoryAsync()
    {
        if (_client is not { } client)
            return;
        try
        {
            await LoadHistoryAsync(client);
        }
        catch (Exception ex)
        {
            await CommandFailedAsync(ex, "Loading the undo history");
        }
    }

    private async Task CommandFailedAsync(Exception ex, string what)
    {
        if (ex is EngineServiceException)
        {
            Log.Error(ex, "{What}: the service couldn't run the command", what);
            return;
        }
        Log.Error(ex, "{What} failed", what);
        await DisconnectAsync();
        Status = "Lost the connection to the service. Press Retry.";
    }

    private static void ReloadSessionSettings(SessionRefresh? refresh)
    {
        if (refresh == SessionRefresh.MouseSettings)
            MouseSettings.ReloadFromRegistry();
    }

    private void ShowPage()
    {
        if (IsHistoryPage)
        {
            HistoryMessage = null;
            _ = ShowHistoryAsync();
        }

        Tweaks.Clear();
        // Tweaks that fit this PC first; not-applicable ones sink to the bottom.
        foreach (var tweak in _allTweaks.Where(t => t.View.Section == SelectedPage).OrderBy(t => t.IsNotApplicable))
            Tweaks.Add(tweak);
        SelectedTweak = Tweaks.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsBiosPage));
        OnPropertyChanged(nameof(IsWindowsUpdatePage));
        if (IsWindowsUpdatePage)
            _ = WindowsUpdate.RefreshAsync();
    }

    private async Task FlipAsync(TweakItemViewModel item, bool turnOn)
    {
        if (_client is null)
            return;

        item.IsBusy = true;
        try
        {
            // The service refuses a destructive apply without this flag; the app only sets it after the user confirms.
            var confirmed = false;
            if (turnOn && item.View.Reversibility == Reversibility.Destructive)
            {
                confirmed = confirmDestructive(item.View);
                if (!confirmed)
                {
                    item.Message = "Not changed.";
                    return;
                }
            }

            var result = turnOn
                ? await _client.ApplyAsync(item.View.Id, new ApplyOptions(ConfirmDestructive: confirmed))
                : await _client.RevertAsync(item.View.Id);
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
