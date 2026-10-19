using Elynt.Core.Journal;
using Elynt.Core.Tuning;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Time.Testing;

namespace Elynt.Core.Tests;

/// <summary>Intel GPU tuning through the Engine: locked behind Intel's warning, then the same safety as AMD.</summary>
public sealed class IntelGpuTuningTests : IDisposable
{
    private const string Gpu = FakeIntelGpuTuning.DeviceId;

    private readonly FakeIntelGpuTuning _gpu = new();
    private readonly InMemoryTuningStore _tuningStore = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private GpuTuning _service;

    public IntelGpuTuningTests()
    {
        _service = StartService();
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void Tuning_is_locked_until_Intels_warning_is_accepted()
    {
        var engine = NewEngine();

        var gpu = engine.GetGpuTuning().Gpus.Single();
        Assert.True(gpu.Locked);
        Assert.Equal(FakeIntelGpuTuning.Warning, gpu.Warning);

        var result = engine.SetGpuTuning(Gpu, Values((GpuControls.ClockOffset, 100)));

        Assert.False(result.Succeeded);
        Assert.Contains("warning", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _gpu.Value(GpuControls.ClockOffset));
        Assert.Null(result.Gpu!.RevertsAt);
    }

    [Fact]
    public void Accepting_the_warning_unlocks_tuning_on_the_same_15_second_probation()
    {
        var engine = NewEngine();

        var accept = engine.AcceptGpuWarning(Gpu);

        Assert.True(accept.Succeeded);
        Assert.False(accept.Gpu!.Locked);
        Assert.True(_gpu.WaiverSet);

        var result = engine.SetGpuTuning(Gpu, Values((GpuControls.ClockOffset, 100), (GpuControls.PowerLimit, 210)));
        Assert.True(result.Succeeded);
        Assert.Equal(_clock.GetUtcNow().Add(GpuTuning.ConfirmWindow), result.Gpu!.RevertsAt);
        Assert.Equal(100, _gpu.Value(GpuControls.ClockOffset));

        _clock.Advance(GpuTuning.ConfirmWindow);
        Assert.True(_gpu.IsAtDefaults(Gpu));
        Assert.NotNull(engine.GetGpuTuning().Gpus.Single().Notice);
    }

    [Fact]
    public void The_accepted_warning_and_kept_settings_come_back_after_a_clean_restart()
    {
        var engine = NewEngine();
        engine.AcceptGpuWarning(Gpu);
        engine.SetGpuTuning(Gpu, Values((GpuControls.ClockOffset, 100)));
        engine.ConfirmGpuTuning(Gpu);

        _gpu.Force(GpuControls.ClockOffset, 0); // like a driver that doesn't keep settings across a reboot
        CleanRestart();

        Assert.Equal(100, _gpu.Value(GpuControls.ClockOffset));
        var gpu = NewEngine().GetGpuTuning().Gpus.Single();
        Assert.False(gpu.Locked);
        Assert.True(NewEngine().SetGpuTuning(Gpu, Values((GpuControls.ClockOffset, 120))).Succeeded);
    }

    [Fact]
    public void After_a_crash_or_freeze_the_GPU_comes_back_on_defaults_and_the_tune_is_not_reapplied()
    {
        var engine = NewEngine();
        engine.AcceptGpuWarning(Gpu);
        engine.SetGpuTuning(Gpu, Values((GpuControls.ClockOffset, 100), (GpuControls.VoltageOffset, 50)));
        engine.ConfirmGpuTuning(Gpu);

        CrashRestart(); // the driver kept the tune across the reboot

        Assert.True(_gpu.IsAtDefaults(Gpu));
        Assert.Contains("didn't shut down cleanly", NewEngine().GetGpuTuning().Notice);

        CleanRestart();
        Assert.True(_gpu.IsAtDefaults(Gpu));
    }

    [Fact]
    public void A_crash_during_the_15_seconds_comes_back_on_defaults()
    {
        var engine = NewEngine();
        engine.AcceptGpuWarning(Gpu);
        engine.SetGpuTuning(Gpu, Values((GpuControls.ClockOffset, 100)));

        CrashRestart();

        Assert.True(_gpu.IsAtDefaults(Gpu));
    }

    [Fact]
    public void Several_unkept_changes_go_back_to_the_last_kept_settings_and_undo_works_at_once()
    {
        var engine = NewEngine();
        engine.AcceptGpuWarning(Gpu);
        engine.SetGpuTuning(Gpu, Values((GpuControls.ClockOffset, 100)));
        engine.ConfirmGpuTuning(Gpu);

        engine.SetGpuTuning(Gpu, Values((GpuControls.ClockOffset, 200)));
        engine.SetGpuTuning(Gpu, Values((GpuControls.TemperatureLimit, 95)));
        _clock.Advance(GpuTuning.ConfirmWindow);

        Assert.Equal(100, _gpu.Value(GpuControls.ClockOffset));
        Assert.Equal(90, _gpu.Value(GpuControls.TemperatureLimit));

        engine.SetGpuTuning(Gpu, Values((GpuControls.PowerLimit, 220)));
        var undo = engine.RevertGpuTuning(Gpu);
        Assert.True(undo.Succeeded);
        Assert.Equal(190, _gpu.Value(GpuControls.PowerLimit));
    }

    [Fact]
    public void A_value_outside_the_range_IGCL_reports_is_refused()
    {
        var engine = NewEngine();
        engine.AcceptGpuWarning(Gpu);

        var result = engine.SetGpuTuning(Gpu, Values((GpuControls.PowerLimit, 229)));

        Assert.False(result.Succeeded);
        Assert.True(_gpu.IsAtDefaults(Gpu));
    }

    private static Dictionary<string, int> Values(params (string Id, int Value)[] values) =>
        values.ToDictionary(v => v.Id, v => v.Value);

    /// <summary>The service stops cleanly and starts again in a new process.</summary>
    private void CleanRestart()
    {
        _service.Shutdown();
        _service.Dispose();
        _gpu.ProcessEnded();
        _service = StartService();
    }

    /// <summary>The PC crashed or froze: the service never got to stop.</summary>
    private void CrashRestart()
    {
        _service.Dispose();
        _gpu.ProcessEnded();
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
