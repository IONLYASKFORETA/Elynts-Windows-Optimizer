using Elynt.Core;
using Elynt.Core.Cleanup;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;
using Elynt.Windows;

namespace Elynt.Service;

/// <summary>
/// Optional weekly cleaning (off by default). Every hour, for each user who turned it on, cleans the safe categories
/// once a week has passed. Shader caches and destructive categories are never part of it; the Engine enforces that.
/// </summary>
internal sealed class WeeklyCleanupService(
    Catalogue catalogue,
    ChangeJournal journal,
    ICleanupScheduleStore schedules,
    ILoggerFactory loggers) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

    private readonly ILogger _log = loggers.CreateLogger<WeeklyCleanupService>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let sign-in and startup settle before the first check.
        await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        using var timer = new PeriodicTimer(CheckEvery);
        do
        {
            foreach (var sid in schedules.UsersWithWeeklyCleaning())
            {
                try
                {
                    var engine = new Engine(
                        catalogue,
                        new SystemAdapters(new WindowsRegistry(sid), new WindowsSystemInfo(), Cleanup: new WindowsCleanupSystem(sid)),
                        journal,
                        sid,
                        logger: loggers.CreateLogger<Engine>(),
                        cleanupSchedules: schedules);
                    if (await engine.RunWeeklyCleanupIfDueAsync(stoppingToken) is { } report)
                        _log.LogInformation("Weekly cleaning for {User}: {Message}", sid, report.Message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Weekly cleaning for {User} failed", sid);
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
