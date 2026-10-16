using System.Text.Json;
using System.Text.Json.Serialization;
using Elynt.Core.Bios;
using Elynt.Core.Games;

namespace Elynt.Core.Ipc;

/// <summary>One request per <see cref="IEngine"/> command.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(ListTweaksRequest), "listTweaks")]
[JsonDerivedType(typeof(ApplyRequest), "apply")]
[JsonDerivedType(typeof(RevertRequest), "revert")]
[JsonDerivedType(typeof(GetBiosReportRequest), "getBiosReport")]
[JsonDerivedType(typeof(ListGamesRequest), "listGames")]
[JsonDerivedType(typeof(AddGameRequest), "addGame")]
[JsonDerivedType(typeof(RemoveGameRequest), "removeGame")]
public abstract record EngineRequest;

public sealed record ListTweaksRequest : EngineRequest;

public sealed record ApplyRequest(string TweakId, ApplyOptions? Options = null) : EngineRequest;

public sealed record RevertRequest(string TweakId) : EngineRequest;

public sealed record GetBiosReportRequest : EngineRequest;

public sealed record ListGamesRequest : EngineRequest;

public sealed record AddGameRequest(string ExePath) : EngineRequest;

public sealed record RemoveGameRequest(string ExePath) : EngineRequest;

/// <summary>The reply to one request. <see cref="Error"/> is set only when the service itself failed.</summary>
public sealed record EngineResponse(
    IReadOnlyList<TweakView>? Tweaks = null,
    CommandResult? Result = null,
    string? Error = null,
    BiosReport? Bios = null,
    IReadOnlyList<InstalledGame>? Games = null,
    GameCommandResult? GameResult = null);

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
        ListGamesRequest => new(Games: engine.ListGames()),
        AddGameRequest add => new(GameResult: engine.AddGame(add.ExePath)),
        RemoveGameRequest remove => new(GameResult: engine.RemoveGame(remove.ExePath)),
        _ => new(Error: $"Unknown command {request.GetType().Name}."),
    };
}
