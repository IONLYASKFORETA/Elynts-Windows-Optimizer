using System.IO.Pipes;
using System.Text.Json;
using Elynt.Core.Apps;
using Elynt.Core.Ipc;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The UI's client and the service's host carry every Engine command unchanged.</summary>
public sealed class PipeContractTests : IAsyncLifetime
{
    private readonly string _pipeName = $"elynt-test-{Guid.NewGuid():N}";
    private readonly StubEngine _engine = new();
    private readonly CancellationTokenSource _stop = new();
    private Task _server = Task.CompletedTask;
    private EnginePipeClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var server = new EnginePipeServer(
            () => new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            _ => _engine);
        _server = server.RunAsync(_stop.Token);
        _client = await EnginePipeClient.ConnectAsync(_pipeName, cancellation: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _stop.CancelAsync();
        await _server;
    }

    [Fact]
    public async Task ListTweaks_arrives_unchanged()
    {
        var tweaks = await _client.ListTweaksAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.ListTweaks(), tweaks);
    }

    [Fact]
    public async Task ListApps_arrives_unchanged()
    {
        var apps = await _client.ListAppsAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.ListApps(), apps);
    }

    [Fact]
    public async Task InstallApps_streams_progress_while_it_runs_then_the_report()
    {
        var seen = new List<InstallProgress>();
        var firstSeen = new TaskCompletionSource();
        _engine.WaitBetweenProgress = firstSeen.Task;

        var report = await _client.InstallAppsAsync(
            ["Valve.Steam"], allRuntimes: true,
            new SyncProgress(p => { seen.Add(p); firstSeen.TrySetResult(); }),
            TestContext.Current.CancellationToken);

        Assert.Equal("InstallApps Valve.Steam runtimes=True", _engine.LastCall);
        AssertSameJson(StubEngine.Progress, seen);
        AssertSameJson(StubEngine.Report, report);
    }

    [Fact]
    public async Task Apply_arrives_unchanged()
    {
        var result = await _client.ApplyAsync("some.tweak", TestContext.Current.CancellationToken);

        Assert.Equal("Apply some.tweak", _engine.LastCall);
        AssertSameJson(_engine.Apply("some.tweak"), result);
    }

    [Fact]
    public async Task Revert_arrives_unchanged()
    {
        var result = await _client.RevertAsync("some.tweak", TestContext.Current.CancellationToken);

        Assert.Equal("Revert some.tweak", _engine.LastCall);
        AssertSameJson(_engine.Revert("some.tweak"), result);
    }

    [Fact]
    public async Task A_failing_command_reports_an_error_and_keeps_the_connection()
    {
        _engine.Throw = true;
        await Assert.ThrowsAsync<EngineServiceException>(() => _client.ApplyAsync("some.tweak", TestContext.Current.CancellationToken));

        _engine.Throw = false;
        Assert.True((await _client.ApplyAsync("some.tweak", TestContext.Current.CancellationToken)).Succeeded);
    }

    [Fact]
    public void Every_engine_command_has_a_client_method()
    {
        var commands = typeof(IEngine).GetMethods().Select(m => m.Name + "Async");
        var clientMethods = typeof(EnginePipeClient).GetMethods().Select(m => m.Name);

        Assert.Empty(commands.Except(clientMethods));
    }

    private sealed class SyncProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }

    private static void AssertSameJson<T>(T expected, T actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private sealed class StubEngine : IEngine
    {
        public string? LastCall { get; private set; }
        public bool Throw { get; set; }

        private static readonly TweakView View = new(
            "some.tweak", Sections.Gaming, "Some tweak", "Does a thing.", "Measured.",
            [new Source("A source", "https://example.com")], EvidenceTier.Situational,
            [@"Set HKCU\Some\Value to ""1"" (REG_SZ)"], "Puts it back.", SessionRefresh.MouseSettings, TweakState.On, CanUndo: true);

        public IReadOnlyList<TweakView> ListTweaks() => [View];

        public IReadOnlyList<AppView> ListApps() =>
            [new("Valve.Steam", "Steam", AppGroups.GameLaunchers, "Valve's store.", false)];

        public static readonly IReadOnlyList<InstallProgress> Progress =
        [
            new(1, 1, "Valve.Steam", "Steam", InstallStage.Installing, 0.25, "Installing Steam…"),
            new(1, 1, "Valve.Steam", "Steam", InstallStage.Installed, null, "Steam is installed."),
        ];

        public static readonly InstallReport Report = new(true, "1 of 1 installed.",
            [new("Valve.Steam", "Steam", InstallStage.Installed, "Steam is installed.", false)]);

        /// <summary>When set, the second progress event waits for this, so the client must have seen the first.</summary>
        public Task? WaitBetweenProgress { get; set; }

        public InstallReport InstallApps(IReadOnlyList<string> appIds, bool allRuntimes, IProgress<InstallProgress>? progress = null)
        {
            LastCall = $"InstallApps {string.Join(",", appIds)} runtimes={allRuntimes}";
            progress?.Report(Progress[0]);
            if (WaitBetweenProgress is not null && !WaitBetweenProgress.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The client never saw the first progress event.");
            progress?.Report(Progress[1]);
            return Report;
        }

        public CommandResult Apply(string tweakId)
        {
            if (Throw)
                throw new InvalidOperationException("Boom.");
            LastCall = $"Apply {tweakId}";
            return new CommandResult(true, "Turned on.", View);
        }

        public CommandResult Revert(string tweakId)
        {
            LastCall = $"Revert {tweakId}";
            return new CommandResult(false, "Nothing to undo.", View with { State = TweakState.Off });
        }
    }
}
