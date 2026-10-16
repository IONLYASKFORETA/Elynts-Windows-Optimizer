namespace Elynt.Core.Ipc;

public sealed record ListStartupItemsRequest : EngineRequest;

public sealed record SetStartupItemEnabledRequest(string ItemId, bool Enabled) : EngineRequest;
