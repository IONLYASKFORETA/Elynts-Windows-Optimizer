using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elynt.Core.UpdateControl;

public enum UpdateKind
{
    /// <summary>Monthly cumulative and security updates, Defender definitions, .NET and the like.</summary>
    Quality,
    Driver,
    /// <summary>The yearly Windows 11 version upgrade.</summary>
    Feature,
}

/// <summary>One update Windows Update offers and this PC doesn't have yet.</summary>
public sealed record AvailableUpdate(string Id, string Title, UpdateKind Kind);

public sealed record InstallOutcome(bool Succeeded, bool RebootRequired, string? Error = null);

/// <summary>
/// The Windows Update agent adapter. The real one wraps the Windows Update Agent API, which searches and installs
/// on request whatever the pause and deferral settings say.
/// </summary>
public interface IWindowsUpdateAgent
{
    /// <summary>Updates not installed yet and not hidden.</summary>
    IReadOnlyList<AvailableUpdate> Search();

    void Download(AvailableUpdate update);

    InstallOutcome Install(AvailableUpdate update);
}

public enum UpdatePhase
{
    Idle,
    Checking,
    Downloading,
    Installing,
    Finished,
    Failed,
}

/// <summary>Where "check and install now" stands, as the Windows Update page shows it.</summary>
/// <param name="Done">Updates finished in the current phase.</param>
/// <param name="Total">Updates to download and install.</param>
/// <param name="Current">The update being downloaded or installed.</param>
/// <param name="Skipped">Drivers and feature updates, which this button never installs.</param>
public sealed record UpdateRunStatus(
    UpdatePhase Phase,
    string Message,
    int Done = 0,
    int Total = 0,
    string? Current = null,
    bool RebootRequired = false,
    IReadOnlyList<string>? Installed = null,
    IReadOnlyList<string>? NotInstalled = null,
    IReadOnlyList<string>? Skipped = null)
{
    public bool Running => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing;

    /// <summary>Overall progress: each update is downloaded, then installed, so each counts twice.</summary>
    public int Percent => Phase switch
    {
        UpdatePhase.Finished => 100,
        UpdatePhase.Downloading when Total > 0 => Done * 100 / (2 * Total),
        UpdatePhase.Installing when Total > 0 => (Total + Done) * 100 / (2 * Total),
        _ => 0,
    };

    public static UpdateRunStatus Idle { get; } = new(UpdatePhase.Idle, "Not checked yet.");
}

/// <summary>
/// "Check and install now": searches Windows Update, then downloads and installs quality updates one at a time,
/// in the background. Drivers and feature updates are left alone, so it never undoes driver exclusion or version
/// pinning. One shared instance lives in the service so any app window can follow the progress.
/// </summary>
/// <param name="runInBackground">Starts the work; the service runs it on the thread pool, tests run it inline.</param>
public sealed class ManualUpdate(
    IWindowsUpdateAgent agent,
    Func<Action, Task>? runInBackground = null,
    ILogger<ManualUpdate>? logger = null)
{
    private readonly Func<Action, Task> _run = runInBackground ?? Task.Run;
    private readonly ILogger _log = logger ?? NullLogger<ManualUpdate>.Instance;
    private readonly Lock _lock = new();
    private UpdateRunStatus _status = UpdateRunStatus.Idle;

    public UpdateRunStatus Status
    {
        get { lock (_lock) return _status; }
        private set { lock (_lock) _status = value; }
    }

    /// <summary>Starts a check-and-install, or shows the one already running.</summary>
    public UpdateRunStatus Start()
    {
        lock (_lock)
        {
            if (_status.Running)
                return _status;
            _status = new UpdateRunStatus(UpdatePhase.Checking, "Checking for updates...");
        }
        _run(Run);
        return Status;
    }

    private void Run()
    {
        try
        {
            var found = agent.Search();
            var toInstall = found.Where(u => u.Kind == UpdateKind.Quality).ToList();
            var skipped = found.Where(u => u.Kind != UpdateKind.Quality).Select(u => u.Title).ToList();
            var skippedNote = skipped.Count == 0 ? "" : $" Skipped {skipped.Count} drivers and feature updates; this button never installs those.";

            if (toInstall.Count == 0)
            {
                Status = new UpdateRunStatus(UpdatePhase.Finished, $"Windows is up to date.{skippedNote}", Skipped: skipped);
                return;
            }

            var failed = new List<string>();
            var downloaded = new List<AvailableUpdate>();
            for (var i = 0; i < toInstall.Count; i++)
            {
                var update = toInstall[i];
                Status = new UpdateRunStatus(UpdatePhase.Downloading, $"Downloading {i + 1} of {toInstall.Count}: {update.Title}", i, toInstall.Count, update.Title);
                try
                {
                    agent.Download(update);
                    downloaded.Add(update);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Downloading {Update} failed", update.Title);
                    failed.Add(update.Title);
                }
            }

            var installed = new List<string>();
            var reboot = false;
            for (var i = 0; i < downloaded.Count; i++)
            {
                var update = downloaded[i];
                Status = new UpdateRunStatus(UpdatePhase.Installing, $"Installing {i + 1} of {downloaded.Count}: {update.Title}", i, downloaded.Count, update.Title);
                InstallOutcome outcome;
                try
                {
                    outcome = agent.Install(update);
                }
                catch (Exception ex)
                {
                    outcome = new InstallOutcome(false, false, ex.Message);
                }

                if (outcome.Succeeded)
                {
                    installed.Add(update.Title);
                    reboot |= outcome.RebootRequired;
                }
                else
                {
                    _log.LogWarning("Installing {Update} failed: {Error}", update.Title, outcome.Error);
                    failed.Add(update.Title);
                }
            }

            var message = failed.Count == 0
                ? $"Installed {installed.Count} updates."
                : $"Installed {installed.Count} of {toInstall.Count} updates. These didn't install: {string.Join(", ", failed)}.";
            if (reboot)
                message += " Restart to finish installing.";
            _log.LogInformation("Check and install now: {Message}", message);
            Status = new UpdateRunStatus(UpdatePhase.Finished, message + skippedNote, installed.Count, toInstall.Count, null, reboot, installed, failed, skipped);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Check and install now failed");
            Status = new UpdateRunStatus(UpdatePhase.Failed, $"Couldn't check for updates: {ex.Message}");
        }
    }
}
