using Elynt.Core.Drivers;
using Elynt.Core.Journal;
using Elynt.Core.Tuning;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Time.Testing;

namespace Elynt.Core.Tests;

/// <summary>NVIDIA GPU tuning through the Engine, on a fake NVML: probed controls, confirm-or-revert, clean-start re-apply.</summary>
public sealed class NvidiaTuningTests : IDisposable
{
    private const string Gpu = "nvidia:" + FakeNvml.Uuid;

    private readonly FakeNvml _nvml = new();
    private readonly InMemoryTuningStore _tuningStore = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private GpuTuning? _service;

    public void Dispose() => _service?.Dispose();

    [Fact]
    public void A_card_that_accepts_every_control_shows_offsets_power_limit_and_clock_locking()
    {
        _nvml.Add();

        var gpu = Start().GetGpuTuning().Gpus.Single();

        Assert.Equal(Gpu, gpu.DeviceId);
        Assert.Equal("NVIDIA GeForce RTX 4070", gpu.Name);
        Assert.Equal(GpuVendor.Nvidia, gpu.Vendor);
        Assert.Equal(
            [GpuControls.CoreClockOffset, GpuControls.MemoryClockOffset, GpuControls.PowerLimit, GpuControls.MinClock, GpuControls.MaxClock],
            gpu.Controls.Select(c => c.Id));
        var power = gpu.Controls.Single(c => c.Id == GpuControls.PowerLimit);
        Assert.Equal(("W", 100, 250, 200, 200), (power.Unit, power.Min, power.Max, power.Default, power.Current));
        var core = gpu.Controls.Single(c => c.Id == GpuControls.CoreClockOffset);
        Assert.Equal(("MHz", -500, 1000, 0), (core.Unit, core.Min, core.Max, core.Default));
        var max = gpu.Controls.Single(c => c.Id == GpuControls.MaxClock);
        Assert.Equal((210, 3105, 3105, 3105), (max.Min, max.Max, max.Default, max.Current));
        var min = gpu.Controls.Single(c => c.Id == GpuControls.MinClock);
        Assert.Equal((210, 3105, 210, 210), (min.Min, min.Max, min.Default, min.Current));
        Assert.Empty(gpu.AutoTunes);
        Assert.True(gpu.AtDefaults);
    }

    [Fact]
    public void Controls_a_card_refuses_are_hidden_for_that_card_only()
    {
        var laptop = _nvml.Add("NVIDIA GeForce RTX 4060 Laptop GPU", "GPU-laptop");
        laptop.Offsets = FakeNvml.Support.None;     // the driver doesn't report offsets at all
        laptop.Power = FakeNvml.Support.ReadOnly;   // it reports the power limit but refuses to change it
        _nvml.Add("NVIDIA GeForce RTX 4090", "GPU-desktop");

        var gpus = Start().GetGpuTuning().Gpus;

        Assert.Equal([GpuControls.MinClock, GpuControls.MaxClock], gpus.Single(g => g.DeviceId == "nvidia:GPU-laptop").Controls.Select(c => c.Id));
        Assert.Equal(5, gpus.Single(g => g.DeviceId == "nvidia:GPU-desktop").Controls.Count);
        Assert.True(laptop.AtDefaults);
    }

    [Fact]
    public void A_card_that_refuses_everything_is_not_listed()
    {
        var card = _nvml.Add();
        card.Offsets = card.Power = card.Locking = FakeNvml.Support.ReadOnly;

        var report = Start().GetGpuTuning();

        Assert.Empty(report.Gpus);
        Assert.NotNull(report.Notice);
    }

    [Fact]
    public void Offsets_power_limit_and_a_clock_cap_reach_the_card_and_go_back_after_15_seconds_unless_kept()
    {
        var card = _nvml.Add();
        var engine = Start();

        var result = engine.SetGpuTuning(Gpu, Values(
            (GpuControls.CoreClockOffset, 150), (GpuControls.MemoryClockOffset, 1000),
            (GpuControls.PowerLimit, 180), (GpuControls.MaxClock, 2600)));

        Assert.True(result.Succeeded);
        Assert.Equal((150, 1000, 180_000, (210, 2600)), (card.CoreOffset, card.MemoryOffset, card.PowerMilliwatts, card.Lock));
        Assert.Equal(_clock.GetUtcNow().AddSeconds(15), result.Gpu!.RevertsAt);
        Assert.False(result.Gpu.AtDefaults);
        Assert.Equal(2600, result.Gpu.Controls.Single(c => c.Id == GpuControls.MaxClock).Current);

        _clock.Advance(TimeSpan.FromSeconds(15));

        Assert.True(card.AtDefaults);
        var gpu = engine.GetGpuTuning().Gpus.Single();
        Assert.True(gpu.AtDefaults);
        Assert.NotNull(gpu.Notice);
    }

    [Fact]
    public void Unkept_changes_go_back_to_the_last_kept_settings()
    {
        var card = _nvml.Add();
        var engine = Start();
        engine.SetGpuTuning(Gpu, Values((GpuControls.CoreClockOffset, 100), (GpuControls.MinClock, 1500)));
        engine.ConfirmGpuTuning(Gpu);

        engine.SetGpuTuning(Gpu, Values((GpuControls.CoreClockOffset, 200), (GpuControls.PowerLimit, 120)));
        engine.SetGpuTuning(Gpu, Values((GpuControls.MinClock, 2000)));
        _clock.Advance(TimeSpan.FromSeconds(15));

        Assert.Equal((100, 200_000, (1500, 3105)), (card.CoreOffset, card.PowerMilliwatts, card.Lock));
    }

    [Fact]
    public void Clearing_the_clock_lock_frees_the_clocks()
    {
        var card = _nvml.Add();
        var engine = Start();
        engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 2400)));
        engine.ConfirmGpuTuning(Gpu);

        engine.SetGpuTuning(Gpu, Values((GpuControls.MaxClock, 3105)));

        Assert.Null(card.Lock);
        Assert.True(engine.GetGpuTuning().Gpus.Single().AtDefaults);
    }

    [Fact]
    public void A_min_clock_above_the_cap_is_refused_and_nothing_stays_changed()
    {
        var card = _nvml.Add();
        var engine = Start();

        var result = engine.SetGpuTuning(Gpu, Values((GpuControls.CoreClockOffset, 100), (GpuControls.MinClock, 2500), (GpuControls.MaxClock, 2000)));

        Assert.False(result.Succeeded);
        Assert.True(card.AtDefaults);
    }

    [Fact]
    public void Reset_puts_every_control_back_at_once()
    {
        var card = _nvml.Add();
        var engine = Start();
        engine.SetGpuTuning(Gpu, Values((GpuControls.CoreClockOffset, 100), (GpuControls.PowerLimit, 150), (GpuControls.MaxClock, 2000)));
        engine.ConfirmGpuTuning(Gpu);

        var reset = engine.ResetGpuTuning(Gpu);

        Assert.True(reset.Succeeded);
        Assert.True(card.AtDefaults);
        Assert.True(reset.Gpu!.AtDefaults);
    }

    [Fact]
    public void Kept_settings_come_back_at_the_next_login_after_a_clean_shutdown()
    {
        var card = _nvml.Add();
        var engine = Start();
        engine.SetGpuTuning(Gpu, Values((GpuControls.CoreClockOffset, 120), (GpuControls.PowerLimit, 190), (GpuControls.MaxClock, 2500)));
        engine.ConfirmGpuTuning(Gpu);

        _service!.Shutdown();
        _nvml.Reboot(); // NVML settings never survive a restart
        var gpu = Start().GetGpuTuning().Gpus.Single();

        Assert.Equal((120, 190_000, (210, 2500)), (card.CoreOffset, card.PowerMilliwatts, card.Lock));
        Assert.Equal(2500, gpu.Controls.Single(c => c.Id == GpuControls.MaxClock).Current);
        Assert.False(gpu.AtDefaults);
    }

    [Fact]
    public void Kept_settings_are_not_reapplied_after_a_crash_and_the_card_starts_on_defaults()
    {
        var card = _nvml.Add();
        var engine = Start();
        engine.SetGpuTuning(Gpu, Values((GpuControls.CoreClockOffset, 120)));
        engine.ConfirmGpuTuning(Gpu);

        _nvml.Reboot(); // a crash: the service never got to stop
        var report = Start().GetGpuTuning();

        Assert.True(card.AtDefaults);
        Assert.Contains("didn't shut down cleanly", report.Notice);

        _service!.Shutdown();
        _nvml.Reboot();
        Start();
        Assert.True(card.AtDefaults);
    }

    [Fact]
    public void An_unkept_change_is_not_reapplied_after_a_clean_shutdown()
    {
        var card = _nvml.Add();
        Start().SetGpuTuning(Gpu, Values((GpuControls.CoreClockOffset, 120)));

        _service!.Shutdown();
        _nvml.Reboot();
        Start();

        Assert.True(card.AtDefaults);
    }

    [Fact]
    public void The_card_shows_live_readouts_it_reports_and_skips_the_ones_it_does_not()
    {
        var card = _nvml.Add();
        card.Readings = new NvmlReadings(TemperatureC: 61, GraphicsClockMHz: 2655, MemoryClockMHz: 10501, PowerMilliwatts: 187_400, UtilizationPercent: 97);
        var engine = Start();

        var readouts = engine.GetGpuTuning().Gpus.Single().Readouts!;

        Assert.Equal(
            [("Temperature", 61, "°C"), ("GPU clock", 2655, "MHz"), ("Memory clock", 10501, "MHz"), ("Power draw", 187, "W"), ("GPU load", 97, "%")],
            readouts.Select(r => (r.Name, r.Value, r.Unit)));

        card.Readings = card.Readings with { TemperatureC = 70 };
        Assert.Equal(70, engine.GetGpuTuning().Gpus.Single().Readouts![0].Value);
    }

    [Fact]
    public void Automatic_tuning_is_left_to_the_NVIDIA_App()
    {
        var card = _nvml.Add();

        var result = Start().AutoTuneGpu(Gpu, AutoTuneKind.OverclockGpu);

        Assert.False(result.Succeeded);
        Assert.True(card.AtDefaults);
    }

    [Fact]
    public void A_PC_without_the_NVIDIA_driver_says_why()
    {
        _nvml.UnavailableReason = "NVIDIA GPU tuning isn't available: no NVIDIA driver was found.";

        var report = Start().GetGpuTuning();

        Assert.Empty(report.Gpus);
        Assert.Equal("NVIDIA GPU tuning isn't available: no NVIDIA driver was found.", report.Notice);
    }

    private static Dictionary<string, int> Values(params (string Id, int Value)[] values) =>
        values.ToDictionary(v => v.Id, v => v.Value);

    private Engine Start()
    {
        _service?.Dispose();
        var safety = new TuningSafety(_tuningStore);
        _service = new GpuTuning([new NvidiaGpuTuning(_nvml)], safety, _clock);
        _service.RecoverAtStartup(safety.BeginSession());
        return NewEngine();
    }

    private Engine NewEngine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo()),
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test",
        clock: _clock,
        gpuTuning: _service);
}
