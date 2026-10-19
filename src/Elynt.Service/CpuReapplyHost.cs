using Elynt.Core.Tuning;
using Elynt.Core.Tuning.Cpu;
using Elynt.Windows;

namespace Elynt.Service;

/// <summary>
/// Tells CPU and GPU tuning when someone signs in, and CPU tuning when the PC wakes from sleep, so kept tunes come
/// back after sign-in and never at boot. Polls, because a hosted service gets no session-change or power messages.
/// </summary>
internal sealed class CpuReapplyHost(CpuTuning cpu, GpuTuning gpu, ILogger<CpuReapplyHost> log) : BackgroundService
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);

    /// <summary>More sleep than this between two polls counts as a resume.</summary>
    private static readonly TimeSpan SleepJump = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? user = null;
        var asleep = SafeTimeAsleep();
        using var timer = new PeriodicTimer(Poll);
        do
        {
            try
            {
                var now = WindowsSessionWatch.ConsoleUser();
                if (now is not null && now != user)
                {
                    cpu.OnLogon();
                    gpu.OnLogon();
                }
                user = now;

                var nowAsleep = SafeTimeAsleep();
                if (nowAsleep - asleep > SleepJump)
                    cpu.OnResume();
                asleep = nowAsleep;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Watching for sign-in and resume failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private static TimeSpan SafeTimeAsleep()
    {
        try
        {
            return WindowsSessionWatch.TimeAsleep();
        }
        catch (Exception)
        {
            return TimeSpan.Zero;
        }
    }
}
