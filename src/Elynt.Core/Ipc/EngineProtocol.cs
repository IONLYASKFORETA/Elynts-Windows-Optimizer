using System.Text.Json;
using Elynt.Core.Apps;
using System.Text.Json.Serialization;

namespace Elynt.Core.Ipc;

/// <summary>One request per <see cref="IEngine"/> command.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(ListTweaksRequest), "listTweaks")]
[JsonDerivedType(typeof(ApplyRequest), "apply")]
[JsonDerivedType(typeof(RevertRequest), "revert")]
[JsonDerivedType(typeof(ListAppsRequest), "listApps")]
[JsonDerivedType(typeof(InstallAppsRequest), "installApps")]
public abstract record EngineRequest;

public sealed record ListTweaksRequest : EngineRequest;

public sealed record ApplyRequest(string TweakId) : EngineRequest;

public sealed record RevertRequest(string TweakId) : EngineRequest;

public sealed record ListAppsRequest : EngineRequest;

public sealed record InstallAppsRequest(IReadOnlyList<string> AppIds, bool AllRuntimes) : EngineRequest;

/// <summary>
/// The reply to one request. <see cref="Error"/> is set only when the service itself failed. A line carrying
/// <see cref="Progress"/> is an event streamed while the command runs; the reply proper follows it.
/// </summary>
public sealed record EngineResponse(
    IReadOnlyList<TweakView>? Tweaks = null,
    CommandResult? Result = null,
    string? Error = null,
    IReadOnlyList<AppView>? Apps = null,
    InstallReport? Install = null,
    InstallProgress? Progress = null)
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
        ApplyRequest apply => new(Result: engine.Apply(apply.TweakId)),
        RevertRequest revert => new(Result: engine.Revert(revert.TweakId)),
        ListAppsRequest => new(Apps: engine.ListApps()),
        InstallAppsRequest install => new(Install: engine.InstallApps(
            install.AppIds, install.AllRuntimes, new Relay<InstallProgress>(p => sendEvent?.Invoke(new(Progress: p))))),
        _ => new(Error: $"Unknown command {request.GetType().Name}."),
    };

    /// <summary>Passes progress on synchronously, so events keep their order (unlike <see cref="Progress{T}"/>).</summary>
    private sealed class Relay<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
