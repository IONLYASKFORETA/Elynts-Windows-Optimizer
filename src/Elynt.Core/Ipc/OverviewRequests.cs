using Elynt.Core.Overview;

namespace Elynt.Core.Ipc;

public sealed record GetPcOverviewRequest : EngineRequest;

/// <summary>The PC overview's side of the UI's client.</summary>
public sealed partial class EnginePipeClient
{
    public async Task<PcOverview> GetPcOverviewAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new GetPcOverviewRequest(), cancellation)).Overview ?? throw Missing("PC overview");
}
