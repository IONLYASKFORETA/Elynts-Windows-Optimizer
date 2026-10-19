using Elynt.Core.Memory;
using Elynt.Windows;

namespace Elynt.Service;

/// <summary>Runs the standby-list cleaner every few seconds; it does nothing unless its tweak is switched on.</summary>
internal sealed class StandbyListCleanerHost(ILogger<StandbyListCleanerHost> log) : BackgroundService
{
    // The switch is under HKLM, so no user's settings are involved.
    private readonly StandbyListCleaner _cleaner = new(new WindowsRegistry(), new WindowsMemoryLists());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(StandbyListCleaner.Interval);
        var failedBefore = false;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (_cleaner.Tick())
                    log.LogInformation("Emptied the standby list");
                failedBefore = false;
            }
            catch (Exception ex)
            {
                // Log a run of failures once, not every 10 seconds.
                if (!failedBefore)
                    log.LogError(ex, "The standby-list cleaner failed");
                failedBefore = true;
            }
        }
    }
}
