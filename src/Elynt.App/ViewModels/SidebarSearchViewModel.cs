using System.Collections.ObjectModel;
using Elynt.Core.Ipc;
using Elynt.Core.Search;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The search box at the top of the sidebar: what was typed, the tweaks, repair tools and apps the service found for
/// it, best first, and opening one on its page with its row selected.
/// </summary>
/// <param name="client">The current connection to the service, or null while disconnected.</param>
/// <param name="open">Opens a page with a row selected: the window's model does it.</param>
public sealed class SidebarSearchViewModel(Func<EnginePipeClient?> client, Action<PageTarget> open) : ObservableObject
{
    private string _query = "";
    private bool _isOpen;
    private string? _message;

    /// <summary>What is typed in the box. Each change asks the service again.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (Set(ref _query, value))
                Searching = SearchAsync(value);
        }
    }

    /// <summary>What the service found for <see cref="Query"/>, best match first.</summary>
    public ObservableCollection<SearchResult> Results { get; } = [];

    /// <summary>Said in place of the results when there are none to show: why, in plain words. Null otherwise.</summary>
    public string? Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    /// <summary>Whether the results show: from typing until a result is chosen or the user clicks away.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set => Set(ref _isOpen, value);
    }

    /// <summary>The search the last change to <see cref="Query"/> started.</summary>
    internal Task Searching { get; private set; } = Task.CompletedTask;

    /// <summary>Opens a result's page with its row selected, and empties the box.</summary>
    public void Choose(SearchResult result)
    {
        Query = "";
        open(PageTarget.Of(result));
    }

    private async Task SearchAsync(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            Results.Clear();
            Message = null;
            IsOpen = false;
            return;
        }
        IsOpen = true;
        if (client() is not { } connection)
        {
            Show([], "Search needs the service, which isn't connected. Press Retry at the bottom of the sidebar.");
            return;
        }
        try
        {
            var found = await connection.SearchAsync(query);
            // Typed on (or emptied) meanwhile: the newer query's answer is the one to show.
            if (query != Query.Trim())
                return;
            Show(found, found.Count == 0 ? $"No tweak, tool or app matches \"{query}\"." : null);
        }
        catch (EngineServiceException ex)
        {
            Log.Error(ex, "The service couldn't search for {Query}", query);
            Show([], $"The service couldn't search: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Searching for {Query} couldn't reach the service", query);
            Show([], "Search couldn't reach the service. Press Retry at the bottom of the sidebar, then search again.");
        }
    }

    private void Show(IReadOnlyList<SearchResult> found, string? message)
    {
        Results.Clear();
        foreach (var result in found)
            Results.Add(result);
        Message = message;
    }
}
