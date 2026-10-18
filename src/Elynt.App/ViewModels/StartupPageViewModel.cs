using System.Collections.ObjectModel;
using Elynt.Core.Ipc;
using Elynt.Core.Startup;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Startup list on the Startup &amp; Services page: every startup item with publisher, signature and impact, a
/// switch to turn each off like Task Manager does, and a separate, confirmed delete.
/// </summary>
/// <param name="client">The current connection to the service, or null while disconnected.</param>
public sealed class StartupPageViewModel(Func<EnginePipeClient?> client) : ObservableObject
{
    private string? _status;
    private bool _isLoading;

    public ObservableCollection<StartupItemViewModel> Items { get; } = [];

    /// <summary>Asks the user, separately, to confirm a delete that can't be undone. Set by the window.</summary>
    public Func<StartupItemView, bool> ConfirmDelete { get; set; } = _ => false;

    public string? Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public async Task LoadAsync()
    {
        if (client() is not { } engine || IsLoading)
            return;
        IsLoading = true;
        try
        {
            var items = await engine.ListStartupItemsAsync();
            Items.Clear();
            foreach (var item in items)
                Items.Add(new StartupItemViewModel(item, FlipAsync));
            Status = items.Count == 0 ? "Nothing starts at sign-in." : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading startup items failed");
            Status = $"Couldn't read the startup items: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Turns one item off (or on) by its id, as the tray note's "turn it off" does.</summary>
    public async Task<StartupCommandResult?> SetEnabledAsync(string itemId, bool enabled)
    {
        if (client() is not { } engine)
            return null;
        var result = await engine.SetStartupItemEnabledAsync(itemId, enabled);
        Log.Information("Startup item {Item} enabled={Enabled}: {Succeeded} {Message}", itemId, enabled, result.Succeeded, result.Message);
        if (Items.FirstOrDefault(i => i.View.Id == itemId) is { } shown && result.Item is not null)
        {
            shown.View = result.Item;
            shown.Message = result.Message;
        }
        return result;
    }

    public async Task DeleteAsync(StartupItemViewModel item)
    {
        if (client() is not { } engine || !item.CanDelete)
            return;
        if (!ConfirmDelete(item.View))
        {
            item.Message = "Not deleted.";
            return;
        }

        item.IsBusy = true;
        try
        {
            var result = await engine.DeleteStartupItemAsync(item.View.Id, confirmDestructive: true);
            Log.Information("Delete startup item {Item}: {Succeeded} {Message}", item.View.Id, result.Succeeded, result.Message);
            if (result.Succeeded)
            {
                Items.Remove(item);
                Status = result.Message;
            }
            else
            {
                item.Message = result.Message;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Deleting startup item {Item} failed", item.View.Id);
            item.Message = $"Couldn't do this: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    /// <summary>Items added since the last look, for the tray note. Empty while disconnected or on failure.</summary>
    public async Task<IReadOnlyList<StartupItemView>> CheckNewAsync()
    {
        if (client() is not { } engine)
            return [];
        try
        {
            return await engine.CheckNewStartupItemsAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Checking for new startup items failed");
            return [];
        }
    }

    private async Task FlipAsync(StartupItemViewModel item, bool turnOn)
    {
        item.IsBusy = true;
        try
        {
            var result = await SetEnabledAsync(item.View.Id, turnOn);
            if (result is not null)
                item.Message = result.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Switching startup item {Item} failed", item.View.Id);
            item.Message = $"Couldn't do this: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
            item.RefreshSwitch();
        }
    }
}
