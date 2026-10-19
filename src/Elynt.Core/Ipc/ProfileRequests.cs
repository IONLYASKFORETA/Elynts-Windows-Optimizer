using Elynt.Core.Profiles;

namespace Elynt.Core.Ipc;

public sealed record ListProfilesRequest : EngineRequest;

public sealed record SaveProfileRequest(GameProfile Profile) : EngineRequest;

/// <summary>Checks a profile being edited, without saving it.</summary>
public sealed record CheckProfileRequest(GameProfile Profile) : EngineRequest;

public sealed record DeleteProfileRequest(string ExePath) : EngineRequest;

public sealed record TakeProfileNotesRequest : EngineRequest;

/// <summary>The profile editor's side of the UI's client.</summary>
public sealed partial class EnginePipeClient
{
    /// <summary>Whether the profile could be saved, with the game's anti-cheat warnings. Saves nothing.</summary>
    public async Task<ProfileResult> CheckProfileAsync(GameProfile profile, CancellationToken cancellation = default) =>
        (await SendAsync(new CheckProfileRequest(profile), cancellation)).ProfileResult ?? throw Missing("result");
}
