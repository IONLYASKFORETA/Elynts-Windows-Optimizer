using System.IO.Pipes;
using System.Text.Json;
using Elynt.Core.Apps;
using Elynt.Core.Bios;
using Elynt.Core.Cleanup;
using Elynt.Core.Drivers;
using Elynt.Core.Games;
using Elynt.Core.Profiles;
using Elynt.Core.Ipc;
using Elynt.Core.LowLevel;
using Elynt.Core.Sensors;
using Elynt.Core.Tuning.Cpu;
using Elynt.Core.Startup;
using Elynt.Core.Repair;
using Elynt.Core.Services;
using Elynt.Core.Tuning;
using Elynt.Core.Tweaks;
using Elynt.Core.UpdateControl;

namespace Elynt.Core.Tests;

/// <summary>The UI's client and the service's host carry every Engine command unchanged.</summary>
public sealed class PipeContractTests : IAsyncLifetime
{
    private readonly string _pipeName = $"elynt-test-{Guid.NewGuid():N}";
    private readonly StubEngine _engine = new();
    private readonly FakeAdministratorCheck _administrators = new();
    private int _enginesBuilt;
    private readonly CancellationTokenSource _stop = new();
    private Task _server = Task.CompletedTask;
    private EnginePipeClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var server = new EnginePipeServer(
            () => new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            _ =>
            {
                Interlocked.Increment(ref _enginesBuilt);
                return _engine;
            },
            _administrators);
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
        var uninstall = await _client.UninstallAppsAsync(["Valve.Steam"], confirmed: true, progress, TestContext.Current.CancellationToken);
        Assert.Equal("UninstallApps Valve.Steam True", _engine.LastCall);

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
    public async Task ListServices_arrives_unchanged()
    {
        var services = await _client.ListServicesAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.ListServices(), services);
    }

    [Fact]
    public async Task SetServiceStart_arrives_unchanged_with_advanced_mode()
    {
        var result = await _client.SetServiceStartAsync("Spooler", ServiceStart.Disabled, new ApplyOptions(Advanced: true), TestContext.Current.CancellationToken);

        Assert.Equal("SetServiceStart Spooler Disabled", _engine.LastCall);
        Assert.Equal(new ApplyOptions(Advanced: true), _engine.LastOptions);
        AssertSameJson(_engine.SetServiceStart("Spooler", ServiceStart.Disabled), result);
    }

    [Fact]
    public async Task UndoServiceChange_arrives_unchanged()
    {
        var result = await _client.UndoServiceChangeAsync("Spooler", TestContext.Current.CancellationToken);

        Assert.Equal("UndoServiceChange Spooler", _engine.LastCall);
        AssertSameJson(_engine.UndoServiceChange("Spooler"), result);
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
    public async Task ScanCleanup_streams_each_category_then_the_scan_unchanged()
    {
        var progress = new List<CleanupProgress>();

        var scan = await _client.ScanCleanupAsync(new CollectCleanup(progress), TestContext.Current.CancellationToken);

        AssertSameJson(StubEngine.CleanupSteps, progress);
        AssertSameJson(StubEngine.Scan, scan);
    }

    [Fact]
    public async Task Clean_carries_the_ticked_categories_and_the_destructive_confirmation()
    {
        var progress = new List<CleanupProgress>();

        var report = await _client.CleanAsync(["cleanup.temp", "cleanup.recycle-bin"], confirmDestructive: true, new CollectCleanup(progress),
            TestContext.Current.CancellationToken);

        Assert.Equal("Clean cleanup.temp,cleanup.recycle-bin True", _engine.LastCall);
        AssertSameJson(StubEngine.CleanupSteps, progress);
        AssertSameJson(StubEngine.Cleaned, report);
    }

    [Fact]
    public async Task Weekly_cleaning_schedule_arrives_unchanged()
    {
        var set = await _client.SetWeeklyCleanupAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal("SetWeeklyCleanup True", _engine.LastCall);
        AssertSameJson(_engine.GetCleanupSchedule(), set);
        AssertSameJson(_engine.GetCleanupSchedule(), await _client.GetCleanupScheduleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Drift_commands_arrive_unchanged()
    {
        var token = TestContext.Current.CancellationToken;

        AssertSameJson(StubEngine.DriftFound, await _client.CheckDriftAsync(token));

        var reapplied = await _client.ReapplyDriftAsync(["debloat.remove-copilot"], confirmDestructive: true, token);
        Assert.Equal("ReapplyDrift debloat.remove-copilot True", _engine.LastCall);
        AssertSameJson(StubEngine.Reapplied, reapplied);

        await _client.ReapplyDriftAsync(null, cancellation: token);
        Assert.Equal("ReapplyDrift <all> False", _engine.LastCall);

        Assert.True((await _client.SetAutoReapplyAsync(true, token)).AutoReapply);
        Assert.True((await _client.GetDriftSettingsAsync(token)).AutoReapply);
    }

    [Fact]
    public async Task Other_commands_still_answer_while_a_clean_runs()
    {
        _engine.HoldRepair = new TaskCompletionSource();

        var clean = _client.CleanAsync(["cleanup.temp"], false, null, TestContext.Current.CancellationToken);
        await _engine.RepairStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await using var second = await EnginePipeClient.ConnectAsync(_pipeName, cancellation: TestContext.Current.CancellationToken);
        Assert.Single(await second.ListTweaksAsync(TestContext.Current.CancellationToken));

        _engine.HoldRepair.SetResult();
        await clean;
    }

    private sealed class CollectCleanup(List<CleanupProgress> into) : IProgress<CleanupProgress>
    {
        public void Report(CleanupProgress value) => into.Add(value);
    }

    [Fact]
    public async Task GetBiosReport_arrives_unchanged()
    {
        var report = await _client.GetBiosReportAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.GetBiosReport(), report);
    }

    [Fact]
    public async Task GetPcOverview_arrives_unchanged()
    {
        var overview = await _client.GetPcOverviewAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.GetPcOverview(), overview);
    }

    [Fact]
    public async Task ListStartupItems_arrives_unchanged()
    {
        var items = await _client.ListStartupItemsAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.ListStartupItems(), items);
    }

    [Fact]
    public async Task SetStartupItemEnabled_arrives_unchanged()
    {
        var result = await _client.SetStartupItemEnabledAsync("run:HKCU:Discord", false, TestContext.Current.CancellationToken);

        Assert.Equal("SetStartupItemEnabled run:HKCU:Discord False", _engine.LastCall);
        AssertSameJson(_engine.SetStartupItemEnabled("run:HKCU:Discord", false), result);
    }

    [Fact]
    public async Task DeleteStartupItem_carries_the_destructive_confirmation()
    {
        var result = await _client.DeleteStartupItemAsync("run:HKCU:Discord", true, TestContext.Current.CancellationToken);

        Assert.Equal("DeleteStartupItem run:HKCU:Discord True", _engine.LastCall);
        AssertSameJson(_engine.DeleteStartupItem("run:HKCU:Discord", true), result);
    }

    [Fact]
    public async Task CheckNewStartupItems_arrives_unchanged()
    {
        var items = await _client.CheckNewStartupItemsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("CheckNewStartupItems", _engine.LastCall);
        AssertSameJson(_engine.CheckNewStartupItems(), items);
    }

    [Fact]
    public async Task StartWindowsUpdate_arrives_unchanged()
    {
        var status = await _client.StartWindowsUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal("StartWindowsUpdate", _engine.LastCall);
        AssertSameJson(_engine.StartWindowsUpdate(), status);
    }

    [Fact]
    public async Task GetWindowsUpdateStatus_arrives_unchanged()
    {
        var status = await _client.GetWindowsUpdateStatusAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.GetWindowsUpdateStatus(), status);
    }

    [Fact]
    public async Task GetDrivers_arrives_unchanged()
    {
        var report = await _client.GetDriversAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.GetDrivers(), report);
    }

    [Fact]
    public async Task CheckDriver_arrives_unchanged()
    {
        var request = new DriverRequest(@"PCI\VEN_10DE&DEV_2786", DriverBranch.Studio, CleanInstall: true);

        var check = await _client.CheckDriverAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(request, _engine.LastDriverRequest);
        AssertSameJson(_engine.CheckDriver(request), check);
    }

    [Fact]
    public async Task InstallDriver_arrives_unchanged()
    {
        var request = new DriverRequest(@"PCI\VEN_10DE&DEV_2786", DriverBranch.GameReady, CleanInstall: true);

        var progress = new List<DriverInstallProgress>();

        var result = await _client.InstallDriverAsync(
            request, new SyncCollect<DriverInstallProgress>(progress), TestContext.Current.CancellationToken);

        Assert.Equal(request, _engine.LastDriverRequest);
        AssertSameJson(StubEngine.DriverSteps, progress);
        AssertSameJson(_engine.InstallDriver(request), result);
    }

    [Fact]
    public void A_driver_install_runs_in_the_long_running_lane()
    {
        var install = new InstallDriverRequest(new DriverRequest(@"PCI\VEN_10DE&DEV_2786"));

        Assert.True(EngineProtocol.IsLongRunning(install));
        Assert.False(EngineProtocol.IsExclusive(install));
        var line = EngineProtocol.Serialize(new EngineResponse(Event: new DriverInstallProgressEvent(StubEngine.DriverSteps[0])));
        Assert.Contains("\"event\":\"driverInstallProgress\"", line);
        AssertSameJson(StubEngine.DriverSteps[0],
            Assert.IsType<DriverInstallProgressEvent>(EngineProtocol.Deserialize<EngineResponse>(line).Event).Progress);
    }

    [Fact]
    public async Task GetDriverCleaning_arrives_unchanged()
    {
        var report = await _client.GetDriverCleaningAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.GetDriverCleaning(), report);
    }

    [Fact]
    public async Task CleanDrivers_arrives_unchanged_and_runs_in_the_long_running_lane()
    {
        var request = new DriverCleanRequest(GpuVendor.Amd, DriverCleanMethod.Ddu, Confirmed: true);

        var result = await _client.CleanDriversAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(request, _engine.LastCleanRequest);
        AssertSameJson(_engine.CleanDrivers(request), result);
        Assert.True(EngineProtocol.IsLongRunning(new CleanDriversRequest(request)));
    }

    [Fact]
    public async Task RestoreDrivers_arrives_unchanged_and_runs_in_the_long_running_lane()
    {
        var result = await _client.RestoreDriversAsync("20261016-120000-Nvidia", TestContext.Current.CancellationToken);

        Assert.Equal("20261016-120000-Nvidia", _engine.LastRestoredBackup);
        AssertSameJson(_engine.RestoreDrivers("20261016-120000-Nvidia"), result);
        Assert.True(EngineProtocol.IsLongRunning(new RestoreDriversRequest("x")));
    }

    private sealed class SyncCollect<T>(List<T> into) : IProgress<T>
    {
        public void Report(T value) => into.Add(value);
    }

    [Fact]
    public async Task ListGames_arrives_unchanged()
    {
        var games = await _client.ListGamesAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.ListGames(), games);
    }

    [Fact]
    public async Task AddGame_and_RemoveGame_arrive_unchanged()
    {
        var added = await _client.AddGameAsync(@"C:\Games\Game.exe", TestContext.Current.CancellationToken);
        Assert.Equal(@"AddGame C:\Games\Game.exe", _engine.LastCall);
        AssertSameJson(_engine.AddGame(@"C:\Games\Game.exe"), added);

        var removed = await _client.RemoveGameAsync(@"C:\Games\Game.exe", TestContext.Current.CancellationToken);
        Assert.Equal(@"RemoveGame C:\Games\Game.exe", _engine.LastCall);
        AssertSameJson(_engine.RemoveGame(@"C:\Games\Game.exe"), removed);
    }

    [Fact]
    public async Task GetGpuTuning_arrives_unchanged()
    {
        var report = await _client.GetGpuTuningAsync(TestContext.Current.CancellationToken);

        AssertSameJson(_engine.GetGpuTuning(), report);
    }

    [Fact]
    public async Task GPU_tuning_commands_arrive_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var values = new Dictionary<string, int> { [GpuControls.MaxClock] = 2600, [GpuControls.PowerLimit] = -5 };

        AssertSameJson(_engine.SetGpuTuning("amd:0", values), await _client.SetGpuTuningAsync("amd:0", values, ct));
        Assert.Equal("SetGpuTuning amd:0 gpuMaxClock=2600,powerLimit=-5", _engine.LastCall);
        await _client.AutoTuneGpuAsync("amd:0", AutoTuneKind.UndervoltGpu, ct);
        Assert.Equal("AutoTuneGpu amd:0 UndervoltGpu", _engine.LastCall);
        await _client.ConfirmGpuTuningAsync("amd:0", ct);
        Assert.Equal("ConfirmGpuTuning amd:0", _engine.LastCall);
        await _client.RevertGpuTuningAsync("amd:0", ct);
        Assert.Equal("RevertGpuTuning amd:0", _engine.LastCall);
        await _client.ResetGpuTuningAsync("amd:0", ct);
        Assert.Equal("ResetGpuTuning amd:0", _engine.LastCall);
        AssertSameJson(_engine.AcceptGpuWarning("intel:0"), await _client.AcceptGpuWarningAsync("intel:0", ct));
        Assert.Equal("AcceptGpuWarning intel:0", _engine.LastCall);
    }

    [Fact]
    public async Task PawnIO_and_sensor_commands_arrive_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;

        AssertSameJson(_engine.GetPawnIo(), await _client.GetPawnIoAsync(ct));
        AssertSameJson(_engine.GetSensors(), await _client.GetSensorsAsync(ct));
        AssertSameJson(_engine.InstallPawnIo(true), await _client.InstallPawnIoAsync(acceptFaceitWarning: true, ct));
        Assert.Equal("InstallPawnIo True", _engine.LastCall);
        await _client.InstallPawnIoAsync(acceptFaceitWarning: false, ct);
        Assert.Equal("InstallPawnIo False", _engine.LastCall);
        AssertSameJson(_engine.UninstallPawnIo(), await _client.UninstallPawnIoAsync(ct));
        Assert.Equal("UninstallPawnIo", _engine.LastCall);
    }

    [Fact]
    public async Task CPU_tuning_commands_arrive_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var tune = new CpuTune(CurveOptimizer: -12, PptWatts: 120);
        var options = new CpuTuningOptions(Advanced: true, Unofficial: true);

        AssertSameJson(_engine.GetCpuTuning(true), await _client.GetCpuTuningAsync(advanced: true, ct));
        Assert.Equal("GetCpuTuning True", _engine.LastCall);
        AssertSameJson(_engine.SetCpuTuning(tune, options), await _client.SetCpuTuningAsync(tune, options, ct));
        Assert.Equal($"SetCpuTuning {tune} {options}", _engine.LastCall);
        AssertSameJson(_engine.ConfirmCpuTuning(), await _client.ConfirmCpuTuningAsync(ct));
        AssertSameJson(_engine.RevertCpuTuning(), await _client.RevertCpuTuningAsync(ct));
        Assert.Equal("RevertCpuTuning", _engine.LastCall);
        AssertSameJson(_engine.ResetCpuTuning(), await _client.ResetCpuTuningAsync(ct));
        Assert.Equal("ResetCpuTuning", _engine.LastCall);
        AssertSameJson(_engine.RunCpuStressTest(ct), await _client.RunCpuStressTestAsync(ct));
        Assert.Equal("RunCpuStressTest", _engine.LastCall);
        AssertSameJson(_engine.SetCpuAutoApply(true), await _client.SetCpuAutoApplyAsync(true, ct));
        Assert.Equal("SetCpuAutoApply True", _engine.LastCall);
    }

    [Fact]
    public void The_CPU_stress_test_runs_in_the_long_running_lane()
    {
        Assert.True(EngineProtocol.IsLongRunning(new RunCpuStressTestRequest()));
    }

    [Fact]
    public async Task Profile_commands_arrive_unchanged()
    {
        var cancellation = TestContext.Current.CancellationToken;
        AssertSameJson(_engine.ListProfiles(), await _client.ListProfilesAsync(cancellation));

        var saved = await _client.SaveProfileAsync(StubEngine.Profile, cancellation);
        Assert.Equal($"SaveProfile {StubEngine.Profile.ExePath}", _engine.LastCall);
        AssertSameJson(_engine.SaveProfile(StubEngine.Profile), saved);
        Assert.Equal(StubEngine.Profile, saved.Profile);

        var checkedProfile = await _client.CheckProfileAsync(StubEngine.Profile, cancellation);
        Assert.Equal(@"CheckProfile C:\Steam\cs2.exe -20 1100", _engine.LastCall);
        AssertSameJson(_engine.CheckProfile(StubEngine.Profile), checkedProfile);

        var deleted = await _client.DeleteProfileAsync(@"C:\Steam\cs2.exe", cancellation);
        Assert.Equal(@"DeleteProfile C:\Steam\cs2.exe", _engine.LastCall);
        AssertSameJson(_engine.DeleteProfile(@"C:\Steam\cs2.exe"), deleted);

        AssertSameJson(_engine.TakeProfileNotes(), await _client.TakeProfileNotesAsync(cancellation));
    }

    [Fact]
    public async Task A_standard_account_is_refused_with_a_plain_message_and_no_command_runs()
    {
        _administrators.ConnectingAccountIsAdministrator = false;
        var cancellation = TestContext.Current.CancellationToken;

        var refused = await Assert.ThrowsAsync<AccountRefusedException>(() => _client.ListTweaksAsync(cancellation));
        // Asking again on the same connection gets no further.
        await Assert.ThrowsAsync<AccountRefusedException>(() => _client.ApplyAsync("some.tweak", cancellation: cancellation));

        Assert.Equal(
            "Only an administrator account can use Elynt's Windows Optimizer. Sign in with one, or ask whoever looks after this PC.",
            refused.Message);
        Assert.Null(_engine.LastCall);
        Assert.Equal(0, _enginesBuilt);
    }

    [Fact]
    public async Task An_administrator_account_is_served_as_before()
    {
        _administrators.ConnectingAccountIsAdministrator = true;

        var result = await _client.ApplyAsync("some.tweak", cancellation: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("Apply some.tweak", _engine.LastCall);
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
        // Commands only the service's own hosts call (weekly cleaning; the profile run commands come from
        // IProfileEngine): a user must not be able to trigger them over the pipe.
        string[] serviceOnly = [nameof(IEngine.RunWeeklyCleanupIfDueAsync)];
        var commands = typeof(IEngine).GetMethods().Select(m => m.Name.EndsWith("Async") ? m.Name : m.Name + "Async").Except(serviceOnly);
        var clientMethods = typeof(EnginePipeClient).GetMethods().Select(m => m.Name);

        Assert.Empty(commands.Except(clientMethods));
        Assert.DoesNotContain(clientMethods, m => serviceOnly.Contains(m) || m is "StartProfileAsync" or "StopProfileAsync" or "RecoverTemporaryChangesAsync");
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
            AffectedGames = [new GameConflict("Counter-Strike 2", AntiCheat.FaceIt, AntiCheatSeverity.WontLaunch, "VBS required")],
            AntiCheatWarning = "Turning this on may stop Counter-Strike 2 (FACEIT) from launching.",
            PendingReboot = true,
            NotApplicableReason = "Needs an AMD CPU.",
            ProfileEligible = true,
        };

        public IReadOnlyList<TweakView> ListTweaks() => [View];

        private static readonly ServiceView Service = new(
            "Spooler", "Print Spooler", "Queues print jobs.", ServiceKind.Service, ServiceStart.Automatic, DelayedAutoStart: true,
            Running: true, ServiceAccess.Listed, LockReason: null,
            new ServiceRecommendation(ServiceStart.Manual, "No printer was found."),
            [ServiceStart.Automatic, ServiceStart.Manual], [ServiceStart.Automatic, ServiceStart.Manual, ServiceStart.Disabled],
            CanUndo: true);

        public IReadOnlyList<ServiceView> ListServices() =>
            [Service, Service with { Name = "vgc", Access = ServiceAccess.Protected, LockReason = "anti-cheat", Recommendation = null }];

        public ServiceResult SetServiceStart(string serviceName, ServiceStart start, ApplyOptions? options = null)
        {
            LastCall = $"SetServiceStart {serviceName} {start}";
            LastOptions = options;
            return new ServiceResult(true, "Print Spooler now starts Manual.", Service with { Start = start });
        }

        public ServiceResult UndoServiceChange(string serviceName)
        {
            LastCall = $"UndoServiceChange {serviceName}";
            return new ServiceResult(false, "Nothing to undo.", Service with { CanUndo = false });
        }

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

        public InstallReport UninstallApps(IReadOnlyList<string> appIds, bool confirmed, IProgress<InstallProgress>? progress = null)
        {
            LastCall = $"UninstallApps {string.Join(",", appIds)} {confirmed}";
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

        public Overview.PcOverview GetPcOverview() => new(
            new Overview.PcHardware("ASUSTeK COMPUTER INC. ROG STRIX B650E-F GAMING WIFI", "AMD Ryzen 7 7800X3D 8-Core Processor",
                ["NVIDIA GeForce RTX 4070 SUPER", "AMD Radeon(TM) Graphics"], 32768),
            new Overview.WindowsRelease(WindowsEdition.Pro, "24H2", "26100.4652"),
            TweaksOn: 12, TweaksApplicable: 58, PendingReboots: 2,
            [new Overview.SectionCount(Sections.Gaming, 5, 20), new Overview.SectionCount(Sections.Repair, 0, 0)]);

        public DriverReport GetDrivers() => new(
            [new InstalledGpu("NVIDIA GeForce RTX 4070", @"PCI\VEN_10DE&DEV_2786", GpuVendor.Nvidia, "591.74", "32.0.15.9174")],
            "A problem.");

        public DriverRequest? LastDriverRequest { get; private set; }

        private static readonly DriverOffer Offer = new(
            "617.42", DriverBranch.Studio, new Uri("https://us.download.nvidia.com/Windows/617.42/x.exe"),
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "ab12");

        public DriverCheck CheckDriver(DriverRequest request)
        {
            LastDriverRequest = request;
            return new DriverCheck(true, "Newer.", GetDrivers().Gpus[0], Offer, true, "https://www.nvidia.com/en-us/drivers/");
        }

        public static readonly IReadOnlyList<DriverInstallProgress> DriverSteps =
        [
            new(DriverInstallStage.Downloading, "Downloading driver 617.42…"),
            new(DriverInstallStage.Installing, "Installing driver 617.42…"),
        ];

        public DriverInstallResult InstallDriver(DriverRequest request, IProgress<DriverInstallProgress>? progress = null)
        {
            LastDriverRequest = request;
            foreach (var step in DriverSteps)
                progress?.Report(step);
            return new DriverInstallResult(true, RebootNeeded: true, "Restart to finish.", "617.42", "https://www.nvidia.com/en-us/drivers/");
        }

        public DriverCleanRequest? LastCleanRequest { get; private set; }
        public string? LastRestoredBackup { get; private set; }

        public DriverCleaningReport GetDriverCleaning() => new(
            [new DriverBackup("20261016-120000-Nvidia", GpuVendor.Nvidia, new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), ["nv_dispi.inf 32.0.15.9174"])],
            [new ProblemDriver("Samsung NVMe storage driver", "secnvme.sys", "3.3.0.2003", "Stops Valorant from launching.")],
            "DDU is a third-party tool.");

        public DriverCleanResult CleanDrivers(DriverCleanRequest request)
        {
            LastCleanRequest = request;
            return new DriverCleanResult(true, RebootNeeded: true, "Cleaned.", "20261016-120000-Nvidia", "https://www.wagnardsoft.com/");
        }

        public DriverCleanResult RestoreDrivers(string backupId)
        {
            LastRestoredBackup = backupId;
            return new DriverCleanResult(true, false, "Restored.", backupId);
        }

        private static readonly InstalledGame Game = new(
            "Counter-Strike 2", GameLauncher.Steam, @"C:\Steam\steamapps\common\Counter-Strike Global Offensive", @"C:\Steam\cs2.exe", [AntiCheat.FaceIt]);

        public IReadOnlyList<InstalledGame> ListGames() => [Game];

        public GameCommandResult AddGame(string exePath)
        {
            LastCall = $"AddGame {exePath}";
            return new GameCommandResult(true, "Added.", Game with { Launcher = GameLauncher.Manual });
        }

        public GameCommandResult RemoveGame(string exePath)
        {
            LastCall = $"RemoveGame {exePath}";
            return new GameCommandResult(true, "Removed.", null);
        }

        public static readonly GameProfile Profile = new(@"C:\Steam\cs2.exe")
        {
            TweakIds = ["gaming.game-mode-on", "gaming.high-performance-plan"],
            AppsToClose = [@"C:\Users\Aden\AppData\Local\Discord\Discord.exe"],
            Priority = GamePriority.High,
            RaiseTimerResolution = true,
            GpuTuning = [new("amd:0", new Dictionary<string, int> { [GpuControls.Voltage] = 1100, [GpuControls.MaxClock] = 2600 })],
            CpuTuning = new(new CpuTune(CurveOptimizer: -20, PptWatts: 120), Advanced: true),
        };

        public IReadOnlyList<GameProfile> ListProfiles() => [Profile];

        public ProfileResult CheckProfile(GameProfile profile)
        {
            LastCall = $"CheckProfile {profile.ExePath} {profile.CpuTuning?.Tune.CurveOptimizer} {profile.GpuTuning.Single().Values[GpuControls.Voltage]}";
            return new ProfileResult(true, "This profile can be saved.", profile, ["CPU tuning: FACEIT blocks PawnIO (test)."]);
        }

        public ProfileResult SaveProfile(GameProfile profile)
        {
            LastCall = $"SaveProfile {profile.ExePath}";
            return new ProfileResult(true, "Saved.", profile, ["Game Mode: FACEIT in Counter-Strike 2 (test)."]);
        }

        public ProfileResult DeleteProfile(string exePath)
        {
            LastCall = $"DeleteProfile {exePath}";
            return new ProfileResult(true, "Deleted.", null, []);
        }

        public IReadOnlyList<ProfileNote> TakeProfileNotes() =>
            [new ProfileNote("S-1-5-21-test", ProfileNoteKind.Skipped, "Profile skipped", "cs2's profile was skipped.")];

        // Service-side commands with no pipe request: on IEngine so the service reaches them through the seam.
        public GameProfile? ProfileFor(string exePath) => null;

        public ProfileRunResult StartProfile(string exePath, int processId) => new(true, "Started.", []);

        public ProfileRunResult StopProfile(string exePath) => new(true, "Stopped.", []);

        public ProfileRunResult RecoverTemporaryChanges() => new(true, "Nothing left behind.", []);

        public Task<CleanupReport?> RunWeeklyCleanupIfDueAsync(CancellationToken cancellation = default) =>
            Task.FromResult<CleanupReport?>(null);

        private static readonly GpuTuningView Gpu = new(
            "amd:0", "AMD Radeon RX 7800 XT", GpuVendor.Amd,
            [new TuningControl(GpuControls.MaxClock, "Max GPU clock", "MHz", 500, 3000, 5, 2500, 2600)],
            [AutoTuneKind.OverclockGpu, AutoTuneKind.UndervoltGpu],
            AtDefaults: false,
            RevertsAt: new DateTimeOffset(2026, 10, 16, 12, 0, 15, TimeSpan.Zero),
            Notice: "Waiting.");

        private static readonly GpuTuningView Nvidia = new(
            "nvidia:GPU-1234", "NVIDIA GeForce RTX 4070", GpuVendor.Nvidia,
            [new TuningControl(GpuControls.CoreClockOffset, "Core clock offset", "MHz", -500, 1000, 1, 0, 150)],
            [],
            AtDefaults: false,
            RevertsAt: null,
            Notice: null,
            Readouts: [new GpuReadout("Temperature", 61, "°C"), new GpuReadout("Power draw", 187, "W")]);

        public GpuTuningReport GetGpuTuning() => new([Gpu, IntelGpu, Nvidia], "A page note.");

        public TuningResult SetGpuTuning(string deviceId, IReadOnlyDictionary<string, int> values)
        {
            LastCall = $"SetGpuTuning {deviceId} " + string.Join(",", values.OrderBy(v => v.Key).Select(v => $"{v.Key}={v.Value}"));
            return new TuningResult(true, "Changed.", Gpu);
        }

        public TuningResult AutoTuneGpu(string deviceId, AutoTuneKind kind)
        {
            LastCall = $"AutoTuneGpu {deviceId} {kind}";
            return new TuningResult(true, "Changed.", Gpu);
        }

        public TuningResult ConfirmGpuTuning(string deviceId)
        {
            LastCall = $"ConfirmGpuTuning {deviceId}";
            return new TuningResult(true, "Kept.", Gpu);
        }

        public TuningResult RevertGpuTuning(string deviceId)
        {
            LastCall = $"RevertGpuTuning {deviceId}";
            return new TuningResult(true, "Put back.", Gpu);
        }

        private static readonly CpuTuningReport Cpu = new(
            true, "AMD Ryzen 7 7800X3D", CpuMaker.Amd,
            [new TuningControl(CpuControls.CurveOptimizer, "Curve Optimizer (all cores)", "counts", -15, 0, 1, 0, -10)],
            new CpuTune(CurveOptimizer: -10), new CpuTune(CurveOptimizer: -10, PptWatts: 120),
            new DateTimeOffset(2026, 10, 19, 12, 1, 0, TimeSpan.Zero), true, false, 1, false, false,
            new CpuTune(Pl1Watts: 125, Pl2Watts: 253), ["FACEIT's anti-cheat blocks PawnIO."], "A note.");

        public CpuTuningReport GetCpuTuning(bool advanced)
        {
            LastCall = $"GetCpuTuning {advanced}";
            return Cpu;
        }

        public CpuTuningResult SetCpuTuning(CpuTune tune, CpuTuningOptions options)
        {
            LastCall = $"SetCpuTuning {tune} {options}";
            return new CpuTuningResult(true, "Changed.", Cpu);
        }

        public CpuTuningResult ConfirmCpuTuning()
        {
            LastCall = "ConfirmCpuTuning";
            return new CpuTuningResult(true, "Kept.", Cpu);
        }

        public CpuTuningResult RevertCpuTuning()
        {
            LastCall = "RevertCpuTuning";
            return new CpuTuningResult(true, "Put back.", Cpu with { Active = null });
        }

        public CpuTuningResult ResetCpuTuning()
        {
            LastCall = "ResetCpuTuning";
            return new CpuTuningResult(true, "Reset.", Cpu with { Kept = null });
        }

        public CpuTuningResult RunCpuStressTest(CancellationToken cancellation = default)
        {
            LastCall = "RunCpuStressTest";
            return new CpuTuningResult(false, "Core 3 returned a wrong result.", Cpu);
        }

        public CpuTuningResult SetCpuAutoApply(bool on)
        {
            LastCall = $"SetCpuAutoApply {on}";
            return new CpuTuningResult(true, "On.", Cpu with { AutoApply = on });
        }

        private static readonly PawnIoReport PawnIo = new(
            true, true, "2.0.1", "FACEIT's anti-cheat blocks PawnIO.", ["Counter-Strike 2"], "PawnIO 2.0.1 is installed.");

        public PawnIoReport GetPawnIo() => PawnIo;

        public PawnIoResult InstallPawnIo(bool acceptFaceitWarning)
        {
            LastCall = $"InstallPawnIo {acceptFaceitWarning}";
            return new PawnIoResult(true, "Installed PawnIO 2.0.1.", PawnIo, RebootNeeded: true);
        }

        public PawnIoResult UninstallPawnIo()
        {
            LastCall = "UninstallPawnIo";
            return new PawnIoResult(true, "Uninstalled PawnIO.", PawnIo with { Installed = false, Version = null });
        }

        public SensorReport GetSensors() => new(
            [
                new SensorView(SensorIds.CpuTemperature, "CPU temperature", SensorKind.Temperature, 61.5, "°C", "PawnIO"),
                new SensorView("gpu0.clock", "GPU clock", SensorKind.Clock, 2610, "MHz", "NVIDIA NVML"),
            ],
            FromPawnIo: true,
            Note: null);

        public TuningResult ResetGpuTuning(string deviceId)
        {
            LastCall = $"ResetGpuTuning {deviceId}";
            return new TuningResult(true, "Reset.", Gpu with { AtDefaults = true, RevertsAt = null });
        }

        public TuningResult AcceptGpuWarning(string deviceId)
        {
            LastCall = $"AcceptGpuWarning {deviceId}";
            return new TuningResult(true, "Unlocked.", IntelGpu with { Locked = false });
        }

        private static readonly GpuTuningView IntelGpu = new(
            "intel:0", "Intel Arc A770", GpuVendor.Intel,
            [new TuningControl(GpuControls.ClockOffset, "GPU clock offset", "MHz", 0, 250, 1, 0, 0)],
            [],
            AtDefaults: true,
            RevertsAt: null,
            Notice: null,
            Warning: "Overclocking may damage your GPU.",
            Locked: true);

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

        private static readonly StartupItemView StartupItem = new(
            "run:HKCU:Discord", "Discord", StartupSource.RunKey, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            @"""C:\Discord\Update.exe"" --processStart Discord.exe", "Discord Inc.", SignatureStatus.Signed, StartupImpact.High,
            Enabled: false, CanSwitch: true, CanDelete: true)
        {
            CanUndo = true,
            Note = "A note.",
        };

        public IReadOnlyList<StartupItemView> ListStartupItems() => [StartupItem];

        public StartupCommandResult SetStartupItemEnabled(string itemId, bool enabled)
        {
            LastCall = $"SetStartupItemEnabled {itemId} {enabled}";
            return new StartupCommandResult(true, "Turned off.", StartupItem);
        }

        public IReadOnlyList<StartupItemView> CheckNewStartupItems()
        {
            LastCall = "CheckNewStartupItems";
            return [StartupItem with { Enabled = true }];
        }

        public StartupCommandResult DeleteStartupItem(string itemId, bool confirmDestructive)
        {
            LastCall = $"DeleteStartupItem {itemId} {confirmDestructive}";
            return new StartupCommandResult(true, "Deleted.", null);
        }
        private static readonly UpdateRunStatus UpdateStatus = new(
            UpdatePhase.Finished, "Installed 1 of 2 updates. Restart to finish installing.", 1, 2, null, RebootRequired: true,
            Installed: ["2026-10 Cumulative Update"], NotInstalled: ["Defender update"], Skipped: ["NVIDIA - Display"]);

        public UpdateRunStatus StartWindowsUpdate()
        {
            LastCall = "StartWindowsUpdate";
            return UpdateStatus with { Phase = UpdatePhase.Installing, Current = "2026-10 Cumulative Update" };
        }

        public UpdateRunStatus GetWindowsUpdateStatus() => UpdateStatus;

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

        public static readonly IReadOnlyList<CleanupProgress> CleanupSteps =
        [
            new(CleanupPhase.Scanning, "cleanup.temp", "Temporary files", 1, 2),
            new(CleanupPhase.Cleaning, "cleanup.recycle-bin", "Recycle Bin", 2, 2),
        ];

        public static readonly CleanupScan Scan = new(
        [
            new("cleanup.temp", "Temporary files", "Deletes leftovers.", CleanupKind.Safe, null, 3_500, 3, [@"C:\Windows\Temp"], null, Ticked: true),
            new("cleanup.component-store", "ResetBase", "DISM.", CleanupKind.Destructive, "Can't uninstall updates.", null, 0, [], "Busy.", Ticked: false),
        ]);

        public static readonly CleanupReport Cleaned = new(false, "Freed 3 KB.", 3_500,
        [
            new("cleanup.temp", "Temporary files", true, 3_500, 3, 1, "Freed 3 KB. 1 in use were left alone."),
            new("cleanup.recycle-bin", "Recycle Bin", false, 0, 0, 0, "Couldn't clean this."),
        ]);

        private CleanupSchedule _schedule = new(false, new DateTimeOffset(2026, 10, 9, 3, 0, 0, TimeSpan.Zero));

        public Task<CleanupScan> ScanCleanupAsync(IProgress<CleanupProgress>? progress = null, CancellationToken cancellation = default)
        {
            LastCall = "ScanCleanup";
            foreach (var p in CleanupSteps)
                progress?.Report(p);
            return Task.FromResult(Scan);
        }

        public async Task<CleanupReport> CleanAsync(IReadOnlyList<string> categoryIds, bool confirmDestructive, IProgress<CleanupProgress>? progress = null, CancellationToken cancellation = default)
        {
            LastCall = $"Clean {string.Join(",", categoryIds)} {confirmDestructive}";
            RepairStarted.TrySetResult();
            foreach (var p in CleanupSteps)
                progress?.Report(p);
            if (HoldRepair is not null)
                await HoldRepair.Task.WaitAsync(cancellation);
            return Cleaned;
        }

        public static readonly Drift.DriftReport DriftFound = new(
            Drift.DriftCheckKind.Full, "26200.1",
            [new("debloat.remove-copilot", "Remove Copilot", Drift.DriftKind.AppReturned, ["Uninstall Copilot"], false, false)],
            [new("debloat.start-recommendations-off", "Start recommendations", Drift.DriftKind.SettingChanged, ["Set it"], false, false)],
            [new("privacy.remove-recall", "Remove Recall", "Gone.")],
            "Windows undid 1 change: Remove Copilot. Re-apply it?");

        public static readonly Drift.ReapplyResult Reapplied = new(true, "Re-applied 1 change.",
            [new("debloat.remove-copilot", "Remove Copilot", true, "Re-applied.")]);

        private Drift.DriftSettings _driftSettings = new(false);

        public Drift.DriftReport CheckDrift() => DriftFound;

        public Drift.ReapplyResult ReapplyDrift(IReadOnlyList<string>? tweakIds, bool confirmDestructive = false)
        {
            LastCall = $"ReapplyDrift {(tweakIds is null ? "<all>" : string.Join(",", tweakIds))} {confirmDestructive}";
            return Reapplied;
        }

        public Drift.DriftSettings GetDriftSettings() => _driftSettings;

        public Drift.DriftSettings SetAutoReapply(bool enabled) => _driftSettings = new(enabled);

        public CleanupSchedule GetCleanupSchedule() => _schedule;

        public CleanupSchedule SetWeeklyCleanup(bool enabled)
        {
            LastCall = $"SetWeeklyCleanup {enabled}";
            return _schedule = _schedule with { WeeklyEnabled = enabled };
        }

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
