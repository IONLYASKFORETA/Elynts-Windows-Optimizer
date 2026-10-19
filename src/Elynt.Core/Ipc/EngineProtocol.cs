using System.Text.Json;
using System.Text.Json.Serialization;
using Elynt.Core.Apps;
using Elynt.Core.Bios;
using Elynt.Core.Services;
using Elynt.Core.Tweaks;
using Elynt.Core.Startup;
using Elynt.Core.Cleanup;
using Elynt.Core.UpdateControl;
using Elynt.Core.Drivers;
using Elynt.Core.Games;
using Elynt.Core.LowLevel;
using Elynt.Core.Repair;
using Elynt.Core.Sensors;
using Elynt.Core.Tuning;

namespace Elynt.Core.Ipc;

/// <summary>One request per <see cref="IEngine"/> command.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(ListTweaksRequest), "listTweaks")]
[JsonDerivedType(typeof(ApplyRequest), "apply")]
[JsonDerivedType(typeof(RevertRequest), "revert")]
[JsonDerivedType(typeof(ListHistoryRequest), "listHistory")]
[JsonDerivedType(typeof(UndoChangeRequest), "undoChange")]
[JsonDerivedType(typeof(RestoreEverythingRequest), "restoreEverything")]
[JsonDerivedType(typeof(ListAppsRequest), "listApps")]
[JsonDerivedType(typeof(InstallAppsRequest), "installApps")]
[JsonDerivedType(typeof(SearchAppsRequest), "searchApps")]
[JsonDerivedType(typeof(ListInstalledAppsRequest), "listInstalledApps")]
[JsonDerivedType(typeof(UpdateAppsRequest), "updateApps")]
[JsonDerivedType(typeof(UpdateAllAppsRequest), "updateAllApps")]
[JsonDerivedType(typeof(UninstallAppsRequest), "uninstallApps")]
[JsonDerivedType(typeof(ListRepairToolsRequest), "listRepairTools")]
[JsonDerivedType(typeof(RunRepairRequest), "runRepair")]
[JsonDerivedType(typeof(GetBiosReportRequest), "getBiosReport")]
[JsonDerivedType(typeof(ListServicesRequest), "listServices")]
[JsonDerivedType(typeof(SetServiceStartRequest), "setServiceStart")]
[JsonDerivedType(typeof(UndoServiceChangeRequest), "undoServiceChange")]
[JsonDerivedType(typeof(ListStartupItemsRequest), "listStartupItems")]
[JsonDerivedType(typeof(SetStartupItemEnabledRequest), "setStartupItemEnabled")]
[JsonDerivedType(typeof(DeleteStartupItemRequest), "deleteStartupItem")]
[JsonDerivedType(typeof(CheckNewStartupItemsRequest), "checkNewStartupItems")]
[JsonDerivedType(typeof(ScanCleanupRequest), "scanCleanup")]
[JsonDerivedType(typeof(CleanRequest), "clean")]
[JsonDerivedType(typeof(GetCleanupScheduleRequest), "getCleanupSchedule")]
[JsonDerivedType(typeof(SetWeeklyCleanupRequest), "setWeeklyCleanup")]
[JsonDerivedType(typeof(StartWindowsUpdateRequest), "startWindowsUpdate")]
[JsonDerivedType(typeof(GetWindowsUpdateStatusRequest), "getWindowsUpdateStatus")]
[JsonDerivedType(typeof(GetDriversRequest), "getDrivers")]
[JsonDerivedType(typeof(CheckDriverRequest), "checkDriver")]
[JsonDerivedType(typeof(InstallDriverRequest), "installDriver")]
[JsonDerivedType(typeof(ListGamesRequest), "listGames")]
[JsonDerivedType(typeof(AddGameRequest), "addGame")]
[JsonDerivedType(typeof(RemoveGameRequest), "removeGame")]
[JsonDerivedType(typeof(GetGpuTuningRequest), "getGpuTuning")]
[JsonDerivedType(typeof(SetGpuTuningRequest), "setGpuTuning")]
[JsonDerivedType(typeof(AutoTuneGpuRequest), "autoTuneGpu")]
[JsonDerivedType(typeof(ConfirmGpuTuningRequest), "confirmGpuTuning")]
[JsonDerivedType(typeof(RevertGpuTuningRequest), "revertGpuTuning")]
[JsonDerivedType(typeof(ResetGpuTuningRequest), "resetGpuTuning")]
[JsonDerivedType(typeof(AcceptGpuWarningRequest), "acceptGpuWarning")]

[JsonDerivedType(typeof(GetPawnIoRequest), "getPawnIo")]
[JsonDerivedType(typeof(InstallPawnIoRequest), "installPawnIo")]
[JsonDerivedType(typeof(UninstallPawnIoRequest), "uninstallPawnIo")]
[JsonDerivedType(typeof(GetSensorsRequest), "getSensors")]
public abstract record EngineRequest;

public sealed record ListTweaksRequest : EngineRequest;

public sealed record ApplyRequest(string TweakId, ApplyOptions? Options = null) : EngineRequest;

public sealed record RevertRequest(string TweakId) : EngineRequest;

public sealed record ListHistoryRequest : EngineRequest;

public sealed record UndoChangeRequest(Guid ChangeId) : EngineRequest;

public sealed record RestoreEverythingRequest : EngineRequest;

public sealed record ListAppsRequest : EngineRequest;

/// <summary>Streams an <see cref="InstallProgressEvent"/> per step of the queue, then the report.</summary>
public sealed record InstallAppsRequest(IReadOnlyList<string> AppIds, bool AllRuntimes, bool AllowNotReviewed = false) : EngineRequest;

public sealed record SearchAppsRequest(string Query) : EngineRequest;

public sealed record ListInstalledAppsRequest : EngineRequest;

/// <summary>Streams an <see cref="InstallProgressEvent"/> per step, like an install.</summary>
public sealed record UpdateAppsRequest(IReadOnlyList<string> AppIds) : EngineRequest;

/// <summary>Streams an <see cref="InstallProgressEvent"/> per step, like an install.</summary>
public sealed record UpdateAllAppsRequest : EngineRequest;

/// <summary>Streams an <see cref="InstallProgressEvent"/> per step, like an install.</summary>
public sealed record UninstallAppsRequest(IReadOnlyList<string> AppIds) : EngineRequest;

public sealed record ListRepairToolsRequest : EngineRequest;

/// <summary>Long-running: streams a <see cref="RepairProgressEvent"/> per step and output line, then the result.</summary>
public sealed record RunRepairRequest(string ToolId, bool Confirmed) : EngineRequest;

public sealed record GetBiosReportRequest : EngineRequest;

public sealed record ListServicesRequest : EngineRequest;

public sealed record SetServiceStartRequest(string ServiceName, ServiceStart Start, ApplyOptions? Options = null) : EngineRequest;

public sealed record UndoServiceChangeRequest(string ServiceName) : EngineRequest;

/// <summary>Long-running: streams a <see cref="CleanupProgressEvent"/> per category measured, then the scan.</summary>
public sealed record ScanCleanupRequest : EngineRequest;

/// <summary>Long-running: streams a <see cref="CleanupProgressEvent"/> per category cleaned, then the report.</summary>
public sealed record CleanRequest(IReadOnlyList<string> CategoryIds, bool ConfirmDestructive) : EngineRequest;

public sealed record GetCleanupScheduleRequest : EngineRequest;

public sealed record SetWeeklyCleanupRequest(bool Enabled) : EngineRequest;
public sealed record StartWindowsUpdateRequest : EngineRequest;

public sealed record GetWindowsUpdateStatusRequest : EngineRequest;

public sealed record GetDriversRequest : EngineRequest;

public sealed record CheckDriverRequest(DriverRequest Request) : EngineRequest;

/// <summary>Long-running: streams a <see cref="DriverInstallProgressEvent"/> per step, then the result.</summary>
public sealed record InstallDriverRequest(DriverRequest Request) : EngineRequest;

public sealed record ListGamesRequest : EngineRequest;

public sealed record AddGameRequest(string ExePath) : EngineRequest;

public sealed record RemoveGameRequest(string ExePath) : EngineRequest;

public sealed record GetGpuTuningRequest : EngineRequest;

public sealed record SetGpuTuningRequest(string DeviceId, IReadOnlyDictionary<string, int> Values) : EngineRequest;

public sealed record AutoTuneGpuRequest(string DeviceId, AutoTuneKind Kind) : EngineRequest;

public sealed record ConfirmGpuTuningRequest(string DeviceId) : EngineRequest;

public sealed record RevertGpuTuningRequest(string DeviceId) : EngineRequest;

public sealed record ResetGpuTuningRequest(string DeviceId) : EngineRequest;

public sealed record AcceptGpuWarningRequest(string DeviceId) : EngineRequest;

public sealed record GetPawnIoRequest : EngineRequest;

/// <summary>Long-running: downloads and runs PawnIO's setup.</summary>
public sealed record InstallPawnIoRequest(bool AcceptFaceitWarning) : EngineRequest;

/// <summary>Long-running: runs PawnIO's uninstaller.</summary>
public sealed record UninstallPawnIoRequest : EngineRequest;

public sealed record GetSensorsRequest : EngineRequest;

/// <summary>
/// Something a running command reports before its reply. Every command that streams progress uses this one
/// stream; each kind of event is one derived record.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "event")]
[JsonDerivedType(typeof(InstallProgressEvent), "installProgress")]
[JsonDerivedType(typeof(RepairProgressEvent), "repairProgress")]
[JsonDerivedType(typeof(CleanupProgressEvent), "cleanupProgress")]
[JsonDerivedType(typeof(DriverInstallProgressEvent), "driverInstallProgress")]
public abstract record EngineEvent;

public sealed record InstallProgressEvent(InstallProgress Progress) : EngineEvent;

public sealed record RepairProgressEvent(RepairProgress Progress) : EngineEvent;

public sealed record CleanupProgressEvent(CleanupProgress Progress) : EngineEvent;
public sealed record DriverInstallProgressEvent(DriverInstallProgress Progress) : EngineEvent;

/// <summary>
/// One line from the service. <see cref="Error"/> is set only when the service itself failed. A line carrying
/// <see cref="Event"/> is streamed while the command runs; any number of them come before the reply proper.
/// </summary>
public sealed record EngineResponse(
    IReadOnlyList<TweakView>? Tweaks = null,
    CommandResult? Result = null,
    string? Error = null,
    IReadOnlyList<ChangeView>? History = null,
    RestoreResult? Restore = null,
    BiosReport? Bios = null,
    IReadOnlyList<AppView>? Apps = null,
    InstallReport? Install = null,
    AppSearchReport? AppSearch = null,
    InstalledAppsReport? InstalledApps = null,
    IReadOnlyList<RepairToolView>? RepairTools = null,
    RepairResult? RepairResult = null,
    DriverReport? Drivers = null,
    DriverCheck? DriverCheck = null,
    DriverInstallResult? DriverInstall = null,
    IReadOnlyList<InstalledGame>? Games = null,
    GameCommandResult? GameResult = null,
    GpuTuningReport? GpuTuning = null,
    TuningResult? Tuning = null,
    UpdateRunStatus? WindowsUpdate = null,
    EngineEvent? Event = null,
    PawnIoReport? PawnIo = null,
    PawnIoResult? PawnIoResult = null,
    SensorReport? Sensors = null,
    IReadOnlyList<ServiceView>? Services = null,
    ServiceResult? ServiceResult = null,
    IReadOnlyList<StartupItemView>? StartupItems = null,
    StartupCommandResult? StartupResult = null,
    CleanupScan? CleanupScan = null,
    CleanupReport? CleanupReport = null,
    CleanupSchedule? CleanupSchedule = null)
{
    [JsonIgnore]
    public bool IsEvent => Event is not null;
}

/// <summary>
/// Newline-delimited JSON over a named pipe: one request line, then any number of event lines, then one response line.
/// </summary>
public static class EngineProtocol
{
    public const string PipeName = "Elynt.Optimizer.Engine";

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);

    public static T Deserialize<T>(string line) =>
        JsonSerializer.Deserialize<T>(line, Options) ?? throw new JsonException("Empty message.");

    /// <param name="sendEvent">Receives the command's events, in order, on the thread running the command.</param>
    public static async Task<EngineResponse> DispatchAsync(
        IEngine engine,
        EngineRequest request,
        Action<EngineEvent>? sendEvent = null,
        CancellationToken cancellation = default) => request switch
    {
        ListTweaksRequest => new(Tweaks: engine.ListTweaks()),
        ApplyRequest apply => new(Result: engine.Apply(apply.TweakId, apply.Options)),
        RevertRequest revert => new(Result: engine.Revert(revert.TweakId)),
        ListHistoryRequest => new(History: engine.ListHistory()),
        UndoChangeRequest undo => new(Result: engine.UndoChange(undo.ChangeId)),
        RestoreEverythingRequest => new(Restore: engine.RestoreEverything()),
        ListAppsRequest => new(Apps: engine.ListApps()),
        InstallAppsRequest install => new(Install: engine.InstallApps(
            install.AppIds, install.AllRuntimes, Relay<InstallProgress>(p => new InstallProgressEvent(p), sendEvent), install.AllowNotReviewed)),
        SearchAppsRequest search => new(AppSearch: engine.SearchApps(search.Query)),
        ListInstalledAppsRequest => new(InstalledApps: engine.ListInstalledApps()),
        UpdateAppsRequest update => new(Install: engine.UpdateApps(
            update.AppIds, Relay<InstallProgress>(p => new InstallProgressEvent(p), sendEvent))),
        UpdateAllAppsRequest => new(Install: engine.UpdateAllApps(Relay<InstallProgress>(p => new InstallProgressEvent(p), sendEvent))),
        UninstallAppsRequest uninstall => new(Install: engine.UninstallApps(
            uninstall.AppIds, Relay<InstallProgress>(p => new InstallProgressEvent(p), sendEvent))),
        ListRepairToolsRequest => new(RepairTools: engine.ListRepairTools()),
        RunRepairRequest run => new(RepairResult: await engine.RunRepairAsync(
            run.ToolId, run.Confirmed, Relay<RepairProgress>(p => new RepairProgressEvent(p), sendEvent), cancellation)),
        GetBiosReportRequest => new(Bios: engine.GetBiosReport()),
        ListServicesRequest => new(Services: engine.ListServices()),
        SetServiceStartRequest set => new(ServiceResult: engine.SetServiceStart(set.ServiceName, set.Start, set.Options)),
        UndoServiceChangeRequest undo => new(ServiceResult: engine.UndoServiceChange(undo.ServiceName)),
        ListStartupItemsRequest => new(StartupItems: engine.ListStartupItems()),
        SetStartupItemEnabledRequest set => new(StartupResult: engine.SetStartupItemEnabled(set.ItemId, set.Enabled)),
        CheckNewStartupItemsRequest => new(StartupItems: engine.CheckNewStartupItems()),
        DeleteStartupItemRequest delete => new(StartupResult: engine.DeleteStartupItem(delete.ItemId, delete.ConfirmDestructive)),
        ScanCleanupRequest => new(CleanupScan: await engine.ScanCleanupAsync(
            Relay<CleanupProgress>(p => new CleanupProgressEvent(p), sendEvent), cancellation)),
        CleanRequest clean => new(CleanupReport: await engine.CleanAsync(
            clean.CategoryIds, clean.ConfirmDestructive, Relay<CleanupProgress>(p => new CleanupProgressEvent(p), sendEvent), cancellation)),
        GetCleanupScheduleRequest => new(CleanupSchedule: engine.GetCleanupSchedule()),
        SetWeeklyCleanupRequest weekly => new(CleanupSchedule: engine.SetWeeklyCleanup(weekly.Enabled)),
        StartWindowsUpdateRequest => new(WindowsUpdate: engine.StartWindowsUpdate()),
        GetWindowsUpdateStatusRequest => new(WindowsUpdate: engine.GetWindowsUpdateStatus()),
        GetDriversRequest => new(Drivers: engine.GetDrivers()),
        CheckDriverRequest check => new(DriverCheck: engine.CheckDriver(check.Request)),
        InstallDriverRequest install => new(DriverInstall: engine.InstallDriver(
            install.Request, Relay<DriverInstallProgress>(p => new DriverInstallProgressEvent(p), sendEvent))),
        ListGamesRequest => new(Games: engine.ListGames()),
        AddGameRequest add => new(GameResult: engine.AddGame(add.ExePath)),
        RemoveGameRequest remove => new(GameResult: engine.RemoveGame(remove.ExePath)),
        GetGpuTuningRequest => new(GpuTuning: engine.GetGpuTuning()),
        SetGpuTuningRequest set => new(Tuning: engine.SetGpuTuning(set.DeviceId, set.Values)),
        AutoTuneGpuRequest auto => new(Tuning: engine.AutoTuneGpu(auto.DeviceId, auto.Kind)),
        ConfirmGpuTuningRequest confirm => new(Tuning: engine.ConfirmGpuTuning(confirm.DeviceId)),
        RevertGpuTuningRequest revert => new(Tuning: engine.RevertGpuTuning(revert.DeviceId)),
        ResetGpuTuningRequest reset => new(Tuning: engine.ResetGpuTuning(reset.DeviceId)),
        AcceptGpuWarningRequest accept => new(Tuning: engine.AcceptGpuWarning(accept.DeviceId)),

        GetPawnIoRequest => new(PawnIo: engine.GetPawnIo()),
        InstallPawnIoRequest install => new(PawnIoResult: engine.InstallPawnIo(install.AcceptFaceitWarning)),
        UninstallPawnIoRequest => new(PawnIoResult: engine.UninstallPawnIo()),
        GetSensorsRequest => new(Sensors: engine.GetSensors()),
        _ => new(Error: $"Unknown command {request.GetType().Name}."),
    };

    /// <summary>Whether the command can run for minutes, so it runs in its own lane and must not hold up other commands.</summary>
    public static bool IsLongRunning(EngineRequest request) =>
        request is RunRepairRequest or InstallDriverRequest or ScanCleanupRequest or CleanRequest or InstallPawnIoRequest or UninstallPawnIoRequest;

    /// <summary>Whether nothing else may run alongside the command, not even a long-running one (an app install, update or uninstall queue).</summary>
    public static bool IsExclusive(EngineRequest request) =>
        request is InstallAppsRequest or UpdateAppsRequest or UpdateAllAppsRequest or UninstallAppsRequest;

    private static IProgress<T> Relay<T>(Func<T, EngineEvent> toEvent, Action<EngineEvent>? sendEvent) =>
        new SyncProgress<T>(value => sendEvent?.Invoke(toEvent(value)));

    /// <summary>Passes progress on synchronously, so events keep their order (unlike <see cref="Progress{T}"/>).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
