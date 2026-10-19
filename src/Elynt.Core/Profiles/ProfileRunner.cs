using Elynt.Core.Games;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elynt.Core.Profiles;

/// <param name="User">The SID of the user who started the process.</param>
public sealed record ProcessStarted(int ProcessId, string ExePath, string User);

/// <summary>
/// Processes starting and exiting, pushed by Windows (WMI process traces backed by ETW), never polled. This is how
/// profiles see games start and close.
/// </summary>
public interface IProcessEvents
{
    event EventHandler<ProcessStarted>? Started;

    /// <summary>Raised with the process id.</summary>
    event EventHandler<int>? Exited;
}

/// <summary>The Engine commands the runner drives, one Engine per user.</summary>
public interface IProfileEngine
{
    GameProfile? ProfileFor(string exePath);

    ProfileRunResult StartProfile(string exePath, int processId);

    ProfileRunResult StopProfile(string exePath);

    ProfileRunResult RecoverTemporaryChanges();
}

/// <summary>
/// Applies a game's profile when its .exe starts and undoes it <see cref="Grace"/> after the last copy closes, so
/// launcher and crash-reporter restarts don't flicker settings. One profile runs at a time, PC-wide: the first game
/// wins and any other game with a profile is skipped, with a note.
/// </summary>
public sealed class ProfileRunner : IDisposable
{
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);

    private readonly IProcessEvents _events;
    private readonly Func<string, IProfileEngine> _engineFor;
    private readonly ProfileNotes _notes;
    private readonly TimeProvider _clock;
    private readonly ILogger _log;
    private readonly CommandLane _lane;
    private readonly Lock _gate = new();
    private Running? _running;

    /// <param name="lane">The lane pipe commands take turns in, so a profile never interleaves with one.</param>
    public ProfileRunner(
        IProcessEvents events, Func<string, IProfileEngine> engineFor, ProfileNotes notes, TimeProvider? clock = null, ILogger? logger = null,
        CommandLane? lane = null)
    {
        _events = events;
        _lane = lane ?? new CommandLane();
        _engineFor = engineFor;
        _notes = notes;
        _clock = clock ?? TimeProvider.System;
        _log = logger ?? NullLogger.Instance;
        _events.Started += OnStarted;
        _events.Exited += OnExited;
    }

    /// <summary>The game whose profile is in charge, if any.</summary>
    public string? ActiveGame
    {
        get
        {
            lock (_gate)
                return _running?.ExePath;
        }
    }

    /// <summary>
    /// Undoes temporary changes a crash or reboot left behind, for each of <paramref name="users"/>. Skips the user
    /// whose profile is running now, since those changes are live, not left behind.
    /// </summary>
    public void RecoverLeftovers(IEnumerable<string> users)
    {
        lock (_gate)
        {
            foreach (var user in users)
            {
                if (_running?.User == user)
                    continue;
                try
                {
                    var result = _lane.Run(_engineFor(user).RecoverTemporaryChanges);
                    if (!result.Succeeded)
                        _notes.Post(new(user, ProfileNoteKind.Problem, "Per-game profile", result.Message));
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Recovering temporary changes for {User} failed", user);
                }
            }
        }
    }

    private void OnStarted(object? sender, ProcessStarted process)
    {
        lock (_gate)
        {
            try
            {
                if (_running is { } running)
                {
                    if (running.User == process.User && WinPath.Same(running.ExePath, process.ExePath))
                    {
                        // The same game again (a relaunch, or a second process): keep the profile, cancel any undo.
                        running.ProcessIds.Add(process.ProcessId);
                        running.CancelGrace();
                        return;
                    }

                    if (_engineFor(process.User).ProfileFor(process.ExePath) is not null)
                        _notes.Post(new(process.User, ProfileNoteKind.Skipped, "Profile skipped",
                            $"{Name(process.ExePath)}'s profile was skipped: {Name(running.ExePath)}'s profile is already in charge."));
                    return;
                }

                var engine = _engineFor(process.User);
                if (engine.ProfileFor(process.ExePath) is null)
                    return;

                _running = new Running(process.User, process.ExePath, engine) { ProcessIds = { process.ProcessId } };
                var result = _lane.Run(() => engine.StartProfile(process.ExePath, process.ProcessId));
                _notes.Post(new(process.User, result.Succeeded ? ProfileNoteKind.Applied : ProfileNoteKind.Problem, "Profile applied", result.Message));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Starting the profile for {Game} failed", process.ExePath);
            }
        }
    }

    private void OnExited(object? sender, int processId)
    {
        lock (_gate)
        {
            if (_running is not { } running || !running.ProcessIds.Remove(processId) || running.ProcessIds.Count > 0)
                return;
            running.CancelGrace();
            running.GraceTimer = _clock.CreateTimer(_ => Stop(running), null, Grace, Timeout.InfiniteTimeSpan);
        }
    }

    private void Stop(Running running)
    {
        lock (_gate)
        {
            // A relaunch may have cancelled this undo after the timer fired.
            if (_running != running || running.ProcessIds.Count > 0)
                return;
            running.CancelGrace();
            _running = null;
            try
            {
                var result = _lane.Run(() => running.Engine.StopProfile(running.ExePath));
                _notes.Post(new(running.User, result.Succeeded ? ProfileNoteKind.Reverted : ProfileNoteKind.Problem, "Profile undone", result.Message));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Undoing the profile for {Game} failed", running.ExePath);
            }
        }
    }

    private static string Name(string exePath) => Path.GetFileNameWithoutExtension(WinPath.FileName(exePath));

    public void Dispose()
    {
        _events.Started -= OnStarted;
        _events.Exited -= OnExited;
        lock (_gate)
            _running?.CancelGrace();
    }

    private sealed class Running(string user, string exePath, IProfileEngine engine)
    {
        public string User { get; } = user;
        public string ExePath { get; } = exePath;
        public IProfileEngine Engine { get; } = engine;
        public HashSet<int> ProcessIds { get; } = [];
        public ITimer? GraceTimer { get; set; }

        public void CancelGrace()
        {
            GraceTimer?.Dispose();
            GraceTimer = null;
        }
    }
}
