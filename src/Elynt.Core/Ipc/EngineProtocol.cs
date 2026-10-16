using System.Text.Json;
using System.Text.Json.Serialization;
using Elynt.Core.Repair;

namespace Elynt.Core.Ipc;

/// <summary>One request per <see cref="IEngine"/> command.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(ListTweaksRequest), "listTweaks")]
[JsonDerivedType(typeof(ApplyRequest), "apply")]
[JsonDerivedType(typeof(RevertRequest), "revert")]
[JsonDerivedType(typeof(ListRepairToolsRequest), "listRepairTools")]
[JsonDerivedType(typeof(RunRepairRequest), "runRepair")]
public abstract record EngineRequest;

public sealed record ListTweaksRequest : EngineRequest;

public sealed record ApplyRequest(string TweakId) : EngineRequest;

public sealed record RevertRequest(string TweakId) : EngineRequest;

public sealed record ListRepairToolsRequest : EngineRequest;

/// <summary>Long-running: the service answers with any number of <see cref="EngineResponse.RepairProgress"/> lines, then the result.</summary>
public sealed record RunRepairRequest(string ToolId, bool Confirmed) : EngineRequest;

/// <summary>
/// The reply to one request. <see cref="Error"/> is set only when the service itself failed. A response carrying
/// <see cref="RepairProgress"/> is interim: more lines follow until the final one.
/// </summary>
public sealed record EngineResponse(
    IReadOnlyList<TweakView>? Tweaks = null,
    CommandResult? Result = null,
    string? Error = null,
    IReadOnlyList<RepairToolView>? RepairTools = null,
    RepairResult? RepairResult = null,
    RepairProgress? RepairProgress = null);

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

    /// <param name="sendInterim">Sends an interim response (progress) before the final one. Called synchronously.</param>
    public static async Task<EngineResponse> DispatchAsync(
        IEngine engine, EngineRequest request, Action<EngineResponse> sendInterim, CancellationToken cancellation) => request switch
    {
        ListTweaksRequest => new(Tweaks: engine.ListTweaks()),
        ApplyRequest apply => new(Result: engine.Apply(apply.TweakId)),
        RevertRequest revert => new(Result: engine.Revert(revert.TweakId)),
        ListRepairToolsRequest => new(RepairTools: engine.ListRepairTools()),
        RunRepairRequest run => new(RepairResult: await engine.RunRepairAsync(
            run.ToolId, run.Confirmed, new Relay(p => sendInterim(new(RepairProgress: p))), cancellation)),
        _ => new(Error: $"Unknown command {request.GetType().Name}."),
    };

    /// <summary>Whether the command can run for minutes, so it must not hold up other commands.</summary>
    public static bool IsLongRunning(EngineRequest request) => request is RunRepairRequest;

    /// <summary>Passes progress on synchronously, so every line is sent before the final response.</summary>
    private sealed class Relay(Action<RepairProgress> report) : IProgress<RepairProgress>
    {
        public void Report(RepairProgress value) => report(value);
    }
}
