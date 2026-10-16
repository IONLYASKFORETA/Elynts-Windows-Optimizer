using System.IO.Pipes;
using System.Text.Json;
using Elynt.Core.Bios;
using Elynt.Core.Ipc;
using Elynt.Core.Repair;
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
        var result = await _client.ApplyAsync("some.tweak", cancellation: TestContext.Current.CancellationToken);

        Assert.Equal("Apply some.tweak", _engine.LastCall);
        AssertSameJson(_engine.Apply("some.tweak"), result);
    }

    [Fact]
    public async Task Apply_carries_the_destructive_confirmation_and_advanced_mode()
    {
        await _client.ApplyAsync("some.tweak", new ApplyOptions(ConfirmDestructive: true, Advanced: true), TestContext.Current.CancellationToken);

        Assert.Equal(new ApplyOptions(ConfirmDestructive: true, Advanced: true), _engine.LastOptions);
    }

    [Fact]
    public async Task Revert_arrives_unchanged()
    {
        var result = await _client.RevertAsync("some.tweak", TestContext.Current.CancellationToken);

        Assert.Equal("Revert some.tweak", _engine.LastCall);
        AssertSameJson(_engine.Revert("some.tweak"), result);
    }

    [Fact]
    public async Task ListRepairTools_arrives_unchanged()
    {
        var tools = await _client.ListRepairToolsAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.ListRepairTools(), tools);
    }

    [Fact]
    public async Task RunRepair_streams_every_progress_line_then_the_result_unchanged()
    {
        var progress = new List<RepairProgress>();

        var result = await _client.RunRepairAsync("some.repair", confirmed: true, new Collect(progress), TestContext.Current.CancellationToken);

        Assert.Equal("RunRepair some.repair True", _engine.LastCall);
        AssertSameJson(StubEngine.Progress, progress);
        AssertSameJson(StubEngine.Repaired, result);
    }

    [Fact]
    public async Task Other_commands_still_answer_while_a_repair_runs()
    {
        _engine.HoldRepair = new TaskCompletionSource();
        await using var second = await EnginePipeClient.ConnectAsync(_pipeName, cancellation: TestContext.Current.CancellationToken);
        var repair = _client.RunRepairAsync("some.repair", confirmed: false, null, TestContext.Current.CancellationToken);

        var tweaks = await second.ListTweaksAsync(TestContext.Current.CancellationToken);

        Assert.Single(tweaks);
        Assert.False(repair.IsCompleted);
        _engine.HoldRepair.SetResult();
        Assert.True((await repair).Succeeded);
    }

    private sealed class Collect(List<RepairProgress> into) : IProgress<RepairProgress>
    {
        public void Report(RepairProgress value) => into.Add(value);
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
        await Assert.ThrowsAsync<EngineServiceException>(() => _client.ApplyAsync("some.tweak", cancellation: TestContext.Current.CancellationToken));

        _engine.Throw = false;
        Assert.True((await _client.ApplyAsync("some.tweak", cancellation: TestContext.Current.CancellationToken)).Succeeded);
    }

    [Fact]
    public void Every_engine_command_has_a_client_method()
    {
        var commands = typeof(IEngine).GetMethods().Select(m => m.Name.EndsWith("Async") ? m.Name : m.Name + "Async");
        var clientMethods = typeof(EnginePipeClient).GetMethods().Select(m => m.Name);

        Assert.Empty(commands.Except(clientMethods));
    }

    private static void AssertSameJson<T>(T expected, T actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private sealed class StubEngine : IEngine
    {
        public string? LastCall { get; private set; }
        public ApplyOptions? LastOptions { get; private set; }
        public bool Throw { get; set; }

        private static readonly TweakView View = new(
            "some.tweak", Sections.Gaming, "Some tweak", "Does a thing.", "Measured.",
            [new Source("A source", "https://example.com")], EvidenceTier.Situational,
            [@"Set HKCU\Some\Value to ""1"" (REG_SZ)"], "Puts it back.", SessionRefresh.MouseSettings, TweakState.On, CanUndo: true)
        {
            Reversibility = Reversibility.Destructive,
            DestructiveWarning = "Gone for good.",
            Risks = RiskFlags.SecurityRisk | RiskFlags.NeedsReboot,
            AntiCheat = [new AntiCheatConflict(AntiCheat.FaceIt, AntiCheatSeverity.WontLaunch, "VBS required")],
            PendingReboot = true,
            NotApplicableReason = "Needs an AMD CPU.",
            ProfileEligible = true,
        };

        public IReadOnlyList<TweakView> ListTweaks() => [View];

        public CommandResult Apply(string tweakId, ApplyOptions? options = null)
        {
            if (Throw)
                throw new InvalidOperationException("Boom.");
            LastCall = $"Apply {tweakId}";
            LastOptions = options;
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

        public TaskCompletionSource? HoldRepair { get; set; }

        public static readonly IReadOnlyList<RepairProgress> Progress =
        [
            new("some.repair", 1, 2, "First", null, null),
            new("some.repair", 1, 2, "First", 42.5, "[=== 42.5% ===]"),
            new("some.repair", 2, 2, "Second", null, "Done with it."),
        ];

        public static readonly RepairResult Repaired = new(
            "some.repair", true, "Done. Restart the PC to finish.\n• First: ok", true, "some.follow-up", @"C:\logs\repair.log", "the log");

        public IReadOnlyList<RepairToolView> ListRepairTools() =>
        [
            new("some.repair", Sections.Repair, "Some repair", "Repairs a thing.", ["Run thing.exe /fix"], RestartNeed.Restart,
                "Needs a restart.", "Are you sure?", IsFollowUp: false, Available: false, "Not on this PC."),
        ];

        public async Task<RepairResult> RunRepairAsync(string toolId, bool confirmed, IProgress<RepairProgress>? progress = null, CancellationToken cancellation = default)
        {
            LastCall = $"RunRepair {toolId} {confirmed}";
            foreach (var p in Progress)
                progress?.Report(p);
            if (HoldRepair is not null)
                await HoldRepair.Task.WaitAsync(cancellation);
            return Repaired;
        }
    }
}
