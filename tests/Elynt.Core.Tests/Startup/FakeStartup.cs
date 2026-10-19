using Elynt.Core.Startup;

namespace Elynt.Core.Tests.Startup;

/// <summary>In-memory startup sources: Run values, Startup folder files and Store app startup tasks.</summary>
public sealed class FakeStartupSources : IStartupSources
{
    public List<RunValue> Run { get; } = [];
    public List<StartupFile> Files { get; } = [];
    public List<StoreStartupTask> Store { get; } = [];

    public IReadOnlyList<RunValue> RunValues() => [.. Run];
    public IReadOnlyList<StartupFile> StartupFiles() => [.. Files];
    public IReadOnlyList<StoreStartupTask> StoreStartupTasks() => [.. Store];

    public void DeleteRunValue(RunKey key, string name) =>
        Run.RemoveAll(r => r.Key == key && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void DeleteStartupFile(StartupFolder folder, string fileName) =>
        Files.RemoveAll(f => f.Folder == folder && f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
}

public sealed class FakeScheduledTasks : IScheduledTasks
{
    public List<ScheduledTaskInfo> Tasks { get; } = [];

    public IReadOnlyList<ScheduledTaskInfo> List() => [.. Tasks];

    public void SetEnabled(string path, bool enabled)
    {
        var i = Tasks.FindIndex(t => t.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
            throw new InvalidOperationException($"No task {path}.");
        Tasks[i] = Tasks[i] with { Enabled = enabled };
    }

    public void Delete(string path) => Tasks.RemoveAll(t => t.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    public bool IsEnabled(string path) => Tasks.Single(t => t.Path == path).Enabled;
}

public sealed class FakeFileSignatures : IFileSignatures
{
    public Dictionary<string, FileSignature> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FileSignature Inspect(string path) =>
        Files.TryGetValue(path, out var signature) ? signature : new FileSignature(SignatureStatus.Unsigned, null);
}

public sealed class FakeStartupImpactLog : IStartupImpactLog
{
    public List<StartupMeasurement> Measurements { get; } = [];

    public IReadOnlyList<StartupMeasurement> Read() => [.. Measurements];
}
