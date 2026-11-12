using Elynt.Core.Search;

namespace Elynt.Core.Ipc;

public sealed record SearchRequest(string Query) : EngineRequest;

/// <summary>The sidebar search's side of the UI's client.</summary>
public sealed partial class EnginePipeClient
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellation = default) =>
        (await SendAsync(new SearchRequest(query), cancellation)).SearchResults ?? throw Missing("search results");
}
