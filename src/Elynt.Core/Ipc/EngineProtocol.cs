using System.Text.Json;
using System.Text.Json.Serialization;
using Elynt.Core.Apps;
using Elynt.Core.Bios;
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
[JsonDerivedType(typeof(GetBiosReportRequest), "getBiosReport")]
[JsonDerivedType(typeof(GetGpuTuningRequest), "getGpuTuning")]
[JsonDerivedType(typeof(SetGpuTuningRequest), "setGpuTuning")]
[JsonDerivedType(typeof(AutoTuneGpuRequest), "autoTuneGpu")]
[JsonDerivedType(typeof(ConfirmGpuTuningRequest), "confirmGpuTuning")]
[JsonDerivedType(typeof(RevertGpuTuningRequest), "revertGpuTuning")]
[JsonDerivedType(typeof(ResetGpuTuningRequest), "resetGpuTuning")]
public abstract record EngineRequest;

public sealed record ListTweaksRequest : EngineRequest;

public sealed record ApplyRequest(string TweakId, ApplyOptions? Options = null) : EngineRequest;

public sealed record RevertRequest(string TweakId) : EngineRequest;

public sealed record ListHistoryRequest : EngineRequest;

public sealed record UndoChangeRequest(Guid ChangeId) : EngineRequest;

public sealed record RestoreEverythingRequest : EngineRequest;

public sealed record ListAppsRequest : EngineRequest;

public sealed record InstallAppsRequest(IReadOnlyList<string> AppIds, bool AllRuntimes) : EngineRequest;

public sealed record GetBiosReportRequest : EngineRequest;

public sealed record GetGpuTuningRequest : EngineRequest;

public sealed record SetGpuTuningRequest(string DeviceId, IReadOnlyDictionary<string, int> Values) : EngineRequest;

public sealed record AutoTuneGpuRequest(string DeviceId, AutoTuneKind Kind) : EngineRequest;

public sealed record ConfirmGpuTuningRequest(string DeviceId) : EngineRequest;

public sealed record RevertGpuTuningRequest(string DeviceId) : EngineRequest;

public sealed record ResetGpuTuningRequest(string DeviceId) : EngineRequest;

/// <summary>
/// The reply to one request. <see cref="Error"/> is set only when the service itself failed. A line carrying
/// <see cref="Progress"/> is an event streamed while the command runs; the reply proper follows it.
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
    InstallProgress? Progress = null,
    GpuTuningReport? GpuTuning = null,
    TuningResult? Tuning = null)
{
    [JsonIgnore]
    public bool IsEvent => Progress is not null;
}

/// <summary>Newline-delimited JSON over a named pipe: one request line, one response line.</summary>
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

    /// <param name="sendEvent">Receives progress events, in order, on the thread running the command.</param>
    public static EngineResponse Dispatch(IEngine engine, EngineRequest request, Action<EngineResponse>? sendEvent = null) => request switch
    {
        ListTweaksRequest => new(Tweaks: engine.ListTweaks()),
        ApplyRequest apply => new(Result: engine.Apply(apply.TweakId, apply.Options)),
        RevertRequest revert => new(Result: engine.Revert(revert.TweakId)),
        ListHistoryRequest => new(History: engine.ListHistory()),
        UndoChangeRequest undo => new(Result: engine.UndoChange(undo.ChangeId)),
        RestoreEverythingRequest => new(Restore: engine.RestoreEverything()),
        ListAppsRequest => new(Apps: engine.ListApps()),
        InstallAppsRequest install => new(Install: engine.InstallApps(
            install.AppIds, install.AllRuntimes, new Relay<InstallProgress>(p => sendEvent?.Invoke(new(Progress: p))))),
        GetBiosReportRequest => new(Bios: engine.GetBiosReport()),
        GetGpuTuningRequest => new(GpuTuning: engine.GetGpuTuning()),
        SetGpuTuningRequest set => new(Tuning: engine.SetGpuTuning(set.DeviceId, set.Values)),
        AutoTuneGpuRequest auto => new(Tuning: engine.AutoTuneGpu(auto.DeviceId, auto.Kind)),
        ConfirmGpuTuningRequest confirm => new(Tuning: engine.ConfirmGpuTuning(confirm.DeviceId)),
        RevertGpuTuningRequest revert => new(Tuning: engine.RevertGpuTuning(revert.DeviceId)),
        ResetGpuTuningRequest reset => new(Tuning: engine.ResetGpuTuning(reset.DeviceId)),
        _ => new(Error: $"Unknown command {request.GetType().Name}."),
    };

    /// <summary>Passes progress on synchronously, so events keep their order (unlike <see cref="Progress{T}"/>).</summary>
    private sealed class Relay<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
