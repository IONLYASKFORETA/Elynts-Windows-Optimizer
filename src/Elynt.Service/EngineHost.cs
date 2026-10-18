using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Elynt.Core;
using Elynt.Core.Drivers;
using Elynt.Core.Games;
using Elynt.Core.Ipc;
using Elynt.Core.Journal;
using Elynt.Core.Repair;
using Elynt.Core.Safety;
using Elynt.Core.Tuning;
using Elynt.Core.Tweaks;
using Elynt.Core.UpdateControl;
using Elynt.Windows;

namespace Elynt.Service;

/// <summary>Serves Engine commands over the named pipe to the signed-in user's app window.</summary>
internal sealed class EngineHost(
    Catalogue catalogue,
    ChangeJournal journal,
    SafetyNet safetyNet,
    IRepairLogStore repairLogs,
    GpuTuning gpuTuning,
    ILoggerFactory loggers) : BackgroundService
{
    private readonly ISystemInfo _systemInfo = new WindowsSystemInfo();
    private readonly WindowsHardwareInfo _hardware = new();
    private readonly WindowsServiceManager _services = new();
    private readonly WindowsDevicePresence _devices = new();

    private readonly DriverAdapters _drivers = new(
        new WindowsDriverInventory(),
        [new NvidiaDriverSource(new HttpClient { Timeout = TimeSpan.FromSeconds(20) })],
        ServicePaths.Downloads,
        new HttpDownloader(new HttpClient { Timeout = TimeSpan.FromMinutes(30) }),
        new WinTrustSignatureVerifier(),
        new ProcessInstallerRunner());
    private readonly WindowsGameSources _games = new();
    private readonly IGameStore _gameStore = new JsonFileGameStore(ServicePaths.Games);
    private readonly WindowsBootConfig _boot = new();
    private readonly WindowsPowerPlans _power = new();
    private bool _firstPipe = true;

    // One "check and install now" for the whole PC, so any app window can follow its progress.
    private readonly ManualUpdate _windowsUpdate = new(new WindowsUpdateAgent(), logger: loggers.CreateLogger<ManualUpdate>());

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var server = new EnginePipeServer(CreatePipe, EngineForClient, loggers.CreateLogger<EnginePipeServer>());
        return server.RunAsync(stoppingToken);
    }

    /// <summary>
    /// Only interactively signed-in users may connect; network logons are denied outright. The app window runs
    /// without admin rights, so this is what lets it talk to the elevated service, and nothing else can.
    /// </summary>
    private NamedPipeServerStream CreatePipe()
    {
        // The first instance must be ours, so another program cannot already be holding the name.
        var options = _firstPipe ? PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance : PipeOptions.Asynchronous;
        _firstPipe = false;
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        // Lets the service also run from an elevated console while developing: it may open more pipe instances, but
        // connecting is still only for interactive users.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.CreateNewInstance | PipeAccessRights.ReadPermissions, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            EngineProtocol.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            options,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    /// <summary>Commands act on the settings of the user who sent them, identified by impersonating the pipe client.</summary>
    private Engine EngineForClient(NamedPipeServerStream pipe)
    {
        // The identity is kept for the connection's life: per-user app installs run winget with it.
        WindowsIdentity? identity = null;
        pipe.RunAsClient(() => identity = WindowsIdentity.GetCurrent(TokenAccessLevels.MaximumAllowed));
        var sid = identity?.User?.Value ?? throw new InvalidOperationException("Couldn't tell which user is connected.");

        return new Engine(
            catalogue,
            new SystemAdapters(
                new WindowsRegistry(sid),
                _systemInfo,
                _hardware,
                new WingetPackageManager(identity, loggers.CreateLogger<WingetPackageManager>()),
                new WindowsRepairSystem(sid),
                _drivers,
                _games) { Boot = _boot, Power = _power, Services = _services, Devices = _devices },
            journal,
            sid,
            safetyNet,
            logger: loggers.CreateLogger<Engine>(),
            repairLogs: repairLogs,
            gameStore: _gameStore,
            gpuTuning: gpuTuning,
            windowsUpdate: _windowsUpdate);
    }
}
