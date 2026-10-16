using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elynt.Core.Ipc;

/// <summary>One request per <see cref="IEngine"/> command.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(ListTweaksRequest), "listTweaks")]
[JsonDerivedType(typeof(ApplyRequest), "apply")]
[JsonDerivedType(typeof(RevertRequest), "revert")]
[JsonDerivedType(typeof(ListHistoryRequest), "listHistory")]
[JsonDerivedType(typeof(UndoChangeRequest), "undoChange")]
[JsonDerivedType(typeof(RestoreEverythingRequest), "restoreEverything")]
public abstract record EngineRequest;

public sealed record ListTweaksRequest : EngineRequest;

public sealed record ApplyRequest(string TweakId) : EngineRequest;

public sealed record RevertRequest(string TweakId) : EngineRequest;

public sealed record ListHistoryRequest : EngineRequest;

public sealed record UndoChangeRequest(Guid ChangeId) : EngineRequest;

public sealed record RestoreEverythingRequest : EngineRequest;

/// <summary>The reply to one request. <see cref="Error"/> is set only when the service itself failed.</summary>
public sealed record EngineResponse(
    IReadOnlyList<TweakView>? Tweaks = null,
    CommandResult? Result = null,
    string? Error = null,
    IReadOnlyList<ChangeView>? History = null,
    RestoreResult? Restore = null);

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
        ApplyRequest apply => new(Result: engine.Apply(apply.TweakId)),
        RevertRequest revert => new(Result: engine.Revert(revert.TweakId)),
        ListHistoryRequest => new(History: engine.ListHistory()),
        UndoChangeRequest undo => new(Result: engine.UndoChange(undo.ChangeId)),
        RestoreEverythingRequest => new(Restore: engine.RestoreEverything()),
        _ => new(Error: $"Unknown command {request.GetType().Name}."),
    };
}
