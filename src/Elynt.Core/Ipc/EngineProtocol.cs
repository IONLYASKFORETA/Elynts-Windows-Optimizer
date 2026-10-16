using System.Text.Json;
using System.Text.Json.Serialization;
using Elynt.Core.Bios;
using Elynt.Core.Tuning;

namespace Elynt.Core.Ipc;

/// <summary>One request per <see cref="IEngine"/> command.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(ListTweaksRequest), "listTweaks")]
[JsonDerivedType(typeof(ApplyRequest), "apply")]
[JsonDerivedType(typeof(RevertRequest), "revert")]
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

public sealed record GetBiosReportRequest : EngineRequest;

public sealed record GetGpuTuningRequest : EngineRequest;

public sealed record SetGpuTuningRequest(string DeviceId, IReadOnlyDictionary<string, int> Values) : EngineRequest;

public sealed record AutoTuneGpuRequest(string DeviceId, AutoTuneKind Kind) : EngineRequest;

public sealed record ConfirmGpuTuningRequest(string DeviceId) : EngineRequest;

public sealed record RevertGpuTuningRequest(string DeviceId) : EngineRequest;

public sealed record ResetGpuTuningRequest(string DeviceId) : EngineRequest;

/// <summary>The reply to one request. <see cref="Error"/> is set only when the service itself failed.</summary>
public sealed record EngineResponse(
    IReadOnlyList<TweakView>? Tweaks = null,
    CommandResult? Result = null,
    string? Error = null,
    BiosReport? Bios = null,
    GpuTuningReport? GpuTuning = null,
    TuningResult? Tuning = null);

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

    public static EngineResponse Dispatch(IEngine engine, EngineRequest request) => request switch
    {
        ListTweaksRequest => new(Tweaks: engine.ListTweaks()),
        ApplyRequest apply => new(Result: engine.Apply(apply.TweakId, apply.Options)),
        RevertRequest revert => new(Result: engine.Revert(revert.TweakId)),
        GetBiosReportRequest => new(Bios: engine.GetBiosReport()),
        GetGpuTuningRequest => new(GpuTuning: engine.GetGpuTuning()),
        SetGpuTuningRequest set => new(Tuning: engine.SetGpuTuning(set.DeviceId, set.Values)),
        AutoTuneGpuRequest auto => new(Tuning: engine.AutoTuneGpu(auto.DeviceId, auto.Kind)),
        ConfirmGpuTuningRequest confirm => new(Tuning: engine.ConfirmGpuTuning(confirm.DeviceId)),
        RevertGpuTuningRequest revert => new(Tuning: engine.RevertGpuTuning(revert.DeviceId)),
        ResetGpuTuningRequest reset => new(Tuning: engine.ResetGpuTuning(reset.DeviceId)),
        _ => new(Error: $"Unknown command {request.GetType().Name}."),
    };
}
