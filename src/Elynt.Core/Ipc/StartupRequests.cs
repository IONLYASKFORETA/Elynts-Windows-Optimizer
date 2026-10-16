namespace Elynt.Core.Ipc;

public sealed record ListStartupItemsRequest : EngineRequest;

public sealed record SetStartupItemEnabledRequest(string ItemId, bool Enabled) : EngineRequest;

public sealed record DeleteStartupItemRequest(string ItemId, bool ConfirmDestructive) : EngineRequest;

public sealed record CheckNewStartupItemsRequest : EngineRequest;
