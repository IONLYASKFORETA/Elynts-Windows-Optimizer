using System.Collections.ObjectModel;
using Elynt.Core;
using Elynt.Core.Ipc;
using Elynt.Core.Tweaks;
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
        // Tweaks that fit this PC first; not-applicable ones sink to the bottom.
        foreach (var tweak in _allTweaks.Where(t => t.View.Section == SelectedPage).OrderBy(t => t.IsNotApplicable))
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
