using System.Text.RegularExpressions;
using Elynt.Core.Repair;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;

namespace Elynt.Core.Cleanup;

/// <summary>Scans and cleans for one user. Every folder the PC names is checked against the never-touch rules first.</summary>
internal sealed partial class CleanupBench(
    CleanupCatalogue catalogue,
    SystemAdapters system,
    ICleanupScheduleStore schedules,
    string user,
    TimeProvider clock,
    ILogger log)
{
    public static readonly TimeSpan WeeklyInterval = TimeSpan.FromDays(7);

    private static readonly ProgramRun ResetBase = new("dism.exe", ["/Online", "/Cleanup-Image", "/StartComponentCleanup", "/ResetBase"]);

    private ICleanupSystem? Pc => system.Cleanup;

    public Task<CleanupScan> ScanAsync(IProgress<CleanupProgress>? progress, CancellationToken cancellation)
    {
        if (Pc is null)
            return Task.FromResult(new CleanupScan([]));

        var views = new List<CleanupCategoryView>();
        for (var i = 0; i < catalogue.Categories.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var category = catalogue.Categories[i];
            progress?.Report(new(CleanupPhase.Scanning, category.Id, category.Name, i + 1, catalogue.Categories.Count));
            views.Add(Measure(category));
        }
        return Task.FromResult(new CleanupScan(views));
    }

    private CleanupCategoryView Measure(CleanupCategory category)
    {
        var skipped = SkipReason(category);
        long? size = 0;
        var files = 0;
        var locations = new List<string>();
        try
        {
            foreach (var source in category.Sources)
            {
                switch (source)
                {
                    case FolderSource folder:
                        foreach (var path in SafeFolders(folder))
                        {
                            var measured = Pc!.Measure(path, folder.Pattern, folder.IncludeSubfolders);
                            size += measured.Bytes;
                            files += measured.Files;
                            locations.Add(folder.Pattern == "*" ? path : $@"{path}\{folder.Pattern}");
                        }
                        break;
                    case ComponentStoreSource:
                        size = null;
                        locations.Add($"Windows component store ({ResetBase})");
                        break;
                    case OldDriverPackagesSource:
                        foreach (var package in OldDriverPackages())
                        {
                            size += package.SizeBytes;
                            files++;
                            locations.Add($"{package.PublishedName}: {package.Provider} {package.OriginalName} {package.Version} ({package.Date:yyyy-MM-dd})");
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't measure {Category}", category.Id);
            skipped ??= $"Couldn't check this: {ex.Message}";
        }

        return new(category.Id, category.Name, category.WhatItDoes, category.Kind, category.Warning, size, files, locations,
            skipped, Ticked: category.Kind == CleanupKind.Safe && skipped is null);
    }

    public async Task<CleanupReport> CleanAsync(
        IReadOnlyList<string> categoryIds, bool confirmDestructive, IProgress<CleanupProgress>? progress, CancellationToken cancellation)
    {
        if (Pc is null)
            return Refused("Cleanup isn't available here. Nothing was deleted.");
        if (categoryIds.Count == 0)
            return Refused("Nothing is ticked, so nothing was deleted.");

        var categories = new List<CleanupCategory>();
        foreach (var id in categoryIds.Distinct())
        {
            if (!catalogue.TryGet(id, out var category))
                return Refused($"There is no cleanup category called '{id}'. Nothing was deleted.");
            categories.Add(category);
        }

        var destructive = categories.Where(c => c.Kind == CleanupKind.Destructive).ToList();
        if (destructive.Count > 0 && !confirmDestructive)
            return Refused(
                $"{string.Join(", ", destructive.Select(c => c.Name))} can't be undone, so cleaning needs a separate confirmation first. " +
                $"{string.Join(" ", destructive.Select(c => c.Warning))} Nothing was deleted.");

        var outcomes = new List<CleanupOutcome>();
        for (var i = 0; i < categories.Count; i++)
        {
            var category = categories[i];
            progress?.Report(new(CleanupPhase.Cleaning, category.Id, category.Name, i + 1, categories.Count));
            if (cancellation.IsCancellationRequested)
            {
                outcomes.Add(new(category.Id, category.Name, false, 0, 0, 0, "Stopped before this was cleaned."));
                continue;
            }
            outcomes.Add(await CleanOneAsync(category, cancellation));
        }

        var freed = outcomes.Sum(o => o.BytesFreed);
        var failed = outcomes.Where(o => !o.Cleaned).ToList();
        var message = failed.Count == 0
            ? $"Done. Freed {Bytes(freed)}."
            : $"Freed {Bytes(freed)}. {failed.Count} of {outcomes.Count} couldn't be cleaned; see each one below.";
        log.LogInformation("Cleanup for {User}: {Categories}, freed {Bytes} bytes", user, string.Join(",", categories.Select(c => c.Id)), freed);
        return new(failed.Count == 0, message, freed, outcomes);
    }

    private async Task<CleanupOutcome> CleanOneAsync(CleanupCategory category, CancellationToken cancellation)
    {
        if (SkipReason(category) is { } skipped)
            return new(category.Id, category.Name, false, 0, 0, 0, $"Skipped: {skipped}");

        int removed = 0, inUse = 0;
        long freed = 0;
        var notes = new List<string>();
        try
        {
            foreach (var source in category.Sources)
            {
                switch (source)
                {
                    case FolderSource folder:
                        foreach (var path in SafeFolders(folder))
                        {
                            var result = Pc!.Clear(path, folder.Pattern, folder.IncludeSubfolders, folder.RemoveFolder);
                            removed += result.Removed;
                            inUse += result.InUse;
                            freed += result.BytesFreed;
                        }
                        break;
                    case ComponentStoreSource:
                        var exit = await Pc!.RunAsync(ResetBase, line => log.LogDebug("dism: {Line}", line), cancellation);
                        if (exit != 0)
                            return new(category.Id, category.Name, false, 0, 0, 0, $"DISM stopped with error {exit}. Nothing else was changed.");
                        notes.Add("Removed superseded Windows updates.");
                        break;
                    case OldDriverPackagesSource:
                        foreach (var package in OldDriverPackages())
                        {
                            if (Pc!.DeleteDriverPackage(package.PublishedName))
                            {
                                removed++;
                                freed += package.SizeBytes;
                            }
                            else
                            {
                                inUse++;
                            }
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return new(category.Id, category.Name, false, freed, removed, inUse, "Stopped before it finished.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Cleaning {Category} failed", category.Id);
            return new(category.Id, category.Name, false, freed, removed, inUse, $"Couldn't clean this: {ex.Message}");
        }

        var message = notes.Count > 0 ? string.Join(" ", notes) : $"Freed {Bytes(freed)}.";
        if (inUse > 0)
            message += $" {inUse} in use were left alone.";
        return new(category.Id, category.Name, true, freed, removed, inUse, message);
    }

    public CleanupSchedule GetSchedule() => schedules.Load(user);

    public CleanupSchedule SetWeekly(bool enabled)
    {
        var schedule = schedules.Load(user) with { WeeklyEnabled = enabled };
        schedules.Save(user, schedule);
        log.LogInformation("Weekly cleaning {State} for {User}", enabled ? "on" : "off", user);
        return schedule;
    }

    /// <summary>Weekly cleaning: only safe categories, never shader caches or destructive ones, and only when due.</summary>
    public async Task<CleanupReport?> RunWeeklyIfDueAsync(CancellationToken cancellation)
    {
        var schedule = schedules.Load(user);
        var now = clock.GetUtcNow();
        if (!schedule.WeeklyEnabled || (schedule.LastRun is { } last && now - last < WeeklyInterval))
            return null;

        var safe = catalogue.Categories.Where(c => c.Kind == CleanupKind.Safe).Select(c => c.Id).ToList();
        var report = await CleanAsync(safe, confirmDestructive: false, progress: null, cancellation);
        schedules.Save(user, schedule with { LastRun = now });
        return report;
    }

    private string? SkipReason(CleanupCategory category) =>
        category.BlockingProcess is { } process && Pc!.IsProcessRunning(process)
            ? $"{category.BlockingProgramName ?? process} is open. Close it, then scan again."
            : null;

    /// <summary>The folders a location names, minus anything the never-touch rules forbid or that isn't a real folder below a drive.</summary>
    private IEnumerable<string> SafeFolders(FolderSource source)
    {
        foreach (var path in Pc!.FindFolders(source.Root, source.RelativePath, user))
        {
            if (WhyUnsafe(path) is { } reason)
            {
                log.LogWarning("Cleanup refused {Path}: {Reason}", path, reason);
                continue;
            }
            yield return path;
        }
    }

    /// <summary>Why cleanup must never clear this folder, or null when it may.</summary>
    internal static string? WhyUnsafe(string path)
    {
        var normal = path.Replace('/', '\\').TrimEnd('\\');
        if (!AbsoluteFolder().IsMatch(normal) || normal.Split('\\').Any(part => part is ".." or "." or ""))
            return "not a plain folder below a drive";
        return HardRules.Violation(new FileTarget(normal));
    }

    /// <summary>For each driver (same .inf, provider and class), every version except the newest, unless a device uses it.</summary>
    private IEnumerable<DriverPackage> OldDriverPackages() =>
        Pc!.ListDriverPackages()
            .GroupBy(p => (Inf: p.OriginalName.ToLowerInvariant(), Provider: p.Provider.ToLowerInvariant(), Class: p.ClassName.ToLowerInvariant()))
            .SelectMany(group => group.OrderByDescending(p => p.Version).ThenByDescending(p => p.Date).Skip(1))
            .Where(p => !p.InUse)
            .OrderBy(p => p.PublishedName, StringComparer.OrdinalIgnoreCase);

    private static CleanupReport Refused(string message) => new(false, message, 0, []);

    private static string Bytes(long bytes) => CleanupSizes.Format(bytes);

    /// <summary>A drive letter and at least one folder below it: never a whole drive.</summary>
    [GeneratedRegex(@"^[A-Za-z]:\\[^\\]+", RegexOptions.IgnoreCase)]
    private static partial Regex AbsoluteFolder();
}
