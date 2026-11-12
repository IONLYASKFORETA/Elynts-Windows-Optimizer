using System.IO.Pipes;
using Elynt.App.ViewModels;
using Elynt.Core.Ipc;
using Elynt.Core.Search;
using Elynt.Core.Tests;
using Elynt.Core.Tweaks;

namespace Elynt.App.Tests;

/// <summary>
/// The search box at the top of the sidebar, against the screenshot tour's made-up PC served over a real pipe as the
/// app sees it: what typing shows, and what choosing a result opens.
/// </summary>
public sealed class SidebarSearchTests : IAsyncLifetime
{
    private readonly string _pipeName = $"elynt-test-{Guid.NewGuid():N}";
    private readonly DirectoryInfo _scratch = Directory.CreateTempSubdirectory("elynt-sidebar-search-");
    private readonly CancellationTokenSource _stop = new();
    private readonly List<PageTarget> _opened = [];
    private Task _server = Task.CompletedTask;
    private EnginePipeClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var engine = SamplePc.NewEngine(_scratch.FullName);
        var server = new EnginePipeServer(
            () => new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            _ => engine,
            new FakeAdministratorCheck());
        _server = server.RunAsync(_stop.Token);
        _client = await EnginePipeClient.ConnectAsync(_pipeName, cancellation: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _stop.CancelAsync();
        await _server;
        _scratch.Delete(recursive: true);
    }

    /// <summary>The box as the window builds it, opening pages into <see cref="_opened"/>.</summary>
    private SidebarSearchViewModel NewBox() => new(() => _client, _opened.Add);

    /// <summary>Types into the box, as the user does, and waits for the answer.</summary>
    private static async Task TypeAsync(SidebarSearchViewModel box, string query, CancellationToken cancellation)
    {
        box.Query = query;
        await box.Searching.WaitAsync(cancellation);
    }

    [Fact(Timeout = 10_000)]
    public async Task Typing_shows_what_the_service_found_best_match_first()
    {
        var box = NewBox();

        await TypeAsync(box, "steam", TestContext.Current.CancellationToken);

        var best = box.Results[0];
        Assert.Equal((SearchResultKind.App, "Valve.Steam", Sections.Apps), (best.Kind, best.Id, best.Page));
        Assert.True(box.IsOpen);
    }

    [Fact(Timeout = 10_000)]
    public async Task Choosing_a_result_opens_its_page_with_its_row_and_empties_the_box()
    {
        var box = NewBox();
        await TypeAsync(box, "pointer precision", TestContext.Current.CancellationToken);

        box.Choose(box.Results[0]);

        Assert.Equal([new PageTarget(Sections.Gaming, "gaming.enhance-pointer-precision-off")], _opened);
        Assert.Equal(("", 0, false), (box.Query, box.Results.Count, box.IsOpen));
    }

    [Fact(Timeout = 10_000)]
    public async Task A_query_nothing_matches_says_so()
    {
        var box = NewBox();

        await TypeAsync(box, " zzqx ", TestContext.Current.CancellationToken);

        Assert.Empty(box.Results);
        Assert.Equal("No tweak, tool or app matches \"zzqx\".", box.Message);
        Assert.True(box.IsOpen);
    }

    [Fact(Timeout = 10_000)]
    public async Task Emptying_the_box_closes_the_results()
    {
        var box = NewBox();
        await TypeAsync(box, "zzqx", TestContext.Current.CancellationToken);

        await TypeAsync(box, "", TestContext.Current.CancellationToken);

        Assert.Equal((0, null, false), (box.Results.Count, box.Message, box.IsOpen));
    }

    [Fact(Timeout = 10_000)]
    public async Task While_disconnected_the_box_says_search_needs_the_service()
    {
        var box = new SidebarSearchViewModel(() => null, _opened.Add);

        await TypeAsync(box, "steam", TestContext.Current.CancellationToken);

        Assert.Empty(box.Results);
        Assert.Equal("Search needs the service, which isn't connected. Press Retry at the bottom of the sidebar.", box.Message);
        Assert.True(box.IsOpen);
    }

    [Fact(Timeout = 10_000)]
    public async Task When_the_service_cannot_be_reached_the_box_says_so_in_place_of_old_results()
    {
        var lost = await EnginePipeClient.ConnectAsync(_pipeName, cancellation: TestContext.Current.CancellationToken);
        var box = new SidebarSearchViewModel(() => lost, _opened.Add);
        await TypeAsync(box, "steam", TestContext.Current.CancellationToken);
        await lost.DisposeAsync();

        await TypeAsync(box, "steam deck", TestContext.Current.CancellationToken);

        Assert.Empty(box.Results);
        Assert.Equal("Search couldn't reach the service. Press Retry at the bottom of the sidebar, then search again.", box.Message);
    }
}
