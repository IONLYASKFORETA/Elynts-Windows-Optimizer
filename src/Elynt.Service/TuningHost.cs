using Elynt.Core.Tuning;
using Elynt.Core.Tuning.Cpu;

namespace Elynt.Service;

/// <summary>
/// Opens the hardware-tuning session when the service starts and closes it cleanly when it stops. Registered before
/// the pipe, so after a crash or freeze the GPU is back on defaults before the app can send any command, and stopped
/// after it, so unconfirmed changes go back before the session is marked clean.
/// </summary>
internal sealed class TuningHost(TuningSafety safety, GpuTuning gpu, CpuTuning cpu, ILogger<TuningHost> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var start = safety.BeginSession();
            if (start.Unclean)
                log.LogWarning("The service didn't stop cleanly last time; tuned hardware goes back to defaults");
            gpu.RecoverAtStartup(start);
            cpu.RecoverAtStartup(start);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Recovering hardware tuning at startup failed");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // The CPU first: GPU shutdown ends the session, which must come after every unkept change went back.
            cpu.Shutdown();
            gpu.Shutdown();
        }
        catch (Exception ex)
        {
            // The session stays open, so the next start treats this as unclean and resets to defaults.
            log.LogError(ex, "Closing the tuning session failed");
        }
        return Task.CompletedTask;
    }
}
