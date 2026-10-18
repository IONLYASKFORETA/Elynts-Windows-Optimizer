using Elynt.Core.Startup;

namespace Elynt.Windows;

/// <summary>
/// Task Scheduler. Definitions are read from the task store (System32\Tasks, readable by the service as SYSTEM);
/// enable, disable and delete go through schtasks so Task Scheduler itself makes the change.
/// </summary>
public sealed class WindowsScheduledTasks : IScheduledTasks
{
    private static readonly string Store = Path.Combine(SystemCommand.System32, "Tasks");

    public IReadOnlyList<ScheduledTaskInfo> List()
    {
        var tasks = new List<ScheduledTaskInfo>();
        if (!Directory.Exists(Store))
            return tasks;
        foreach (var file in Directory.EnumerateFiles(Store, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            var path = @"\" + Path.GetRelativePath(Store, file);
            try
            {
                if (ScheduledTaskXml.Parse(path, File.ReadAllText(file)) is { } task)
                    tasks.Add(task with { Command = Expand(task.Command), ExecutablePath = Expand(task.ExecutablePath) });
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return tasks;
    }

    public void SetEnabled(string path, bool enabled) =>
        SystemCommand.Run("schtasks.exe", ["/Change", "/TN", path, enabled ? "/ENABLE" : "/DISABLE"], TimeSpan.FromSeconds(30));

    public void Delete(string path) =>
        SystemCommand.Run("schtasks.exe", ["/Delete", "/TN", path, "/F"], TimeSpan.FromSeconds(30));

    private static string? Expand(string? text) => text is null ? null : Environment.ExpandEnvironmentVariables(text);
}

/// <summary>
/// Windows' own startup measurements for one user: the newest <c>&lt;SID&gt;_StartupInfo*.xml</c> the Diagnostic
/// Policy Service wrote after a sign-in.
/// </summary>
public sealed class WindowsStartupImpactLog(string userSid) : IStartupImpactLog
{
    private static readonly string Folder = Path.Combine(SystemCommand.System32, @"wdi\LogFiles\StartupInfo");

    public IReadOnlyList<StartupMeasurement> Read()
    {
        try
        {
            var newest = new DirectoryInfo(Folder)
                .EnumerateFiles($"{userSid}_StartupInfo*.xml")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            return newest is null ? [] : StartupInfoFile.Parse(File.ReadAllText(newest.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
