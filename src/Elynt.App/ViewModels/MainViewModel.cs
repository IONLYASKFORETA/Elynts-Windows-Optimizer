using System.Collections.ObjectModel;
using Elynt.Core;
using Elynt.Core.Ipc;
using Elynt.Core.Repair;
using Elynt.Core.Tweaks;
using Serilog;

namespace Elynt.App.ViewModels;

public sealed class MainViewModel : ObservableObject
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

    /// <summary>The sidebar: the ten sections, then per-game profiles and the undo history.</summary>
    public IReadOnlyList<string> Pages { get; } = [.. Sections.All, PerGameProfiles, UndoHistory];

    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = [];

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

    public bool IsTweakPage => !IsRepairPage;

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

    public bool HasSelection => IsTweakPage && SelectedTweak is not null;

    public bool IsEmpty => IsTweakPage && Tweaks.Count == 0;

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
            _allRepairTools = (await _client.ListRepairToolsAsync()).Select(v => new RepairToolItemViewModel(v)).ToList();
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
        var repairClient = _repairClient;
        _repairClient = null;
        if (repairClient is not null)
            await repairClient.DisposeAsync();
    }

    private void ShowPage()
    {
        Tweaks.Clear();
        foreach (var tweak in _allTweaks.Where(t => t.View.Section == SelectedPage))
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
    }

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
