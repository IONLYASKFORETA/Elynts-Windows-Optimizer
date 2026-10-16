using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elynt.Core.Ipc;

/// <summary>One request per <see cref="IEngine"/> command.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(ListTweaksRequest), "listTweaks")]
[JsonDerivedType(typeof(ApplyRequest), "apply")]
[JsonDerivedType(typeof(RevertRequest), "revert")]
public abstract record EngineRequest;

public sealed record ListTweaksRequest : EngineRequest;

public sealed record ApplyRequest(string TweakId, ApplyOptions? Options = null) : EngineRequest;

public sealed record RevertRequest(string TweakId) : EngineRequest;

/// <summary>The reply to one request. <see cref="Error"/> is set only when the service itself failed.</summary>
public sealed record EngineResponse(IReadOnlyList<TweakView>? Tweaks = null, CommandResult? Result = null, string? Error = null);

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
        _ => new(Error: $"Unknown command {request.GetType().Name}."),
    };
}
