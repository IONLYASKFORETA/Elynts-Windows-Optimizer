namespace Elynt.Core.Apps;

/// <summary>
/// One winget search result. <paramref name="Reviewed"/> is true only for apps in the curated catalogue; anything
/// else is shown as "not reviewed". <paramref name="Group"/> and <paramref name="Description"/> come from the
/// catalogue, so they are null for a not-reviewed app.
/// </summary>
public sealed record AppSearchResult(string Id, string Name, string? Version, bool Reviewed, string? Group, string? Description);

public sealed record AppSearchReport(bool Succeeded, string Message, IReadOnlyList<AppSearchResult> Results);

/// <summary>
/// An app installed on the PC. <paramref name="UpdateVersion"/> is the newer version in winget's community
/// source, or null when it is up to date or didn't come from that source (updates come only from there).
/// </summary>
public sealed record InstalledAppView(string Id, string Name, string Version, string? UpdateVersion, bool Reviewed)
{
    public bool CanUpdate => UpdateVersion is not null;
}

public sealed record InstalledAppsReport(bool Succeeded, string Message, IReadOnlyList<InstalledAppView> Apps);

/// <summary>Runs the Apps section's search, installed-apps list, update and uninstall commands.</summary>
internal sealed class AppManager(AppCatalogue catalogue, IPackageManager winget)
{
    public AppSearchReport Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
            return new(false, "Type part of an app's name to search.", []);

        try
        {
            if (!winget.IsAvailable())
                return new(false, "winget (App Installer) isn't set up yet, so only the reviewed catalogue was searched. It is set up with the first install.",
                    SearchCatalogue(query));
            var results = winget.Search(query, AppInstaller.CommunitySource)
                .DistinctBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(ToResult)
                .OrderByDescending(r => r.Reviewed)
                .ToList();
            var notReviewed = results.Count(r => !r.Reviewed);
            var message = results.Count == 0
                ? $"Nothing in winget matches \"{query}\"."
                : notReviewed == 0
                    ? $"{results.Count} found, all reviewed."
                    : $"{results.Count} found. {notReviewed} {(notReviewed == 1 ? "isn't" : "aren't")} in the reviewed catalogue: install {(notReviewed == 1 ? "it" : "them")} only if you trust the publisher.";
            return new(true, message, results);
        }
        catch (Exception ex)
        {
            return new(false, $"winget couldn't search: {ex.Message}", SearchCatalogue(query));
        }
    }

    public InstalledAppsReport ListInstalled()
    {
        try
        {
            if (!winget.IsAvailable())
                return new(false, "winget (App Installer) isn't set up yet. It is set up with the first install.", []);
            var apps = Installed();
            var updates = apps.Count(a => a.CanUpdate);
            var message = updates switch
            {
                0 => "Every app is up to date.",
                1 => "1 update available.",
                _ => $"{updates} updates available.",
            };
            return new(true, message, apps);
        }
        catch (Exception ex)
        {
            return new(false, $"winget couldn't list installed apps: {ex.Message}", []);
        }
    }

    /// <summary>Updates the named apps, each of which must be installed with an update waiting.</summary>
    public InstallReport Update(IReadOnlyList<string> appIds, IProgress<InstallProgress>? progress) =>
        WithInstalled(installed =>
        {
            var queue = Pick(appIds, installed.Where(a => a.CanUpdate), out var missing);
            if (missing.Count > 0)
                return new(false, $"These have no update waiting, so nothing was updated: {string.Join(", ", missing)}.", []);
            return queue.Count == 0
                ? new(false, "Nothing is ticked, so there is nothing to update.", [])
                : RunUpdates(queue, progress);
        });

    /// <summary>Updates every installed app with an update waiting in the community source.</summary>
    public InstallReport UpdateAll(IProgress<InstallProgress>? progress) =>
        WithInstalled(installed =>
        {
            var queue = installed.Where(a => a.CanUpdate).ToList();
            return queue.Count == 0 ? new(true, "Every app is up to date.", []) : RunUpdates(queue, progress);
        });

    public InstallReport Uninstall(IReadOnlyList<string> appIds, IProgress<InstallProgress>? progress) =>
        WithInstalled(installed =>
        {
            var queue = Pick(appIds, installed, out var missing);
            if (missing.Count > 0)
                return new(false, $"These aren't installed, so nothing was uninstalled: {string.Join(", ", missing)}.", []);
            return queue.Count == 0
                ? new(false, "Nothing is ticked, so there is nothing to uninstall.", [])
                : PackageQueue.Run(queue.Select(a => (a.Id, a.Name)).ToList(), PackageAction.Uninstall, winget.Uninstall, progress);
        });

    private InstallReport RunUpdates(List<InstalledAppView> queue, IProgress<InstallProgress>? progress) =>
        PackageQueue.Run(
            queue.Select(a => (a.Id, a.Name)).ToList(),
            PackageAction.Update,
            (id, p) => winget.Upgrade(id, AppInstaller.CommunitySource, p),
            progress);

    private InstallReport WithInstalled(Func<List<InstalledAppView>, InstallReport> run)
    {
        List<InstalledAppView> installed;
        try
        {
            if (!winget.IsAvailable())
                return new(false, "winget (App Installer) isn't set up yet, so nothing was changed.", []);
            installed = Installed();
        }
        catch (Exception ex)
        {
            return new(false, $"winget couldn't list installed apps, so nothing was changed: {ex.Message}", []);
        }
        return run(installed);
    }

    private static List<InstalledAppView> Pick(IReadOnlyList<string> appIds, IEnumerable<InstalledAppView> from, out List<string> missing)
    {
        var byId = from.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        missing = appIds.Where(id => !byId.ContainsKey(id)).ToList();
        return appIds.Where(byId.ContainsKey).Select(id => byId[id]).DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<InstalledAppView> Installed() =>
        winget.ListInstalled()
            .DistinctBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(p => new InstalledAppView(
                p.Id,
                catalogue.TryGet(p.Id, out var app) ? app.Name : p.Name,
                p.Version,
                string.Equals(p.Source, AppInstaller.CommunitySource, StringComparison.OrdinalIgnoreCase) ? p.AvailableVersion : null,
                catalogue.TryGet(p.Id, out _)))
            .ToList();

    private AppSearchResult ToResult(PackageInfo package) =>
        catalogue.TryGet(package.Id, out var app)
            ? new(app.Id, app.Name, package.Version, true, app.Group, app.Description)
            : new(package.Id, package.Name, package.Version, false, null, null);

    private List<AppSearchResult> SearchCatalogue(string query) =>
        catalogue.Apps
            .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(a => new AppSearchResult(a.Id, a.Name, null, true, a.Group, a.Description))
            .ToList();
}
