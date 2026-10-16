using System.IO.Pipes;
using System.Text.Json;
using Elynt.Core.Apps;
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
        AssertSameJson(StubEngine.InstallSteps, seen);
        AssertSameJson(StubEngine.Report, report);
    }

    [Fact]
    public async Task InstallApps_carries_the_not_reviewed_acceptance()
    {
        await _client.InstallAppsAsync(["Some.Tool"], allRuntimes: false, allowNotReviewed: true, cancellation: TestContext.Current.CancellationToken);

        Assert.Equal("InstallApps Some.Tool runtimes=False notReviewed=True", _engine.LastCall);
    }

    [Fact]
    public async Task SearchApps_arrives_unchanged()
    {
        var search = await _client.SearchAppsAsync("steam", TestContext.Current.CancellationToken);

        Assert.Equal("SearchApps steam", _engine.LastCall);
        AssertSameJson(_engine.SearchApps("steam"), search);
    }

    [Fact]
    public async Task ListInstalledApps_arrives_unchanged()
    {
        var installed = await _client.ListInstalledAppsAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.ListInstalledApps(), installed);
        Assert.True(installed.Apps[0].CanUpdate);
    }

    [Fact]
    public async Task Updates_and_uninstalls_stream_progress_then_the_report()
    {
        var seen = new List<InstallProgress>();
        var progress = new SyncProgress(seen.Add);

        var update = await _client.UpdateAppsAsync(["Valve.Steam"], progress, TestContext.Current.CancellationToken);
        Assert.Equal("UpdateApps Valve.Steam", _engine.LastCall);
        var all = await _client.UpdateAllAppsAsync(progress, TestContext.Current.CancellationToken);
        Assert.Equal("UpdateAllApps", _engine.LastCall);
        var uninstall = await _client.UninstallAppsAsync(["Valve.Steam"], progress, TestContext.Current.CancellationToken);
        Assert.Equal("UninstallApps Valve.Steam", _engine.LastCall);

        AssertSameJson(StubEngine.InstallSteps.Concat(StubEngine.InstallSteps).Concat(StubEngine.InstallSteps).ToList(), seen);
        AssertSameJson(StubEngine.Report, update);
        AssertSameJson(StubEngine.Report, all);
        AssertSameJson(StubEngine.Report, uninstall);
    }

    [Fact]
    public void Updates_and_uninstalls_run_alone_like_installs()
    {
        Assert.True(EngineProtocol.IsExclusive(new UpdateAppsRequest(["a"])));
        Assert.True(EngineProtocol.IsExclusive(new UpdateAllAppsRequest()));
        Assert.True(EngineProtocol.IsExclusive(new UninstallAppsRequest(["a"])));
        Assert.False(EngineProtocol.IsExclusive(new SearchAppsRequest("a")));
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
        AssertSameJson(StubEngine.RepairSteps, progress);
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

    [Fact]
    public async Task An_install_waits_for_a_running_repair_while_tweak_commands_still_answer()
    {
        _engine.HoldRepair = new TaskCompletionSource();
        await using var installer = await EnginePipeClient.ConnectAsync(_pipeName, cancellation: TestContext.Current.CancellationToken);
        await using var switches = await EnginePipeClient.ConnectAsync(_pipeName, cancellation: TestContext.Current.CancellationToken);
        var repair = _client.RunRepairAsync("some.repair", confirmed: false, null, TestContext.Current.CancellationToken);
        await _engine.RepairStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        var install = installer.InstallAppsAsync(["Valve.Steam"], allRuntimes: false, null, TestContext.Current.CancellationToken);
        var tweaks = await switches.ListTweaksAsync(TestContext.Current.CancellationToken);

        Assert.Single(tweaks);
        Assert.False(install.IsCompleted);
        Assert.Equal("RunRepair some.repair False", _engine.LastCall);
        _engine.HoldRepair.SetResult();
        Assert.True((await repair).Succeeded);
        Assert.True((await install).Succeeded);
    }

    [Fact]
    public void Progress_from_every_command_travels_in_the_one_event_envelope()
    {
        EngineEvent[] events = [new InstallProgressEvent(StubEngine.InstallSteps[0]), new RepairProgressEvent(StubEngine.RepairSteps[1])];

        var lines = events.Select(e => EngineProtocol.Serialize(new EngineResponse(Event: e))).ToList();
        var back = lines.Select(EngineProtocol.Deserialize<EngineResponse>).ToList();

        Assert.All(back, r => Assert.True(r.IsEvent));
        Assert.Contains("\"event\":\"installProgress\"", lines[0]);
        Assert.Contains("\"event\":\"repairProgress\"", lines[1]);
        AssertSameJson(StubEngine.InstallSteps[0], Assert.IsType<InstallProgressEvent>(back[0].Event).Progress);
        AssertSameJson(StubEngine.RepairSteps[1], Assert.IsType<RepairProgressEvent>(back[1].Event).Progress);
        Assert.False(EngineProtocol.Deserialize<EngineResponse>(EngineProtocol.Serialize(new EngineResponse(Install: StubEngine.Report))).IsEvent);
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

    private sealed class SyncProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }

    private static void AssertSameJson<T>(T expected, T actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private sealed class StubEngine : IEngine
    {
        public string? LastCall { get; private set; }
        public ApplyOptions? LastOptions { get; private set; }
        public bool Throw { get; set; }

        public static readonly Guid Change = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

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

        public IReadOnlyList<AppView> ListApps() =>
            [new("Valve.Steam", "Steam", AppGroups.GameLaunchers, "Valve's store.", false)];

        public static readonly IReadOnlyList<InstallProgress> InstallSteps =
        [
            new(1, 1, "Valve.Steam", "Steam", InstallStage.Installing, 0.25, "Installing Steam…"),
            new(1, 1, "Valve.Steam", "Steam", InstallStage.Installed, null, "Steam is installed."),
        ];

        public static readonly InstallReport Report = new(true, "1 of 1 installed.",
            [new("Valve.Steam", "Steam", InstallStage.Installed, "Steam is installed.", false)]);

        /// <summary>When set, the second progress event waits for this, so the client must have seen the first.</summary>
        public Task? WaitBetweenProgress { get; set; }

        public InstallReport InstallApps(IReadOnlyList<string> appIds, bool allRuntimes, IProgress<InstallProgress>? progress = null, bool allowNotReviewed = false)
        {
            LastCall = $"InstallApps {string.Join(",", appIds)} runtimes={allRuntimes}" + (allowNotReviewed ? " notReviewed=True" : "");
            return Stream(progress);
        }

        private InstallReport Stream(IProgress<InstallProgress>? progress)
        {
            progress?.Report(InstallSteps[0]);
            if (WaitBetweenProgress is not null && !WaitBetweenProgress.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The client never saw the first progress event.");
            progress?.Report(InstallSteps[1]);
            return Report;
        }

        public AppSearchReport SearchApps(string query)
        {
            LastCall = $"SearchApps {query}";
            return new(true, "2 found. 1 isn't in the reviewed catalogue.",
            [
                new("Valve.Steam", "Steam", "2.10", true, AppGroups.GameLaunchers, "Valve's store."),
                new("Some.SteamTool", "Steam Tool", "1.0", false, null, null),
            ]);
        }

        public InstalledAppsReport ListInstalledApps() => new(true, "1 update available.",
        [
            new("Valve.Steam", "Steam", "2.10", "2.11", true),
            new(@"ARP\Machine\X64\Old", "Old Thing", "1.0", null, false),
        ]);

        public InstallReport UpdateApps(IReadOnlyList<string> appIds, IProgress<InstallProgress>? progress = null)
        {
            LastCall = $"UpdateApps {string.Join(",", appIds)}";
            return Stream(progress);
        }

        public InstallReport UpdateAllApps(IProgress<InstallProgress>? progress = null)
        {
            LastCall = "UpdateAllApps";
            return Stream(progress);
        }

        public InstallReport UninstallApps(IReadOnlyList<string> appIds, IProgress<InstallProgress>? progress = null)
        {
            LastCall = $"UninstallApps {string.Join(",", appIds)}";
            return Stream(progress);
        }

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

        public TaskCompletionSource? HoldRepair { get; set; }

        /// <summary>Set once a repair is running, holding the repair lane.</summary>
        public TaskCompletionSource RepairStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static readonly IReadOnlyList<RepairProgress> RepairSteps =
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
            RepairStarted.TrySetResult();
            foreach (var p in RepairSteps)
                progress?.Report(p);
            if (HoldRepair is not null)
                await HoldRepair.Task.WaitAsync(cancellation);
            return Repaired;
        }
    }
}
