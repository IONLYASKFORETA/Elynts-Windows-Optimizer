using System.IO.Pipes;
using Elynt.App.ViewModels;
using Elynt.Core.Ipc;
using Elynt.Core.Tests;

namespace Elynt.App.Tests;

/// <summary>The Cleanup page against the screenshot tour's made-up PC, served over a real pipe as the app sees it.</summary>
public sealed class CleanupPageTests : IAsyncLifetime
{
    private readonly string _pipeName = $"elynt-test-{Guid.NewGuid():N}";
    private readonly DirectoryInfo _scratch = Directory.CreateTempSubdirectory("elynt-cleanup-page-");
    private readonly CancellationTokenSource _stop = new();
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

    [Fact(Timeout = 10_000)]
    public async Task Scan_says_scan_again_once_a_scan_has_shown_the_sizes()
    {
        var page = new CleanupPageViewModel(() => _client);
        var before = page.ScanText;

        await page.ScanAsync().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(("Scan", "Scan again"), (before, page.ScanText));
    }
}
