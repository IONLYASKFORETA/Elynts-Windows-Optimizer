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
}

/// <summary>
/// One step of the install queue, streamed to the UI while it runs. <paramref name="Position"/> is 0 and
/// <paramref name="AppId"/> null while winget itself is being set up. <paramref name="Percent"/> is 0–1 when
/// winget reports how far along an install is.
/// </summary>
public sealed record InstallProgress(int Position, int Total, string? AppId, string? Name, InstallStage Stage, double? Percent, string Message);

/// <summary>How one app in the queue ended.</summary>
public sealed record AppInstallOutcome(string AppId, string Name, InstallStage Stage, string Message, bool RebootRequired);

/// <summary>The result of a whole install queue.</summary>
public sealed record InstallReport(bool Succeeded, string Message, IReadOnlyList<AppInstallOutcome> Apps);

/// <summary>Runs the Apps section's commands: lists the catalogue and works through the install queue.</summary>
internal sealed class AppInstaller(AppCatalogue catalogue, IPackageManager winget)
{
    /// <summary>winget's community repository. The Microsoft Store source is never used.</summary>
    public const string CommunitySource = "winget";

    public IReadOnlyList<AppView> ListApps() =>
        catalogue.Apps.Select(a => new AppView(a.Id, a.Name, a.Group, a.Description, a.InAllRuntimes)).ToList();

    public InstallReport Install(IReadOnlyList<string> appIds, bool allRuntimes, IProgress<InstallProgress>? progress)
    {
        var unknown = appIds.Where(id => !catalogue.TryGet(id, out _)).ToList();
        if (unknown.Count > 0)
            return new(false, $"These aren't in the reviewed catalogue, so nothing was installed: {string.Join(", ", unknown)}.", []);

        var ticked = appIds.Select(id => { catalogue.TryGet(id, out var app); return app; });
        var runtimes = allRuntimes ? catalogue.Apps.Where(a => a.InAllRuntimes) : [];
        var queue = ticked.Concat(runtimes).DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToList();
        if (queue.Count == 0)
            return new(false, "Nothing is ticked, so there is nothing to install.", []);

        if (EnsureWinget(queue.Count, progress) is { } problem)
            return new(false, $"Couldn't set up winget (App Installer), so nothing was installed. {problem}", []);
        var outcomes = new List<AppInstallOutcome>();
        for (var i = 0; i < queue.Count; i++)
        {
            var app = queue[i];
            var position = i + 1;
            void Report(InstallStage stage, double? percent, string message) =>
                progress?.Report(new(position, queue.Count, app.Id, app.Name, stage, percent, message));

            Report(InstallStage.Installing, null, $"Installing {app.Name}…");
            var outcome = InstallOne(app, new Relay(p => Report(InstallStage.Installing, p, $"Installing {app.Name}…")));
            Report(outcome.Stage, null, outcome.Message);
            outcomes.Add(outcome);
        }

        return Summarise(outcomes);
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

    private AppInstallOutcome InstallOne(CatalogueApp app, IProgress<double> progress)
    {
        try
        {
            var result = winget.Install(app.Id, CommunitySource, progress);
            return result.Status switch
            {
                PackageInstallStatus.Installed => new(app.Id, app.Name, InstallStage.Installed,
                    result.RebootRequired ? $"{app.Name} is installed. Restart the PC to finish." : $"{app.Name} is installed.",
                    result.RebootRequired),
                PackageInstallStatus.AlreadyInstalled => new(app.Id, app.Name, InstallStage.AlreadyInstalled,
                    $"{app.Name} was already installed.", false),
                _ => new(app.Id, app.Name, InstallStage.Failed,
                    $"{app.Name} didn't install: {result.Error ?? "winget gave no reason."}", false),
            };
        }
        catch (Exception ex)
        {
            return new(app.Id, app.Name, InstallStage.Failed, $"{app.Name} didn't install: {ex.Message}", false);
        }
    }

    private static InstallReport Summarise(List<AppInstallOutcome> outcomes)
    {
        var failed = outcomes.Where(o => o.Stage == InstallStage.Failed).Select(o => o.Name).ToList();
        var message = $"{outcomes.Count - failed.Count} of {outcomes.Count} installed.";
        if (failed.Count > 0)
            message += $" {JoinNames(failed)} didn't install.";
        if (outcomes.Any(o => o.RebootRequired))
            message += " Restart the PC to finish.";
        return new(failed.Count == 0, message, outcomes);
    }

    private static string JoinNames(List<string> names) =>
        names.Count == 1 ? names[0] : $"{string.Join(", ", names[..^1])} and {names[^1]}";

    /// <summary>Passes progress straight through on the calling thread, so it stays in order.</summary>
    private sealed class Relay(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
