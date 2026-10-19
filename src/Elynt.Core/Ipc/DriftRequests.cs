using Elynt.Core.Drift;

namespace Elynt.Core.Ipc;

public sealed record CheckDriftRequest : EngineRequest;

public sealed record ReapplyDriftRequest(IReadOnlyList<string>? TweakIds, bool ConfirmDestructive = false) : EngineRequest;

public sealed record GetDriftSettingsRequest : EngineRequest;

public sealed record SetAutoReapplyRequest(bool Enabled) : EngineRequest;

/// <summary>The drift commands' side of the UI's client.</summary>
public sealed partial class EnginePipeClient
{
    public async Task<DriftReport> CheckDriftAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new CheckDriftRequest(), cancellation)).Drift ?? throw Missing("drift report");

    public async Task<ReapplyResult> ReapplyDriftAsync(IReadOnlyList<string>? tweakIds, bool confirmDestructive = false, CancellationToken cancellation = default) =>
        (await SendAsync(new ReapplyDriftRequest(tweakIds, confirmDestructive), cancellation)).Reapply ?? throw Missing("result");

    public async Task<DriftSettings> GetDriftSettingsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new GetDriftSettingsRequest(), cancellation)).DriftSettings ?? throw Missing("drift settings");

    public async Task<DriftSettings> SetAutoReapplyAsync(bool enabled, CancellationToken cancellation = default) =>
        (await SendAsync(new SetAutoReapplyRequest(enabled), cancellation)).DriftSettings ?? throw Missing("drift settings");
}
