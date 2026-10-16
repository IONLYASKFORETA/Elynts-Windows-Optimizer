using System.IO.Pipes;
using System.Text.Json;
using Elynt.Core.Bios;
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
    public async Task ListHistory_arrives_unchanged()
    {
        var history = await _client.ListHistoryAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.ListHistory(), history);
    }

    [Fact]
    public async Task UndoChange_arrives_unchanged()
    {
        var result = await _client.UndoChangeAsync(StubEngine.Change, TestContext.Current.CancellationToken);

        Assert.Equal($"UndoChange {StubEngine.Change}", _engine.LastCall);
        AssertSameJson(_engine.UndoChange(StubEngine.Change), result);
    }

    [Fact]
    public async Task RestoreEverything_arrives_unchanged()
    {
        var result = await _client.RestoreEverythingAsync(TestContext.Current.CancellationToken);

        Assert.Equal("RestoreEverything", _engine.LastCall);
        AssertSameJson(_engine.RestoreEverything(), result);
    }

    [Fact]
    public async Task GetBiosReport_arrives_unchanged()
    {
        var report = await _client.GetBiosReportAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.GetBiosReport(), report);
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

    private static void AssertSameJson<T>(T expected, T actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private sealed class StubEngine : IEngine
    {
        public string? LastCall { get; private set; }
        public bool Throw { get; set; }

        public static readonly Guid Change = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        private static readonly TweakView View = new(
            "some.tweak", Sections.Gaming, "Some tweak", "Does a thing.", "Measured.",
            [new Source("A source", "https://example.com")], EvidenceTier.Situational,
            [@"Set HKCU\Some\Value to ""1"" (REG_SZ)"], "Puts it back.", SessionRefresh.MouseSettings, TweakState.On, CanUndo: true);

        public IReadOnlyList<TweakView> ListTweaks() => [View];

        public CommandResult Apply(string tweakId)
        {
            if (Throw)
                throw new InvalidOperationException("Boom.");
            LastCall = $"Apply {tweakId}";
            return new CommandResult(true, "Turned on.", View);
        }

        public BiosReport GetBiosReport() => new(
            "ASUS ROG STRIX B650E-F, AMD Ryzen 7 7800X3D",
            [new BiosReadout(BiosReadoutIds.Pbo, "Precision Boost Overdrive", ReadoutState.Unknown, "Can't tell.")],
            [new BiosGuide("bios.guide.x", "Turn on EXPO", "Faster memory.", ["Press Delete."], [new Source("A source", "https://example.com")])],
            [new StabilityTool("OCCT", "https://www.ocbase.com/", "Memory test.")]);

        public CommandResult Revert(string tweakId)
        {
            LastCall = $"Revert {tweakId}";
            return new CommandResult(false, "Nothing to undo.", View with { State = TweakState.Off });
        }

        public CommandResult UndoChange(Guid changeId)
        {
            LastCall = $"UndoChange {changeId}";
            return new CommandResult(true, "Turned off.", View with { State = TweakState.Off, CanUndo = false });
        }

        public RestoreResult RestoreEverything()
        {
            LastCall = "RestoreEverything";
            return new RestoreResult(false, "Put back 1 of 2 changes.",
            [
                new(Change, "Some tweak", true, "Turned off."),
                new(Guid.Empty, "Other tweak", false, "Something else changed it since."),
            ]);
        }

        public IReadOnlyList<ChangeView> ListHistory() =>
        [
            new(Change, "some.tweak", "Some tweak", new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(2)),
                ChangeState.Undone, new DateTimeOffset(2026, 10, 16, 9, 5, 0, TimeSpan.FromHours(2)), CanUndo: false),
        ];
    }
}
