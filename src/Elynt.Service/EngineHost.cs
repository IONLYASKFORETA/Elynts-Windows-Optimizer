using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Elynt.Core;
using Elynt.Core.Cleanup;
using Elynt.Core.Drivers;
using Elynt.Core.Games;
using Elynt.Core.Ipc;
using Elynt.Core.Journal;
using Elynt.Core.Profiles;
using Elynt.Core.LowLevel;
using Elynt.Core.Sensors;
using Elynt.Core.Repair;
using Elynt.Core.Safety;
using Elynt.Core.Startup;
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
    ICleanupScheduleStore cleanupSchedules,
    GpuTuning gpuTuning,
    Core.Tuning.Cpu.CpuTuning cpuTuning,
    Elynt.Core.Drift.IDriftStore driftStore,
    IProfileStore profiles,
    ProfileNotes profileNotes,
    WindowsProcesses processes,
    WindowsTimerResolution timer,
    ProfileHost profileHost,
    Elynt.Core.CommandLane commandLane,
    ILoggerFactory loggers) : BackgroundService
{
    private readonly ISystemInfo _systemInfo = new WindowsSystemInfo();
    private readonly WindowsHardwareInfo _hardware = new();
    private readonly WindowsServiceManager _services = new();
    private readonly WindowsDevicePresence _devices = new();
    private readonly WindowsScheduledTasks _tasks = new();
    private readonly WindowsFileSignatures _signatures = new();
    private readonly JsonFileKnownStartupItems _knownStartupItems = new(ServicePaths.KnownStartupItems);
    private readonly WindowsDevices _pnpDevices = new();

    /// <summary>Shared by every vendor's latest-driver lookup.</summary>
    private static readonly HttpClient LookupClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly DriverAdapters _drivers = new(
        new WindowsDriverInventory(),
        [
            new NvidiaDriverSource(LookupClient),
            new AmdGpuDriverSource(LookupClient),
            new AmdChipsetDriverSource(LookupClient),
            new IntelGpuDriverSource(LookupClient),
            new IntelChipsetDriverSource(LookupClient),
        ],
        ServicePaths.Downloads,
        new HttpDownloader(new HttpClient { Timeout = TimeSpan.FromMinutes(30) }),
        new WinTrustSignatureVerifier(),
        new ProcessInstallerRunner(),
        new SevenZipExtractor());

    // PawnIO is only ever installed by the user's "Turn on PawnIO"; until then these only check whether it is there.
    private readonly PawnIoAdapters _pawnIo = new(
        new WindowsPawnIo(new ProcessInstallerRunner(TimeSpan.FromMinutes(5))),
        new PawnIoCpu(Path.Combine(ServicePaths.PawnIo, "modules")),
        ServicePaths.PawnIo,
        new HttpDownloader(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }),
        new WinTrustSignatureVerifier());

    // Best first: the GPU vendor's SDK, then Windows. PawnIO, when installed, comes before both.
    private readonly IReadOnlyList<ISensorSource> _sensors = [new NvmlSensors(), new WindowsSensors()];

    private readonly DriverCleaningAdapters _driverCleaning = new(
        new PnputilDriverStore(),
        new WindowsKernelDrivers(),
        ServicePaths.DriverBackups,
        new WingetDduSource(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }),
        ServicePaths.Ddu,
        new HttpDownloader(new HttpClient { Timeout = TimeSpan.FromMinutes(10) }),
        new WinTrustSignatureVerifier(),
        new ProcessInstallerRunner());
    private readonly WindowsGameSources _games = new();
    private readonly IGameStore _gameStore = new JsonFileGameStore(ServicePaths.Games);
    private readonly WindowsBootConfig _boot = new();
    private readonly WindowsPowerPlans _power = new();
    private readonly WindowsScanExclusions _scanExclusions = new();
    private bool _firstPipe = true;

    // One "check and install now" for the whole PC, so any app window can follow its progress.
    private readonly ManualUpdate _windowsUpdate = new(new WindowsUpdateAgent(), logger: loggers.CreateLogger<ManualUpdate>());

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var server = new EnginePipeServer(CreatePipe, EngineForClient, loggers.CreateLogger<EnginePipeServer>(), commandLane);
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
        profileHost.RecoverLeftovers(sid);

        return new Engine(
            catalogue,
            new SystemAdapters(
                new WindowsRegistry(sid),
                _systemInfo,
                _hardware,
                new WingetPackageManager(identity, loggers.CreateLogger<WingetPackageManager>()),
                new WindowsRepairSystem(sid),
                _drivers,
                _games,
                Cleanup: new WindowsCleanupSystem(sid))
            {
                Boot = _boot,
                Power = _power,
                ScanExclusions = _scanExclusions,
                Services = _services,
                Devices = _devices,
                PnpDevices = _pnpDevices,
                Startup = new StartupAdapters(
                    new WindowsStartupSources(sid), _tasks, _signatures, new WindowsStartupImpactLog(sid), _knownStartupItems),
                Apps = new WindowsRemovableApps(identity!, loggers.CreateLogger<WindowsRemovableApps>()),
                Features = new WindowsOptionalFeatures(),
                Processes = processes,
                Timer = timer,
                PawnIo = _pawnIo,
                Sensors = _sensors,
                DriverCleaning = _driverCleaning,
            },
            journal,
            sid,
            safetyNet,
            logger: loggers.CreateLogger<Engine>(),
            repairLogs: repairLogs,
            cleanupSchedules: cleanupSchedules,
            gameStore: _gameStore,
            gpuTuning: gpuTuning,
            cpuTuning: cpuTuning,
            windowsUpdate: _windowsUpdate,
            driftStore: driftStore,
            profiles: profiles,
            profileNotes: profileNotes);
    }
}
