namespace Elynt.Core.Tuning;

/// <summary>
/// Keeps hardware changes on probation: each one goes back on its own unless the user confirms it within a window
/// (15 s for GPUs, 60 s for CPUs). A further change to the same hardware while one is waiting restarts the window
/// but keeps the original way back, so an unconfirmed series of changes always returns to the last confirmed state.
/// </summary>
/// <param name="gate">
/// The owner's lock. Expiry runs its revert under it, so a revert never interleaves with the owner's own commands.
/// </param>
public sealed class ConfirmOrRevert(TimeProvider clock, Lock gate, Action<string, Exception>? onRevertFailed = null) : IDisposable
{
    private readonly Dictionary<string, Pending> _pending = [];

    /// <summary>Starts or restarts the window for <paramref name="key"/>. <paramref name="revert"/> is used only when none is waiting yet.</summary>
    public DateTimeOffset Start(string key, TimeSpan window, Action revert)
    {
        lock (gate)
        {
            if (_pending.Remove(key, out var existing))
            {
                existing.Timer.Dispose();
                revert = existing.Revert;
            }

            var deadline = clock.GetUtcNow() + window;
            var pending = new Pending(revert, deadline);
            pending.Timer = clock.CreateTimer(_ => Expire(key, pending), null, window, Timeout.InfiniteTimeSpan);
            _pending[key] = pending;
            return deadline;
        }
    }

    /// <summary>When the waiting change to <paramref name="key"/> goes back, or null when none is waiting.</summary>
    public DateTimeOffset? Deadline(string key)
    {
        lock (gate)
        {
            return _pending.TryGetValue(key, out var pending) ? pending.Deadline : null;
        }
    }

    /// <summary>Keeps the waiting change. False when there was none (it may already have gone back).</summary>
    public bool Confirm(string key) => Drop(key) is not null;

    /// <summary>Forgets the waiting change without reverting it, e.g. because the hardware was reset anyway.</summary>
    public void Cancel(string key) => Drop(key);

    /// <summary>Puts the waiting change back now. False when there was none.</summary>
    public bool RevertNow(string key)
    {
        lock (gate)
        {
            if (Drop(key) is not { } pending)
                return false;
            Run(key, pending);
            return true;
        }
    }

    /// <summary>Puts every waiting change back, e.g. when the service stops.</summary>
    public void RevertAll()
    {
        lock (gate)
        {
            foreach (var key in _pending.Keys.ToList())
                RevertNow(key);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var pending in _pending.Values)
                pending.Timer.Dispose();
            _pending.Clear();
        }
    }

    private void Expire(string key, Pending pending)
    {
        lock (gate)
        {
            // Confirmed, cancelled or replaced while the timer was firing.
            if (!_pending.TryGetValue(key, out var current) || current != pending)
                return;
            _pending.Remove(key);
            pending.Timer.Dispose();
            Run(key, pending);
        }
    }

    private Pending? Drop(string key)
    {
        lock (gate)
        {
            if (!_pending.Remove(key, out var pending))
                return null;
            pending.Timer.Dispose();
            return pending;
        }
    }

    private void Run(string key, Pending pending)
    {
        try
        {
            pending.Revert();
        }
        catch (Exception ex)
        {
            onRevertFailed?.Invoke(key, ex);
        }
    }

    private sealed class Pending(Action revert, DateTimeOffset deadline)
    {
        public Action Revert { get; } = revert;
        public DateTimeOffset Deadline { get; } = deadline;
        public ITimer Timer { get; set; } = null!;
    }
}
