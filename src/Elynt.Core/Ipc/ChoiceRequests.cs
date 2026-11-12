namespace Elynt.Core.Ipc;

/// <param name="TweakId">The option chosen, or null for the choice's default.</param>
public sealed record ChooseRequest(string ChoiceId, string? TweakId, ApplyOptions? Options = null) : EngineRequest;

/// <summary>The pick-one choices' side of the UI's client.</summary>
public sealed partial class EnginePipeClient
{
    /// <param name="tweakId">The option chosen, or null for the choice's default.</param>
    public async Task<ChoiceResult> ChooseAsync(
        string choiceId, string? tweakId, ApplyOptions? options = null, CancellationToken cancellation = default) =>
        (await SendAsync(new ChooseRequest(choiceId, tweakId, options), cancellation)).Choice ?? throw Missing("choice result");
}
