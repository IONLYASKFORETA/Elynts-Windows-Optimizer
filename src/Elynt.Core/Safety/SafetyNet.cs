using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elynt.Core.Safety;

/// <summary>A Windows store that has its own whole-store export, taken before the app first changes anything in it.</summary>
public enum BackupStore
{
    BootConfiguration,
    Firewall,
    PowerPlans,
    ScheduledTasks,
    Drivers,
    Hosts,
}

/// <summary>Exports a whole store (bcdedit /export, netsh advfirewall export, powercfg /export and so on).</summary>
public interface IStoreBackups
{
    /// <summary>Exports the whole store and returns where the backup was saved. Throws when it couldn't be made.</summary>
    string Export(BackupStore store);
}

/// <param name="WasCreated">False when Windows skipped it (the 24-hour limit, or System Protection is off).</param>
/// <param name="Reason">Why it was skipped, in plain words.</param>
public sealed record RestorePointResult(bool WasCreated, string? Reason)
{
    public static RestorePointResult Created() => new(true, null);

    public static RestorePointResult Skipped(string reason) => new(false, reason);
}

/// <summary>System Restore. Windows may skip a new point without failing, so the adapter must say whether it made one.</summary>
public interface IRestorePoints
{
    RestorePointResult TryCreate(string description);
}

/// <summary>Raised when a whole-store backup that must exist before a change couldn't be made.</summary>
public sealed class BackupFailedException(BackupStore store, Exception inner)
    : Exception($"Couldn't back up {SafetyNet.Describe(store)}: {inner.Message}", inner)
{
    public BackupStore Store { get; } = store;
}

/// <summary>
/// The last-resort layers under the change journal, shared by every user's commands: a System Restore point
/// before changes (at most one attempt a day, since Windows skips more anyway), and a whole-store backup before the
/// first change of each kind since the service started.
/// </summary>
public sealed class SafetyNet(
    IRestorePoints restorePoints,
    IStoreBackups backups,
    TimeProvider? clock = null,
    ILogger<SafetyNet>? logger = null)
{
    /// <summary>Windows itself makes at most one restore point in this time, so trying more often only adds noise.</summary>
    public static readonly TimeSpan RestorePointInterval = TimeSpan.FromHours(24);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _log = logger ?? NullLogger<SafetyNet>.Instance;
    private readonly Dictionary<BackupStore, string> _backedUp = [];
    private readonly Lock _gate = new();
    private DateTimeOffset? _lastRestorePointAttempt;

    /// <summary>Backups taken so far, and where each was saved.</summary>
    public IReadOnlyDictionary<BackupStore, string> Backups
    {
        get
        {
            lock (_gate)
                return new Dictionary<BackupStore, string>(_backedUp);
        }
    }

    /// <summary>
    /// Call before changing anything for <paramref name="tweak"/>. Returns a note for the user (a skipped restore
    /// point) or null. Throws <see cref="BackupFailedException"/> when a needed backup couldn't be made: then nothing
    /// may be changed.
    /// </summary>
    public string? BeforeChange(TweakDefinition tweak)
    {
        lock (_gate)
        {
            foreach (var store in tweak.Steps.Select(step => step.Store).OfType<BackupStore>().Distinct())
            {
                if (_backedUp.ContainsKey(store))
                    continue;
                try
                {
                    _backedUp[store] = backups.Export(store);
                    _log.LogInformation("Backed up {Store} to {Path}", store, _backedUp[store]);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Backing up {Store} failed", store);
                    throw new BackupFailedException(store, ex);
                }
            }

            var now = _clock.GetUtcNow();
            if (_lastRestorePointAttempt is { } last && now - last < RestorePointInterval)
                return null;
            _lastRestorePointAttempt = now;

            RestorePointResult result;
            try
            {
                result = restorePoints.TryCreate($"Elynt Optimizer: before {tweak.Name}");
            }
            catch (Exception ex)
            {
                result = RestorePointResult.Skipped(ex.Message);
            }

            if (result.WasCreated)
            {
                _log.LogInformation("Made a restore point before {Tweak}", tweak.Id);
                return null;
            }
            _log.LogWarning("Windows skipped the restore point before {Tweak}: {Reason}", tweak.Id, result.Reason);
            return $"Windows didn't make a restore point: {result.Reason} Your change is still recorded and can be undone from Undo history.";
        }
    }

    public static string Describe(BackupStore store) => store switch
    {
        BackupStore.BootConfiguration => "the boot configuration (BCD)",
        BackupStore.Firewall => "the firewall rules",
        BackupStore.PowerPlans => "the power plans",
        BackupStore.ScheduledTasks => "the scheduled tasks",
        BackupStore.Drivers => "the installed drivers",
        BackupStore.Hosts => "the hosts file",
        _ => store.ToString(),
    };
}
