using System.Text.Json;
using Elynt.Core.Repair;

namespace Elynt.Core.Cleanup;

/// <summary>
/// The fixed folders cleanup may start from, resolved by the adapter. There is deliberately no root for the user's
/// profile itself: only AppData\Local, AppData\LocalLow and the user's Recycle Bin, so user folders (Documents,
/// Downloads, Desktop, Pictures, game saves) can't be named by construction.
/// </summary>
public enum CleanupRoot
{
    /// <summary>%windir%.</summary>
    Windows,
    /// <summary>%ProgramData%.</summary>
    ProgramData,
    /// <summary>The Windows drive's root, e.g. C:\.</summary>
    SystemDrive,
    /// <summary>The signed-in user's AppData\Local.</summary>
    UserLocalAppData,
    /// <summary>The signed-in user's AppData\LocalLow.</summary>
    UserLocalLow,
    /// <summary>The signed-in user's own Recycle Bin on the Windows drive ($Recycle.Bin\&lt;SID&gt;).</summary>
    UserRecycleBin,
}

/// <summary>How a cleanup category is offered.</summary>
public enum CleanupKind
{
    /// <summary>Caches Windows and apps rebuild on their own. Ticked by default, and the only kind weekly cleaning touches.</summary>
    Safe,
    /// <summary>Shader caches: safe to delete, but games stutter while they rebuild them. Unticked, with a warning.</summary>
    ShaderCache,
    /// <summary>Can't be undone (Windows.old, ResetBase, old drivers, Recycle Bin). Opt-in and needs a separate confirmation.</summary>
    Destructive,
}

/// <summary>Where a category's files are.</summary>
public abstract record CleanupSource;

/// <summary>
/// Files in one folder under a fixed root. A <c>*</c> path segment matches any one folder (e.g. a browser profile).
/// </summary>
/// <param name="RemoveFolder">Delete the folder itself too once it is empty (Windows.old).</param>
public sealed record FolderSource(
    CleanupRoot Root,
    string RelativePath,
    string Pattern = "*",
    bool IncludeSubfolders = true,
    bool RemoveFolder = false) : CleanupSource;

/// <summary>Windows' component store: superseded updates removed for good with DISM /ResetBase.</summary>
public sealed record ComponentStoreSource : CleanupSource;

/// <summary>Driver packages in the driver store that a newer version of the same driver has replaced.</summary>
public sealed record OldDriverPackagesSource : CleanupSource;

/// <summary>One thing cleanup can clear, as the catalogue defines it.</summary>
/// <param name="Warning">Shown before cleaning: the stutter warning, or what a destructive category can't put back.</param>
/// <param name="BlockingProcess">A program (without .exe) whose cache this is; the category is skipped while it runs.</param>
public sealed record CleanupCategory(
    string Id,
    string Name,
    string WhatItDoes,
    CleanupKind Kind,
    IReadOnlyList<CleanupSource> Sources,
    string? Warning = null,
    string? BlockingProcess = null,
    string? BlockingProgramName = null);

/// <summary>One category after a scan, as the Cleanup page shows it.</summary>
/// <param name="SizeBytes">What cleaning would free, or null when Windows can only tell by doing it (ResetBase).</param>
/// <param name="Locations">Every folder the scan measured, so the user sees exactly what would be cleared.</param>
/// <param name="SkippedReason">Why it can't be cleaned right now (its browser is open), or null.</param>
/// <param name="Ticked">Ticked by default: safe and not skipped.</param>
public sealed record CleanupCategoryView(
    string Id,
    string Name,
    string WhatItDoes,
    CleanupKind Kind,
    string? Warning,
    long? SizeBytes,
    int FileCount,
    IReadOnlyList<string> Locations,
    string? SkippedReason,
    bool Ticked)
{
    public bool NeedsConfirmation => Kind == CleanupKind.Destructive;
}

public sealed record CleanupScan(IReadOnlyList<CleanupCategoryView> Categories)
{
    /// <summary>What the ticked categories would free.</summary>
    public long TickedBytes => Categories.Where(c => c.Ticked).Sum(c => c.SizeBytes ?? 0);
}

public enum CleanupPhase
{
    Scanning,
    Cleaning,
}

/// <summary>Live progress from a scan or a clean: which category is being worked on.</summary>
/// <param name="Index">1-based.</param>
public sealed record CleanupProgress(CleanupPhase Phase, string CategoryId, string CategoryName, int Index, int Count);

/// <summary>What happened to one category during a clean.</summary>
/// <param name="InUse">Files that were open and so left alone.</param>
public sealed record CleanupOutcome(string CategoryId, string Name, bool Cleaned, long BytesFreed, int Removed, int InUse, string Message);

public sealed record CleanupReport(bool Succeeded, string Message, long BytesFreed, IReadOnlyList<CleanupOutcome> Categories);

/// <summary>Optional weekly cleaning of safe categories only. Off by default.</summary>
public sealed record CleanupSchedule(bool WeeklyEnabled, DateTimeOffset? LastRun)
{
    public static CleanupSchedule Off { get; } = new(false, null);
}

public static class CleanupSizes
{
    /// <summary>A size in plain words, e.g. "1.4 GB".</summary>
    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} bytes",
    };
}

public sealed record FolderMeasure(long Bytes, int Files);

public sealed record FolderCleanResult(int Removed, long BytesFreed, int InUse);

/// <summary>A driver package in the driver store, as pnputil lists it.</summary>
/// <param name="PublishedName">The oemNN.inf name pnputil deletes by.</param>
/// <param name="OriginalName">The vendor's .inf name; packages with the same one are versions of the same driver.</param>
/// <param name="InUse">Some device uses this package right now.</param>
public sealed record DriverPackage(
    string PublishedName,
    string OriginalName,
    string Provider,
    string ClassName,
    Version Version,
    DateOnly Date,
    long SizeBytes,
    bool InUse);

/// <summary>Everything cleanup may touch on the PC. Real adapter wraps Windows; the fake holds in-memory files.</summary>
public interface ICleanupSystem
{
    /// <summary>
    /// The existing folders a location names, as absolute paths, with <c>*</c> segments expanded. Never follows
    /// links out of the root and never returns anything outside it.
    /// </summary>
    IReadOnlyList<string> FindFolders(CleanupRoot root, string relativePath, string user);

    FolderMeasure Measure(string folder, string pattern, bool includeSubfolders);

    /// <summary>Deletes matching files. Files in use are skipped and counted.</summary>
    FolderCleanResult Clear(string folder, string pattern, bool includeSubfolders, bool removeFolder);

    /// <summary>Whether a program (by process name, without .exe) is running in any session.</summary>
    bool IsProcessRunning(string processName);

    IReadOnlyList<DriverPackage> ListDriverPackages();

    /// <summary>Deletes one driver package, never forcing one a device uses.</summary>
    /// <returns>Whether it was deleted.</returns>
    bool DeleteDriverPackage(string publishedName);

    /// <summary>Runs a Windows program to completion, passing each output line as it arrives.</summary>
    Task<int> RunAsync(ProgramRun run, Action<string> output, CancellationToken cancellation);
}

/// <summary>Each user's weekly cleaning choice, kept by the service.</summary>
public interface ICleanupScheduleStore
{
    CleanupSchedule Load(string user);

    void Save(string user, CleanupSchedule schedule);

    /// <summary>Users who turned weekly cleaning on.</summary>
    IReadOnlyList<string> UsersWithWeeklyCleaning();
}

public sealed class InMemoryCleanupScheduleStore : ICleanupScheduleStore
{
    private readonly Dictionary<string, CleanupSchedule> _schedules = [];

    public CleanupSchedule Load(string user)
    {
        lock (_schedules)
            return _schedules.GetValueOrDefault(user, CleanupSchedule.Off);
    }

    public void Save(string user, CleanupSchedule schedule)
    {
        lock (_schedules)
            _schedules[user] = schedule;
    }

    public IReadOnlyList<string> UsersWithWeeklyCleaning()
    {
        lock (_schedules)
            return _schedules.Where(s => s.Value.WeeklyEnabled).Select(s => s.Key).ToList();
    }
}

/// <summary>Every user's weekly cleaning choice in one JSON file.</summary>
public sealed class JsonFileCleanupScheduleStore(string path) : ICleanupScheduleStore
{
    private readonly Lock _gate = new();

    public CleanupSchedule Load(string user)
    {
        lock (_gate)
            return Read().GetValueOrDefault(user, CleanupSchedule.Off);
    }

    public void Save(string user, CleanupSchedule schedule)
    {
        lock (_gate)
        {
            var all = Read();
            all[user] = schedule;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(all));
            File.Move(temp, path, overwrite: true);
        }
    }

    public IReadOnlyList<string> UsersWithWeeklyCleaning()
    {
        lock (_gate)
            return Read().Where(s => s.Value.WeeklyEnabled).Select(s => s.Key).ToList();
    }

    private Dictionary<string, CleanupSchedule> Read() =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, CleanupSchedule>>(File.ReadAllText(path)) ?? []
            : [];
}
