using Elynt.Core;
using Elynt.Core.Games;
using Elynt.Core.Journal;
using Elynt.Core.Profiles;
using Elynt.Core.Safety;
using Elynt.Core.Tweaks;
using Elynt.Windows;

namespace Elynt.Service;

/// <summary>
/// Runs per-game profiles in the service. At start it undoes temporary changes left behind by a crash or reboot,
/// then follows process start and exit events pushed by Windows. Registered before the pipe, so leftovers are gone
/// before the app can send any command.
/// </summary>
internal sealed class ProfileHost(
    Catalogue catalogue,
    ChangeJournal journal,
    SafetyNet safetyNet,
    IProfileStore profiles,
    ProfileNotes notes,
    WindowsProcesses processes,
    WindowsTimerResolution timer,
    ILoggerFactory loggers) : IHostedService, IDisposable
{
    private readonly ILogger _log = loggers.CreateLogger<ProfileHost>();
    private readonly ISystemInfo _systemInfo = new WindowsSystemInfo();
    private readonly WindowsGameSources _games = new();
    private readonly IGameStore _gameStore = new JsonFileGameStore(ServicePaths.Games);
    private readonly WindowsBootConfig _boot = new();
    private readonly WindowsPowerPlans _power = new();
    private readonly WindowsServiceManager _services = new();
    private WmiProcessEvents? _events;
    private ProfileRunner? _runner;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _events = new WmiProcessEvents();
            _runner = new ProfileRunner(_events, EngineFor, notes, logger: _log);
            _runner.RecoverLeftovers(journal.UsersWithTemporaryChanges());
            _events.Start();
        }
        catch (Exception ex)
        {
            // Profiles stop working, but the rest of the service must still run.
            _log.LogError(ex, "Per-game profiles couldn't start");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Undoes this user's leftovers again when their app connects: at service start a user who isn't signed in yet
    /// has no loaded settings (HKCU), so their recovery can only finish now.
    /// </summary>
    public void RecoverLeftovers(string user)
    {
        if (journal.OpenTemporary(user).Count > 0)
            _runner?.RecoverLeftovers([user]);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // A running profile is left journaled as temporary; the next start puts it back.
        _events?.Dispose();
        _events = null;
        return Task.CompletedTask;
    }

    /// <summary>The Engine for one user, with only what profiles touch.</summary>
    private Engine EngineFor(string sid) => new(
        catalogue,
        new SystemAdapters(new WindowsRegistry(sid), _systemInfo, Games: _games)
        {
            Boot = _boot,
            Power = _power,
            Services = _services,
            Processes = processes,
            Timer = timer,
        },
        journal,
        sid,
        safetyNet,
        logger: loggers.CreateLogger<Engine>(),
        gameStore: _gameStore,
        profiles: profiles,
        profileNotes: notes);

    public void Dispose()
    {
        _runner?.Dispose();
        _events?.Dispose();
    }
}
