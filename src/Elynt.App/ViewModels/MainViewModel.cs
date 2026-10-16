using System.Collections.ObjectModel;
using Elynt.Core;
using Elynt.Core.Apps;
using Elynt.Core.Drivers;
using Elynt.Core.Ipc;
using Elynt.Core.Repair;
using Elynt.Core.Setup;
using Elynt.Core.Tweaks;
using Elynt.Core.Updates;
using Serilog;

namespace Elynt.App.ViewModels;

/// <param name="confirmDestructive">
/// Asks the user, separately from flipping the switch, to confirm a change that can't be undone.
/// </param>
/// <param name="askToReboot">Shows the reboot prompt after a driver install that needs a restart.</param>
public sealed class MainViewModel(Func<TweakView, bool> confirmDestructive, Action<DriverInstallResult> askToReboot) : ObservableObject
{
    public const string PerGameProfiles = "Per-game profiles";
    public const string UndoHistory = "Undo history";

    private EnginePipeClient? _client;
    // Repairs hold their connection for minutes, so they get their own and switches keep working meanwhile.
    private EnginePipeClient? _repairClient;
    private IReadOnlyList<TweakItemViewModel> _allTweaks = [];
    private IReadOnlyList<RepairToolItemViewModel> _allRepairTools = [];
    private RepairToolItemViewModel? _selectedRepairTool;
    private string _selectedPage = Sections.Gaming;
    private TweakItemViewModel? _selectedTweak;
    private string _status = "Connecting to the Elynt Optimizer service…";
    private bool _isConnected;
    private bool _isConnecting;
    private string? _historyMessage;
    private bool _isRestoring;
    private readonly AppUpdater _updater = new();
    private string? _updateReady;
    private bool _installAllRuntimes;
    private bool _isInstalling;
    private string? _installStatus;
    private BiosPageViewModel? _bios;
    private DriversPageViewModel? _drivers;

    /// <summary>The sidebar: the ten sections, then per-game profiles and the undo history.</summary>
    public IReadOnlyList<string> Pages { get; } = [.. Sections.All, PerGameProfiles, UndoHistory];

    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = [];

    /// <summary>The Undo history page: every change the app made for this user, newest first.</summary>
    public ObservableCollection<HistoryItemViewModel> History { get; } = [];

    /// <summary>The app catalogue, in group order. The Apps page groups it by <see cref="AppItemViewModel.Group"/>.</summary>
    public ObservableCollection<AppItemViewModel> Apps { get; } = [];

    public bool IsAppsPage => SelectedPage == Sections.Apps;

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

    /// <summary>The Repair page's tools. Follow-ups (like fixing disk errors) are offered from a result instead.</summary>
    public ObservableCollection<RepairToolItemViewModel> RepairTools { get; } = [];

    public RepairToolItemViewModel? SelectedRepairTool
    {
        get => _selectedRepairTool;
        set
        {
            if (Set(ref _selectedRepairTool, value))
                OnPropertyChanged(nameof(HasRepairSelection));
        }
    }

    public bool HasRepairSelection => IsRepairPage && SelectedRepairTool is not null;

    public bool IsRepairPage => SelectedPage == Sections.Repair;

    public string SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (Set(ref _selectedPage, value))
            {
                OnPropertyChanged(nameof(IsHistoryPage));
                OnPropertyChanged(nameof(IsAppsPage));
                OnPropertyChanged(nameof(IsSectionPage));
                OnPropertyChanged(nameof(IsTweakPage));
                ShowPage();
            }
        }
    }

    public bool IsHistoryPage => SelectedPage == UndoHistory;

    /// <summary>Any section page (tweaks, Apps, Repair, BIOS), as opposed to the Undo history.</summary>
    public bool IsSectionPage => !IsHistoryPage;

    /// <summary>A section page that lists tweaks.</summary>
    public bool IsTweakPage => !IsHistoryPage && !IsAppsPage && !IsRepairPage;

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

    public bool HasSelection => IsTweakPage && SelectedTweak is not null;

    public bool IsEmpty => IsTweakPage && Tweaks.Count == 0 && !IsBiosPage && !IsDriversPage;

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

    /// <summary>The detected GPUs with driver lookup and install, shown on the Drivers page. Null until the service sends them.</summary>
    public DriversPageViewModel? Drivers
    {
        get => _drivers;
        private set
        {
            if (Set(ref _drivers, value))
                OnPropertyChanged(nameof(IsDriversPage));
        }
    }

    public bool IsDriversPage => Drivers is not null && SelectedPage == Sections.Drivers;

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
                OnPropertyChanged(nameof(CanRestoreEverything));
                OnPropertyChanged(nameof(CanInstall));
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
            var apps = await _client.ListAppsAsync();
            Apps.Clear();
            foreach (var app in apps)
                Apps.Add(new AppItemViewModel(app));
            _allRepairTools = (await _client.ListRepairToolsAsync()).Select(v => new RepairToolItemViewModel(v)).ToList();
            Bios = await LoadBiosAsync(_client);
            Drivers = await LoadDriversAsync(_client);
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

    private async Task<DriversPageViewModel?> LoadDriversAsync(EnginePipeClient client)
    {
        try
        {
            return new DriversPageViewModel(await client.GetDriversAsync(), () => _client, askToReboot);
        }
        catch (EngineServiceException ex)
        {
            Log.Warning(ex, "The service couldn't detect the GPUs");
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
        var repairClient = _repairClient;
        _repairClient = null;
        if (repairClient is not null)
            await repairClient.DisposeAsync();
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

        RepairTools.Clear();
        if (IsRepairPage)
        {
            foreach (var tool in _allRepairTools.Where(t => !t.View.IsFollowUp))
                RepairTools.Add(tool);
        }
        if (SelectedRepairTool is null || !RepairTools.Contains(SelectedRepairTool))
            SelectedRepairTool = RepairTools.FirstOrDefault();

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsRepairPage));
        OnPropertyChanged(nameof(IsTweakPage));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasRepairSelection));
        OnPropertyChanged(nameof(IsBiosPage));
        OnPropertyChanged(nameof(IsDriversPage));
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
    /// <summary>Runs a repair tool (or the follow-up its last result offered), showing progress on <paramref name="item"/>.</summary>
    /// <param name="confirmed">The user confirmed the tool's opt-in warning.</param>
    public async Task RunRepairAsync(RepairToolItemViewModel item, string toolId, bool confirmed)
    {
        if (_client is null || item.IsRunning)
            return;

        item.Start();
        try
        {
            _repairClient ??= await EnginePipeClient.ConnectAsync(timeout: TimeSpan.FromSeconds(3));
            // Progress<T> posts back to the window's thread.
            var result = await _repairClient.RunRepairAsync(toolId, confirmed, new Progress<RepairProgress>(item.Report));
            Log.Information("Repair {Tool}: {Succeeded} {Summary} (log: {Log})", toolId, result.Succeeded, result.Summary, result.LogLocation);
            item.Finish(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Running repair {Tool} failed", toolId);
            var repairClient = _repairClient;
            _repairClient = null;
            if (repairClient is not null)
                await repairClient.DisposeAsync();
            item.Finish(new RepairResult(toolId, false, $"Couldn't run this through the service: {ex.Message}", false, null, null, ""));
        }
    }

    public RepairToolView? FindRepairTool(string toolId) => _allRepairTools.FirstOrDefault(t => t.View.Id == toolId)?.View;

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
