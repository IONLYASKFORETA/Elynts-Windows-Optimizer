using Elynt.Core.Profiles;

namespace Elynt.Core.Tests.Profiles;

/// <summary>Running programs in memory, keyed by process id.</summary>
public sealed class FakeProcesses : IProcesses
{
    private readonly Dictionary<int, string> _running = [];
    private readonly Dictionary<int, GamePriority> _priorities = [];
    private int _nextId = 10_000;

    /// <summary>Every program started by <see cref="Start"/>, with the user it was started for.</summary>
    public List<(string ExePath, string User)> Started { get; } = [];

    public FakeProcesses Run(string exePath, int processId)
    {
        _running[processId] = exePath;
        return this;
    }

    public void Exit(int processId) => _running.Remove(processId);

    public GamePriority? PriorityOf(int processId) => _priorities.TryGetValue(processId, out var p) ? p : null;

    public bool IsRunning(string exePath) =>
        _running.Values.Any(path => string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase));

    public void Close(string exePath)
    {
        foreach (var id in _running.Where(p => string.Equals(p.Value, exePath, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToList())
            _running.Remove(id);
    }

    public void Start(string exePath, string user)
    {
        Started.Add((exePath, user));
        _running[_nextId++] = exePath;
    }

    public void SetPriority(int processId, GamePriority priority)
    {
        if (!_running.ContainsKey(processId))
            throw new InvalidOperationException($"Process {processId} isn't running.");
        _priorities[processId] = priority;
    }
}

public sealed class FakeTimerResolution : ITimerResolution
{
    public bool Raised { get; private set; }

    public void Raise() => Raised = true;

    public void Release() => Raised = false;
}
