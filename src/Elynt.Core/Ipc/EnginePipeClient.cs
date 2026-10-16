using Elynt.Core.Games;
using System.IO.Pipes;
using Elynt.Core.Apps;
using Elynt.Core.Repair;
using Elynt.Core.Bios;
using Elynt.Core.Startup;
using Elynt.Core.UpdateControl;
using Elynt.Core.Drivers;
using Elynt.Core.Tuning;

namespace Elynt.Core.Ipc;

/// <summary>The UI's side of the pipe: one async method per <see cref="IEngine"/> command.</summary>
public sealed class EnginePipeClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private EnginePipeClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, leaveOpen: true);
        _writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
    }

    public static async Task<EnginePipeClient> ConnectAsync(
        string pipeName = EngineProtocol.PipeName,
        TimeSpan? timeout = null,
        CancellationToken cancellation = default)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeout ?? TimeSpan.FromSeconds(5), cancellation);
            return new EnginePipeClient(pipe);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<TweakView>> ListTweaksAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListTweaksRequest(), cancellation)).Tweaks ?? throw Missing("tweaks");

    public async Task<CommandResult> ApplyAsync(string tweakId, ApplyOptions? options = null, CancellationToken cancellation = default) =>
        (await SendAsync(new ApplyRequest(tweakId, options), cancellation)).Result ?? throw Missing("result");

    public async Task<CommandResult> RevertAsync(string tweakId, CancellationToken cancellation = default) =>
        (await SendAsync(new RevertRequest(tweakId), cancellation)).Result ?? throw Missing("result");

    public async Task<IReadOnlyList<ChangeView>> ListHistoryAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListHistoryRequest(), cancellation)).History ?? throw Missing("history");

    public async Task<CommandResult> UndoChangeAsync(Guid changeId, CancellationToken cancellation = default) =>
        (await SendAsync(new UndoChangeRequest(changeId), cancellation)).Result ?? throw Missing("result");

    public async Task<RestoreResult> RestoreEverythingAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new RestoreEverythingRequest(), cancellation)).Restore ?? throw Missing("restore result");

    public async Task<IReadOnlyList<AppView>> ListAppsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListAppsRequest(), cancellation)).Apps ?? throw Missing("apps");

    /// <param name="progress">Receives each progress event as it arrives, before the report.</param>
    public async Task<InstallReport> InstallAppsAsync(
        IReadOnlyList<string> appIds,
        bool allRuntimes,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellation = default) =>
        (await SendAsync(new InstallAppsRequest(appIds, allRuntimes), cancellation,
            e => { if (e is InstallProgressEvent p) progress?.Report(p.Progress); })).Install
        ?? throw Missing("install report");

    public async Task<IReadOnlyList<RepairToolView>> ListRepairToolsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListRepairToolsRequest(), cancellation)).RepairTools ?? throw Missing("repair tools");

    /// <summary>
    /// Runs a repair tool; <paramref name="progress"/> gets every progress line as it arrives. Holds this connection
    /// until the tool finishes, so the UI uses a second connection for other commands meanwhile.
    /// </summary>
    public async Task<RepairResult> RunRepairAsync(
        string toolId, bool confirmed, IProgress<RepairProgress>? progress = null, CancellationToken cancellation = default) =>
        (await SendAsync(new RunRepairRequest(toolId, confirmed), cancellation,
            e => { if (e is RepairProgressEvent p) progress?.Report(p.Progress); })).RepairResult
        ?? throw Missing("repair result");

    public async Task<BiosReport> GetBiosReportAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new GetBiosReportRequest(), cancellation)).Bios ?? throw Missing("BIOS report");

    public async Task<IReadOnlyList<StartupItemView>> ListStartupItemsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListStartupItemsRequest(), cancellation)).StartupItems ?? throw Missing("startup items");

    public async Task<StartupCommandResult> SetStartupItemEnabledAsync(string itemId, bool enabled, CancellationToken cancellation = default) =>
        (await SendAsync(new SetStartupItemEnabledRequest(itemId, enabled), cancellation)).StartupResult ?? throw Missing("result");

    public async Task<StartupCommandResult> DeleteStartupItemAsync(string itemId, bool confirmDestructive, CancellationToken cancellation = default) =>
        (await SendAsync(new DeleteStartupItemRequest(itemId, confirmDestructive), cancellation)).StartupResult ?? throw Missing("result");

    public async Task<IReadOnlyList<StartupItemView>> CheckNewStartupItemsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new CheckNewStartupItemsRequest(), cancellation)).StartupItems ?? throw Missing("startup items");
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

    public async Task<IReadOnlyList<InstalledGame>> ListGamesAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListGamesRequest(), cancellation)).Games ?? throw Missing("games");

    public async Task<GameCommandResult> AddGameAsync(string exePath, CancellationToken cancellation = default) =>
        (await SendAsync(new AddGameRequest(exePath), cancellation)).GameResult ?? throw Missing("result");

    public async Task<GameCommandResult> RemoveGameAsync(string exePath, CancellationToken cancellation = default) =>
        (await SendAsync(new RemoveGameRequest(exePath), cancellation)).GameResult ?? throw Missing("result");

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

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        await _writer.DisposeAsync();
        await _pipe.DisposeAsync();
    }

    private async Task<EngineResponse> SendAsync(
        EngineRequest request,
        CancellationToken cancellation,
        Action<EngineEvent>? onEvent = null)
    {
        await _oneAtATime.WaitAsync(cancellation);
        try
        {
            await _writer.WriteLineAsync(EngineProtocol.Serialize(request).AsMemory(), cancellation);
            EngineResponse response;
            while (true)
            {
                var line = await _reader.ReadLineAsync(cancellation) ?? throw new IOException("The service closed the connection.");
                response = EngineProtocol.Deserialize<EngineResponse>(line);
                if (!response.IsEvent)
                    break;
                onEvent?.Invoke(response.Event!);
            }
            if (response.Error is not null)
                throw new EngineServiceException(response.Error);
            return response;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task<TuningResult> TuneAsync(EngineRequest request, CancellationToken cancellation) =>
        (await SendAsync(request, cancellation)).Tuning ?? throw Missing("result");

    private static EngineServiceException Missing(string what) => new($"The service's reply had no {what}.");
}

public sealed class EngineServiceException(string message) : Exception(message);
