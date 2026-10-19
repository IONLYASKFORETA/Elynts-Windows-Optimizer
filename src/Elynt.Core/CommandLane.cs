namespace Elynt.Core;

/// <summary>
/// The one-at-a-time lane for everything that changes settings or the journal: pipe commands and the service's own
/// work (per-game profiles starting and stopping). Two changes to the same tweak never interleave.
/// </summary>
public sealed class CommandLane
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Waits for the lane; disposing the result leaves it.</summary>
    public IDisposable Enter()
    {
        _gate.Wait();
        return new Exit(_gate);
    }

    /// <summary>Runs one command once the lane is free.</summary>
    public T Run<T>(Func<T> command)
    {
        using (Enter())
            return command();
    }

    /// <summary>For the pipe server, which waits on lanes asynchronously.</summary>
    internal SemaphoreSlim Semaphore => _gate;

    private sealed class Exit(SemaphoreSlim gate) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
                gate.Release();
        }
    }
}
