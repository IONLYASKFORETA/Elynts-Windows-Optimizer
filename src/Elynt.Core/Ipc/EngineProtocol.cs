using System.Text.Json;
using System.Text.Json.Serialization;
using Elynt.Core.Apps;
using Elynt.Core.Bios;
using Elynt.Core.Cleanup;
using Elynt.Core.Repair;

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
[JsonDerivedType(typeof(ListRepairToolsRequest), "listRepairTools")]
[JsonDerivedType(typeof(RunRepairRequest), "runRepair")]
[JsonDerivedType(typeof(GetBiosReportRequest), "getBiosReport")]
[JsonDerivedType(typeof(ScanCleanupRequest), "scanCleanup")]
[JsonDerivedType(typeof(CleanRequest), "clean")]
[JsonDerivedType(typeof(GetCleanupScheduleRequest), "getCleanupSchedule")]
[JsonDerivedType(typeof(SetWeeklyCleanupRequest), "setWeeklyCleanup")]
public abstract record EngineRequest;

public sealed record ListTweaksRequest : EngineRequest;

public sealed record ApplyRequest(string TweakId, ApplyOptions? Options = null) : EngineRequest;

public sealed record RevertRequest(string TweakId) : EngineRequest;

public sealed record ListHistoryRequest : EngineRequest;

public sealed record UndoChangeRequest(Guid ChangeId) : EngineRequest;

public sealed record RestoreEverythingRequest : EngineRequest;

public sealed record ListAppsRequest : EngineRequest;

/// <summary>Streams an <see cref="InstallProgressEvent"/> per step of the queue, then the report.</summary>
public sealed record InstallAppsRequest(IReadOnlyList<string> AppIds, bool AllRuntimes) : EngineRequest;

public sealed record ListRepairToolsRequest : EngineRequest;

/// <summary>Long-running: streams a <see cref="RepairProgressEvent"/> per step and output line, then the result.</summary>
public sealed record RunRepairRequest(string ToolId, bool Confirmed) : EngineRequest;

public sealed record GetBiosReportRequest : EngineRequest;

/// <summary>Long-running: streams a <see cref="CleanupProgressEvent"/> per category measured, then the scan.</summary>
public sealed record ScanCleanupRequest : EngineRequest;

/// <summary>Long-running: streams a <see cref="CleanupProgressEvent"/> per category cleaned, then the report.</summary>
public sealed record CleanRequest(IReadOnlyList<string> CategoryIds, bool ConfirmDestructive) : EngineRequest;

public sealed record GetCleanupScheduleRequest : EngineRequest;

public sealed record SetWeeklyCleanupRequest(bool Enabled) : EngineRequest;

/// <summary>
/// Something a running command reports before its reply. Every command that streams progress uses this one
/// stream; each kind of event is one derived record.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "event")]
[JsonDerivedType(typeof(InstallProgressEvent), "installProgress")]
[JsonDerivedType(typeof(RepairProgressEvent), "repairProgress")]
[JsonDerivedType(typeof(CleanupProgressEvent), "cleanupProgress")]
public abstract record EngineEvent;

public sealed record InstallProgressEvent(InstallProgress Progress) : EngineEvent;

public sealed record RepairProgressEvent(RepairProgress Progress) : EngineEvent;

public sealed record CleanupProgressEvent(CleanupProgress Progress) : EngineEvent;

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
    IReadOnlyList<RepairToolView>? RepairTools = null,
    RepairResult? RepairResult = null,
    EngineEvent? Event = null,
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
            install.AppIds, install.AllRuntimes, Relay<InstallProgress>(p => new InstallProgressEvent(p), sendEvent))),
        ListRepairToolsRequest => new(RepairTools: engine.ListRepairTools()),
        RunRepairRequest run => new(RepairResult: await engine.RunRepairAsync(
            run.ToolId, run.Confirmed, Relay<RepairProgress>(p => new RepairProgressEvent(p), sendEvent), cancellation)),
        GetBiosReportRequest => new(Bios: engine.GetBiosReport()),
        ScanCleanupRequest => new(CleanupScan: await engine.ScanCleanupAsync(
            Relay<CleanupProgress>(p => new CleanupProgressEvent(p), sendEvent), cancellation)),
        CleanRequest clean => new(CleanupReport: await engine.CleanAsync(
            clean.CategoryIds, clean.ConfirmDestructive, Relay<CleanupProgress>(p => new CleanupProgressEvent(p), sendEvent), cancellation)),
        GetCleanupScheduleRequest => new(CleanupSchedule: engine.GetCleanupSchedule()),
        SetWeeklyCleanupRequest weekly => new(CleanupSchedule: engine.SetWeeklyCleanup(weekly.Enabled)),
        _ => new(Error: $"Unknown command {request.GetType().Name}."),
    };

    /// <summary>Whether the command can run for minutes, so it runs in its own lane and must not hold up other commands.</summary>
    public static bool IsLongRunning(EngineRequest request) => request is RunRepairRequest or ScanCleanupRequest or CleanRequest;

    /// <summary>Whether nothing else may run alongside the command, not even a long-running one (an app install queue).</summary>
    public static bool IsExclusive(EngineRequest request) => request is InstallAppsRequest;

    private static IProgress<T> Relay<T>(Func<T, EngineEvent> toEvent, Action<EngineEvent>? sendEvent) =>
        new SyncProgress<T>(value => sendEvent?.Invoke(toEvent(value)));

    /// <summary>Passes progress on synchronously, so events keep their order (unlike <see cref="Progress{T}"/>).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
