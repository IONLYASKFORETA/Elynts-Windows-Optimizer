using System.Collections.ObjectModel;
using Elynt.Core.Drift;
using Elynt.Core.Ipc;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>One change Windows undid, with a tick to pick it for re-applying.</summary>
public sealed class DriftItemViewModel(DriftItem item) : ObservableObject
{
    private bool _ticked = true;

    public DriftItem Item { get; } = item;

    public string Name => Item.Name;

    public string Detail =>
        (Item.Kind == DriftKind.AppReturned ? "Came back with an update. " : "Windows changed this back. ") +
        string.Join("; ", Item.Changes) +
        (Item.NeedsReboot ? " Takes effect after a restart." : "") +
        (Item.NeedsConfirmation ? " Can't be undone, so it asks first." : "");

    public bool Ticked
    {
        get => _ticked;
        set => Set(ref _ticked, value);
    }
}

/// <summary>
/// What Windows undid, shown at the top of the Undo history: re-apply all, or tick and re-apply some. Also the
/// automatic re-apply switch (off by default; destructive and reboot-needing changes still ask).
/// </summary>
public sealed class DriftViewModel(Func<EnginePipeClient?> client, Func<string, bool> confirmDestructive, Func<Task> afterChange) : ObservableObject
{
    private string? _message;
    private bool _autoReapply;
    private bool _settingsLoaded;
    private bool _isBusy;

    public ObservableCollection<DriftItemViewModel> Items { get; } = [];

    public bool HasDrift => Items.Count > 0;

    public string? Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                OnPropertyChanged(nameof(CanReapply));
        }
    }

    public bool CanReapply => !IsBusy && HasDrift;

    public bool AutoReapply
    {
        get => _autoReapply;
        set
        {
            if (Set(ref _autoReapply, value) && _settingsLoaded)
                _ = SaveAutoReapplyAsync(value);
        }
    }

    /// <summary>The tray note when a Windows update undid changes. On by default; saved per user.</summary>
    public bool NotifyDrift
    {
        get => TrayNoteSettings.Current.Drift;
        set
        {
            TrayNoteSettings.Current.Drift = value;
            TrayNoteSettings.Current.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Runs a drift check and shows what it found. Returns the report for the tray note, or null when it failed.</summary>
    public async Task<DriftReport?> CheckAsync()
    {
        if (client() is not { } c)
            return null;
        try
        {
            _autoReapply = (await c.GetDriftSettingsAsync()).AutoReapply;
            _settingsLoaded = true;
            OnPropertyChanged(nameof(AutoReapply));

            var report = await c.CheckDriftAsync();
            Show(report.Drifted);
            Message = report.Drifted.Count > 0 || report.Reapplied.Count > 0 || report.NewlyUnsupported.Count > 0 ? report.Message : null;
            if (report.Reapplied.Count > 0)
                await afterChange();
            return report;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Drift check failed");
            return null;
        }
    }

    public Task ReapplyAllAsync() => ReapplyAsync(Items.ToList());

    public Task ReapplyTickedAsync() => ReapplyAsync(Items.Where(i => i.Ticked).ToList());

    private async Task ReapplyAsync(IReadOnlyList<DriftItemViewModel> picked)
    {
        if (client() is not { } c || picked.Count == 0 || IsBusy)
            return;

        var destructive = picked.Where(i => i.Item.NeedsConfirmation).Select(i => i.Name).ToList();
        var confirmed = destructive.Count > 0 && confirmDestructive(string.Join("\n", destructive));

        IsBusy = true;
        try
        {
            var result = await c.ReapplyDriftAsync(picked.Select(i => i.Item.TweakId).ToList(), confirmed);
            var problems = result.Changes.Where(o => !o.Succeeded).Select(o => $"{o.Name}: {o.Message}");
            Message = string.Join(Environment.NewLine, [result.Message, .. problems]);
            var done = result.Changes.Where(o => o.Succeeded).Select(o => o.TweakId).ToHashSet();
            foreach (var item in Items.Where(i => done.Contains(i.Item.TweakId)).ToList())
                Items.Remove(item);
            OnPropertyChanged(nameof(HasDrift));
            OnPropertyChanged(nameof(CanReapply));
            await afterChange();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Re-applying drift failed");
            Message = $"Couldn't do this: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(IReadOnlyList<DriftItem> drifted)
    {
        Items.Clear();
        foreach (var item in drifted)
            Items.Add(new DriftItemViewModel(item));
        OnPropertyChanged(nameof(HasDrift));
        OnPropertyChanged(nameof(CanReapply));
    }

    private async Task SaveAutoReapplyAsync(bool enabled)
    {
        if (client() is not { } c)
            return;
        try
        {
            await c.SetAutoReapplyAsync(enabled);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't save the automatic re-apply setting");
            Message = $"Couldn't save that: {ex.Message}";
        }
    }
}
