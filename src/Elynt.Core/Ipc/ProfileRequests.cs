using Elynt.Core.Profiles;

namespace Elynt.Core.Ipc;

public sealed record ListProfilesRequest : EngineRequest;

public sealed record SaveProfileRequest(GameProfile Profile) : EngineRequest;

public sealed record DeleteProfileRequest(string ExePath) : EngineRequest;

public sealed record TakeProfileNotesRequest : EngineRequest;
