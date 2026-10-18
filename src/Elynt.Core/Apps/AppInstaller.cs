namespace Elynt.Core.Apps;

/// <summary>A catalogue app as the UI shows it.</summary>
public sealed record AppView(string Id, string Name, string Group, string Description, bool InAllRuntimes);

public enum InstallStage
{
    /// <summary>winget (App Installer) was missing and is being installed. Comes before any app.</summary>
    SettingUpWinget,
    Installing,
    Installed,
    AlreadyInstalled,
    Failed,
    Updating,
    Updated,
    UpToDate,
    Uninstalling,
    Uninstalled,

    /// <summary>An uninstall found the app already gone.</summary>
    NotInstalled,
}

/// <summary>
/// One step of the install queue, streamed to the UI while it runs. <paramref name="Position"/> is 0 and
/// <paramref name="AppId"/> null while winget itself is being set up. <paramref name="Percent"/> is 0–1 when
/// winget reports how far along an install is.
/// </summary>
public sealed record InstallProgress(int Position, int Total, string? AppId, string? Name, InstallStage Stage, double? Percent, string Message);

/// <summary>How one app in an install, update or uninstall queue ended.</summary>
public sealed record AppInstallOutcome(string AppId, string Name, InstallStage Stage, string Message, bool RebootRequired);

/// <summary>The result of a whole install, update or uninstall queue.</summary>
public sealed record InstallReport(bool Succeeded, string Message, IReadOnlyList<AppInstallOutcome> Apps);

/// <summary>Runs the Apps section's commands: lists the catalogue and works through the install queue.</summary>
internal sealed class AppInstaller(AppCatalogue catalogue, IPackageManager winget)
{
    /// <summary>winget's community repository. The Microsoft Store source is never used.</summary>
    public const string CommunitySource = "winget";

    public IReadOnlyList<AppView> ListApps() =>
        catalogue.Apps.Select(a => new AppView(a.Id, a.Name, a.Group, a.Description, a.InAllRuntimes)).ToList();

    /// <param name="allowNotReviewed">
    /// The user accepted that ids outside the catalogue (from search or an app list) are not reviewed. Without it,
    /// any such id refuses the whole queue.
    /// </param>
    public InstallReport Install(IReadOnlyList<string> appIds, bool allRuntimes, IProgress<InstallProgress>? progress, bool allowNotReviewed = false)
    {
        var unknown = appIds.Where(id => !catalogue.TryGet(id, out _)).ToList();
        if (unknown.Count > 0 && !allowNotReviewed)
            return new(false, $"These aren't in the reviewed catalogue, so nothing was installed: {string.Join(", ", unknown)}.", []);
        var invalid = unknown.Where(id => !AppList.IsWingetId(id)).ToList();
        if (invalid.Count > 0)
            return new(false, $"These aren't winget ids, so nothing was installed: {string.Join(", ", invalid)}.", []);

        var ticked = appIds.Select(id => catalogue.TryGet(id, out var app) ? (app.Id, app.Name) : (id, id));
        var runtimes = allRuntimes ? catalogue.Apps.Where(a => a.InAllRuntimes).Select(a => (a.Id, a.Name)) : [];
        var queue = ticked.Concat(runtimes).DistinctBy(a => a.Item1, StringComparer.OrdinalIgnoreCase).ToList();
        if (queue.Count == 0)
            return new(false, "Nothing is ticked, so there is nothing to install.", []);

        if (EnsureWinget(queue.Count, progress) is { } problem)
            return new(false, $"Couldn't set up winget (App Installer), so nothing was installed. {problem}", []);
        return PackageQueue.Run(
            queue,
            PackageAction.Install,
            (id, p) => winget.Install(id, CommunitySource, p),
            progress);
    }

    /// <summary>Installs App Installer when winget is missing. Returns why it couldn't, or null when winget is ready.</summary>
    private string? EnsureWinget(int total, IProgress<InstallProgress>? progress)
    {
        try
        {
            if (winget.IsAvailable())
                return null;
            progress?.Report(new(0, total, null, null, InstallStage.SettingUpWinget, null,
                "winget (App Installer) is missing. Installing it from Microsoft first…"));
            winget.Bootstrap();
            return winget.IsAvailable() ? null : "It still isn't available after installing App Installer.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
