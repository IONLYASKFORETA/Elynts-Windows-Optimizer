namespace Elynt.Core.Apps;

/// <summary>What a queue does to each app, with the words the UI shows for it.</summary>
internal sealed record PackageAction(
    InstallStage Running,
    InstallStage Done,
    InstallStage NothingToDo,
    string Verb,
    string Infinitive,
    string Past,
    string NothingToDoText)
{
    public static readonly PackageAction Install = new(
        InstallStage.Installing, InstallStage.Installed, InstallStage.AlreadyInstalled, "Installing", "install", "installed", "was already installed");

    public static readonly PackageAction Update = new(
        InstallStage.Updating, InstallStage.Updated, InstallStage.UpToDate, "Updating", "update", "updated", "was already up to date");

    public static readonly PackageAction Uninstall = new(
        InstallStage.Uninstalling, InstallStage.Uninstalled, InstallStage.NotInstalled, "Uninstalling", "uninstall", "uninstalled", "was already gone");
}

/// <summary>Works through a queue of apps one at a time, streaming progress, and sums up how it went.</summary>
internal static class PackageQueue
{
    public static InstallReport Run(
        IReadOnlyList<(string Id, string Name)> queue,
        PackageAction action,
        Func<string, IProgress<double>, PackageInstallResult> run,
        IProgress<InstallProgress>? progress)
    {
        var outcomes = new List<AppInstallOutcome>();
        for (var i = 0; i < queue.Count; i++)
        {
            var (id, name) = queue[i];
            var position = i + 1;
            void Report(InstallStage stage, double? percent, string message) =>
                progress?.Report(new(position, queue.Count, id, name, stage, percent, message));

            var running = $"{action.Verb} {name}…";
            Report(action.Running, null, running);
            var outcome = RunOne(id, name, action, p => Report(action.Running, p, running), run);
            Report(outcome.Stage, null, outcome.Message);
            outcomes.Add(outcome);
        }
        return Summarise(outcomes, action);
    }

    private static AppInstallOutcome RunOne(
        string id, string name, PackageAction action, Action<double> report, Func<string, IProgress<double>, PackageInstallResult> run)
    {
        try
        {
            var result = run(id, new Relay(report));
            return result.Status switch
            {
                PackageInstallStatus.Installed => new(id, name, action.Done,
                    result.RebootRequired ? $"{name} is {action.Past}. Restart the PC to finish." : $"{name} is {action.Past}.",
                    result.RebootRequired),
                PackageInstallStatus.AlreadyInstalled => new(id, name, action.NothingToDo, $"{name} {action.NothingToDoText}.", false),
                _ => new(id, name, InstallStage.Failed,
                    $"{name} didn't {action.Infinitive}: {result.Error ?? "winget gave no reason."}", false),
            };
        }
        catch (Exception ex)
        {
            return new(id, name, InstallStage.Failed, $"{name} didn't {action.Infinitive}: {ex.Message}", false);
        }
    }

    private static InstallReport Summarise(List<AppInstallOutcome> outcomes, PackageAction action)
    {
        var failed = outcomes.Where(o => o.Stage == InstallStage.Failed).Select(o => o.Name).ToList();
        var message = $"{outcomes.Count - failed.Count} of {outcomes.Count} {action.Past}.";
        if (failed.Count > 0)
            message += $" {JoinNames(failed)} didn't {action.Infinitive}.";
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
