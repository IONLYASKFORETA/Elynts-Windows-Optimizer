using System.Collections.ObjectModel;
using Elynt.Core.Apps;
using Elynt.Core.Ipc;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>One winget search result, or an app loaded from an app list: a tick to install it, and a "not reviewed" mark.</summary>
public sealed class SearchResultViewModel(string id, string name, string? version, bool reviewed, string? description) : ObservableObject
{
    private bool _isTicked;
    private string? _status;

    public string Id { get; } = id;
    public string Name { get; } = name;
    public bool Reviewed { get; } = reviewed;
    public bool NotReviewed => !Reviewed;

    /// <summary>The catalogue's description, or the winget id and version for an app outside it.</summary>
    public string Details { get; } = description ?? (version is null ? id : $"{id} · {version}");

    public bool IsTicked
    {
        get => _isTicked;
        set => Set(ref _isTicked, value);
    }

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }
}

/// <summary>One installed app: update it (when an update is waiting) or uninstall it.</summary>
public sealed class InstalledAppViewModel(InstalledAppView view) : ObservableObject
{
    private string? _status;

    public InstalledAppView View { get; } = view;
    public string Name => View.Name;
    public bool NotReviewed => !View.Reviewed;
    public bool CanUpdate => View.CanUpdate;
    public string Details => View.CanUpdate ? $"{View.Id} · {View.Version} → {View.UpdateVersion}" : $"{View.Id} · {View.Version}";

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }
}

/// <summary>
/// The Apps page's search, installed-apps (updates and uninstalls) and app list parts. Every command goes to the
/// service; the app list file is read and written here, as the signed-in user.
/// </summary>
/// <param name="main">The window's model: its connection, its catalogue ticks and its shared "a queue is running" flag.</param>
/// <param name="confirmNotReviewed">Asks the user to accept installing the named apps that aren't in the reviewed catalogue.</param>
public sealed class AppsManagerViewModel(
    MainViewModel main,
    Func<IReadOnlyList<string>, bool> confirmNotReviewed,
    Func<string, string, bool> confirmUninstall) : ObservableObject
{
    private string _query = "";
    private string? _searchStatus;
    private string? _installedStatus;
    private bool _isSearching;

    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = [];

    public ObservableCollection<InstalledAppViewModel> InstalledApps { get; } = [];

    public string Query
    {
        get => _query;
        set => Set(ref _query, value);
    }

    public string? SearchStatus
    {
        get => _searchStatus;
        private set => Set(ref _searchStatus, value);
    }

    public string? InstalledStatus
    {
        get => _installedStatus;
        private set => Set(ref _installedStatus, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => Set(ref _isSearching, value);
    }

    public async Task SearchAsync()
    {
        if (main.Client is not { } client || IsSearching || string.IsNullOrWhiteSpace(Query))
            return;
        IsSearching = true;
        SearchStatus = "Searching winget…";
        try
        {
            var search = await client.SearchAppsAsync(Query);
            var ticked = SearchResults.Where(r => r.IsTicked).ToList();
            SearchResults.Clear();
            // Keep what was ticked from an earlier search or an app list, so it can all be installed together.
            foreach (var kept in ticked)
                SearchResults.Add(kept);
            foreach (var result in search.Results.Where(r => ticked.All(t => !t.Id.Equals(r.Id, StringComparison.OrdinalIgnoreCase))))
                SearchResults.Add(new SearchResultViewModel(result.Id, result.Name, result.Version, result.Reviewed, result.Description));
            SearchStatus = search.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Searching winget failed");
            SearchStatus = $"Couldn't search: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>Installs the ticked search results, after the user accepts any that aren't reviewed.</summary>
    public async Task InstallTickedResultsAsync()
    {
        var ticked = SearchResults.Where(r => r.IsTicked).ToList();
        if (ticked.Count == 0)
        {
            SearchStatus = "Nothing is ticked, so there is nothing to install.";
            return;
        }
        var notReviewed = ticked.Where(r => r.NotReviewed).Select(r => r.Name).ToList();
        if (notReviewed.Count > 0 && !confirmNotReviewed(notReviewed))
            return;

        var byId = ticked.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var result in ticked)
            result.Status = "Queued";
        var report = await RunQueueAsync(
            "install",
            (client, progress) => client.InstallAppsAsync(ticked.Select(r => r.Id).ToList(), allRuntimes: false, progress, allowNotReviewed: notReviewed.Count > 0),
            p => { if (p.AppId is { } id && byId.TryGetValue(id, out var item)) item.Status = p.Message; },
            message => SearchStatus = message);
        if (report is null)
            return;
        foreach (var outcome in report.Apps)
            if (byId.TryGetValue(outcome.AppId, out var item))
            {
                item.Status = outcome.Message;
                if (outcome.Stage != InstallStage.Failed)
                    item.IsTicked = false;
            }
    }

    public async Task RefreshInstalledAsync()
    {
        if (main.Client is not { } client)
            return;
        InstalledStatus = "Reading installed apps and updates from winget…";
        try
        {
            var installed = await client.ListInstalledAppsAsync();
            InstalledApps.Clear();
            foreach (var app in installed.Apps.OrderByDescending(a => a.CanUpdate).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
                InstalledApps.Add(new InstalledAppViewModel(app));
            InstalledStatus = installed.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Listing installed apps failed");
            InstalledStatus = $"Couldn't list installed apps: {ex.Message}";
        }
    }

    public Task UpdateAsync(InstalledAppViewModel app) =>
        RunOnInstalledAsync([app], "update", (client, progress) => client.UpdateAppsAsync([app.View.Id], progress));

    public Task UpdateAllAsync() =>
        RunOnInstalledAsync(InstalledApps.Where(a => a.CanUpdate).ToList(), "update", (client, progress) => client.UpdateAllAppsAsync(progress));

    public Task UninstallAsync(InstalledAppViewModel app) =>
        confirmUninstall(app.Name, app.NotReviewed ? "" : "It is in the reviewed catalogue, so you can install it again from there.")
            ? RunOnInstalledAsync([app], "uninstall", (client, progress) => client.UninstallAppsAsync([app.View.Id], progress))
            : Task.CompletedTask;

    private async Task RunOnInstalledAsync(
        IReadOnlyList<InstalledAppViewModel> apps, string what, Func<EnginePipeClient, IProgress<InstallProgress>, Task<InstallReport>> run)
    {
        var byId = apps.ToDictionary(a => a.View.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
            app.Status = "Queued";
        var report = await RunQueueAsync(
            what, run,
            p => { if (p.AppId is { } id && byId.TryGetValue(id, out var item)) item.Status = p.Message; },
            message => InstalledStatus = message);
        if (report is null)
            return;
        await RefreshInstalledAsync();
        InstalledStatus = report.Message;
    }

    /// <summary>Runs one queue on the service, showing progress, while the rest of the Apps page waits.</summary>
    private async Task<InstallReport?> RunQueueAsync(
        string what,
        Func<EnginePipeClient, IProgress<InstallProgress>, Task<InstallReport>> run,
        Action<InstallProgress> onProgress,
        Action<string> showStatus)
    {
        if (main.Client is not { } client || main.IsInstalling)
            return null;
        main.IsInstalling = true;
        showStatus("Starting…");
        var progress = new Progress<InstallProgress>(p =>
        {
            onProgress(p);
            showStatus(p.Position > 0 ? $"{p.Position} of {p.Total}: {p.Message}" : p.Message);
        });
        try
        {
            var report = await run(client, progress);
            Log.Information("App {What} queue: {Succeeded} {Message}", what, report.Succeeded, report.Message);
            showStatus(report.Message);
            return report;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The app {What} queue failed", what);
            showStatus($"The service couldn't do this: {ex.Message}");
            return null;
        }
        finally
        {
            main.IsInstalling = false;
        }
    }

    /// <summary>The ticked catalogue apps and ticked search results, as app list file text.</summary>
    public (string Text, int Count) BuildAppList()
    {
        var entries = main.Apps.Where(a => a.IsTicked).Select(a => new AppListEntry(a.View.Id, a.Name))
            .Concat(SearchResults.Where(r => r.IsTicked).Select(r => new AppListEntry(r.Id, r.Name)))
            .DistinctBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (AppList.Write(entries), entries.Count);
    }

    /// <summary>
    /// Ticks every app in a loaded list: catalogue apps on the catalogue, the rest as not-reviewed search results,
    /// ready to install with the usual buttons.
    /// </summary>
    public string LoadAppList(string text)
    {
        var list = AppList.Read(text);
        if (!list.Succeeded)
            return list.Message;
        var catalogue = main.Apps.ToDictionary(a => a.View.Id, StringComparer.OrdinalIgnoreCase);
        var others = 0;
        foreach (var entry in list.Apps)
        {
            if (catalogue.TryGetValue(entry.Id, out var app))
            {
                app.IsTicked = true;
                continue;
            }
            others++;
            var existing = SearchResults.FirstOrDefault(r => r.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new SearchResultViewModel(entry.Id, entry.Name, null, reviewed: false, null);
                SearchResults.Add(existing);
            }
            existing.IsTicked = true;
        }
        var message = $"{list.Message} Catalogue apps are ticked on the Catalogue tab.";
        if (others > 0)
            message += $" {others} not in the catalogue {(others == 1 ? "is" : "are")} ticked on the Search tab, marked not reviewed.";
        return message;
    }
}
