using Elynt.Core.Journal;
using Elynt.Core.Tuning;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Time.Testing;

namespace Elynt.Core.Tests;

/// <summary>GPU tuning through the Engine: SDK-bounded sliders, 15 s confirm-or-revert and crash-safe defaults.</summary>
public sealed class GpuTuningTests : IDisposable
{
    private const string Gpu = FakeGpuTuning.DeviceId;

    private readonly FakeGpuTuning _gpu = new();
    private readonly InMemoryTuningStore _tuningStore = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private GpuTuning _service;

    public GpuTuningTests()
    {
        _service = StartService();
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void Sliders_carry_the_ranges_and_defaults_the_SDK_reports()
    {
        var gpu = NewEngine().GetGpuTuning().Gpus.Single();

        Assert.Equal("AMD Radeon RX 7800 XT", gpu.Name);
        var clock = gpu.Controls.Single(c => c.Id == GpuControls.MaxClock);
        Assert.Equal((500, 3000, 5, 2500, 2500), (clock.Min, clock.Max, clock.Step, clock.Default, clock.Current));
        Assert.True(gpu.AtDefaults);
        Assert.Null(gpu.RevertsAt);
    }

    [Theory]
    [InlineData(GpuControls.MaxClock, 3005)]
    [InlineData(GpuControls.MaxClock, 495)]
    [InlineData(GpuControls.Voltage, 1200)]
    [InlineData(GpuControls.PowerLimit, 16)]
    [InlineData(GpuControls.MaxClock, 2602)]
    public void A_value_outside_the_SDK_range_is_refused_and_nothing_changes(string control, int value)
    {
        var result = NewEngine().SetGpuTuning(Gpu, new Dictionary<string, int> { [control] = value });

        Assert.False(result.Succeeded);
        Assert.Equal(0, _gpu.Writes);
        Assert.True(_gpu.IsAtDefaults(Gpu));
    }

    [Fact]
    public void An_unknown_control_is_refused()
    {
        var result = NewEngine().SetGpuTuning(Gpu, new Dictionary<string, int> { ["coreVoltageCurve"] = 1 });

        Assert.False(result.Succeeded);
        Assert.Equal(0, _gpu.Writes);
    }

    [Fact]
    public void A_change_goes_back_after_15_seconds_unless_kept()
    {
        var engine = NewEngine();

        var result = engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2700), (GpuControls.PowerLimit, 10)));

        Assert.True(result.Succeeded);
        Assert.Equal(_clock.GetUtcNow().AddSeconds(15), result.Gpu!.RevertsAt);
        Assert.Equal(2700, _gpu.Value(GpuControls.MaxClock));

        _clock.Advance(TimeSpan.FromSeconds(14.9));
        Assert.Equal(2700, _gpu.Value(GpuControls.MaxClock));

        _clock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.True(_gpu.IsAtDefaults(Gpu));
        var gpu = engine.GetGpuTuning().Gpus.Single();
        Assert.True(gpu.AtDefaults);
        Assert.Null(gpu.RevertsAt);
        Assert.NotNull(gpu.Notice);
        Assert.False(engine.ConfirmGpuTuning(Gpu).Succeeded);
    }

    [Fact]
    public void A_kept_change_stays()
    {
        var engine = NewEngine();
        engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2700)));

        _clock.Advance(TimeSpan.FromSeconds(10));
        var confirm = engine.ConfirmGpuTuning(Gpu);
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True(confirm.Succeeded);
        Assert.Equal(2700, _gpu.Value(GpuControls.MaxClock));
        Assert.Null(engine.GetGpuTuning().Gpus.Single().RevertsAt);
    }

    [Fact]
    public void Several_unkept_changes_go_back_to_the_last_kept_settings()
    {
        var engine = NewEngine();
        engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2700)));
        engine.ConfirmGpuTuning(Gpu);

        engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2800)));
        _clock.Advance(TimeSpan.FromSeconds(10));
        var second = engine.SetGpuTuning(Gpu, Values((GpuControls.Voltage, 1000)));
        Assert.Equal(_clock.GetUtcNow().AddSeconds(15), second.Gpu!.RevertsAt);

        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1000, _gpu.Value(GpuControls.Voltage));

        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(2700, _gpu.Value(GpuControls.MaxClock));
        Assert.Equal(1150, _gpu.Value(GpuControls.Voltage));
    }

    [Fact]
    public void Undo_puts_a_waiting_change_back_straight_away()
    {
        var engine = NewEngine();
        engine.SetGpuTuning(Gpu, Values((GpuControls.FanSpeed, 2500)));

        var undo = engine.RevertGpuTuning(Gpu);

        Assert.True(undo.Succeeded);
        Assert.True(_gpu.IsAtDefaults(Gpu));
        Assert.Null(undo.Gpu!.RevertsAt);
    }

    [Theory]
    [InlineData(AutoTuneKind.OverclockGpu)]
    [InlineData(AutoTuneKind.UndervoltGpu)]
    public void Auto_tuning_is_on_the_same_15_second_probation(AutoTuneKind kind)
    {
        var engine = NewEngine();

        var result = engine.AutoTuneGpu(Gpu, kind);

        Assert.True(result.Succeeded);
        Assert.Equal(kind, _gpu.ActiveAuto);
        _clock.Advance(GpuTuning.ConfirmWindow);
        Assert.True(_gpu.IsAtDefaults(Gpu));
    }

    [Fact]
    public void Reset_goes_to_factory_settings_at_once_and_forgets_the_kept_tune()
    {
        var engine = NewEngine();
        engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2700)));
        engine.ConfirmGpuTuning(Gpu);
        engine.SetGpuTuning(Gpu, Values((GpuControls.Voltage, 1000)));

        var reset = engine.ResetGpuTuning(Gpu);
        _clock.Advance(GpuTuning.ConfirmWindow);

        Assert.True(reset.Succeeded);
        Assert.True(reset.Gpu!.AtDefaults);
        Assert.Null(reset.Gpu.RevertsAt);
        Assert.True(_gpu.IsAtDefaults(Gpu));

        CleanRestart();
        Assert.True(_gpu.IsAtDefaults(Gpu));
    }

    [Fact]
    public void A_change_the_driver_refuses_is_put_back()
    {
        var engine = NewEngine();
        _gpu.FailNextWrite = true;

        var result = engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2700), (GpuControls.Voltage, 1000)));

        Assert.False(result.Succeeded);
        Assert.True(_gpu.IsAtDefaults(Gpu));
        Assert.Null(result.Gpu!.RevertsAt);
    }

    [Fact]
    public void Kept_settings_come_back_after_a_clean_restart()
    {
        var engine = NewEngine();
        engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2700)));
        engine.ConfirmGpuTuning(Gpu);

        _gpu.ResetToDefaults(Gpu); // like a driver that doesn't keep settings across a reboot
        CleanRestart();

        Assert.Equal(2700, _gpu.Value(GpuControls.MaxClock));
        Assert.Null(NewEngine().GetGpuTuning().Notice);
    }

    [Fact]
    public void A_kept_auto_tune_comes_back_after_a_clean_restart()
    {
        var engine = NewEngine();
        engine.AutoTuneGpu(Gpu, AutoTuneKind.UndervoltGpu);
        engine.ConfirmGpuTuning(Gpu);

        _gpu.ResetToDefaults(Gpu);
        CleanRestart();

        Assert.Equal(AutoTuneKind.UndervoltGpu, _gpu.ActiveAuto);
    }

    [Fact]
    public void An_unkept_change_is_put_back_on_a_clean_stop_and_not_reapplied()
    {
        var engine = NewEngine();
        engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2700)));

        CleanRestart();

        Assert.True(_gpu.IsAtDefaults(Gpu));
    }

    [Fact]
    public void After_a_crash_or_freeze_the_GPU_comes_back_on_defaults_and_the_tune_is_not_reapplied()
    {
        var engine = NewEngine();
        engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2700)));
        engine.ConfirmGpuTuning(Gpu);

        CrashRestart(); // the driver kept the tune across the reboot

        Assert.True(_gpu.IsAtDefaults(Gpu));
        Assert.Contains("didn't shut down cleanly", NewEngine().GetGpuTuning().Notice);

        // The next clean restart doesn't bring the tune back either, so a bad tune can't loop.
        CleanRestart();
        Assert.True(_gpu.IsAtDefaults(Gpu));
    }

    [Fact]
    public void A_crash_during_the_15_seconds_comes_back_on_defaults()
    {
        var engine = NewEngine();
        engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2700)));

        CrashRestart();

        Assert.True(_gpu.IsAtDefaults(Gpu));
    }

    [Fact]
    public void A_crash_with_nothing_tuned_leaves_the_GPU_alone()
    {
        CrashRestart();

        Assert.Equal(0, _gpu.Resets);
        Assert.Null(NewEngine().GetGpuTuning().Notice);
    }

    [Fact]
    public void A_damaged_safety_file_counts_as_a_crash()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elynt-tuning-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ \"SessionOpen\": tr");
            _gpu.Write(Gpu, Values((GpuControls.MaxClock, 2700)));
            var safety = new TuningSafety(new JsonFileTuningStore(path));
            using var service = new GpuTuning([_gpu], safety, _clock);

            service.RecoverAtStartup(safety.BeginSession());

            Assert.True(_gpu.IsAtDefaults(Gpu));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_PC_without_a_tunable_GPU_says_why()
    {
        _gpu.UnavailableReason = "No AMD GPU was found.";

        var report = NewEngine().GetGpuTuning();

        Assert.Empty(report.Gpus);
        Assert.Equal("No AMD GPU was found.", report.Notice);
        Assert.False(NewEngine().SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2700))).Succeeded);
    }

    private static Dictionary<string, int> Values(params (string Id, int Value)[] values) =>
        values.ToDictionary(v => v.Id, v => v.Value);

    /// <summary>The service stops cleanly (Windows shut down normally) and starts again.</summary>
    private void CleanRestart()
    {
        _service.Shutdown();
        _service.Dispose();
        _service = StartService();
    }

    /// <summary>The PC crashed or froze: the service never got to stop, and its timers died with it.</summary>
    private void CrashRestart()
    {
        _service.Dispose();
        _service = StartService();
    }

    private GpuTuning StartService()
    {
        var safety = new TuningSafety(_tuningStore);
        var service = new GpuTuning([_gpu], safety, _clock);
        service.RecoverAtStartup(safety.BeginSession());
        return service;
    }

    private Engine NewEngine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo()),
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test",
        clock: _clock,
        gpuTuning: _service);
}
