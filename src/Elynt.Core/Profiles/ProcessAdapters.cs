namespace Elynt.Core.Profiles;

/// <summary>Running programs, for closing background apps during a game and setting the game's priority.</summary>
public interface IProcesses
{
    /// <summary>Whether any process is running from this .exe.</summary>
    bool IsRunning(string exePath);

    /// <summary>Closes every process running from this .exe.</summary>
    void Close(string exePath);

    /// <summary>Starts the .exe as <paramref name="user"/>, in their signed-in session.</summary>
    void Start(string exePath, string user);

    void SetPriority(int processId, GamePriority priority);
}

/// <summary>
/// The system timer's resolution. The request belongs to the service's own process, so it ends by itself if the
/// service stops, and nothing about it needs journaling.
/// </summary>
public interface ITimerResolution
{
    /// <summary>Asks for the finest resolution (0.5 ms).</summary>
    void Raise();

    /// <summary>Drops the request, so Windows goes back to its default.</summary>
    void Release();
}

/// <summary>For PCs where processes can't be managed: nothing runs, so nothing is closed.</summary>
public sealed class NoProcesses : IProcesses
{
    public bool IsRunning(string exePath) => false;

    public void Close(string exePath) { }

    public void Start(string exePath, string user) =>
        throw new InvalidOperationException("Programs can't be started on this PC.");

    public void SetPriority(int processId, GamePriority priority) =>
        throw new InvalidOperationException("Process priority can't be changed on this PC.");
}

public sealed class NoTimerResolution : ITimerResolution
{
    public void Raise() => throw new InvalidOperationException("The timer resolution can't be changed on this PC.");

    public void Release() { }
}
