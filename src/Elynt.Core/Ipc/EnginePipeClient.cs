using Elynt.Core.Tuning.Cpu;
using Elynt.Core.Games;
using System.IO.Pipes;
using System.Security.Principal;
using Elynt.Core.Apps;
using Elynt.Core.Repair;
using Elynt.Core.Bios;
using Elynt.Core.Services;
using Elynt.Core.Tweaks;
using Elynt.Core.Startup;
using Elynt.Core.Cleanup;
using Elynt.Core.UpdateControl;
using Elynt.Core.Drivers;
using Elynt.Core.Tuning;
using Elynt.Core.LowLevel;
using Elynt.Core.Sensors;

namespace Elynt.Core.Ipc;

/// <summary>
/// The UI's side of the pipe: one async method per <see cref="IEngine"/> command. A connection to the service carries
/// one command at a time, so a long job (<see cref="EngineProtocol.IsLongRunning"/>: a driver install, the CPU stress
/// test, a PawnIO install or uninstall, a driver clean or restore, a repair, a cleanup scan or clean) goes over a
/// connection of its own, the long-job connection, and every other command keeps answering on the main one while it
/// runs. Each long job added later (the driver latency test, Check my connection, the bufferbloat test, spot test runs)
/// joins <see cref="EngineProtocol.IsLongRunning"/> and so goes over the long-job connection too. Long jobs take turns
/// on it, as the service runs them one at a time anyway.
/// </summary>
public sealed partial class EnginePipeClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly TimeSpan _timeout;
    private readonly Connection _main;
    // Opened by the first long job, and again by the next one after a long job loses it.
    private Connection? _longJobs;
    private readonly SemaphoreSlim _longJobTurn = new(1, 1);
    private volatile bool _disposed;

    private EnginePipeClient(string pipeName, TimeSpan timeout, Connection main)
    {
        _pipeName = pipeName;
        _timeout = timeout;
        _main = main;
    }

    public static async Task<EnginePipeClient> ConnectAsync(
        string pipeName = EngineProtocol.PipeName,
        TimeSpan? timeout = null,
        CancellationToken cancellation = default)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(5);
        return new EnginePipeClient(pipeName, wait, await Connection.OpenAsync(pipeName, wait, cancellation));
    }

    /// <summary>
    /// The window's end of the service's pipe, not yet connected. It asks Windows to give the service a copy of the
    /// window's token as it was when the pipe opened, with every group in it. The service's administrator check needs
    /// that: an administrator account's unelevated window has Administrators only as a deny-only group, and Windows'
    /// default (the token tracked as it changes, and possibly cut down to its enabled groups) doesn't promise to keep it.
    /// </summary>
    public static NamedPipeClientStream NewPipe(string pipeName = EngineProtocol.PipeName) =>
        new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);

    public async Task<IReadOnlyList<TweakView>> ListTweaksAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListTweaksRequest(), cancellation)).Tweaks ?? throw Missing("tweaks");

    public async Task<CommandResult> ApplyAsync(string tweakId, ApplyOptions? options = null, CancellationToken cancellation = default) =>
        (await SendAsync(new ApplyRequest(tweakId, options), cancellation)).Result ?? throw Missing("result");

    public async Task<CommandResult> UndoTweakAsync(string tweakId, CancellationToken cancellation = default) =>
        (await SendAsync(new UndoTweakRequest(tweakId), cancellation)).Result ?? throw Missing("result");

    public async Task<IReadOnlyList<ChangeView>> ListHistoryAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListHistoryRequest(), cancellation)).History ?? throw Missing("history");

    public async Task<CommandResult> UndoChangeAsync(Guid changeId, CancellationToken cancellation = default) =>
        (await SendAsync(new UndoChangeRequest(changeId), cancellation)).Result ?? throw Missing("result");

    public async Task<RestoreResult> RestoreEverythingAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new RestoreEverythingRequest(), cancellation)).Restore ?? throw Missing("restore result");

    public async Task<IReadOnlyList<AppView>> ListAppsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListAppsRequest(), cancellation)).Apps ?? throw Missing("apps");

    /// <param name="progress">Receives each progress event as it arrives, before the report.</param>
    /// <param name="allowNotReviewed">The user accepted that apps outside the catalogue are not reviewed.</param>
    public async Task<InstallReport> InstallAppsAsync(
        IReadOnlyList<string> appIds,
        bool allRuntimes,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellation = default,
        bool allowNotReviewed = false) =>
        await RunQueueAsync(new InstallAppsRequest(appIds, allRuntimes, allowNotReviewed), progress, cancellation);

    public async Task<AppSearchReport> SearchAppsAsync(string query, CancellationToken cancellation = default) =>
        (await SendAsync(new SearchAppsRequest(query), cancellation)).AppSearch ?? throw Missing("search results");

    public async Task<InstalledAppsReport> ListInstalledAppsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListInstalledAppsRequest(), cancellation)).InstalledApps ?? throw Missing("installed apps");

    public Task<InstallReport> UpdateAppsAsync(
        IReadOnlyList<string> appIds, IProgress<InstallProgress>? progress = null, CancellationToken cancellation = default) =>
        RunQueueAsync(new UpdateAppsRequest(appIds), progress, cancellation);

    public Task<InstallReport> UpdateAllAppsAsync(IProgress<InstallProgress>? progress = null, CancellationToken cancellation = default) =>
        RunQueueAsync(new UpdateAllAppsRequest(), progress, cancellation);

    public Task<InstallReport> UninstallAppsAsync(
        IReadOnlyList<string> appIds, bool confirmed, IProgress<InstallProgress>? progress = null, CancellationToken cancellation = default) =>
        RunQueueAsync(new UninstallAppsRequest(appIds, confirmed), progress, cancellation);

    private async Task<InstallReport> RunQueueAsync(EngineRequest request, IProgress<InstallProgress>? progress, CancellationToken cancellation) =>
        (await SendAsync(request, cancellation,
            e => { if (e is InstallProgressEvent p) progress?.Report(p.Progress); })).Install
        ?? throw Missing("report");

    public async Task<IReadOnlyList<RepairToolView>> ListRepairToolsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListRepairToolsRequest(), cancellation)).RepairTools ?? throw Missing("repair tools");

    /// <summary>
    /// Runs a repair tool; <paramref name="progress"/> gets every progress line as it arrives. A long job, so it goes over
    /// the long-job connection and other commands keep answering meanwhile.
    /// </summary>
    public async Task<RepairResult> RunRepairAsync(
        string toolId, bool confirmed, IProgress<RepairProgress>? progress = null, CancellationToken cancellation = default) =>
        (await SendAsync(new RunRepairRequest(toolId, confirmed), cancellation,
            e => { if (e is RepairProgressEvent p) progress?.Report(p.Progress); })).RepairResult
        ?? throw Missing("repair result");

    public async Task<BiosReport> GetBiosReportAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new GetBiosReportRequest(), cancellation)).Bios ?? throw Missing("BIOS report");

    public async Task<IReadOnlyList<ServiceView>> ListServicesAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListServicesRequest(), cancellation)).Services ?? throw Missing("services");

    public async Task<ServiceResult> SetServiceStartAsync(
        string serviceName, ServiceStart start, ApplyOptions? options = null, CancellationToken cancellation = default) =>
        (await SendAsync(new SetServiceStartRequest(serviceName, start, options), cancellation)).ServiceResult ?? throw Missing("service result");

    public async Task<ServiceResult> UndoServiceChangeAsync(string serviceName, CancellationToken cancellation = default) =>
        (await SendAsync(new UndoServiceChangeRequest(serviceName), cancellation)).ServiceResult ?? throw Missing("service result");

    public async Task<IReadOnlyList<StartupItemView>> ListStartupItemsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListStartupItemsRequest(), cancellation)).StartupItems ?? throw Missing("startup items");

    public async Task<StartupCommandResult> SetStartupItemEnabledAsync(string itemId, bool enabled, CancellationToken cancellation = default) =>
        (await SendAsync(new SetStartupItemEnabledRequest(itemId, enabled), cancellation)).StartupResult ?? throw Missing("result");

    public async Task<StartupCommandResult> DeleteStartupItemAsync(string itemId, bool confirmDestructive, CancellationToken cancellation = default) =>
        (await SendAsync(new DeleteStartupItemRequest(itemId, confirmDestructive), cancellation)).StartupResult ?? throw Missing("result");

    public async Task<IReadOnlyList<StartupItemView>> CheckNewStartupItemsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new CheckNewStartupItemsRequest(), cancellation)).StartupItems ?? throw Missing("startup items");

    /// <summary>Measures every cleanup category. A long job, like a repair.</summary>
    public async Task<CleanupScan> ScanCleanupAsync(IProgress<CleanupProgress>? progress = null, CancellationToken cancellation = default) =>
        (await SendAsync(new ScanCleanupRequest(), cancellation,
            e => { if (e is CleanupProgressEvent p) progress?.Report(p.Progress); })).CleanupScan
        ?? throw Missing("cleanup scan");

    /// <summary>Cleans the ticked categories. A long job, like a repair.</summary>
    public async Task<CleanupReport> CleanAsync(
        IReadOnlyList<string> categoryIds, bool confirmDestructive, IProgress<CleanupProgress>? progress = null, CancellationToken cancellation = default) =>
        (await SendAsync(new CleanRequest(categoryIds, confirmDestructive), cancellation,
            e => { if (e is CleanupProgressEvent p) progress?.Report(p.Progress); })).CleanupReport
        ?? throw Missing("cleanup report");

    public async Task<CleanupSchedule> GetCleanupScheduleAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new GetCleanupScheduleRequest(), cancellation)).CleanupSchedule ?? throw Missing("cleanup schedule");

    public async Task<CleanupSchedule> SetWeeklyCleanupAsync(bool enabled, CancellationToken cancellation = default) =>
        (await SendAsync(new SetWeeklyCleanupRequest(enabled), cancellation)).CleanupSchedule ?? throw Missing("cleanup schedule");
    public async Task<UpdateRunStatus> StartWindowsUpdateAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new StartWindowsUpdateRequest(), cancellation)).WindowsUpdate ?? throw Missing("Windows Update status");

    public async Task<UpdateRunStatus> GetWindowsUpdateStatusAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new GetWindowsUpdateStatusRequest(), cancellation)).WindowsUpdate ?? throw Missing("Windows Update status");

    public async Task<DriverReport> GetDriversAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new GetDriversRequest(), cancellation)).Drivers ?? throw Missing("driver report");

    public async Task<DriverCheck> CheckDriverAsync(DriverRequest request, CancellationToken cancellation = default) =>
        (await SendAsync(new CheckDriverRequest(request), cancellation)).DriverCheck ?? throw Missing("driver check");

    public async Task<DriverInstallResult> InstallDriverAsync(
        DriverRequest request, IProgress<DriverInstallProgress>? progress = null, CancellationToken cancellation = default) =>
        (await SendAsync(new InstallDriverRequest(request), cancellation,
            e => { if (e is DriverInstallProgressEvent p) progress?.Report(p.Progress); })).DriverInstall
            ?? throw Missing("driver install result");

    public async Task<DriverCleaningReport> GetDriverCleaningAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new GetDriverCleaningRequest(), cancellation)).DriverCleaning ?? throw Missing("driver cleaning report");

    public async Task<DriverCleanResult> CleanDriversAsync(DriverCleanRequest request, CancellationToken cancellation = default) =>
        (await SendAsync(new CleanDriversRequest(request), cancellation)).DriverClean ?? throw Missing("driver clean result");

    public async Task<DriverCleanResult> RestoreDriversAsync(string backupId, CancellationToken cancellation = default) =>
        (await SendAsync(new RestoreDriversRequest(backupId), cancellation)).DriverClean ?? throw Missing("driver restore result");

    public async Task<IReadOnlyList<InstalledGame>> ListGamesAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListGamesRequest(), cancellation)).Games ?? throw Missing("games");

    public async Task<GameCommandResult> AddGameAsync(string exePath, CancellationToken cancellation = default) =>
        (await SendAsync(new AddGameRequest(exePath), cancellation)).GameResult ?? throw Missing("result");

    public async Task<GameCommandResult> RemoveGameAsync(string exePath, CancellationToken cancellation = default) =>
        (await SendAsync(new RemoveGameRequest(exePath), cancellation)).GameResult ?? throw Missing("result");

    public async Task<IReadOnlyList<Profiles.GameProfile>> ListProfilesAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListProfilesRequest(), cancellation)).Profiles ?? throw Missing("profiles");

    public async Task<Profiles.ProfileResult> SaveProfileAsync(Profiles.GameProfile profile, CancellationToken cancellation = default) =>
        (await SendAsync(new SaveProfileRequest(profile), cancellation)).ProfileResult ?? throw Missing("result");

    public async Task<Profiles.ProfileResult> DeleteProfileAsync(string exePath, CancellationToken cancellation = default) =>
        (await SendAsync(new DeleteProfileRequest(exePath), cancellation)).ProfileResult ?? throw Missing("result");

    public async Task<IReadOnlyList<Profiles.ProfileNote>> TakeProfileNotesAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new TakeProfileNotesRequest(), cancellation)).ProfileNotes ?? throw Missing("notes");

    public async Task<GpuTuningReport> GetGpuTuningAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new GetGpuTuningRequest(), cancellation)).GpuTuning ?? throw Missing("GPU tuning");

    public Task<TuningResult> SetGpuTuningAsync(string deviceId, IReadOnlyDictionary<string, int> values, CancellationToken cancellation = default) =>
        TuneAsync(new SetGpuTuningRequest(deviceId, values), cancellation);

    public Task<TuningResult> AutoTuneGpuAsync(string deviceId, AutoTuneKind kind, CancellationToken cancellation = default) =>
        TuneAsync(new AutoTuneGpuRequest(deviceId, kind), cancellation);

    public Task<TuningResult> ConfirmGpuTuningAsync(string deviceId, CancellationToken cancellation = default) =>
        TuneAsync(new ConfirmGpuTuningRequest(deviceId), cancellation);

    public Task<TuningResult> RevertGpuTuningAsync(string deviceId, CancellationToken cancellation = default) =>
        TuneAsync(new RevertGpuTuningRequest(deviceId), cancellation);

    public Task<TuningResult> ResetGpuTuningAsync(string deviceId, CancellationToken cancellation = default) =>
        TuneAsync(new ResetGpuTuningRequest(deviceId), cancellation);

    public Task<TuningResult> AcceptGpuWarningAsync(string deviceId, CancellationToken cancellation = default) =>
        TuneAsync(new AcceptGpuWarningRequest(deviceId), cancellation);

    public async Task<PawnIoReport> GetPawnIoAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new GetPawnIoRequest(), cancellation)).PawnIo ?? throw Missing("PawnIO status");

    /// <param name="acceptFaceitWarning">The user accepted the FACEIT warning. The service refuses to install without it.</param>
    public async Task<PawnIoResult> InstallPawnIoAsync(bool acceptFaceitWarning, CancellationToken cancellation = default) =>
        (await SendAsync(new InstallPawnIoRequest(acceptFaceitWarning), cancellation)).PawnIoResult ?? throw Missing("PawnIO result");

    public async Task<PawnIoResult> UninstallPawnIoAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new UninstallPawnIoRequest(), cancellation)).PawnIoResult ?? throw Missing("PawnIO result");

    public async Task<SensorReport> GetSensorsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new GetSensorsRequest(), cancellation)).Sensors ?? throw Missing("sensors");

    public async Task<CpuTuningReport> GetCpuTuningAsync(bool advanced, CancellationToken cancellation = default) =>
        (await SendAsync(new GetCpuTuningRequest(advanced), cancellation)).CpuTuning ?? throw Missing("CPU tuning");

    public Task<CpuTuningResult> SetCpuTuningAsync(CpuTune tune, CpuTuningOptions options, CancellationToken cancellation = default) =>
        CpuAsync(new SetCpuTuningRequest(tune, options), cancellation);

    public Task<CpuTuningResult> ConfirmCpuTuningAsync(CancellationToken cancellation = default) =>
        CpuAsync(new ConfirmCpuTuningRequest(), cancellation);

    public Task<CpuTuningResult> RevertCpuTuningAsync(CancellationToken cancellation = default) =>
        CpuAsync(new RevertCpuTuningRequest(), cancellation);

    public Task<CpuTuningResult> ResetCpuTuningAsync(CancellationToken cancellation = default) =>
        CpuAsync(new ResetCpuTuningRequest(), cancellation);

    /// <summary>Takes minutes: the service runs the stress test before replying. A long job, like a repair.</summary>
    public Task<CpuTuningResult> RunCpuStressTestAsync(CancellationToken cancellation = default) =>
        CpuAsync(new RunCpuStressTestRequest(), cancellation);

    public Task<CpuTuningResult> SetCpuAutoApplyAsync(bool on, CancellationToken cancellation = default) =>
        CpuAsync(new SetCpuAutoApplyRequest(on), cancellation);

    private async Task<CpuTuningResult> CpuAsync(EngineRequest request, CancellationToken cancellation) =>
        (await SendAsync(request, cancellation)).CpuResult ?? throw Missing("CPU tuning result");

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _main.DisposeAsync();
        if (Interlocked.Exchange(ref _longJobs, null) is { } longJobs)
            await longJobs.DisposeAsync();
    }

    private Task<EngineResponse> SendAsync(
        EngineRequest request,
        CancellationToken cancellation,
        Action<EngineEvent>? onEvent = null) =>
        EngineProtocol.IsLongRunning(request)
            ? SendLongJobAsync(request, cancellation, onEvent)
            : _main.SendAsync(request, cancellation, onEvent);

    private async Task<EngineResponse> SendLongJobAsync(EngineRequest request, CancellationToken cancellation, Action<EngineEvent>? onEvent)
    {
        await _longJobTurn.WaitAsync(cancellation);
        try
        {
            var connection = _longJobs ??= await OpenLongJobConnectionAsync(cancellation);
            try
            {
                return await connection.SendAsync(request, cancellation, onEvent);
            }
            catch (Exception ex) when (ex is not (EngineServiceException or AccountRefusedException))
            {
                // Lost, or cancelled with the reply still to come: either way the next long job needs a new connection.
                if (Interlocked.CompareExchange(ref _longJobs, null, connection) == connection)
                    await connection.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _longJobTurn.Release();
        }
    }

    private async Task<Connection> OpenLongJobConnectionAsync(CancellationToken cancellation)
    {
        Connection connection;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            connection = await Connection.OpenAsync(_pipeName, _timeout, cancellation);
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            throw ServiceConnectionLostException.BeforeSending(ex);
        }
        if (!_disposed)
            return connection;
        // The window let go of this client while the connection opened, so nothing is left to close it later.
        await connection.DisposeAsync();
        throw ServiceConnectionLostException.BeforeSending(null);
    }

    private async Task<TuningResult> TuneAsync(EngineRequest request, CancellationToken cancellation) =>
        (await SendAsync(request, cancellation)).Tuning ?? throw Missing("result");

    private static EngineServiceException Missing(string what) => new($"The service's reply had no {what}.");

    /// <summary>One pipe to the service. It carries one command at a time: a command's reply is read before the next is written.</summary>
    private sealed class Connection : IAsyncDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _oneAtATime = new(1, 1);

        private Connection(NamedPipeClientStream pipe)
        {
            _pipe = pipe;
            _reader = new StreamReader(pipe, leaveOpen: true);
            _writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        }

        public static async Task<Connection> OpenAsync(string pipeName, TimeSpan timeout, CancellationToken cancellation)
        {
            var pipe = NewPipe(pipeName);
            try
            {
                await pipe.ConnectAsync(timeout, cancellation);
                return new Connection(pipe);
            }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }
        }

        public async Task<EngineResponse> SendAsync(EngineRequest request, CancellationToken cancellation, Action<EngineEvent>? onEvent)
        {
            await _oneAtATime.WaitAsync(cancellation);
            try
            {
                try
                {
                    await _writer.WriteLineAsync(EngineProtocol.Serialize(request).AsMemory(), cancellation);
                }
                catch (Exception ex) when (IsLost(ex, cancellation))
                {
                    throw ServiceConnectionLostException.BeforeSending(ex);
                }
                EngineResponse response;
                while (true)
                {
                    string? line;
                    try
                    {
                        line = await _reader.ReadLineAsync(cancellation);
                    }
                    catch (Exception ex) when (IsLost(ex, cancellation))
                    {
                        throw ServiceConnectionLostException.PartWay(ex);
                    }
                    if (line is null)
                        throw ServiceConnectionLostException.PartWay(null);
                    response = EngineProtocol.Deserialize<EngineResponse>(line);
                    if (!response.IsEvent)
                        break;
                    onEvent?.Invoke(response.Event!);
                }
                if (response.Refused is not null)
                    throw new AccountRefusedException(response.Refused);
                if (response.Error is not null)
                    throw new EngineServiceException(response.Error);
                return response;
            }
            finally
            {
                _oneAtATime.Release();
            }
        }

        /// <summary>
        /// The pipe broke or closed: the service went, or the window let go of the client. A cancel the caller didn't
        /// ask for is the pipe closing under a read too.
        /// </summary>
        private static bool IsLost(Exception ex, CancellationToken cancellation) =>
            ex is IOException or ObjectDisposedException || (ex is OperationCanceledException && !cancellation.IsCancellationRequested);

        public async ValueTask DisposeAsync()
        {
            _reader.Dispose();
            try
            {
                await _writer.DisposeAsync();
            }
            catch (IOException)
            {
                // A command the service never got is still waiting to go; there's nowhere left to send it.
            }
            await _pipe.DisposeAsync();
        }
    }
}

public sealed class EngineServiceException(string message) : Exception(message);

/// <summary>
/// The window's connection to the service went, or couldn't be opened, before a command's reply came. The message says
/// so in plain words, including whether anything was changed, for the page that sent the command to show as it is.
/// </summary>
public sealed class ServiceConnectionLostException : Exception
{
    private ServiceConnectionLostException(string message, bool mayHaveChanged, Exception? cause) : base(message, cause) =>
        MayHaveChanged = mayHaveChanged;

    /// <summary>
    /// The command had reached the service when the connection went, so the service may have run some or all of it.
    /// False when it never got there, so nothing was changed.
    /// </summary>
    public bool MayHaveChanged { get; }

    /// <summary>The command never reached the service.</summary>
    internal static ServiceConnectionLostException BeforeSending(Exception? cause) => new(
        "Couldn't reach the service, so nothing was changed. Close the app and open it again, then try once more.",
        mayHaveChanged: false, cause);

    /// <summary>The command was sent and its reply never came.</summary>
    internal static ServiceConnectionLostException PartWay(Exception? cause) => new(
        "The connection to the service was lost part-way through, so the app can't tell whether anything was changed. " +
        "Close the app and open it again to see where things stand.",
        mayHaveChanged: true, cause);
}

/// <summary>
/// The service won't serve this Windows account, because it isn't an administrator account (ADR 0007). Nothing was
/// run for it. The message says so in the user's words, for the window to show in place of the sections.
/// </summary>
public sealed class AccountRefusedException(string message) : Exception(message);
